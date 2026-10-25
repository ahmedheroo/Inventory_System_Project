# Backlog status

Tracks the task breakdown from `Project_managment.docx` (TASK-001 → TASK-118) and the eight-sprint
plan from `Project_managment_final.docx` against what is actually in the repository.

Legend: ✅ done · 🟡 partially done · ⬜ not started

## Sprint 1 — Foundation

| Task | State | Notes |
| ---- | ----- | ----- |
| TASK-001 Git repository, README, .gitignore, branch strategy | 🟡 | repository, README and a **repaired** `.gitignore` (it still contained merge-conflict markers) are in place; branch strategy + contribution guide are described in the README, not yet as files |
| TASK-002 Solution with Web / Product / Inventory / Alert / Reporting / **Identity** APIs | ✅ | `Identity.Api` was missing from the scaffold and has been added; all 15 projects build |
| TASK-003 Docker development environment (SQL Server, RabbitMQ) | ✅ | `docker-compose.yml` with health checks, persisted volumes and `.env.example`; **both containers are up** (RabbitMQ healthy, management UI on 15672; SQL Server 2022 on 1433) and **all five databases were migrated on it by the services' own initializers** — `ProductDb` (3 categories, 2 warehouses, 4 products) and `IdentityDb` (`manager`, `clerk`, `viewer`, `console`) with seed data, `InventoryDb` (2 migrations, incl. the outbox), `AlertDb` and `ReportingDb` — then read back with `sqlcmd`. Recipe: [development.md §6](development.md) |
| TASK-004 Shared contracts project (event DTOs + messaging contracts) | ✅ | `InventorySystem.BuildingBlocks.Contracts`: four stock events, `IntegrationEvent` base, `StockTransactionType`, exchange/queue topology |

**Deliverable — "all services compile and infrastructure starts":** met — all 15 projects compile with
no warnings, RabbitMQ and SQL Server both start from `docker compose up -d`, and the five databases are
created on real SQL Server by the services themselves (`Migrate`, not `EnsureCreated`), with the seed
data a fresh environment needs.

## Sprint 2 — Product + Warehouse

| Task | State | Notes |
| ---- | ----- | ----- |
| TASK-005 Product domain model | ✅ | `Products.Domain.Entities.Product` with SKU normalisation, invariants, activate/deactivate, `IsLowStock` |
| TASK-006 Category domain model | ✅ | `Category` with name uniqueness support |
| TASK-007 ProductDbContext | ✅ | three aggregates + EF configurations (`Products`, `Categories`, `Warehouses`) |
| TASK-008 EF Core migrations | ✅ | `InitialCreate`, generated against SQL Server via a design-time factory and validated with `migrations script` |
| TASK-009 Product repository / data access | ✅ | `ProductRepository`, `CategoryRepository`, `WarehouseRepository`, `UnitOfWork` |
| TASK-010 … TASK-014 create / update / retrieve / search / deactivate | ✅ | `ProductService` + `ProductsController`; search matches SKU or name, paging included |
| TASK-015 Product validation | ✅ | domain invariants + DataAnnotations + unique-SKU and unknown-category checks |
| TASK-016 Product service tests | ✅ | 22 domain unit tests + 19 integration tests |
| TASK-017 Warehouse entity | ✅ | `Warehouse` (code/name/address, activate/deactivate) — placed in the Product service, see [architecture.md](architecture.md) decision 1 |
| TASK-018 Warehouse CRUD API | ✅ | `/api/warehouses` |
| TASK-019 Warehouse validation | ✅ | unique code, required fields, length limits |
| TASK-020 Warehouse tests | ✅ | covered by domain + integration tests |

**Deliverable — "products and warehouses can be managed through APIs":** met, and verified through
real HTTP calls against a running instance (create, search, filter, page, update, deactivate,
404/400/409 error shapes).

## Sprint 3 — Inventory

| Task | State | Notes |
| ---- | ----- | ----- |
| TASK-021 Inventory entity | ✅ | `Stock.Domain.Entities.Inventory`: aggregate per (product, warehouse) with the quantity rules |
| TASK-022 InventoryTransaction entity | ✅ | append-only ledger; direction from `Type`, `SignedQuantity` normalises it |
| TASK-023 InventoryDbContext | ✅ | `Inventory` + `InventoryTransactions` with configurations and indexes |
| TASK-024 Database migrations | ✅ | `InitialCreate`, generated against SQL Server and validated with `migrations script` |
| TASK-025 current-stock endpoint | ✅ | `GET /api/inventory` (filter + paging) and `GET /api/inventory/{productId}` |
| TASK-026 receive-stock operation | ✅ | creates/accumulates the row, records `RECEIVE`, publishes `stock.received` |
| TASK-027 issue-stock operation | ✅ | refuses to exceed the available quantity, records `ISSUE`, publishes `stock.issued` |
| TASK-028 transfer operation | ✅ | one commit writes both stock rows and both ledger legs, publishes `stock.transferred` |
| TASK-029 adjustment operation | ✅ | absolute counted quantity, signed delta recorded, publishes `stock.adjusted` |
| TASK-030 inventory history | ✅ | `GET /api/inventory/history` with product/warehouse/type/date filters and paging |
| TASK-031 Prevent negative inventory | ✅ | enforced by the aggregate and covered by unit + integration + concurrency tests |
| TASK-032 Transaction validation | ✅ | quantity rules per transaction type, reference limits |
| TASK-033 inventory unit tests | ✅ | 33 tests in `tests/Inventory.Domain.Tests` |
| TASK-034 inventory integration tests | ✅ | `tests/Inventory.Api.Tests` (35 tests, including the optimistic-concurrency and outbox cases), plus 14 cross-service contract tests |

**Deliverable — "complete inventory operations work through APIs":** met. Verified against two
running services (Product on 5251, Inventory on 5102): receive, issue, refused over-issue, transfer
with both legs, adjustment, history ordering/filtering, and cross-service rejection of an unknown
product and warehouse. Events were published for all five successful operations and for none of the
rejected ones.

## Sprint 4 — Messaging + Alerts

### Epic 5 — RabbitMQ

| Task | State | Notes |
| ---- | ----- | ----- |
| TASK-035 Set up RabbitMQ | ✅ | `docker-compose.yml` (RabbitMQ 3.13 + management UI, health check, persistent volume); brought up and used for the live verification below |
| TASK-036 RabbitMQ connection abstraction | ✅ | `RabbitMqConnectionProvider` + `RabbitMqOptions` (host/port/credentials/vhost from the `RabbitMq` section, `Enabled` switch); one lazily opened, automatically recovering connection per service |
| TASK-037 Event publisher | ✅ | `RabbitMqEventPublisher`: durable `inventory.events` topic exchange, persistent messages, **mandatory routing** and **publisher confirms**, so a publish task only completes once the broker owns the message |
| TASK-038 Event consumer abstraction | ✅ | `IEventConsumer` / `RabbitMqEventConsumer`: declares the queue, its DLX and `<queue>.dead` on start, `prefetch 16`, manual ack after the handler, retry once then dead-letter, reconnect loop, unknown routing key dead-lettered |
| TASK-039 → TASK-042 the four stock events | ✅ | Contracts already existed; they now travel over the bus with `EventName` as the routing key, `EventRegistry` mapping routing key ↔ type on both sides and `EventSerializer` shared with the outbox |
| TASK-043 Test event publishing | ✅ | `InventoryOutboxTests` (commit, rollback, ordering, broker outage) + contract tests; verified live against the real broker |
| TASK-044 Test event consumption | ✅ | in-memory `TestEventConsumer` + Alert API tests; live checks for duplicate delivery, unregistered routing key and a malformed (poison) message |

**Beyond the task list — transactional outbox (closes architecture decision 10).** Stock operations
used to publish after the commit and could only log a failure. The event is now written to
`OutboxMessages` in the same `SaveChanges` as the stock change, and a background `OutboxDispatcher`
(5s poll, batch 50, exponential backoff 2s → 60s, no attempt limit) delivers it to RabbitMQ. The
HTTP request never touches the broker, a broker outage cannot fail a stock change, and an event is
only marked processed once the broker confirmed it (at-least-once, de-duplicated by consumers).

### Epic 6 — Alert Service

| Task | State | Notes |
| ---- | ----- | ----- |
| TASK-045 Alert entity | ✅ | `Alert` with the condition it observed, read state, lifecycle (ACTIVE/RESOLVED) and the source event id; invariants reject an alert above the minimum |
| TASK-046 AlertDbContext | ✅ | `Alerts` + `ProcessedEvents` (de-duplication ledger), configurations, indexes, `InitialCreate` migration |
| TASK-047 Alert repository | ✅ | `AlertRepository` (`FindActiveAsync`, filtered paged list), `ProcessedEventStore`, `UnitOfWork` |
| TASK-048 Consume inventory events | ✅ | `InventoryEventsConsumer` hosted service on `alert.inventory` with `stock.*`; each message is processed in its own DI scope |
| TASK-049 Low-stock detection | ✅ | `LowStockDetector.Detect` — one definition of `quantity <= minimum`, classifying `OUT_OF_STOCK` at zero |
| TASK-050 Create low-stock alerts | ✅ | `InventoryEventProcessor` projects an event onto one or two stock levels (a transfer) and raises, refreshes or resolves accordingly |
| TASK-051 Get-alerts API | ✅ | `GET /api/alerts` (product/warehouse/type/read-state/status filters, newest first, paged; active by default) and `GET /api/alerts/{id}` |
| TASK-052 Mark-as-read API | ✅ | `PUT /api/alerts/{id}/read`, idempotent (keeps the first `readAt`), and never re-opened unread by a refresh |
| TASK-053 Handle duplicate alerts | ✅ | two layers: `ProcessedEvents` ignores a redelivered event, and one active alert per (product, warehouse) is refreshed instead of duplicated |
| TASK-054 Alert Service tests | ✅ | 21 domain tests + 22 HTTP tests in `tests/Alert.Domain.Tests` and `tests/Alert.Api.Tests` |

**Deliverable — "Inventory change → RabbitMQ → Alert Service works":** met and verified live with
all three services running against the real broker (Product 5251, Inventory 5102, Alert 5212):

1. receive 20 (above the minimum of 10) → no alert; issue 12 (8 left) → `LOW_STOCK` alert, queue
drained to 0;
2. replenish to 16 → the alert is `RESOLVED`; over-issue of 100 is refused with `400` and produces no
event and no alert;
3. **broker stopped**, then issue 16 → the stock change commits, the dispatcher retries with
backoff (`attempt 1 … next attempt after …`), and no alert appears; **broker restarted** → the
pending event is delivered and the `OUT_OF_STOCK` alert appears: nothing was lost;
4. transfer 25 leaving the source at 5 → exactly one alert, for the depleted warehouse (the
destination, at 25, was not alerted);
5. a re-published event (same event id) → "already processed; ignoring the duplicate", no second
alert;
6. a malformed payload → retried once, then dead-lettered (`alert.inventory.dead`); an unregistered
routing key → dead-lettered immediately; the queue kept flowing afterwards (the next event refreshed
the alert to 4 left).

## Sprint 5 — Reporting

### Epic 7 — Reporting Service

| Task | State | Notes |
| ---- | ----- | ----- |
| TASK-055 Create reporting database | ✅ | `ReportingDb` (`ReportingDbContext`: `StockLevels`, `StockMovements`, `ProcessedEvents`); `InitialCreate` migration generated against SQL Server through `ReportingDbContextFactory` and validated with `migrations script`; SQLite fallback for local runs |
| TASK-056 Create reporting data models | ✅ | `StockLevelSnapshot` (one row per product/warehouse: current quantity, minimum, source event) and `StockMovement` (append-only ledger with `SignedQuantity`); the projection's read model, so the types live in the Application layer (see [architecture.md](architecture.md)) |
| TASK-057 Consume inventory events | ✅ | `InventoryEventsConsumer` hosted service on `reporting.inventory` with `stock.*`; `InventoryEventProjector` upserts each affected stock level and appends its movements, together with the event's de-duplication record in one transaction |
| TASK-058 Build current inventory report | ✅ | `GET /api/reports/inventory` — rows enriched with sku/name/warehouse/unit price, value column, totals over the filtered set, paged |
| TASK-059 Build low-stock report | ✅ | `GET /api/reports/low-stock` — the `quantity <= minimumStockLevel` rule, shortfall per row, `LOW_STOCK`/`OUT_OF_STOCK` classification, worst first |
| TASK-060 Build stock movement report | ✅ | `GET /api/reports/movement` — the ledger newest first (a transfer is two rows), signed quantities, `quantityAfter`, and a summary (received/issued/transferred/adjusted/net change) |
| TASK-061 Build inventory valuation report | ✅ | `GET /api/reports/valuation` — `quantity × unitPrice` per product/warehouse, most valuable first, with a per-warehouse roll-up |
| TASK-062 Add date filtering | ✅ | `from`/`to` on the movement report, both inclusive, applied to the event's own timestamp; a reversed range is rejected with `400` |
| TASK-063 Add warehouse filtering | ✅ | `warehouseId` on all four reports, plus `productId` on three, `categoryId` on valuation and `type`/`includeOutOfStock` where they apply |
| TASK-064 Write reporting tests | ✅ | 24 tests in `tests/Reporting.Api.Tests`: the projection (upsert, transfer legs, adjustment delta, duplicate event, consumer path) and the four endpoints (filters, paging, summaries, `400`, `503`, empty read model) |

**Deliverable — "Reporting API works":** met and verified live with Product, Inventory and Reporting
running against the real broker (Product 5251, Inventory 5102, Reporting 5312). Six real stock
operations (2 receives, 2 issues, a transfer and an adjustment) produced, on the report endpoints:

1. **inventory** — 3 stock levels, `totalQuantity` 12 and `totalValue` 74 (4 × 12.50 + 8 × 3.00 +
   0), with the low-stock and out-of-stock flags on the right rows and sku/warehouse names resolved
   from the Product service;
2. **low-stock** — the out-of-stock row first (shortfall 2), then the low row (shortfall 1), with
   `includeOutOfStock=false` and `warehouseId` narrowing it as documented;
3. **movement** — 7 rows newest first (receive, issue, transfer out + transfer in, adjustment),
   signed quantities and `quantityAfter`, `netChange` 12 matching the quantity the inventory report
   shows; `type`, `productId`, `warehouseId`, paging and an inclusive `from`/`to` window all filter
   the rows and the summary; a reversed range returns `400 application/problem+json`;
4. **valuation** — 50 / 24 / 0, totals 74, per-warehouse roll-up, `categoryId` filtering;
5. a seventh operation (receive 5) reached the read model about 4 seconds later through
   outbox → RabbitMQ → consumer, moving the row to 9 and updating every report;
6. with the Product service stopped, the reports answer `503` (not an empty report), and the broker
   shows `reporting.inventory` with 1 consumer and 0 messages after the run — everything was acked.

**Beyond the task list — report downloads.** Each of the four reports is also served as a file
(`GET /api/reports/{report}/export?format=csv|xlsx`): the same filters, **every** matching row rather
than one page, CSV as RFC 4180 UTF-8-with-BOM and XLSX as a real Open XML workbook built from
`System.IO.Compression` (no spreadsheet dependency). The totals travel with the data so a downloaded
report is complete on its own. 11 further tests in `tests/Reporting.Api.Tests` cover the shapes a
person notices — the file opens, the numbers are numbers, the rows are the filtered ones, a comma in
a product name survives, and an unknown format is a `400`. Details: [reports-api.md](reports-api.md).

## Sprint 6 — MVC Application (Epic 8)

`Inventory.Web` is the presentation layer: it owns no data, calls the four service APIs over HTTP and
publishes nothing. Its own clients are typed `HttpClient`s over hand-mirrored contract records, so a
change on either side of an API has to be made on both.

| Task | State | Notes |
| ---- | ----- | ----- |
| TASK-065 Create MVC project | ✅ | `src/Inventory.Web` — MVC + Razor, one typed client per service (`ICatalogApi`, `IInventoryApi`, `IAlertsApi`, `IReportingApi`), base URLs from `Services:*:BaseUrl`, no service assembly referenced |
| TASK-066 Create shared layout | ✅ | `Views/Shared/_Layout.cshtml` + `wwwroot/css/console.css`: a 232px rail, a page band (section, title, note, the page's own actions), one content column capped at 1440px; Bootstrap, jQuery and the template's privacy page were removed with it |
| TASK-067 Create navigation menu | ✅ | the rail groups the screens by job — Signals (alerts), Catalogue, Stock, Operations, Analysis — and marks the active one; the active key is `ViewData["Nav"]` |
| TASK-068 Create dashboard | ✅ | `GET /` — position board (one column per warehouse: units, value, its lowest rows as fill bars against their minimum), a readout of the totals, what needs attention with a `Receive` action per row, the last movements and the open alerts. Four reads in parallel: valuation, low-stock, movement, alerts |
| TASK-069 Create product list page | ✅ | `GET /products` — search, category, include-inactive, page size; every row links to its stock, its ledger and its edit page |
| TASK-070 Create product creation page | ✅ | `GET|POST /products/new` — the sku is chosen once; a duplicate sku comes back on the sku field, in the service's words |
| TASK-071 Create product edit page | ✅ | `GET|POST /products/{id}/edit` (+ `POST /products/{id}/sku`, `.../deactivate`, `.../activate`) — update, change sku, deactivate/reactivate; a refused update keeps the typed values |
| TASK-072 Create warehouse pages | ✅ | `GET /warehouses` (list, create, edit, activate/deactivate) and the category page beside it — warehouses are never deleted, because the ledger points at them |
| TASK-073 Create inventory page | ✅ | `GET /inventory` — product/warehouse filters, the minimum resolved from the catalogue, the state read by the same rule the alerts use; an operation lands here with the changed row highlighted |
| TASK-074 Create receive-stock page | ✅ | `GET|POST /inventory/receive` |
| TASK-075 Create issue-stock page | ✅ | `GET|POST /inventory/issue` — a refused over-issue re-renders the form with the service's message and everything that was typed |
| TASK-076 Create transfer page | ✅ | `GET|POST /inventory/transfer` — source and destination, rejected here as well as by the service when they are the same |
| TASK-077 Create inventory history page | ✅ | `GET /inventory/history` — product/warehouse/type/date filters, the end date widened to the end of its day so a day means the whole day |
| TASK-078 Create alerts page | ✅ | `GET /alerts` — status/acknowledged/type/product/warehouse filters, acknowledge one or a batch, and the acknowledgement returns to the view it was made from (a local path only) |
| TASK-079 Create reports page | ✅ | `/reports/inventory`, `/reports/low-stock`, `/reports/movement`, `/reports/valuation` — the report's filters, its totals above the table, its pager, and both downloads |

Tests: `tests/Inventory.Web.Tests` (52 tests) drives every controller against faked service clients
and asserts the things that would otherwise be noticed by a person: which call the console makes with
which filters, where an operation lands, that a refusal keeps what was typed, that a download carries
the filters and never the paging, that an acknowledgement cannot be redirected off-site, and that a
service failure becomes the page that names the service instead of an empty table.

The console's design brief, tokens, layout and the directions that were rejected are in
[web-ui.md](web-ui.md).

**Deliverable — "a user can use the system through the browser":** met, and verified in the browser
against the four services running locally (Product 5251, Inventory 5102, Alert 5212, Reporting 5312,
RabbitMQ up): created a warehouse, a category and a product; received 5 units (row highlighted,
below minimum), had a 9-unit issue refused with the service's message and the typed values kept;
counted 40 (adjustment), transferred 10 to another warehouse (both legs), saw them in the ledger,
acknowledged an alert, and confirmed the events had reached the read model — the low-stock alert
appeared on the alerts page and in the reports, and the CSV/XLSX downloads carried the same rows and
the filters in force.

## Sprint 7 — Real-time + Authentication

The sprint plan lists SignalR, real-time inventory, login, roles and authorization. The real-time
half is implemented; authentication is not, and it is the one part of the plan that is now the
project's largest gap (see the limitations below).

| Scope | State | Notes |
| ----- | ----- | ----- |
| SignalR hub in the Inventory service | ✅ | `StockHub` at `/hubs/stock` (`IStockClient.StockChanged`), a broadcast of every **committed** stock change. `InventoryService` notifies through `IStockBroadcaster` after `SaveChanges`; `SignalRStockBroadcaster` logs and drops rather than failing an operation that has already answered. A refused operation broadcasts nothing, and a transfer is one notification carrying both legs. |
| Real-time inventory on the console | ✅ | The overview and `/inventory` are live regions: on a notification the browser re-reads `/live/overview` or `/live/levels` — the same server-rendered partials the pages are built from — and the row that moved is pointed at. The stock page sends the query string it is showing, so a refresh keeps the reader's filters. No JavaScript is required for a page to work. |
| Live alert count in the rail | ✅ | A `AttentionBadge` view component on every screen, refreshed from `/live/attention` when stock moves, on a 20-second interval for alert state that changes without a movement, and when a background tab returns. It reads the Alert service's total (not the page it fetched), follows the worst open alert for its tone, and says `Alerts not read` rather than showing a zero when the service is down. |
| A movement's own terminal recognises it | ✅ | The console mints an opaque station per browser (`inventory.station` cookie, `StationIdMiddleware`), sends it as `X-Origin-Id` on the stock operations that browser performs (`StationHeaderHandler`), and the Inventory service sanitises it and echoes it on the notification. A screen then says `· your movement of 6 units is confirmed` for its own work and `· another terminal moved 5 units` for somebody else's, with the row washed verdigris for its own movement and amber for another terminal's. A label, not a credential: nothing is authorised by it and no sign-in exists yet. |
| Login | ⬜ | `Identity.Api` is still a scaffold: no users table, no token endpoint, no sign-in screen, no session in the console. |
| Roles and authorization | ⬜ | Every page and every hub connection is anonymous. `Cors:AllowedOrigins` on the Inventory service is the only origin control there is, and it is empty outside `Development`. |

Tests: 14 further tests in `tests/Inventory.Api.Tests` (`StockBroadcastTests`: the four notifications,
nothing for a refused operation, one notification for a transfer, the hub's negotiate endpoint, and
what the service does with an origin — kept, trimmed, truncated, dropped when unusable) and 19 in
`tests/Inventory.Web.Tests` (9 in `LiveUpdatesTests` and 10 in `StationTests`: the fragments return the page's own
regions under the page's own filters, the rail count is the service's total and admits when it could
not be read, the hub URL comes from configuration, the pages, layout and client agree on every live
region, a browser keeps the station it was given and a hand-edited cookie is replaced, and the
attribution header goes on writes and only when there is a station to name).

**Deliverable — "two browser windows: one changes inventory, the other updates automatically":**
met, and verified in the browser with the four services and RabbitMQ running (Product 5251,
Inventory 5102, Alert 5212, Reporting 5312, console 5159):

1. window A on `/inventory`, window B on `/inventory/receive`; B received 6 units of `LIVE-WIDGET`
   into `LIVE-WH` and landed on the row it changed (`?focus=1`, flash *“Received 6 of Live Widget
   into Live Warehouse. Live Warehouse now holds 30.”*); **window A moved from 24 to 30 on its own**,
   highlighted the row, re-read its state (*above minimum*) and stamped the update;
2. a receive posted straight to the Inventory API (no console involved) moved `/inventory` the same
   way, and the rail's count fell from 3 to 2 active alerts as the Alert service consumed the event
   and resolved the alert — the count corrected itself without a reload, including its second pass
   ~6s later when the Reporting read model had caught up;
3. an issue that took the same product below its minimum raised the rail from 2 to 3 and put the row
   back under *needs attention* on the overview;
4. the browser log shows a WebSocket to `ws://localhost:5102/hubs/stock` and every `/live/*` fetch
   answering `200`; the rail reads *Live updates on*, and a hidden tab defers its refreshes until it
   is looked at again;
5. **who moved it**: the same movement performed through the console read back in the other window as
   `Updated 19:51:07 UTC · your movement of 6 units is confirmed` with the row washed verdigris
   (`is-mine`), a movement posted with `X-Origin-Id: dock-terminal-2` read back as
   `· another terminal moved 5 units` with the amber wash (`is-changed`), and a movement from a caller
   that named nobody read back as a plain `Updated 19:52:58 UTC`. The receipt stayed on screen through
   eleven later region reads, instead of being erased by the next one.

**Limitation.** The live view is anonymous: anyone who can reach the hub can watch every stock
movement, and anyone who can reach the console can move stock. The hub also needs the console's
origin in `Cors:AllowedOrigins` outside `Development`, which means a deployment that puts the console
on another origin has to configure it (documented in [development.md](development.md) and
[inventory-api.md](inventory-api.md)).

## Sprint 8 (first half) — Containers

The sprint plan lists "integration/E2E suites, Dockerfiles, `docker compose up` for the whole system".
The infrastructure half existed; the apps had none, so the system was still six terminals and a
`dotnet run` each.

| Scope | State | Notes |
| ----- | ----- | ----- |
| One Dockerfile for the six apps | ✅ | [Dockerfile](../Dockerfile) builds the five services and the console from the `PROJECT` build argument — the only thing that differs between them — rather than six near-identical files. The context is the repository root, because every app references projects outside its own folder, and the source copy and the solution-wide `dotnet restore` sit deliberately *above* the argument, so both layers are shared by all six images. Release publish, the image's unprivileged `app` user, `ASPNETCORE_HTTP_PORTS=8080`, and `curl` for the probe. |
| The six apps in `docker-compose.yml` | ✅ | One service per app, each publishing the host port a local `dotnet run` uses (5251, 5102, 5212, 5312, 5402, 5159). Inside the network nothing is `localhost`: the console, the Inventory service and the Reporting service are given `http://product-api:8080` and the like, every app is given `Database__Provider=SqlServer` and its connection string *above* the SQLite fallback it would otherwise select, and Inventory, Alert and Reporting are given the broker. |
| Health checks and start order | ✅ | `/health` is the probe for the five services (200 only when the service is up *and* its database answers); the console has no `/health` — it is a page, not a probe — so its probe is `/sign-in`. `depends_on` reads them: the console waits for Identity to be *healthy*, because nothing can be done without signing in, and only for the other four to be *started*, because a screen whose service is down says so rather than showing an empty table. |
| `.env.example`, README and development.md | ✅ | The four knobs the stack reads (`MSSQL_SA_PASSWORD`, `RABBITMQ_*`, `ASPNETCORE_ENVIRONMENT`, `JWT_SIGNING_KEY`), the port table, the two workflows (everything in containers, or the code on the host against the same two containers) and what a production run has to supply instead: [development.md §4](development.md#4-containers). |

**Deliverable — "`docker compose up` brings the whole system up":** met, and verified on Linux
containers (Docker 28, Desktop 4.40) against the SQL Server container:

1. `docker compose build` built all six images over one shared restore layer, and `docker compose up -d`
   started nine containers with SQL Server and RabbitMQ left as they were; `docker compose ps` reports
   healthy for all nine, and every app answers on its documented port from the host — `/health` 200 for
   the five services, `/sign-in` 200 for the console;
2. each app logged its own migration (`ProductDb is ready.`, `IdentityDb is ready.`, …) and the console
   logged `Following the live stock view at http://inventory-api:8080/hubs/stock` after renewing its own
   token against the Identity service; Alert and Reporting logged `Consuming 'alert.inventory' …` and
   `Consuming 'reporting.inventory' …` at `rabbitmq:5672`;
3. signing in as `manager`, `clerk` and `viewer` through the console on 5159 landed on the overview with
   the right name and role in the rail. The viewer was refused `/inventory/receive` (denied page) and a
   POST carrying the session's antiforgery token was refused the same way — no row was written: the
   ledger still held its six rows, none of them the attempted quantity, and the two container-era
   receives below are the only rows after 23:00 UTC;
4. two receives of SKU-1003 into WH-CAI, 3 units then 4 (posted as a second station, in another
   window's terms), wrote `InventoryTransactions` rows 7 and 8, drained the outbox
   (`pending_outbox = 0`), **resolved the out-of-stock alert** in `AlertDb` (resolved 23:02:38) and
   appended both movements to the Reporting read model — all read back with `sqlcmd` inside the SQL
   Server container;
5. the live view reached the browser: a SignalR client added in the page's own context received
   `StockChanged` (change `RECEIVE`, product 5, warehouse 1, 4 units, origin `2cc359d6`) from the
   console's hub, and the console's own live fragments then read back `14 units on hand`,
   `0 out of stock` and `No active alerts` — the movement and the resolved alert, without a reload.
   (The preview tab reports itself hidden, so the page's own scan of `document.hidden` deferred its
   refresh, exactly as designed for a background tab; the message and the refreshed regions were read
   directly.)
6. the sign-out defect fixed in Sprint 7 is in the images: the rail's form renders
   `action="/sign-out"`, that POST signs out (302 → `/sign-in`), and `/` then redirects to the sign-in
   page;
7. the production knobs behave as documented: Identity **refuses to start** in `Production` on the
   published development key (`Jwt:SigningKey is still the development key…`), starts when given a
   48-character one, and seeds the four accounts when `Identity__Seed__*` is set;
8. first run against an empty database — the path a fresh clone takes: each of the five services,
   pointed at a database that did not exist yet, applied its own migrations
   (`Applying ProductDb migrations (SqlServer)…`, and the initial migration of each of the other four),
   answered `/health` 200 afterwards, and seeded what it owns (`ProductDb`: 3 categories, 2 warehouses,
   4 products; `IdentityDb`: `manager`, `clerk`, `viewer`, `console`). The throwaway databases used for
   this and for step 7 were dropped afterwards.

**Limitation.** The stack is HTTP end to end, and the console's data-protection keys live inside its own
container next to its session cookie, so *recreating* that container (which is what a rebuild does)
signs every browser out — restarting it does not. A deployment behind TLS, with only the console
published and a key ring mounted into it, is the follow-on.

## Not started

| Sprint | Scope | Next step |
| ------ | ----- | --------- |
| 7 — Authentication half | login, roles, authorization on the pages and on the hub | `Identity.Api` (users, roles, JWT), a sign-in screen in the console, `[Authorize]` on the screens and a token on the hub connection; the rail and the page band are where an unauthenticated visitor is turned away |
| 8 — Testing + Docker + docs | Integration/E2E suites, Dockerfiles, `docker compose up` for the whole system | the containers are done (above); what remains is the end-to-end suite and the documentation sweep for Sprints 7 and 8 |

## Suggested immediate next steps

1. ✅ Done — the SQL Server container is up and every database is migrated and seeded on it (see
   TASK-003 above and [development.md §6](development.md#6-working-with-migrations)). What remains is
   the walkthrough: run the five services plus the console against SQL Server rather than the SQLite
   fallback, so the SQL the services emit at runtime — including the grouped aggregates behind the
   movement summary — is exercised on the provider production uses.
2. Sprint 7's remaining half: authentication and roles (`Identity.Api`, a sign-in screen, `[Authorize]`
   on the screens, and a token on the hub connection — a live view of the stock is exactly the sort of
   thing that should not be readable by anyone who reaches the port).
3. Add a `develop` branch and PR template to complete TASK-001.

