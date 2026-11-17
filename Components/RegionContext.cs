using Merch.Site;
using Microsoft.AspNetCore.Components;

namespace Merch.Components;

/// <summary>
/// The region of the request and what every page and the layout need from it: its texts and the page tree for the
/// navigation. Scoped: one per request in static SSR. The layout and the page both ask for it to be loaded, whichever
/// comes first starts the read; the page that knows its counterparts in the other regions sets <see cref="Alternates"/>.
/// </summary>
public sealed class RegionContext(Labels labels, SiteTree tree, NavigationManager navigation)
{
    Task? loading;
    Texts? text;
    SiteMap? map;

    public Region Region { get; private set; } = Region.Us;
    /// <summary>False only on the error page when NovaDB cannot be reached: it then stands alone in English.</summary>
    public bool Loaded => text is not null && map is not null;
    public Texts Text => text ?? throw new InvalidOperationException("The region is not loaded");
    /// <summary>The region's page tree. Not named <c>Site</c>: that is also the namespace <c>Merch.Site</c>.</summary>
    public SiteMap Map => map ?? throw new InvalidOperationException("The region is not loaded");
    /// <summary>Region → URL of the same content, for the region switch; a region missing here links to its home page.</summary>
    public IReadOnlyDictionary<Region, string> Alternates { get; private set; } = new Dictionary<Region, string>();

    /// <summary>Raised when the page has set <see cref="Alternates"/> or the region is read: the layout, which was rendered before, renders again.</summary>
    public event Action? Changed;

    public void SetAlternates(IReadOnlyDictionary<Region, string> alternates)
    {
        Alternates = alternates;
        Changed?.Invoke();
    }

    /// <summary>Loads the region of the URL (<c>us</c> where its first segment is none).</summary>
    public Task LoadAsync() =>
        LoadAsync(Region.Find(navigation.ToBaseRelativePath(navigation.Uri).Split('/', 2)[0]) ?? Region.Us);

    public Task LoadAsync(Region region)
    {
        if (loading is not null && region == Region) return loading;
        Region = region;
        return loading = ReadAsync(region);
    }

    async Task ReadAsync(Region region)
    {
        var (loadedText, loadedMap) = (labels.LoadAsync(region), tree.LoadAsync(region));
        (text, map) = (await loadedText, await loadedMap);
        Changed?.Invoke(); // the layout may have rendered with another region before this one was asked for (the error page)
    }
}
