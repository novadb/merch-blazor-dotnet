using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Merch.Components;
using Merch.NovaDB;
using Merch.Site;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

// Numbers in the HTML (volumes, inches, counts) print the same on every machine; prices take the region's culture.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
// The platform names the port in PORT (as for every demo); ASP.NET Core's own setting is HTTP_PORTS.
if (builder.Configuration["PORT"] is { Length: > 0 } port) builder.WebHost.UseSetting(WebHostDefaults.HttpPortsKey, port);

// The Delivery login comes from the environment (or `dotnet user-secrets`). Without it the site does not start.
builder.Services.AddOptions<DeliveryOptions>()
    .Configure<IConfiguration>((options, config) =>
    {
        options.Url = config["NOVA_DELIVERY_URL"];
        options.User = config["NOVA_DELIVERY_USER"];
        options.Password = config["NOVA_DELIVERY_PASSWORD"];
        if (config["DEMO_BRANCH"] is { Length: > 0 } branch) options.Branch = branch;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpClient(Delivery.HttpClientName, (services, client) =>
{
    var login = services.GetRequiredService<IOptions<DeliveryOptions>>().Value;
    client.BaseAddress = new Uri(login.Url!);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login.User}:{login.Password}")));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});
// A Delivery answer is kept as JSON bytes: the default limit of 1 MB would leave the list of all articles uncached.
builder.Services.AddHybridCache(options => options.MaximumPayloadBytes = 32 * 1024 * 1024);
builder.Services.AddSingleton<Delivery>();
builder.Services.AddSingleton<Labels>();
builder.Services.AddSingleton<SiteTree>();
builder.Services.AddSingleton<Catalog>();
builder.Services.AddSingleton<Images>();
builder.Services.AddSingleton<Media>();
builder.Services.AddScoped<RegionContext>();

// Razor Components rendered on the server only (static SSR): no circuit, no WebAssembly. blazor.web.js adds enhanced navigation.
builder.Services.AddRazorComponents();
// Umlauts and "·" stay readable in the HTML instead of becoming character references.
builder.Services.AddWebEncoders(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddResponseCompression();
// TLS ends at the ingress: believe its X-Forwarded-Proto and -Host, so that the request carries the public https URL.
// Remove this where the site is reached directly, without a proxy: a client could then forge those headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
app.UseForwardedHeaders();
app.UseResponseCompression();
// One URL per page: `/de` → `/de/`, `/de/shop/` → `/de/shop`, permanently; a foreign query string (tracking) is kept.
var regions = string.Join('|', Region.All.Select(r => r.Code));
app.UseRewriter(new RewriteOptions()
    .AddRedirect($"^({regions})$", "$1/", StatusCodes.Status301MovedPermanently)
    .AddRedirect($"^((?:{regions})/.+)/$", "$1", StatusCodes.Status301MovedPermanently));
app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/error/{0}", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();

// `/` → the visitor's region: `/de/` for German browsers, `/uk/` for British English and its neighbours, else `/us/`.
var ukTags = new Regex(@"^en-(gb|ie|au|nz|za|in|sg)\b");
app.MapGet("/", (HttpRequest request) =>
{
    var preferred = request.GetTypedHeaders().AcceptLanguage.OrderByDescending(language => language.Quality ?? 1).FirstOrDefault()?.Value.Value?.ToLowerInvariant() ?? "";
    return Results.Redirect(preferred.StartsWith("de") ? "/de/" : ukTags.IsMatch(preferred) ? "/uk/" : "/us/");
});

// The name keeps the extension because NovaDB serves a file only under it. Neither route can change its content under its name.
// An SVG stays vector: it has no scaled variant.
var media = app.MapGroup("/media/{md5}");
// GET /media/{md5}/original{ext}: the file `files/{md5}{ext}`.
// The constraint keeps `original.png.webp` (an extension with a dot in it) away from this route: it belongs to the next one, as a 404.
media.MapGet("original.{ext:regex(^[a-z0-9]{{1,5}}$)}", (string md5, string ext, HttpContext context, Media media) =>
    Media.FileName().IsMatch($"{md5}.{ext}") ? media.ServeOriginalAsync(context, md5, $".{ext}") : Task.FromResult(Results.NotFound()));
// GET /media/{md5}/{width}{ext}.webp: the same file scaled to one of the widths as WebP.
media.MapGet("{width}.{ext}.webp", (string md5, string width, string ext, HttpContext context, Media media) =>
{
    var size = Media.Widths.FirstOrDefault(w => w.ToString() == width); // not int.Parse: it would also read "0400" as 400
    return size > 0 && ext != "svg" && Media.FileName().IsMatch($"{md5}.{ext}") ? media.ServeScaledAsync(context, md5, $".{ext}", size) : Task.FromResult(Results.NotFound());
});

// GET /api/suggest?lang=&q=&list=: the header's typeahead list as HTML, a Razor component rendered on its own (up to 5 products and 3 pages).
// `list` is the ID of the list in the page: the options' IDs start with it.
app.MapGet("/api/suggest", async Task<IResult> (string? lang, string? q, string? list, Labels labels, Catalog catalog, SiteTree tree) =>
{
    if (Region.Find(lang) is not { } region) return Results.NotFound();
    var query = (q ?? "").Trim();
    var filter = query.Length < 2 ? null : Catalog.SearchFilter(query, Schema.ProductSearchFields);
    var text = labels.LoadAsync(region);
    var products = filter is null ? Task.FromResult(new List<Product>()) : catalog.LoadAsync(region, filter, "$score", take: 5);
    var pages = filter is null ? Task.FromResult(new List<SitePage>()) : tree.SearchAsync(region, query, 3);
    return new RazorComponentResult<SuggestionList>(new { ListId = list ?? "", Query = filter is null ? null : query, Region = region, Text = await text, Products = await products, Pages = await pages });
});

app.MapRazorComponents<App>();
app.Run();
