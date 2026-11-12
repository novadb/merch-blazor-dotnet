# Nova Merchandising — ASP.NET Core Blazor, static server rendering

The demo site of NovaDB as CMS and PIM in one, built with ASP.NET Core 10 Blazor and nothing but Razor Components,
rendered on the server only (static SSR): no circuit, no WebAssembly, no `blazor.web.js`, no client framework. Every page, product, price, picture and UI text is read live from NovaDB's Delivery API
(branch `merchDemo`) with a plain `HttpClient`; there is no NovaDB package in between.

Everything works without JavaScript; `wwwroot/site.js` only adds filters that apply on change and in place (the POST is
fetched, `section#range` replaced, no page load), the search typeahead and Shopify's own cart in the site's colours.

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

Container: `uv run scripts/refresh-container.py --full` builds the `Dockerfile` and runs it at http://localhost:4011
with the variables from the environment or `.env` (see `.env.example`).

## Test

The site passes the shared Playwright behaviour suite of the Nova Merchandising demos (Firefox): run it from its
checkout with `BASE_URL=http://localhost:4011 npx playwright test`. The tests tagged `@shopify` (buy button, cart) need
the Shopify variables.

## Where things are

| Path | What |
|---|---|
| `NOVADB.md` | how the site reads NovaDB: the model, the Delivery API's concepts, what the site decided |
| `NovaDB/Schema.cs` | every NovaDB name (branch, types, attributes, market variants) as API identifiers, and the `props` records: the one place to change |
| `NovaDB/Delivery.cs` | Delivery API client on `HttpClient` (Basic auth set in `Program.cs`): paging, 60 s cache |
| `Site/Regions.cs`, `Site/Labels.cs` | regions (`de`, `uk`, `us`: content language, market, currency) and the UI texts, read from NovaDB's labels |
| `Site/SiteTree.cs` | page tree, URLs from `merchPageUrlSegment` + parent page, breadcrumb, category page of a group |
| `Site/Catalog.cs` | products, full-text filter, facets (counted on the server), sorting |
| `Site/Images.cs`, `Site/Media.cs` | picture URLs by content address; the `/media` proxy with disk cache and scaled WebP variants (SkiaSharp) |
| `Site/Seo.cs` | canonical and hreflang URLs |
| `Program.cs` | services, middleware, `/` → region by `Accept-Language`, `/media/…`, `POST /api/suggest` |
| `Components/App.razor` | the root component: the router |
| `Components/Pages/Show.razor` | `/{region}/…`: a page (content + live range) or a product |
| `Components/Pages/Search.razor`, `Error.razor` | search page; 404 and error page |
| `Components/Pages/RegionPage.cs` | base of every page: region, texts, page tree, the posted form fields |
| `Components/Layout/Shell.razor` | document, header, region switch, footer; every page wraps itself in it |
| `Components/Content.cs` | Visual Document renderer and page teaser, in C# (`BuildRenderTree`): the CMS names each element |
| `Components/RangeSection.razor`, `Facet.razor` | the live range with its facets |
| `Components/ProductCard.razor`, `Crumbs.razor` | product card; breadcrumb |
| `wwwroot/` | `site.css`, `site.js`, fonts, icons, `robots.txt`: the same files in every demo; `MapStaticAssets` serves CSS and JS under a fingerprinted name |

## Deviations

- Scaled pictures (`/media/{width}/…`) are made with SkiaSharp: their bytes differ slightly from other demos' (resampling at
  high-contrast edges), and an animated GIF or WebP is scaled to its first frame.
