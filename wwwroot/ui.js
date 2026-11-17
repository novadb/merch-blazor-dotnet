// What Blazor does not do by itself: the search typeahead, filters that apply on change, more cards while scrolling, and the
// focus and scroll position after the range changed. The pages are Razor components, rendered on the server; the range's
// form (`data-enhance`) is fetched by Blazor's enhanced navigation, which patches the answer into the page. Everything here
// listens on `document`, because that patch replaces what a script would have set up on an element.

// ---- Typeahead: an ARIA combobox over /api/suggest ---------------------------------------------------
const searchForm = (event) => event.target.closest?.("form.search");
let typing = 0;
let asking = null;

// Two characters or more, 150 ms after the last key: ask for the list (HTML, rendered by a Razor component) and put it in place of the old one.
document.addEventListener("input", (event) => {
  const input = event.target;
  if (!searchForm(event) || !input.dataset.suggest) return;
  clearTimeout(typing);
  asking?.abort();
  const q = input.value.trim();
  const show = (list) => input.setAttribute("aria-expanded", String(!list.hidden));
  if (q.length < 2) return show(Object.assign(document.getElementById(input.dataset.suggest), { hidden: true }));
  typing = setTimeout(async () => {
    asking = new AbortController();
    try {
      const url = `/api/suggest?${new URLSearchParams({ lang: document.body.dataset.lang, q, list: input.dataset.suggest })}`;
      const res = await fetch(url, { signal: asking.signal });
      document.getElementById(input.dataset.suggest).outerHTML = await res.text();
      show(document.getElementById(input.dataset.suggest));
    } catch (error) {
      if (error.name !== "AbortError") throw error;
    }
  }, 150);
});

// Arrow keys move the highlight through the options (round), Escape closes the list. Enter opens the highlighted
// product or page; on "search all" (no address) or with nothing highlighted Enter submits the search as usual.
document.addEventListener("keydown", (event) => {
  const form = searchForm(event);
  if (!form) return;
  const list = form.querySelector(".suggest"), input = form.querySelector("input"), options = [...list.querySelectorAll("[role=option]")];
  const active = options.findIndex((option) => option.getAttribute("aria-selected") === "true");
  if (list.hidden || !options.length) return;
  if (event.key === "Escape") { list.hidden = true; input.setAttribute("aria-expanded", "false"); }
  else if (event.key === "Enter" && options[active]?.dataset.href) location.href = options[active].dataset.href;
  else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
    const next = options.at((active + (event.key === "ArrowDown" ? 1 : -1)) % options.length);
    options.forEach((option) => option.setAttribute("aria-selected", String(option === next)));
    input.setAttribute("aria-activedescendant", next.id);
    next.scrollIntoView({ block: "nearest" });
  }
  else return;
  event.preventDefault();
});

// A click on an option: the product or page, or "search all", which submits the form. On mousedown, before the field loses focus.
document.addEventListener("mousedown", (event) => {
  const form = searchForm(event), option = event.target.closest?.("[role=option]");
  if (!form || !option) return;
  event.preventDefault();
  if (option.dataset.href) location.href = option.dataset.href; else form.requestSubmit();
});

document.addEventListener("focusout", (event) => {
  const form = searchForm(event);
  if (form) form.querySelector(".suggest").hidden = true;
});

// ---- Range: filters on change, more cards while scrolling ---------------------------------------------
const range = () => document.getElementById("range");

// A change of a facet or of the sort select (not a number field's keystrokes) submits the form. The select stands outside it (`form="facets"`).
document.addEventListener("change", (event) => {
  const form = event.target.form;
  if (form?.id === "facets" && event.target.type !== "number" && form.reportValidity()) form.requestSubmit();
});

// The form's action is `#range`, for the page a browser without script loads: it then lands on the range. Enhanced navigation would
// scroll there too, so the fragment is left out of the address it fetches. Blazor patches the attribute back with the answer.
document.addEventListener("submit", (event) => {
  if (event.target.id === "facets") event.target.action = location.pathname;
}, true);

let scrolled = 0; // the scroll position when an update started
let cardsBefore = null; // the number of cards while a keyboard click on "Show more" runs

window.Blazor?.addEventListener("enhancednavigationstart", () => {
  scrolled = scrollY;
  range()?.setAttribute("aria-busy", "true");
  const more = document.activeElement?.matches(".range__more");
  cardsBefore = more ? document.querySelectorAll(".grid .product-card").length : null;
});

window.Blazor?.addEventListener("enhancednavigationend", () => {
  const section = range();
  if (!section) return;
  section.removeAttribute("aria-busy");
  // A keyboard user goes on with the first new card: the button they pressed is gone or has moved on.
  if (cardsBefore !== null) section.querySelectorAll(".grid .product-card a")[cardsBefore]?.focus();
  cardsBefore = null;
  // The scroll position stays. Only when it cannot (the page got shorter than it) or shows no result any more (the
  // range ends above the window) the range is shown from its start, below the sticky header (its scroll-margin-top).
  const gone = section.lastElementChild.getBoundingClientRect().bottom < parseFloat(getComputedStyle(section).scrollMarginTop);
  if (Math.abs(scrollY - scrolled) > 1 || gone) section.scrollIntoView({ behavior: "instant" });
});

// "Show more" is clicked as soon as it comes near the window: the same GET is fetched and the new cards are patched in.
const nearEnd = new IntersectionObserver((entries) => {
  for (const entry of entries) if (entry.isIntersecting && !range()?.hasAttribute("aria-busy")) entry.target.click();
}, { rootMargin: "600px 0px" });

function watchMore() {
  nearEnd.disconnect();
  for (const button of document.querySelectorAll(".range__more")) nearEnd.observe(button);
}
watchMore();
window.Blazor?.addEventListener("enhancedload", watchMore);
