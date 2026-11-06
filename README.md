# Real-Time Inventory Management System

An educational microservices application that tracks inventory in real time, raises low-stock
alerts and produces inventory reports. Built with ASP.NET Core 8, Entity Framework Core,
SQL Server, RabbitMQ, SignalR and Docker.

> **Status:** Sprints 1–6 are implemented and tested (442 tests): the five services (Product,
> Inventory, Alert, Reporting, Identity) with RabbitMQ messaging and a transactional outbox, and the
> MVC console that a manager and a clerk actually work in — sign-in and roles, dashboard, catalogue,
> stock, the four stock operations, alerts and the four reports with CSV/Excel downloads. See
> [docs/backlog-status.md](docs/backlog-status.md) for the task-by-task state and what comes next,
> and [docs/web-ui.md](docs/web-ui.md) for how the console is put together.

## Architecture at a glance

```
        ┌────────────────────────┐
        │  Inventory.Web (MVC)   │   Razor server-rendered console, no JS framework
        └───────────┬────────────┘
                    │ HTTP / SignalR
   ┌────────────────┼────────────────┬─────────────────┐
   ▼                ▼                ▼                 ▼
┌──────────┐  ┌───────────┐   ┌───────────┐    ┌────────────┐
│ Product  │  │ Inventory │   │  Alert    │    │ Reporting  │
│ Service  │  │  Service  │   │  Service  │    │  Service   │
└────┬─────┘  └─────┬─────┘   └─────▲─────┘    └─────▲──────┘
     │              │               │                │
 ProductDb     InventoryDb      AlertDb         ReportingDb
                    │  events
                    ▼
              ┌───────────┐
              │ RabbitMQ  │
              └───────────┘
```

Every service owns its database; no service reads another service's database. Cross-service
data flows through REST calls or RabbitMQ events, and the Inventory service writes each event to a
transactional outbox in the same transaction as the stock change, so a broker outage delays events
instead of losing them (see [docs/architecture.md](docs/architecture.md)).

## Repository layout

```
InventorySystem.sln
Dockerfile                             the six apps, one recipe: which project to publish is a build argument
docker-compose.yml                     SQL Server + RabbitMQ + the six apps
src/
  BuildingBlocks/
    InventorySystem.BuildingBlocks.Contracts   shared event contracts + messaging topology
    InventorySystem.BuildingBlocks.Messaging  RabbitMQ transport (publisher, consumer, options)
  Inventory.Web/                               MVC console: the pages, the design system, the service clients
  Services/    Product/        Product.Api / Application / Domain / Infrastructure   ✅ implemented
    Inventory/      Inventory.Api / Application / Domain / Infrastructure ✅ implemented
    Alert/          Alert.Api / Application / Domain / Infrastructure    ✅ implemented
    Reporting/      Reporting.Api / Application / Infrastructure          ✅ implemented
    Identity/       Identity.Api / Application / Domain / Infrastructure ✅ implemented
tests/
  Alert.Domain.Tests/           21 unit tests
  Alert.Api.Tests/              22 HTTP integration tests
  Products.Domain.Tests/        22 unit tests
  Products.Api.Tests/           19 HTTP integration tests
  Inventory.Domain.Tests/       33 unit tests
  Inventory.Api.Tests/          58 HTTP integration tests (incl. concurrency, outbox, live broadcast)
  Reporting.Api.Tests/          35 HTTP integration tests (projection, the four reports, the downloads)
  Identity.Domain.Tests/        57 unit tests
  Identity.Api.Tests/           41 HTTP integration tests
  Inventory.Web.Tests/          120 console tests (sign-in, roles and sign-out, controllers, live regions, relay)
  CrossService.Contracts.Tests/ 14 cross-service HTTP contract tests
docs/                           api.md, inventory-api.md, alerts-api.md, reports-api.md, architecture.md, …
```

## Prerequisites

- .NET SDK 8.0 or later (developed against 8.0 / 9.0 SDK)
- Docker Desktop (optional for local development, required for `docker compose`)

## Quick start

### Option A — everything in containers

```bash
cp .env.example .env           # passwords, the environment, the token signing key
docker compose up -d --build   # SQL Server + RabbitMQ + the five services + the console
```

One command builds the six apps from the single [Dockerfile](Dockerfile) — one recipe, with the project
to publish as its build argument — starts them next to the two pieces of infrastructure, and waits for
their health checks. Each app migrates its own database and seeds what it owns on startup, so a fresh
clone signs in as soon as the console is healthy (`docker compose ps`).

| What | Where |
| ---- | ----- |
| Console | <http://localhost:5159> — `manager`, `clerk` or `viewer`, password `inventory-console-2026` |
| Product service | <http://localhost:5251> · `/swagger` |
| Inventory service | <http://localhost:5102> · `/swagger` |
| Alert service | <http://localhost:5212> · `/swagger` |
| Reporting service | <http://localhost:5312> · `/swagger` |
| Identity service | <http://localhost:5402> · `/swagger` |
| RabbitMQ | <http://localhost:15672> — `inventory` / `inventory` |

`docker compose logs -f console` follows one app's output, and `docker compose up -d --build
inventory-api` rebuilds just that one after a change. The port numbers are the ones a local run uses,
so every URL in this README works either way. What the apps are given (and what a production run has to
supply instead) is in [development.md §4](docs/development.md).

### Option B — the code on the host

Debugging it is easier from the host, against the same two containers:

```bash
# 1. Optional: shared infrastructure (RabbitMQ, and SQL Server when you want it)
cp .env.example .env
docker compose up -d sqlserver rabbitmq

# 2. Run the Product service (http://localhost:5251) — owns products, categories, warehouses
dotnet run --project src/Services/Product/Product.Api

# 3. Run the Inventory service (http://localhost:5102) in a second terminal
#    It validates products/warehouses through the Product service, so start that first.
dotnet run --project src/Services/Inventory/Inventory.Api

# 4. Run the Alert service (http://localhost:5212) in a third terminal — consumes stock events
dotnet run --project src/Services/Alert/Alert.Api

# 5. Run the Reporting service (http://localhost:5312) in a fourth terminal — projects the same
#    events into its read model and serves the reports
#    It reads sku/names/prices from the Product service, so start that first.
dotnet run --project src/Services/Reporting/Reporting.Api

# 6. Run the Identity service (http://localhost:5402) in a fifth terminal — accounts, roles and the
#    tokens the rest of the system validates. The console signs people in through it.
dotnet run --project src/Services/Identity/Identity.Api

# 7. Run the console (http://localhost:5159) in a sixth terminal — it reads the five services above
dotnet run --project src/Inventory.Web

# 8. Run the tests (no broker, no service and no database needed)
dotnet test
```

A stock change then flows end to end: Inventory commits the change together with its event and
announces it on its SignalR hub, the outbox dispatcher hands the event to RabbitMQ, the Alert service
raises a low-stock alert that `GET /api/alerts` returns, and the Reporting service moves the product's
stock level and appends the movement that `GET /api/reports/*` reports. An open console screen
re-reads itself when that announcement arrives, so two browser windows on the same system agree
without anybody reloading. Without RabbitMQ the services still run — events wait in the outbox (see
`RabbitMq:Enabled` in [docs/development.md](docs/development.md)).

**Without any container:** each service falls back to its own local SQLite file (`product.dev.db`,
`inventory.dev.db`, `alert.dev.db`, `reporting.dev.db`, `identity.dev.db`), so all five run
immediately — the Product service also seeds a small sample catalogue and the Identity service
seeds the three accounts you sign in with (`manager`, `clerk`, `viewer`; password
`inventory-console-2026`). To run on SQL Server instead, start the container and migrate every
database (seeded) as in [development.md §6](docs/development.md), or set `Database:Provider` to
`SqlServer` (`Database__Provider=SqlServer` as an environment variable) for each service.

In Development each service exposes Swagger UI: `http://localhost:5251/swagger` (Product),
`http://localhost:5102/swagger` (Inventory), `http://localhost:5212/swagger` (Alert) and
`http://localhost:5312/swagger` (Reporting).

## Product service API

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/api/products` | list / search / filter / page |
| GET | `/api/products/{id}` | product details |
| POST | `/api/products` | create a product |
| PUT | `/api/products/{id}` | update a product |
| PUT | `/api/products/{id}/sku` | change a SKU |
| DELETE | `/api/products/{id}` | deactivate (soft delete) |
| POST | `/api/products/{id}/activate` | reactivate |
| GET/POST/PUT/DELETE | `/api/categories[/{id}]` | category management |
| GET/POST/PUT/DELETE | `/api/warehouses[/{id}]` | warehouse management |
| GET | `/health` | health probe |

Request/response examples: [docs/api.md](docs/api.md).

## Inventory service API

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/api/inventory` | current stock (filter by product/warehouse, paged) |
| GET | `/api/inventory/{productId}` | stock of one product across warehouses |
| GET | `/api/inventory/history` | stock ledger (filter by product/warehouse/type/date, paged) |
| POST | `/api/inventory/receive` | receive stock |
| POST | `/api/inventory/issue` | issue stock (refused if it exceeds what is available) |
| POST | `/api/inventory/transfer` | move stock between warehouses (records both legs) |
| POST | `/api/inventory/adjust` | set an absolute counted quantity |
| GET | `/health` | health probe |

Every successful stock change appends a transaction and queues a stock event
(`stock.received`, `stock.issued`, `stock.transferred`, `stock.adjusted`) in the outbox; the
dispatcher delivers it to RabbitMQ independently of the HTTP request.

Full request/response examples: [docs/api.md](docs/api.md) and [docs/inventory-api.md](docs/inventory-api.md).

## Alert service API

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/api/alerts` | list alerts (filter by product/warehouse/type/read state/status; active by default; paged, newest first) |
| GET | `/api/alerts/{id}` | one alert |
| PUT | `/api/alerts/{id}/read` | mark as read (idempotent) |
| GET | `/health` | health probe |

There is no create endpoint on purpose: alerts are raised by the consumer when a stock event reports
a quantity at or below the product's minimum stock level. The alerts are refreshed rather than
duplicated while the condition lasts, resolved when stock comes back, and a redelivered event is
ignored. Details: [docs/alerts-api.md](docs/alerts-api.md).

## Reporting service API

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/api/reports/inventory` | current stock levels with value, totals over the filtered set |
| GET | `/api/reports/low-stock` | stock at or below its minimum, with the shortfall, worst first |
| GET | `/api/reports/movement` | the movement ledger (product/warehouse/type/date filters, paged, with a summary) |
| GET | `/api/reports/valuation` | `quantity × unitPrice` per product/warehouse, plus a per-warehouse roll-up |
| GET | `/api/reports/{report}/export?format=csv\|xlsx` | the report as a file: same filters, **every** matching row, CSV or a real .xlsx workbook |
| GET | `/health` | health probe |

ReportingDb is a **read model**, not a second inventory: one row per (product, warehouse) plus an
append-only movement ledger, projected from the stock events in its own `reporting.inventory` queue
and de-duplicated on the event id. The reports resolve sku, names and unit prices from the Product
service, so they answer `503` rather than an empty report when it is down. Details and examples:
[docs/reports-api.md](docs/reports-api.md).

## The console (Inventory.Web)

Runs on `http://localhost:5159` and owns no data: every screen is built from the service APIs, so a
service that is down is reported as such rather than shown as an empty table. Every screen also sits
behind a sign-in: the console asks the Identity service whether a username and a password are an
account and then keeps a session cookie of its own — the person's token is not kept, and what the
console sends to the Inventory service's live stock view is the console's own credential.

| Route | Screen |
| ----- | ------ |
| `/sign-in`, `/sign-in/denied` | sign-in (`manager`, `clerk`, `viewer` in Development) and the refusal page |
| `/` | overview: position per warehouse, what is short, the last movements, open alerts |
| `/products`, `/products/new`, `/products/{id}/edit` | product list, creation, edit (sku, deactivate/reactivate) |
| `/categories`, `/warehouses` | categories and warehouses, with their create/edit forms on the page |
| `/inventory`, `/inventory/history` | stock levels (state read against the catalogue minimum) and the movement ledger |
| `/inventory/receive`, `/issue`, `/transfer`, `/adjust` | the four stock operations, each ending on the row it changed |
| `/alerts` | active/resolved alerts, acknowledge one or a batch |
| `/reports/inventory`, `/low-stock`, `/movement`, `/valuation` | the four reports, filters, totals, pager + CSV/Excel |
| `/reports/{report}/export?format=csv\|xlsx` | the download, proxied so the browser only talks to the console |

The overview and `/inventory` are **live**: they follow the Inventory service's hub
(`/hubs/stock`) and re-read their own server-rendered regions when stock moves, and the rail's alert
count keeps itself current on every screen (a movement in one window appears in another without a
reload). Each browser carries an opaque station, so a screen also says *whose* movement it was —
`your movement of 6 units is confirmed` or `another terminal moved 5 units`. Nothing is required for a
page to render — without JavaScript or with the hub unreachable, every screen works as it is served.
Downloads, timestamps, where a name comes from and how the live regions behave are documented in
[docs/web-ui.md](docs/web-ui.md); the console's own tests live in `tests/Inventory.Web.Tests`.

## Conventions

- **Namespaces:** `InventorySystem.<BoundedContext>.<Layer>`, where the bounded-context segment
  must not reuse an entity type name (the Product service uses `InventorySystem.Products.*` so the
  `Product` entity stays unambiguous). See [docs/architecture.md](docs/architecture.md).
- **Commits:** conventional commits (`feat:`, `fix:`, `test:`, `refactor:`, `docs:`).
- **Branches:** `main`, `develop`, `feature/*`, `bugfix/*`; changes reach `main` through pull requests.
