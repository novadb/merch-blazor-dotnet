using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Merch.NovaDB;

namespace Merch.Site;

/// <summary>The sellable variant of a product: SKU, colour, stock and delivery time.</summary>
/// <param name="Color">A PIM object (<c>merchColor</c>) held by ID; its name and swatch come with the labels (<see cref="Texts.Value"/>).</param>
/// <param name="Stock">Pieces in stock; 0 is sold out.</param>
public sealed record Article(int Id, int ProductId, string? Name, string? Sku, int? Color, int Stock, int? DeliveryDays);

/// <summary>One row of a set product: how many of which article, linked to the article's product page (none if that is not on the site).</summary>
public sealed record SetRow(int Quantity, string Name, string? Url);

/// <summary>A product from the PIM in one region: its texts in the region's language, its price in the region's market.</summary>
/// <param name="Path">The URL without the region, e.g. <c>shop/t-shirts/we-build-value</c>.</param>
/// <param name="Articles">The released articles; a product that has none is shown as available, but no filter counts it.</param>
/// <param name="Materials">Materials and finishes are PIM objects (<c>merchMaterial</c>, <c>merchFinish</c>), held by ID like an article's colour.</param>
/// <param name="Volume">Cup volume in ml.</param>
/// <param name="ScreenSize">Laptop sleeve screen size in inches.</param>
/// <param name="Image">The first picture, <c>/media/{md5}/original{ext}</c>.</param>
/// <param name="Similar">Object IDs of products: similar ones, accessories and spare parts, as an editor ordered them.</param>
/// <param name="Claims">The texts of the claims the product makes.</param>
/// <param name="SetItems">The rows of a set product: an article (by object ID) and how many of it.</param>
public sealed record Product(
    int Id, string Name, string Url, string Path, IReadOnlyList<Article> Articles, int[] Materials, decimal? Volume, int[] Finishes,
    int? Pieces, decimal? ScreenSize, decimal? Price, string? Image, int? GroupId, string? DescriptionHtml,
    int[] Similar, int[] Accessories, int[] SpareParts, string[] Claims, IReadOnlyList<(int ArticleId, int Quantity)> SetItems,
    SeoHints Seo, string? ShopifyId)
{
    /// <summary>The colours of the articles, without repeats.</summary>
    public IEnumerable<int> Colors => Articles.Select(a => a.Color).OfType<int>().Distinct();

    /// <summary><c>false</c> is sold out (no article in stock); <c>null</c>: no article, shown as available, but not counted by the in-stock filter.</summary>
    public bool? InStock => Articles.Count > 0 ? Articles.Any(a => a.Stock > 0) : null;
}

/// <summary>Products from the PIM: loading, search, facets and sorting.</summary>
public sealed partial class Catalog(Delivery delivery)
{
    /// <summary>Only what the workflow has released is on the site: <paramref name="filter"/> narrows the released objects further.</summary>
    static string Released(string workflow, string? filter) =>
        filter is null ? $"{workflow} = {Schema.WorkflowReleased}" : $"{workflow} = {Schema.WorkflowReleased} && {filter}";

    /// <summary>All released articles of a language.</summary>
    async Task<List<Article>> LoadArticlesAsync(Region region)
    {
        var (objs, _) = await delivery.ListAsync<ArticleProps>(Schema.Types.Article, new(region.Culture, Filter: Released(Schema.Article.Workflow, null)));
        return [.. objs.Where(o => o.Props.Product is not null)
            .Select(o => new Article(o.Meta.Id, o.Props.Product!.Value, o.Props.Name, o.Props.Number, o.Props.Color, o.Props.Stock ?? 0, o.Props.DeliveryDays))];
    }

    /// <summary>
    /// Every released product matching a Delivery filter, or only the first <paramref name="take"/>; pictures, claims and set
    /// rows arrive in the same response (<c>resolve</c>).
    /// </summary>
    public async Task<List<Product>> LoadAsync(Region region, string? filter = null, string? sort = null, int? take = null)
    {
        var read = delivery.ListAsync<ProductProps>(Schema.Types.Product, new(
            region.Culture, Filter: Released(Schema.Product.Workflow, filter), Sort: sort, Take: take, Variants: Schema.MarketVariant(region.Market),
            Resolve: string.Join(",", Schema.Product.Images, Schema.Product.Claims, Schema.Product.SetItems)));
        var articles = LoadArticlesAsync(region);
        var (objs, refs) = await read;
        var images = Images.From(refs);
        var byId = refs.DistinctBy(o => o.Meta.Id).ToDictionary(o => o.Meta.Id);
        var articlesOf = (await articles).ToLookup(a => a.ProductId);
        return [.. objs.Where(o => o.Props is { Name: not null, UrlSegment: not null })
            .Select(o => ToProduct(o.Meta.Id, o.Props, region, [.. articlesOf[o.Meta.Id]], byId, images))];
    }

    /// <summary>Products by ID, in the order of <paramref name="ids"/> (the order an editor chose).</summary>
    public async Task<List<Product>> ByIdsAsync(IReadOnlyCollection<int> ids, Region region)
    {
        if (ids.Count == 0) return [];
        var byId = (await LoadAsync(region, $"meta.id = {string.Join(", ", ids)}")).ToDictionary(p => p.Id);
        return [.. ids.Select(byId.GetValueOrDefault).OfType<Product>()];
    }

    public async Task<Product?> ByPathAsync(Region region, string path) =>
        (await LoadAsync(region, $"{Schema.Product.UrlSegment} = \"{path.Replace("\"", "").Replace("\\", "")}\"")).FirstOrDefault();

    /// <summary>The rows of a set product, linked to the product page of each article (the page of a product that is not on the site has no link).</summary>
    public async Task<List<SetRow>> SetContentsAsync(Product product, Region region)
    {
        if (product.SetItems.Count == 0) return [];
        var articles = (await LoadArticlesAsync(region)).ToDictionary(a => a.Id);
        var rows = product.SetItems.Where(i => articles.TryGetValue(i.ArticleId, out var a) && a.Name is not null)
            .Select(i => (i.Quantity, Article: articles[i.ArticleId])).ToList();
        var products = (await ByIdsAsync([.. rows.Select(r => r.Article.ProductId).Distinct()], region)).ToDictionary(p => p.Id);
        return [.. rows.Select(r => new SetRow(r.Quantity, r.Article.Name!, products.GetValueOrDefault(r.Article.ProductId)?.Url))];
    }

    /// <summary><paramref name="refs"/>: the objects the read resolved (pictures, claims, set rows), by object ID.</summary>
    static Product ToProduct(int id, ProductProps p, Region region, List<Article> articles, Dictionary<int, NovaObject<JsonElement>> refs, IReadOnlyDictionary<int, string> images)
    {
        T? Ref<T>(int refId) where T : class => refs.TryGetValue(refId, out var o) ? o.Props.Deserialize<T>(JsonSerializerOptions.Web) : null;
        return new(
            id, p.Name!, $"/{region.Code}/{p.UrlSegment}", p.UrlSegment!, articles, p.Materials ?? [], p.Volume, p.Finishes ?? [],
            p.Pieces, p.ScreenSize, p.Price, p.Images is [var first, ..] ? images.GetValueOrDefault(first) : null, p.Group,
            p.Description?.Xml is { } xml ? CleanHtml(xml) : null, p.Related ?? [], p.Accessories ?? [], p.SpareParts ?? [],
            [.. (p.Claims ?? []).Select(c => Ref<ClaimProps>(c)?.Text).OfType<string>()],
            [.. (p.SetItems ?? []).Select(i => Ref<SetItemProps>(i)).OfType<SetItemProps>()
                .Where(row => row is { Article: not null, Quantity: not null }).Select(row => (row.Article!.Value, row.Quantity!.Value))],
            new(p.SeoTitle, p.SeoDescription, p.SeoNoIndex == true, p.SeoNoFollow == true), p.ShopifyId);
    }

    /// <summary>Removes the XHTML namespace the CMS stores with rich text.</summary>
    public static string CleanHtml(string html) => XmlNamespace().Replace(html, "");

    [GeneratedRegex(@"\sxmlns=""[^""]*""")]
    private static partial Regex XmlNamespace();

    /// <summary>Full-text filter over the given text fields: every term must match within one field; null when nothing usable is left.</summary>
    public static string? SearchFilter(string query, IEnumerable<string> fields)
    {
        // Lucene-safe prefix terms (`+term*`) from the first 100 characters a visitor typed.
        var terms = string.Join(" ", LuceneSyntax().Replace(query[..Math.Min(query.Length, 100)].ToLowerInvariant(), " ")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(6).Select(term => $"+{term}*"));
        return terms.Length > 0 ? $"({string.Join(" || ", fields.Select(f => $"{f}: {terms}"))})" : null;
    }

    [GeneratedRegex("""[+\-!(){}[\]^"~*?:\\/&|]""")]
    private static partial Regex LuceneSyntax();

    // ---- facets ------------------------------------------------------------------------------------

    public static readonly string[] Sorts = ["relevance", "name", "price-asc", "price-desc"];

    /// <summary>
    /// Cards a range shows at first and adds per "Show more": the grid has 1, 2 or 3 columns, so a multiple of 6 always
    /// fills whole rows, and 12 cover the first screen and a half below the facets. The whole range is loaded all the same,
    /// because the facets are counted over all of it: batches are a matter of rendering, not of Delivery paging.
    /// </summary>
    public const int RangeBatch = 12;

    /// <summary>The most cards a posted <c>shown</c> can ask for: more than any range holds, and no overflow below it.</summary>
    const int MaxShown = 100_000;

    /// <summary>
    /// The filters and sorting of a posted range form; the form's "reset" button clears them all. Only the "Show more"
    /// button posts <c>shown</c>, so any other change starts again with the first batch.
    /// </summary>
    public static Selection ParseSelection(IFormCollection posted)
    {
        var form = posted.ContainsKey("reset") ? FormCollection.Empty : posted;
        decimal? Num(string key) => decimal.TryParse(form[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
        // A posted ID is digits that fit an int: anything else is no ID of ours (the facets and labels treat them as int).
        int[] Ids(string key) => [.. form[key].Select(v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : (int?)null).OfType<int>()];
        var sort = form["sort"].ToString();
        var shown = int.TryParse(form["shown"], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? Math.Min(n, MaxShown) : 0;
        return new Selection(
            Ids("group"),
            [.. Ids("color").Select(id => id.ToString())],
            [.. Ids("material").Select(id => id.ToString())],
            form["stock"] == "1",
            Num("min"), Num("max"),
            Sorts.Contains(sort) ? sort : null,
            Math.Max(1, (shown + RangeBatch - 1) / RangeBatch) * RangeBatch);
    }

    /// <summary>The facet values of a product: group, colour and materials, as the strings their checkboxes post.</summary>
    static IEnumerable<string> FacetValues(Product p, string facet) => facet switch
    {
        "group" => p.GroupId is { } g ? [g.ToString()] : [],
        "color" => p.Colors.Select(c => c.ToString()),
        "material" => p.Materials.Select(m => m.ToString()),
        _ => throw new ArgumentOutOfRangeException(nameof(facet)),
    };

    /// <summary>Whether a product passes every selected facet except <paramref name="skip"/> (so a facet's own counts stay open).</summary>
    static bool Passes(Product p, Selection s, string? skip = null)
    {
        bool Any(string facet, IReadOnlyList<string> selected) =>
            skip == facet || selected.Count == 0 || FacetValues(p, facet).Any(selected.Contains);
        return Any("group", s.GroupValues) && Any("color", s.Color) && Any("material", s.Material)
            && (skip == "stock" || !s.Stock || p.InStock == true)
            && (skip == "price" || ((s.Min is null || (p.Price ?? 0) >= s.Min) && (s.Max is null || p.Price <= s.Max)));
    }

    /// <summary>
    /// A facet's values. The list is stable: every value of the unfiltered range, most products first, whatever is selected,
    /// so a filter change moves no row. <c>Count</c> is what the other facets leave of it and may be 0.
    /// </summary>
    static List<FacetValue> Count(List<Product> products, Selection s, string facet, IReadOnlyList<string> selected)
    {
        var totals = new Dictionary<string, int>();
        var counts = new Dictionary<string, int>();
        foreach (var p in products)
            foreach (var v in FacetValues(p, facet))
            {
                totals[v] = totals.GetValueOrDefault(v) + 1;
                if (Passes(p, s, facet)) counts[v] = counts.GetValueOrDefault(v) + 1;
            }
        foreach (var v in selected) totals.TryAdd(v, 0);
        return [.. totals.OrderByDescending(t => t.Value).ThenBy(t => t.Key, StringComparer.InvariantCulture)
            .Select(t => new FacetValue(t.Key, counts.GetValueOrDefault(t.Key), selected.Contains(t.Key)))];
    }

    /// <summary>Applies the selection and counts each facet over the products the other facets let through.</summary>
    public static ProductRange ApplySelection(List<Product> products, Selection s, string defaultSort)
    {
        var prices = products.Select(p => p.Price).OfType<decimal>().ToList();
        var facets = new Facets(
            Count(products, s, "group", s.GroupValues),
            Count(products, s, "color", s.Color),
            Count(products, s, "material", s.Material),
            products.Count(p => p.InStock == true && Passes(p, s, "stock")),
            prices.Count > 0 ? Math.Floor(prices.Min()) : 0,
            prices.Count > 0 ? Math.Ceiling(prices.Max()) : 0);
        var items = products.Where(p => Passes(p, s));
        items = (s.Sort ?? defaultSort) switch // "relevance" keeps the order the products came in; LINQ sorts stably
        {
            "name" => items.OrderBy(p => p.Name, StringComparer.InvariantCulture),
            "price-asc" => items.OrderBy(p => p.Price ?? decimal.MaxValue),
            "price-desc" => items.OrderByDescending(p => p.Price ?? decimal.MaxValue),
            _ => items,
        };
        return new ProductRange([.. items], facets, s, defaultSort);
    }
}

/// <summary><c>Shown</c>: how many cards of the result are rendered (a multiple of <see cref="Catalog.RangeBatch"/>); facets and count cover them all.</summary>
public sealed record Selection(int[] Group, string[] Color, string[] Material, bool Stock, decimal? Min, decimal? Max, string? Sort, int Shown)
{
    public string[] GroupValues => [.. Group.Select(g => g.ToString())];
}

public sealed record FacetValue(string Value, int Count, bool Selected);
public sealed record Facets(List<FacetValue> Group, List<FacetValue> Color, List<FacetValue> Material, int InStock, decimal PriceMin, decimal PriceMax);

/// <summary>A live product range: the filtered, sorted products and the facets counted over all of them.</summary>
/// <param name="DefaultSort">"relevance" is offered only where it is the default: a search with usable terms.</param>
public sealed record ProductRange(List<Product> Items, Facets Facets, Selection Selection, string DefaultSort);
