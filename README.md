# Nova Merchandising — ASP.NET Core Blazor, static server rendering

**Live:** <https://blazor-dotnet.merch.novadb.com/>: this repository's `main`, running against NovaDB. `/` picks the
region by browser language (`/de/`, `/uk/`, `/us/`); the checkout is in Shopify's test mode.

The same shop in other stacks: React Router ([live](https://react-router.merch.novadb.com/),
[source](https://github.com/novadb/merch-react-router)) · Razor Pages + htmx
([live](https://razor-htmx.merch.novadb.com/), [source](https://github.com/novadb/merch-razor-htmx)) · Nuxt
([live](https://vue-nuxt.merch.novadb.com/), [source](https://github.com/novadb/merch-vue-nuxt)) · Astro
([live](https://astro.merch.novadb.com/), [source](https://github.com/novadb/merch-astro))

The demo site of NovaDB as CMS and PIM in one, built with ASP.NET Core 10 Blazor and nothing but Razor Components,
rendered on the server only (static SSR): no circuit, no WebAssembly, no client framework. Every page, product, price,
picture and UI text is read live from NovaDB's Delivery API (branch `merchDemo`) with a plain `HttpClient`; there is no
NovaDB package in between.

## What this demo shows, and what it does not

It shows the **Razor component model** at work on a CMS and a PIM: components with typed parameters (`ProductCard`,
`RangeSection`, `Facet`), a component written in C# because the CMS names the elements (`Content.cs`), `PageTitle` and
`HeadContent` for the head, a layout over a scoped `RegionContext`, `[SupplyParameterFromQuery]` for the filters in the
address, Blazor's **enhanced navigation** for the range's form, and a component rendered as a fragment on its own
(`SuggestionList`, by `RazorComponentResult`).

It does not use an **interactive render mode** (Server, WebAssembly, Auto) on purpose. A shop front only reads: a circuit
per visitor on every page, the header's typeahead being everywhere, would cost more than it gives. The components are
written so that `@rendermode` could be added to one of them later.

Everything works without JavaScript: filters, sorting, "Show more" and search are GET forms, their fields are the address
(a filtered view can be shared, Back returns to the one before). With `blazor.web.js` the range's form is fetched and the page is
patched in place, cards that stay keep their element. Two small scripts: `wwwroot/ui.js` (the typeahead's keys, filters that
apply on change, more cards while scrolling, focus and scroll position) and `wwwroot/shop.js` (Shopify's own Buy Button and
cart in the site's colours). The server sends HTML only, never JSON.

## Run

```bash
dotnet run           # http://localhost:5000
```

It needs the Delivery API login of your NovaDB instance: `NOVA_DELIVERY_URL`, `NOVA_DELIVERY_USER` and
`NOVA_DELIVERY_PASSWORD`, from the environment or from `dotnet user-secrets set NOVA_DELIVERY_URL https://…` (the same
for the other two). Without them the site does not start. Optional: `DEMO_BRANCH` (default `merchDemo`),
`SHOPIFY_DOMAIN` and `SHOPIFY_STOREFRONT_TOKEN` (public Buy Button token; without them there is no cart), `SITE_URL`
(public origin for the absolute canonical and hreflang URLs; default: the ingress's `X-Forwarded-*` headers, which a
client can forge, so set it in production), `PORT`, `MEDIA_CACHE_DIR`.

Container: `uv run scripts/refresh-container.py --full` builds the `Dockerfile` and runs it at http://localhost:4004
with the variables from the environment or `.env` (see `.env.example`).

## Test

The site passes the shared Playwright behaviour suite of the Nova Merchandising demos (not part of this repository, one
project per demo): run it from its folder with `BASE_URL=http://localhost:4004 npx playwright test --project blazor-dotnet`.
The tests tagged `@shopify` (buy button, cart) need the Shopify variables.

## Where things are

| Path | What |
|---|---|
| `NOVADB.md` | how the site reads NovaDB: the model, the Delivery API's concepts, what the site decided |
| `NovaDB/Schema.cs` | every NovaDB name (branch, types, attributes, market variants) as API identifiers, and the `props` records: the one place to change |
| `NovaDB/Delivery.cs` | Delivery API client on `HttpClient` (login and branch as options, checked at start): paging, 60 s `HybridCache` |
| `Site/Regions.cs`, `Site/Labels.cs` | regions (`de`, `uk`, `us`: content language, market, currency) and the UI texts, read from NovaDB's labels |
| `Site/SiteTree.cs` | page tree, URLs from `merchPageUrlSegment` + parent page, breadcrumb, category page of a group |
| `Site/Catalog.cs` | the product and article types, full-text filter, facets (counted on the server), sorting, the selection read from the address; reading released products with their articles, claims and set rows |
| `Site/Images.cs`, `Site/Media.cs` | picture URLs by content address; the `/media` proxy with disk cache and scaled WebP variants (SkiaSharp) |
| `Site/Seo.cs` | canonical and hreflang URLs, a product's meta description and robots |
| `Program.cs` | services, middleware, `/` → region by `Accept-Language`, `/media/…`, `GET /api/suggest` |
| `Components/App.razor` | the root component: the router and the layout |
| `Components/Layout/MainLayout.razor` | document, header, region switch, footer |
| `Components/RegionContext.cs` | the region of a request with its texts and page tree, shared by layout and page |
| `Components/Pages/Show.razor` | `/{region}/…`: a page (content + live range) or a product |
| `Components/Pages/Search.razor`, `Error.razor` | search page; 404 and error page |
| `Components/ProductPage.razor` | a product's page |
| `Components/Content.cs` | Visual Document renderer and page teaser, in C# (`BuildRenderTree`): the CMS names each element |
| `Components/ContentContext.cs` | what a document refers to (embedded objects, links, pictures) by ID, and the rewriting of its CMS links to site URLs |
| `Components/RangeSection.razor`, `Facet.razor` | the live range with its facets; it reads its fields from the address |
| `Components/ProductCard.razor`, `Crumbs.razor` | product card; breadcrumb |
| `Components/SearchBox.razor`, `SuggestionList.razor` | search form with the typeahead; the typeahead's list, rendered by `/api/suggest` |
| `wwwroot/` | `site.css`, fonts, icons, `robots.txt`: the same files in every demo; `ui.js`; `shop.js`; `MapStaticAssets` serves CSS and JS under a fingerprinted name |

## Deviations

- No `site.js`: what it adds in other demos is Blazor's enhanced navigation and `wwwroot/ui.js` here, and `wwwroot/shop.js` is its Shopify part.
- The typeahead's list (`ul.suggest`) and ARIA attributes are in the HTML from the start instead of being added by a script: the patch of an enhanced
  navigation would remove what a script had added.
- The forms are GET forms: a filtered view has an address of its own, and "Show more" adds a history entry per batch.
- Scaled pictures (`/media/{md5}/{width}{ext}.webp`) are made with SkiaSharp: their bytes differ slightly from other demos' (resampling at
  high-contrast edges), and an animated GIF or WebP is scaled to its first frame.
