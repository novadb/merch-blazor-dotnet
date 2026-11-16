using Merch.NovaDB;
using Merch.Site;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Merch.Components;

/// <summary>
/// Renders a Visual Document (the page content) and the teasers it embeds; <see cref="AfterFirst"/> (the live range)
/// goes right after its first component, the hero. Written in C#, not Razor markup: the CMS names each node's element
/// (<c>div</c>, <c>section</c>, <c>h2</c>, …), and markup cannot write an element whose name is a variable.
/// </summary>
public sealed class Content : ComponentBase
{
    [Parameter] public VNode? Node { get; set; }
    [Parameter, EditorRequired] public ContentContext Ctx { get; set; } = null!;
    [Parameter] public RenderFragment? AfterFirst { get; set; }

    // The first picture of the document is on screen at once (the hero): lazy loading would delay the largest contentful
    // paint. Found by reference, as a record compares by value and a picture embedded twice must be eager only once.
    VNode? firstImage;

    protected override void OnParametersSet() => firstImage = Node is null ? null : FirstImage(Node);

    VNode? FirstImage(VNode node)
    {
        if (node is { Type: "image", ObjectId: { } id } && Ctx.Images.ContainsKey(id)) return node;
        return node.Children?.Select(FirstImage).FirstOrDefault(found => found is not null);
    }

    // A sequence number names a place in this code, not a node: the page is rendered once (static SSR), never diffed.
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        if (Node is null)
        {
            b.AddContent(0, AfterFirst);
            return;
        }
        // The root node (`visualComponentRoot`) comes from the CMS as `<div class="main">`: the `.main` of site.css.
        Open(b, Node);
        var children = Node.Children ?? [];
        for (var i = 0; i < children.Count; i++)
        {
            Render(b, children[i]);
            if (i == 0) b.AddContent(1, AfterFirst);
        }
        if (children.Count == 0) b.AddContent(2, AfterFirst);
        b.CloseElement();
    }

    // `sizes` of a content picture: half a page beside text; the chapter's text column when it stands straight in a
    // chapter, where site.css shows it wide.
    const string ContentSizes = "(min-width: 760px) 50vw, 100vw";
    const string ChapterSizes = "(min-width: 1280px) 1024px, (min-width: 760px) 78vw, 100vw";

    // A node is rendered by its type, element and class; the classes (`merch*`) are a closed set that the CMS defines, so
    // no code here knows them one by one. The node's `definition` is not read: the class names the component.
    void Render(RenderTreeBuilder b, VNode node, string sizes = ContentSizes)
    {
        switch (node.Type)
        {
            case "group":
                Open(b, node);
                // A chapter hands its own `sizes` to its children, and the picture's figure on to the picture; any other group starts again.
                var childSizes = node.Class switch { "merchChapter" => ChapterSizes, "imageWithCaption" => sizes, _ => ContentSizes };
                foreach (var child in node.Children ?? []) Render(b, child, childSizes);
                b.CloseElement();
                break;
            case "image":
                if (node.ObjectId is { } imageId && Ctx.Images.GetValueOrDefault(imageId) is { } src)
                    Image(b, node.Class, src, sizes, eager: ReferenceEquals(node, firstImage));
                break;
            case "object":
                // Like the CMS, which wraps what an object component renders in the component's own element: the
                // editor's stylesheet (built from site.css) then matches the same DOM as the site.
                if (node.ObjectId is { } id && Ctx.Products.GetValueOrDefault(id) is { } product)
                {
                    Open(b, node);
                    b.OpenComponent<ProductCard>(10);
                    b.AddComponentParameter(11, nameof(ProductCard.Product), product);
                    b.AddComponentParameter(12, nameof(ProductCard.Ctx), Ctx);
                    b.CloseComponent();
                    b.CloseElement();
                }
                else if (node.ObjectId is { } targetId && Ctx.Targets.GetValueOrDefault(targetId) is { } target)
                {
                    Open(b, node);
                    b.OpenElement(20, "a");
                    b.AddAttribute(21, "class", "page-teaser");
                    b.AddAttribute(22, "href", target.Url);
                    if (target.Image is { } teaser) Image(b, null, teaser, "(min-width: 760px) 33vw, 100vw");
                    b.OpenElement(23, "span");
                    b.AddAttribute(24, "class", "page-teaser__title");
                    b.AddContent(25, target.Title);
                    b.CloseElement();
                    b.CloseElement();
                    b.CloseElement();
                }
                break;
            default:
                // Rich text: the CMS's HTML, with its object links turned into site URLs.
                Open(b, node);
                b.AddMarkupContent(30, Ctx.RewriteLinks(node.Content ?? ""));
                b.CloseElement();
                break;
        }
    }

    /// <summary>The element a node names, with its class: the CMS decides both. A null attribute is left out.</summary>
    static void Open(RenderTreeBuilder b, VNode node)
    {
        b.OpenElement(40, string.IsNullOrEmpty(node.Element) ? "div" : node.Element);
        b.AddAttribute(41, "class", node.Class);
    }

    static void Image(RenderTreeBuilder b, string? cssClass, string src, string sizes, bool eager = false)
    {
        b.OpenElement(50, "img");
        b.AddAttribute(51, "class", cssClass);
        b.AddAttribute(52, "src", src);
        b.AddAttribute(53, "srcset", Images.SrcSet(src));
        b.AddAttribute(54, "sizes", sizes);
        b.AddAttribute(55, "alt", "");
        b.AddAttribute(56, "loading", eager ? "eager" : "lazy");
        b.AddAttribute(57, "fetchpriority", eager ? "high" : null);
        b.CloseElement();
    }
}
