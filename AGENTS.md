# AGENTS.md

For coding agents; `CLAUDE.md` only imports it. A line stays only if an agent would decide wrongly without it.

## Project

"Nova Merchandising": a demo website that shows NovaDB as CMS and PIM in one. Every page, product, price, picture and UI text is read live from NovaDB's Delivery API. Stack: ASP.NET Core 10 Blazor with Razor Components only, rendered on the server (static SSR) with enhanced navigation (`blazor.web.js`), C#; no interactive render mode, no circuit, no WebAssembly, no client framework, no NovaDB package (a plain `HttpClient`). Browser behaviour: enhanced navigation for the range's form, `wwwroot/ui.js`, `wwwroot/shop.js`.

The idea of this demo is the Razor component model: typed parameters, a component written in C# (`Content.cs`), `PageTitle`/`HeadContent`, a layout over a scoped `RegionContext`, a component rendered as a fragment (`SuggestionList`). Not shown on purpose: interactive render modes; a shop front only reads, and a circuit per visitor would cost more than it gives.

Agencies and developers read this code to learn how the stack works with NovaDB, then change it for their project. Measure every change by it: easy to read (a short path from URL to HTML, no layer of our own, fewer lines); the NovaDB code in the open, one place per concern; everything else (shop, pictures, SEO, deployment) in the smallest form that is right in concept; modern, idiomatic ASP.NET Core and C#; a complete site on the live API.

The same site exists in other stacks and must look and behave identically: HTML structure, CSS classes, label keys, page URLs and the shared files in `wwwroot/` are a shared contract, checked by the shared Playwright suite. Change none of them here alone. How a request travels (GET, query string, fragment endpoints) is this stack's own.

Non-goals: client-side rendering, interactive render modes, JSON to the browser, writes to NovaDB, a database or an external cache, a cart of our own.

## Commands and gates

- Run: `dotnet run` (http://localhost:5000). It needs the Delivery login (`NOVA_DELIVERY_URL`, `NOVA_DELIVERY_USER`, `NOVA_DELIVERY_PASSWORD`) in its environment or `dotnet user-secrets`, else it does not start.
- `dotnet build` (warnings are errors), `docker build .`
- Local container (`localhost:4004`, podman): `uv run scripts/refresh-container.py` after every completed change; `--full` after a change to `Merch.csproj` or `Dockerfile`.
- Tests: the shared Playwright suite (outside this repository, one project per demo), from its folder: `BASE_URL=http://localhost:4004 npx playwright test --project blazor-dotnet`. The tests tagged `@shopify` need the Shopify variables.
- Done means: build and `docker build` pass and the suite is green against the real Delivery API; report each gate with its output.
- The suite asserts structure and data rules, not CMS copy: a missing seed path is CMS data, not a defect.
- Scripts live in `scripts/`, which `Merch.csproj` excludes from its build. Python through `uv run` with a PEP 723 block. A script that needs this project's classes (`#:project ../Merch.csproj`), or that C# plainly does better, is a C# file-based app: `dotnet run --file scripts/name.cs -- args`, never `dotnet run name.cs` (that runs the project); `#:package` needs a version.

## NovaDB

Before touching `NovaDB/`, `Site/` or `Components/Content.cs` read `NOVADB.md`: the model and the Delivery API's concepts (identifiers, `lang`, `props`, variants, references, Visual Document, files, search). The wire protocol is in the product documentation it links. `README.md` lists which file does what.

- Read-only, branch `merchDemo`.
- Only products and articles in the workflow state "released" (`Schema.WorkflowReleased`) are on the site, never an unfinished one. A product is the card, URL and page; SKU, colour, stock and delivery time come from its articles.
- Every NovaDB name (branch, types, attributes, market variants) is an API identifier and stands only in `NovaDB/Schema.cs`, as a `const` the `props` records use in `[JsonPropertyName]`. An unavoidable numeric ID goes there too, as a named constant.
- A missing `props` key means unset or untranslated: it is `null`, omit it. Never fall back to another language or to `displayName`.
- Never print or log `NOVA_DELIVERY_URL/USER/PASSWORD`; the `HttpClient` logs stay at `Warning` (`appsettings.json`), they name the host. The login stays on the server: pictures go through the `/media` proxy.
- `NovaDB/Delivery.cs` caches responses for 60 s (`HybridCache`): after a NovaDB data change wait a minute or restart before judging output.
- A UI text is a `merchLabel` object in NovaDB: a new text is a new label there, read with `text["key"]`. The name of a product attribute is not a label: it is the name of the attribute's definition (`Labels.AttributeText`). A missing text prints `⟦key⟧` and fails the suite.

## Rules

- Fix the cause, not the symptom: no swallowed exception, fallback value or retry that hides a defect. Missing configuration or data fails loudly.
- Never weaken, skip or work around a test of the suite to get green.
- Change only what the task needs; list unrelated findings at the end of your answer (`file:line`).
- Ask before adding a package or touching `Dockerfile` or deployment.
- Comments say why the code is this way (a constraint of NovaDB, the framework, browser or contract) or what a name stands for, true for a reader who never saw an earlier version. A declaration other files use gets a doc comment when name and type leave something open; a file header or one comment for a group may cover several. Never what the code plainly does or how it changed. No TODO, no commented-out code; test names likewise.

## Where the code does not say why

- Static SSR with enhanced navigation: never add a render mode, `<base href>` (it would move the forms' `action="#range"`), `@formname`, `EditForm` or `[SupplyParameterFromForm]`. `blazor.web.js` serves the range's form (`data-enhance`, GET), which is fetched and patched into the page; `data-enhance-nav="false"` on `<body>` keeps links normal page loads, so every page starts its scripts afresh (Shopify). `Merch.csproj` sets `RequiresAspNetWebAssets`: the Dockerfile restores before it copies the components, and the image would lack the script.
- Filters, sorting, "Show more" and search are GET forms: their fields are the address. `RangeSection` reads them with `[SupplyParameterFromQuery]` as strings: Blazor throws for a number it cannot read (`Catalog.SelectionOf` drops what does not read). `ui.js` leaves `#range` out of the fetched address (Blazor would scroll there) and listens on `document`: the patch replaces what a script set up on an element, so the typeahead's empty list and ARIA attributes are in `SearchBox`.
- `RegionContext` (scoped) holds region, texts and page tree; layout and page both ask for it to load, the page sets `Alternates` and the layout renders again (`Changed`). The router has no custom constraints: a page answers an unknown region with `NavigationManager.NotFound()`.
- Razor Components drop whitespace between elements: where content flows inline (a price and its badge, a facet option), keep the elements on one line, as the shared HTML has it.
- The error page renders what `NavigationManager.NotFound()` raised. Redirects to the canonical URL are the rewriter's in `Program.cs` (Blazor redirects only with 302).
- `wwwroot/site.css`, `NOVADB.md` and the other shared files in `wwwroot/` (`favicon.svg`, `novadb.svg`, `github.svg`, `robots.txt`, the fonts) are kept identical from outside this repository: never adapt them. `site.css` is also the CMS editor's stylesheet. The shared client script `site.js` is not used here: `ui.js` and `shop.js` are this demo's own.
- Page content is made from a closed set of the CMS's Visual Components (classes `merch*`): compose, or follow the structure with CSS, before asking for a new one. `Content.cs` renders them alike from the elements and classes the CMS names: never a Razor Component per Visual Component.
- The shop is Shopify's own Buy Button and cart, started in `shop.js` from a product's `data-shopify-product`; its texts are `data-*` attributes of `<body>`. Without `SHOPIFY_DOMAIN` and `SHOPIFY_STOREFRONT_TOKEN` there are no buy buttons and no cart, silently: keep both in `.env`.

## Git

- Branch from a fresh `origin/main` as `feature/<id>-<slug>` or `bugfix/<id>-<slug>` (`<id>` is the number of the work item, the slug lower-case words). `main` changes only by pull request, completed as **squash merge** with the source branch deleted; direct and force pushes to `main` are rejected.
- **Title and description of the PR are the one public commit message.** Write them with the skill `pr-text` (title up to 60 characters, description optional, up to three lines), check them with `scripts/check-pr-text.py`, get the user's approval, and set the merge commit message explicitly to that text.
- The PR needs a review, a linked work item, resolved comments and a green build.
- Everything on `main` is public and mirrored read-only to GitHub (never push there): no addresses, accounts, tracker numbers or secrets in files or commit messages. No tags, no version numbers.
- Pushing, merging and tagging need explicit approval.

## Deployment

The site is one container (`Dockerfile`, port 3000 via `PORT`) with no state of its own: the media cache (`MEDIA_CACHE_DIR`) refills by itself. Where it is hosted is the operator's choice; it needs its environment (see NovaDB) from the platform's secrets, with `SITE_URL` set to the public origin. Never push images, deploy or change DNS without explicit approval.
