// Nova Merchandising — the client script of the demos without client components of their own, shared unchanged by them.
// The site works without it; it adds: filters that apply in place, more cards while scrolling, the search typeahead, the Shopify cart.

const text = JSON.parse(document.body.dataset.text || "{}");

// ---- forms that apply on change (facets) ----------------------------------------------------
// Without JS such a form posts to "#range" and the browser loads the answer as a new page. With JS the same POST is
// fetched and only the form's section is replaced: no page load, no picture loaded twice, nothing flickers or jumps
// (the server keeps the list of filter values stable, so the control that was used stays where it is).
const cards = new Map(); // every product card seen so far, by its HTML: a card that comes back is reused with its loaded picture
for (const card of document.querySelectorAll("section:has(form[data-autosubmit]) .product-card")) cards.set(card.outerHTML, card);
let pending = null; // the running update; a newer change aborts it
let native = false; // set when the answer is unusable: the browser then submits the form itself and shows what the server says

const selectorOf = (el) =>
  el.name ? `[name="${CSS.escape(el.name)}"]${el.type === "checkbox" ? `[value="${CSS.escape(el.value)}"]` : ""}` : "button:not([name])";
const inView = (el) => el.getBoundingClientRect().bottom > 0 && el.getBoundingClientRect().top < innerHeight;

document.addEventListener("change", (event) => {
  const form = event.target.form; // also set for the sort <select form="facets"> outside the form
  if (form?.matches("form[data-autosubmit]") && event.target.type !== "number" && form.reportValidity()) update(form);
});
document.addEventListener("submit", (event) => {
  if (native || !event.target.matches("form[data-autosubmit]")) return;
  event.preventDefault();
  if (event.submitter?.matches(".range__more")) more(event.target, event.submitter);
  else update(event.target, event.submitter);
});

/** Posts the form and replaces its section with the one in the answer. */
async function update(form, submitter) {
  const section = form.closest("section");
  const request = new AbortController();
  pending?.abort();
  pending = request;
  section.setAttribute("aria-busy", "true");
  let fresh = null;
  try {
    const res = await fetch(form.action, { method: "POST", body: new URLSearchParams(new FormData(form, submitter)), signal: request.signal });
    if (res.ok) fresh = new DOMParser().parseFromString(await res.text(), "text/html").getElementById(section.id);
  } catch (error) {
    if (error.name === "AbortError") return;
  }
  if (!fresh) {
    native = true;
    return form.requestSubmit(submitter);
  }
  const next = [...document.adoptNode(fresh).childNodes];
  const pairs = [...fresh.querySelectorAll(".product-card")].map((card) => {
    const html = card.outerHTML;
    if (!cards.has(html)) cards.set(html, card);
    return [card, cards.get(html)]; // the card in the answer and the one that will be shown for it
  });
  const previous = [...section.childNodes];
  const active = section.contains(document.activeElement) ? document.activeElement : null;
  const [focused, y] = [active && selectorOf(active), scrollY];
  // The scroll position stays. Only when it cannot (the page is now shorter than it) or shows no result any more
  // (they end above the window) the range is shown from its start.
  const place = () => {
    const from = scrollY;
    section.replaceChildren(...next);
    const start = parseFloat(getComputedStyle(section).scrollMarginTop); // where the range starts below the sticky header
    if (Math.abs(scrollY - from) > 1 || section.lastElementChild.getBoundingClientRect().bottom < start) section.scrollIntoView({ behavior: "instant" });
  };
  place();
  // Pictures that would now be on screen but are not loaded yet (lazy) would pop in: put the old content back (nothing
  // was painted in between), load them, then replace for good.
  const missing = pairs.filter(([card]) => inView(card)).map(([, shown]) => shown.querySelector("img")).filter((img) => img && !img.complete);
  if (missing.length) {
    section.replaceChildren(...previous);
    scrollTo({ top: y, behavior: "instant" });
    active?.focus({ preventScroll: true });
    for (const img of missing) img.loading = "eager";
    await Promise.race([Promise.allSettled(missing.map((img) => img.decode())), new Promise((done) => setTimeout(done, 1500))]);
    if (request.signal.aborted) return;
    place();
  }
  for (const [card, shown] of pairs) if (shown !== card) card.replaceWith(shown);
  if (focused) section.querySelector(focused)?.focus({ preventScroll: true });
  section.removeAttribute("aria-busy");
  watchMore();
}

// ---- range: more cards while scrolling -------------------------------------------------------
// The range renders the first batch of cards and a "Show more" button that posts the filters with `shown` raised by
// one batch. Without JS the browser loads that answer. With JS the button is clicked as soon as it comes near the
// window, the same POST is fetched and only the cards behind the ones already shown are appended.
const nearEnd = new IntersectionObserver((entries) => {
  for (const entry of entries) if (entry.isIntersecting && !native && !entry.target.dataset.loading) entry.target.click();
}, { rootMargin: "600px 0px" });

function watchMore() {
  nearEnd.disconnect();
  for (const button of document.querySelectorAll(".range__more")) nearEnd.observe(button);
}
watchMore();

/** Fetches the range with one batch more and appends the new cards; the button is replaced by the answer's (or removed). */
async function more(form, button) {
  const section = form.closest("section");
  if (button.dataset.loading || section.hasAttribute("aria-busy")) return; // a filter change is running: its answer comes first
  const grid = section.querySelector(".grid");
  const request = new AbortController();
  pending?.abort();
  pending = request;
  button.dataset.loading = "1";
  grid.setAttribute("aria-busy", "true");
  let fresh = null;
  try {
    const res = await fetch(form.action, { method: "POST", body: new URLSearchParams(new FormData(form, button)), signal: request.signal });
    if (res.ok) fresh = new DOMParser().parseFromString(await res.text(), "text/html").getElementById(section.id);
  } catch (error) {
    if (error.name === "AbortError") return; // a filter change took over; it replaces the whole section
  }
  const fetched = fresh && [...fresh.querySelectorAll(".grid > .product-card")].slice(grid.children.length);
  if (!fetched?.length) {
    native = true;
    return form.requestSubmit(button);
  }
  const added = fetched.map((card) => cards.get(card.outerHTML) ?? (cards.set(card.outerHTML, card), card)); // a card seen before comes back as the element with its picture loaded
  const keyboard = document.activeElement === button;
  grid.append(...added.map((card) => document.adoptNode(card)));
  const next = fresh.querySelector(".range__more");
  if (next) button.replaceWith(document.adoptNode(next));
  else button.remove();
  if (keyboard) added[0].querySelector("a")?.focus(); // a keyboard user goes on with the first new card
  grid.removeAttribute("aria-busy");
  watchMore();
}

// ---- typeahead: an ARIA combobox over /api/suggest -------------------------------------------
let suggestCount = 0;
for (const input of document.querySelectorAll("input[data-suggest]")) setupSuggest(input);

function setupSuggest(input) {
  const id = `suggest-${++suggestCount}`;
  const list = document.createElement("ul");
  Object.assign(list, { id, className: "suggest", hidden: true });
  list.setAttribute("role", "listbox");
  input.after(list);
  input.setAttribute("role", "combobox");
  input.setAttribute("aria-controls", id);
  input.setAttribute("aria-autocomplete", "list");
  input.setAttribute("aria-expanded", "false");

  let controller = null;
  let timer = 0;
  let active = -1;
  const options = () => [...list.querySelectorAll("[role=option]")];
  // An option leads to a product or page; the last one ("search all") has no address and submits the search form.
  const open = (option) => (option.dataset.href ? (location.href = option.dataset.href) : input.form.requestSubmit());

  const close = () => {
    list.hidden = true;
    active = -1;
    input.setAttribute("aria-expanded", "false");
    input.removeAttribute("aria-activedescendant");
  };
  const highlight = (index) => {
    const all = options();
    active = (index + all.length) % all.length;
    all.forEach((el, i) => el.setAttribute("aria-selected", String(i === active)));
    input.setAttribute("aria-activedescendant", all[active].id);
    all[active].scrollIntoView({ block: "nearest" });
  };

  async function load(q) {
    controller?.abort();
    controller = new AbortController();
    try {
      const body = new URLSearchParams({ lang: document.body.dataset.lang, q }); // POST: no URL carries a query string
      const res = await fetch(input.dataset.suggest, { method: "POST", body, signal: controller.signal });
      render(q, await res.json());
    } catch (error) {
      if (error.name !== "AbortError") close();
    }
  }

  function render(q, data) {
    list.replaceChildren();
    let n = 0;
    const option = (href, ...children) => {
      const li = document.createElement("li");
      Object.assign(li, { id: `${id}-${n++}`, className: "suggest__option" });
      li.setAttribute("role", "option");
      if (href) li.dataset.href = href;
      li.append(...children.filter(Boolean)); // a product without a picture has no <img>
      li.addEventListener("mousedown", (event) => {
        event.preventDefault();
        open(li);
      });
      list.append(li);
    };
    const span = (className, value) => Object.assign(document.createElement("span"), { className, textContent: value });
    const heading = (value) => {
      const li = Object.assign(document.createElement("li"), { className: "suggest__heading", textContent: value });
      li.setAttribute("role", "presentation");
      list.append(li);
    };
    if (data.products.length) heading(text.products);
    for (const p of data.products) {
      const img = p.image && Object.assign(document.createElement("img"), { src: p.image, alt: "", width: 40, height: 40 });
      option(p.url, img, span("suggest__name", p.name), span("suggest__price", p.price));
    }
    if (data.pages.length) heading(text.pages);
    for (const page of data.pages) option(page.url, span("suggest__name", page.title));
    option(null, span("suggest__name suggest__all", `${text.searchAll} “${q}”`));
    active = -1;
    list.hidden = false;
    input.setAttribute("aria-expanded", "true");
  }

  input.addEventListener("input", () => {
    clearTimeout(timer);
    const q = input.value.trim();
    if (q.length < 2) {
      controller?.abort();
      return close();
    }
    timer = setTimeout(() => load(q), 150);
  });
  input.addEventListener("keydown", (event) => {
    if (list.hidden) return;
    if (event.key === "ArrowDown") highlight(active + 1);
    else if (event.key === "ArrowUp") highlight(active - 1);
    else if (event.key === "Escape") close();
    else if (event.key === "Enter" && active >= 0) open(options()[active]);
    else return;
    event.preventDefault();
  });
  input.addEventListener("blur", () => setTimeout(close, 100));
}

// ---- Shopify Buy Button: product buttons and Shopify's own cart ------------------------------------
// The cart (a floating toggle and a drawer) is Shopify's, configured in the shop where possible: product titles
// (translations), the markets GB and US with fixed prices. Handed in: the site's colours, and the region's country, so that
// the cart prices in the region's market and currency. Also handed in: the label of the buy button and the cart's five
// visible texts, because the SDK's are English and no shop setting changes them (its accessibility labels stay English).
// Without the shop's domain and token (two meta tags, from the server's environment) there are no buttons and no cart.
const shopDomain = document.querySelector('meta[name="shopify-domain"]')?.content;
const shopToken = document.querySelector('meta[name="shopify-token"]')?.content;
const buttons = document.querySelectorAll("[data-shopify-product]");
// First URL segment = region; each region is a market of the shop. `moneyFormat` null: the shop's own (EUR).
const markets = { de: { country: "DE", moneyFormat: null }, uk: { country: "GB", moneyFormat: "£{{amount}}" }, us: { country: "US", moneyFormat: "${{amount}}" } };
const market = markets[location.pathname.split("/")[1]];

if (shopDomain && shopToken) startShopify().catch((error) => console.warn("Shopify unavailable", error));

async function startShopify() {
  const ShopifyBuy = await loadScript("https://sdks.shopifycdn.com/buy-button/latest/buy-button-storefront.min.js").then(() => window.ShopifyBuy);
  const client = ShopifyBuy.buildClient({ domain: shopDomain, storefrontAccessToken: shopToken, language: document.body.dataset.lang === "de" ? "de" : "en" });
  if (market) priceInMarket(client, market.country);
  const ui = await ShopifyBuy.UI.onReady(client);

  // The cart formats every amount with one `moneyFormat` (SDK default "${{amount}}"), whatever the cart's currency: the region's, else the shop's.
  const moneyFormat = market?.moneyFormat ?? (await client.shop.fetchInfo()).moneyFormat;

  // Shopify draws everything in iframes: the site's colours (light or dark theme) go in as styles.
  const css = getComputedStyle(document.documentElement);
  const [accent, accentInk, surface, ink, line] = ["--accent", "--accent-ink", "--surface", "--ink", "--line"].map((name) => css.getPropertyValue(name).trim());
  const accented = { "background-color": accent, color: accentInk };
  const button = { ...accented, "border-radius": "999px", ":hover": accented, ":focus": accented };
  const panel = { "background-color": surface, color: ink };
  const inks = (...parts) => Object.fromEntries(parts.map((part) => [part, { color: ink }]));
  const control = { color: ink, "background-color": surface, "border-color": line };

  await ui.createComponent("cart", {
    moneyFormat,
    options: {
      cart: { startOpen: false, text: { title: text.cart, total: text.subtotal, button: text.checkout, empty: text.cartEmpty, notice: text.cartNotice }, styles: { cart: panel, header: panel, footer: panel, ...inks("title", "close", "subtotalText", "subtotal", "notice", "empty"), button } },
      lineItem: { styles: { lineItem: { "border-color": line }, ...inks("itemTitle", "variantTitle", "price", "fullPrice"), quantityInput: control, quantityButton: control } },
      toggle: { styles: { toggle: button, iconPath: { fill: accentInk }, count: { color: accentInk } } },
    },
  });

  for (const node of buttons) {
    ui.createComponent("product", {
      id: node.dataset.shopifyProduct,
      node,
      options: {
        product: {
          contents: { img: false, imgWithCarousel: false, title: false, variantTitle: false, price: false, description: false, quantity: false, buttonWithQuantity: false },
          text: { button: text.addToCart },
          styles: { product: { "max-width": "none", "margin": "0", "text-align": "left" }, button: { ...button, padding: "14px 28px" } },
        },
      },
    });
  }
}

// A cart prices in the market of its buyer's country. A new cart gets the country of the region; a cart that was started in
// another region (there is one cart per browser) is moved to it before the cart shows it.
function priceInMarket(client, countryCode) {
  const create = client.checkout.create.bind(client.checkout);
  client.checkout.create = (input = {}) => create({ ...input, buyerIdentity: { ...input.buyerIdentity, countryCode } });
  const fetchCart = client.checkout.fetch.bind(client.checkout);
  client.checkout.fetch = async (id) => {
    const cart = await fetchCart(id);
    if (!cart || cart.buyerIdentity?.countryCode === countryCode) return cart;
    const moved = await fetch(`https://${shopDomain}/api/2026-07/graphql.json`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Shopify-Storefront-Access-Token": shopToken },
      body: JSON.stringify({
        query: "mutation($cartId: ID!, $buyerIdentity: CartBuyerIdentityInput!) { cartBuyerIdentityUpdate(cartId: $cartId, buyerIdentity: $buyerIdentity) { userErrors { message } } }",
        variables: { cartId: id, buyerIdentity: { countryCode } },
      }),
    }).then((res) => res.json());
    if (moved.errors || moved.data.cartBuyerIdentityUpdate.userErrors.length) console.warn("Shopify: the cart stays in its old market", moved);
    return fetchCart(id);
  };
}

function loadScript(src) {
  return new Promise((resolve, reject) => {
    const script = Object.assign(document.createElement("script"), { src, async: true, onload: resolve, onerror: reject });
    document.head.append(script);
  });
}
