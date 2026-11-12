using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Merch.Components;
using Merch.NovaDB;
using Merch.Site;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

// Numbers in the HTML (volumes, inches, counts) print the same on every machine; prices take the region's culture.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
// The platform names the port in PORT (as for every demo); ASP.NET Core's own setting is HTTP_PORTS.
if (builder.Configuration["PORT"] is { Length: > 0 } port) builder.WebHost.UseSetting(WebHostDefaults.HttpPortsKey, port);

// The Delivery login comes from the environment (or `dotnet user-secrets`) and is never logged. Without it the site does not start.
string Required(string name) => builder.Configuration[name] is { Length: > 0 } value ? value : throw new InvalidOperationException($"{name} is not set (see README, \"Run\")");
var deliveryUrl = new Uri(Required("NOVA_DELIVERY_URL"));
var login = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Required("NOVA_DELIVERY_USER")}:{Required("NOVA_DELIVERY_PASSWORD")}"));
builder.Services.AddHttpClient(Delivery.HttpClientName, client =>
{
    client.BaseAddress = deliveryUrl;
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", login);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Delivery>();
builder.Services.AddSingleton<Labels>();
builder.Services.AddSingleton<SiteTree>();
builder.Services.AddSingleton<Catalog>();
builder.Services.AddSingleton<Images>();
builder.Services.AddSingleton<Media>();

// Razor Components rendered on the server only (static SSR): no circuit, no WebAssembly, no blazor.web.js.
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
// Blazor takes a POST only for a form it named (a hidden `_handler` field, bound with [SupplyParameterFromForm]) and
// answers any other with 400. The forms here are the plain HTML forms of every demo, answered with the page itself:
// a page renders their POST as it renders a GET, and reads the fields from the request (RegionPage.Form).
app.Use((context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) && context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is not null)
        context.Request.Method = HttpMethods.Get;
    return next(context);
});
app.MapStaticAssets();

// `/` → the visitor's region: `/de/` for German browsers, `/uk/` for British English and its neighbours, else `/us/`.
var ukTags = new Regex(@"^en-(gb|ie|au|nz|za|in|sg)\b");
app.MapGet("/", (HttpRequest request) =>
{
    var preferred = request.Headers.AcceptLanguage.ToString().Split(',')[0].Trim().ToLowerInvariant();
    return Results.Redirect(preferred.StartsWith("de") ? "/de/" : ukTags.IsMatch(preferred) ? "/uk/" : "/us/");
});

// GET /media/{md5}{ext}: the file `files/{md5}{ext}`, unchangeable under this name.
app.MapGet("/media/{name}", (string name, HttpContext context, Media media) =>
    Media.FileName().IsMatch(name) ? media.ServeAsync(context, name) : Task.FromResult(Results.NotFound()));

// GET /media/{width}/{md5}{ext}: the same file scaled to one of the widths as WebP. SVG stays vector: it has no scaled variant.
app.MapGet("/media/{width}/{name}", (string width, string name, HttpContext context, Media media) =>
{
    var w = Media.Widths.FirstOrDefault(w => w.ToString() == width); // not int.Parse: it would also read "0400" as 400
    return w > 0 && Media.FileName().IsMatch(name) && !name.EndsWith(".svg") ? media.ServeScaledAsync(context, name, w) : Task.FromResult(Results.NotFound());
});

// POST /api/suggest with the form fields `lang` and `q` → { products: [...5], pages: [...3] } for the header's typeahead.
app.MapPost("/api/suggest", async (HttpRequest request, Catalog catalog, SiteTree tree) =>
{
    var form = await request.ReadFormAsync();
    if (Region.Find(form["lang"]) is not { } region) return Results.NotFound();
    var q = form["q"].ToString().Trim();
    if (q.Length < 2 || Catalog.SearchFilter(q, Schema.ProductSearchFields) is not { } filter) return Results.Json(new { products = Array.Empty<object>(), pages = Array.Empty<object>() });

    var products = catalog.LoadAsync(region, filter, "$score");
    var pages = tree.SearchAsync(region, q, 3);
    return Results.Json(new
    {
        products = (await products).Take(5).Select(p => new { name = p.Name, url = p.Url, image = p.Image is null ? null : Images.SmallSrc(p.Image), price = region.FormatPrice(p.Price) }),
        pages = (await pages).Select(p => new { title = p.Title, url = p.Url }),
    });
});

// The forms only read (filters, sorting, search) and must work from a cached page and from site.js's fetch: no
// antiforgery token. Blazor asks for one on every POST unless the endpoints say otherwise.
app.MapRazorComponents<App>().DisableAntiforgery();
app.Run();
