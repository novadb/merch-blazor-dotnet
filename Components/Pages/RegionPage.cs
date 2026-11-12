using Merch.Site;
using Microsoft.AspNetCore.Components;

namespace Merch.Components.Pages;

/// <summary>
/// What every page and its shell need: the region (language, texts, market and currency follow from it), its texts,
/// the page tree for the navigation, and the region switch's targets.
/// </summary>
public abstract class RegionPage : ComponentBase
{
    [Inject] Labels Labels { get; set; } = null!;
    [Inject] protected SiteTree Tree { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    /// <summary>The request: a page is rendered once on the server (static SSR), while it is open.</summary>
    [CascadingParameter] protected HttpContext HttpContext { get; set; } = null!;

    public Region Region { get; private set; } = Region.Us;
    /// <summary>Null only on the error page when NovaDB cannot be reached: it then stands alone in English.</summary>
    public Texts? Text { get; private set; }
    public SiteMap? Site { get; private set; }
    /// <summary>Region → URL of the same content; a region missing here links to its home page.</summary>
    public IReadOnlyDictionary<Region, string> Alternates { get; protected set; } = new Dictionary<Region, string>();
    /// <summary>
    /// The fields of a POST form (filters, sorting, search); empty when the page was simply opened. The site reads no
    /// query string. Read from the request, not with [SupplyParameterFromForm]: that binds only a form Blazor named
    /// (a hidden `_handler` field), and these forms are the same plain HTML forms as in every other demo.
    /// </summary>
    protected IFormCollection Form { get; private set; } = FormCollection.Empty;

    protected async Task LoadRegionAsync(Region region)
    {
        Region = region;
        var (text, site) = (Labels.LoadAsync(region), Tree.LoadAsync(region));
        (Text, Site) = (await text, await site);
        if (HttpContext.Request.HasFormContentType) Form = await HttpContext.Request.ReadFormAsync();
    }
}
