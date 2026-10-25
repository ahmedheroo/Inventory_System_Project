# The console: design system and pages

`Inventory.Web` is the presentation layer of the system (specification section 11). It owns no data:
every screen is built from the service APIs, and it never touches a service database.

This document is the design brief the screens were built against, so a future change can be judged
against the intent rather than against taste.

## 1. What this is, for whom

An internal **inventory control console** for the warehouse team of a distributor: a manager working
from an office screen and clerks working from a shared terminal on the dock. Primary job, in order:

1. know the stock position — what is short, what is dead, what the stock is worth;
2. act — receive, issue, transfer, adjust;
3. clear what needs attention — the low-stock alerts.

The subject's own materials are the design source: racking signage, pallet labels, tally sheets,
carbon-copy delivery notes, safety marking, the label printer. That world is *functional, numbered
and legible at a glance*, and the console borrows from it rather than from a marketing page.

## 2. Tokens

| Token | Value | Role |
| ----- | ----- | ---- |
| `--ink` | `#131A20` | text and the structural rail; graphite, not black |
| `--canvas` | `#E9EDF1` | the page behind the work surface |
| `--surface` | `#FFFFFF` | tables, forms, panels |
| `--rule` | `#C8D1DA` | hairlines between rows, sections and inputs |
| `--slate` | `#59636E` | secondary text: labels, metadata, help |
| `--amber` | `#B26A0C` | attention: low stock, the one accent |
| `--signal` | `#A32B21` | critical: out of stock, refused operations |
| `--verdigris` | `#1D6A63` | confirmed: stock healthy, operation succeeded |

Semantics: three states (attention / critical / confirmed) and no other colour. Status is never
carried by colour alone — every state also has its own word.

## 3. Type

One family for the interface, one for machine identifiers:

- **Interface** — `"Segoe UI Variable Text", "Segoe UI", "Helvetica Neue", Arial, sans-serif`, with
  `font-variant-numeric: tabular-nums` everywhere numbers are compared in columns. Figures that line
  up vertically are the point: this console is mostly read down a column.
- **Identifiers** — `"Cascadia Mono", Consolas, "SF Mono", monospace`, used only for sku, warehouse
  codes and ids: values a person copies into another system, where a wrong character matters.
- **Scale** — 12.5 (table metadata) / 13.5 (body) / 15 (section) / 18 (section heading) / 19
  (readout figures) / 22 (page title). Line height 1.45 for prose, 1.2 for headings; prose capped at
  76ch. These are the values the stylesheet uses; nothing outside them was introduced later.
- Headings share the interface family at 600–700 weight and −0.01em tracking: hierarchy comes from
  size, weight and space, not from a second typeface.

## 4. Layout

A fixed left rail (232px, `--ink`) carrying the sections of the system, a page band with the title on
the left and the page's filters/actions on the right, and the work surface below. One content column,
maximum 1440px, left aligned — the console is a document, not a poster.

```
┌───────────┬─────────────────────────────────────────────────────────────────┐
│ INVENTORY │  CATALOGUE                                                      │  band crumb
│  console  │  Stock levels                    [filters]  [operations]       │  page title
│           ├─────────────────────────────────────────────────────────────────┤
│ Overview  │  readout: value · units · levels · below minimum · out · alerts  │  one line
│ SIGNALS   │ ┌─────────────────────────────────────────────────────────────┐ │
│  Alerts   │ │ SKU      Product         Warehouse  On hand Min State  Act │ │  manifest
│ CATALOGUE │ │ SKU-1001 Wireless mouse  Cairo Main      9    5  above      │ │  table
│  Products │ │ SKU-1002 Mechanical kbd  Alexandria     0    2  out        │ │
│  Categori…│ └─────────────────────────────────────────────────────────────┘ │
│  Warehous…│  ── pager: rows 21–40 of 84 ──                                   │
│ STOCK     │                                                                 │
│  Levels   │                                                                 │
│  Ledger   │                                                                 │
│ OPERATIONS│                                                                 │
│  Receive  │                                                                 │
│  Issue    │                                                                 │
│  Transfer │                                                                 │
│  Adjust   │                                                                 │
│ ANALYSIS  │                                                                 │
│  Reports  │                                                                 │
└───────────┴─────────────────────────────────────────────────────────────────┘
```

The rail carries no counts: an alert badge would mean a service call on every page render for a
number nobody acts on from the rail, and the overview already answers "what needs me now" with the
open alerts themselves.

The **dashboard** opens with the *position board*: one column per warehouse, each showing that
warehouse's units and value and its lowest-stock rows as fill bars (quantity against the minimum).
It answers "where am I short?" before any total does — the characteristic view of the subject rather
than a row of interchangeable metric cards.

```
┌───────────────┬───────────────┬───────────────┐
│ Cairo Main    │ Alexandria    │ Port Said     │   warehouse column
│ 17 units      │ 0 units       │ 240 units     │
│ 136.50 value  │ 0.00          │ 1,204.00      │
│ ▁▁▁ Widget  9 │ ▁ Wireless  0 │ ▃▃▃ Pallet 40 │   fill bars: qty vs minimum
└───────────────┴───────────────┴───────────────┘
```

Forms are single-column with a persistent action bar; long lists (products, warehouses, alerts,
history) are filterable above the table and paged below it. Operations (receive, issue, transfer,
adjust) are their own pages so the quantity and reference the clerk types are not lost in a modal.

## 5. Principles

1. **Numbers are the interface.** Totals and quantities are set in tabular figures and right aligned;
   a table is scanned down a column, never read as prose.
2. **The table is the hero of every list page.** No cards wrapping cards, no icon per row, no
   decoration inside a cell: identifier, description, quantity, state.
3. **Status is a label, not a colour.** Each state carries a word (`short 2`, `out`, `healthy`) with a
   square marker; colour reinforces it, and a reader who cannot see colour loses nothing.
4. **Flat surfaces, hairline structure.** No drop shadows, no gradient washes, one border radius
   (2px, because a printed label has nearly square corners) for inputs and chips.
5. **Motion only answers an action.** A row highlights briefly after a successful operation, a
   message appears above the table after a redirect. Nothing animates on load; `prefers-reduced-motion`
   removes what remains.
6. **Copy is operational.** Sentence case, plain verbs, the same word for the same thing through the
   flow (`Receive stock` on the button produces `Received 5 units into Cairo Main`). Errors say what
   failed and what to do next, in the interface's voice, and empty states invite the next action.

## 6. Rejected directions

Recorded because the fastest way to end up with a generic screen is to take the first idea:

- a **KPI card grid** (four rounded cards, soft shadows, a gradient behind the big number) — reads as
  a template and hides the thing a warehouse manager needs, which is *per-warehouse* position;
- **cream + high-contrast serif + warm clay accent** — the fashionable editorial palette, unrelated
  to the subject;
- **near-black with one acid accent**, **broadsheet hairlines with zero radius**, **an all-caps
  eyebrow above every heading**, **pill badges with emoji icons** — all legitimate in some brief, none
  chosen here;
- **a modal for stock operations** — a clerk on a shared terminal loses a half-typed transfer when a
  modal closes; the operation gets a page.

## 7. Pages

| Route | Screen | Reads |
| ----- | ------ | ----- |
| `/` | Overview: position board, open alerts, recent movements, value by warehouse | Reporting, Alert |
| `/products` | Product list: search, category filter, include inactive, page size, paged | Product |
| `/products/new`, `/products/{id}/edit` | Product form: create; update, change sku, deactivate/reactivate (each posted to its own route) | Product |
| `/categories` | Category list with product counts and the create form; `?edit={id}` swaps the form to edit | Product |
| `/warehouses` | Warehouse list with the create form (`?edit={id}`, `?includeInactive=true`) | Product |
| `/inventory` | Stock levels: product/warehouse filters, paged; `?focus={id}` marks the row an operation changed | Inventory (+ Product for the minimum) |
| `/inventory/history` | Movement ledger: product/warehouse/type/date filters, paged | Inventory |
| `/inventory/receive`, `/issue`, `/transfer`, `/adjust` | The four stock operations, each ending on the row it changed | Inventory (+ Product for the pickers) |
| `/alerts` | Alerts: status/acknowledged/type/product/warehouse filters, acknowledge one or a batch | Alert |
| `/reports/inventory`, `/low-stock`, `/movement`, `/valuation` | The four reports with their filters, totals, pager and both downloads | Reporting |

A stock operation is a page, not a modal (see the rejected directions): the clerk's quantity and
reference survive, and the same form is re-rendered with the service's own words when the service
refuses the operation. Downloads are proxied through the console
(`/reports/{report}/export?format=csv|xlsx`) so the browser only ever talks to the web app and the
Reporting service stays an internal component — and so the file carries exactly the filters the
table on screen was built from.

## 8. Live updates

A screen that is open should not be wrong five minutes later, and nobody should have to press
refresh to find out that a shelf went empty. Two screens follow the Inventory service's hub — the
overview and the stock levels page — and the rail's alert count keeps itself current on every screen.

The design rules for it:

- **The browser re-reads the server's own fragments, it does not render data.** `/live/overview` and
  `/live/levels` return exactly the partials the pages are built from, so a live refresh and a reload
  cannot disagree about how a screen looks. The stock page sends the query string it is showing, so a
  refresh keeps the filters, the pager and the sort the person chose.
- **The rail count is a label, not a colour.** It reads `3 active alerts · including out of stock`,
  the tone follows the worst open alert, and when the Alert service cannot be read it says
  `Alerts not read` rather than showing a confident zero.
- **A stale screen says when it was last read.** The page band carries `Updated 12:04:31 UTC` after a
  refresh — and `Live update failed at 12:04:31 UTC — reload this page` when one fails, because a
  screen that silently stops moving is worse than one that admits it is stale.
- **A movement is attributed.** The console mints an opaque station id per browser (cookie
  `inventory.station`, eight hex characters, `HttpOnly`), sends it as `X-Origin-Id` with the stock
  operations that browser performs, and receives it back on the notification. So the page band says
  `Updated 12:04:31 UTC · your movement of 6 units is confirmed` for your own work and
  `Updated 12:04:31 UTC · another terminal moved 5 units` for somebody else's — and the two row
  highlights differ by meaning: the amber wash is a movement by another terminal (attention), the
  verdigris wash is your own movement confirmed (confirmed). It is a label, not a sign-in: two people
  sharing one browser share one station, and nothing is authorised by it.
- **Nothing animates on load.** The only motion a live update adds is the changed row. It answers
  "what changed here", which is the one thing the numbers alone cannot say, and the receipt in the
  page band says whose change it was. `prefers-reduced-motion` removes the motion like everything
  else; the words stay.
- **A tab nobody is looking at is not refreshed.** Movements that arrive while the tab is in the
  background are remembered and read when it comes back; the rail's count is also re-read on a slow
  20-second interval, which is how an acknowledgement made somewhere else shows up.
- **Everything is optional.** Without JavaScript, or with the hub unreachable, every page works as
  it is served; the rail says `Live updates unavailable — reload to try again` and the rest is
  unchanged. The one third-party file in the console is the SignalR browser client
  (`wwwroot/lib/signalr/signalr.min.js`, @microsoft/signalr 8.0.29, MIT — see the note beside it),
  vendored rather than loaded from a CDN so the console works offline and cannot change under us.

## 9. How the console behaves when something is missing

- **A service that cannot be read is never an empty table.** An `ApiException` becomes a page that
  names the service, quotes its problem detail and answers `503` (unreachable) or `502` (answered
  badly). The console only reads from the services and sends their operations, so nothing is
  half-written by a failed read — the page says so and offers to try again.
- **A refused *write* stays on the form.** A `400`/`409` from a service re-renders the page with the
  service's message in the summary and everything typed still in place.
- **Where a name comes from.** The Inventory, Alert and Reporting services store ids; the console
  resolves them against one catalogue read per request and writes `Product 42` rather than nothing
  when a name is unknown. The minimum stock level shown on the stock page comes from that same
  catalogue, and the state is read with the alert rule (`quantity <= minimum`), so the screen cannot
  disagree with the service that raises the alerts.
- **Timestamps are UTC**, shown as the services report them, and every table says so in its header.
  Date filters are days: the inclusive end date a person types is widened to the last tick of that
  day before the service is asked, so "to 2026-10-16" includes what happened at 16:00.
- **Numbers are formatted with the invariant culture** (and the request culture is pinned to it), so
  a quantity reads the same on every machine and a decimal typed as `12.50` cannot bind as `1250`.

## 10. Tests

`tests/Inventory.Web.Tests` drives every controller against faked service clients: which call the
console makes and with which filters, where an operation lands, that a refusal keeps what was typed,
that a download carries the filters and never the paging, that an acknowledgement cannot be
redirected off-site, and that a failing service produces the page above rather than an empty list.
The rendering itself was verified in the browser against the four running services.

The live view is covered the same way: the fragment endpoints must return exactly the region the page
renders, under the filters the page is showing; the rail's count must be the Alert service's total
rather than the length of the page it read, and must say when it could not be read; and the pages,
the layout and the client are read as text to check that every live region names a fragment that
exists. What is left to the browser is the part only a browser can show: a movement arriving and the
screen updating itself (see [development.md](development.md)).

Sprint 7 adds authentication and roles next; there is deliberately no Users or Login screen yet.
