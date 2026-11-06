# Real-Time Inventory Management System

An educational microservices application that tracks inventory in real time, raises low-stock
alerts automatically and produces inventory reports. Built with **ASP.NET Core 8**, **Entity
Framework Core**, **SQL Server**, **RabbitMQ**, **SignalR** and **Docker**.

---

## Table of contents

1. [Project objectives](#1-project-objectives)
2. [Key features](#2-key-features)
3. [Architecture](#3-architecture)
4. [Users, roles and permissions](#4-users-roles-and-permissions)
5. [Functional modules](#5-functional-modules)
6. [API reference](#6-api-reference)
7. [Security and reliability](#7-security-and-reliability)
8. [Getting started](#8-getting-started)
9. [Verifying the system end to end](#9-verifying-the-system-end-to-end)
10. [Troubleshooting](#10-troubleshooting)
11. [Repository layout](#11-repository-layout)
12. [Development guidelines](#12-development-guidelines)
13. [Glossary](#13-glossary)
14. [Further documentation](#14-further-documentation)

---

## 1. Project objectives

The system exists to make the company's stock **visible, checkable and foreseeable**. Concretely, it
aims to:

1. **Maintain a single, trusted catalogue** of products, categories and warehouses that every other
   part of the system relies on.
2. **Record every stock movement** — goods received, issued, transferred between warehouses and
   adjusted after a physical count — in an auditable, append-only ledger.
3. **Warn the business before shelves run empty** by raising low-stock and out-of-stock alerts the
   moment a quantity reaches a product's minimum stock level.
4. **Answer the core business questions** — what do we hold, what is short, what moved, and what is
   it worth — through filterable reports whose downloads match the screen exactly.
5. **Keep every user's view current** with a web console whose tables and alert counts update live,
   without a page refresh.

As an educational project it also demonstrates a set of architectural practices: database-per-service
microservices, event-driven integration, the transactional outbox pattern, idempotent consumers,
read-model projections, role-based access with JWTs, and containerised deployment.

---

## 2. Key features

- **Catalogue management** — products (SKU, price, minimum stock level), categories and warehouses,
  with soft delete so nothing ever disappears from history.
- **Four stock operations** — receive, issue, transfer and adjust, each written to the ledger and
  announced as an event.
- **Guaranteed integrity** — no negative stock, optimistic concurrency between operators, and stock
  changes that are committed together with their events.
- **Automatic alerts** — one alert per low-stock situation, refreshed while it lasts and resolved
  when stock is replenished.
- **Four reports** — inventory, low stock, stock movement and valuation, exportable as CSV or a real
  `.xlsx` workbook.
- **Live console** — a server-rendered MVC application that updates over SignalR and still works
  fully without JavaScript.
- **Role-based access** — Viewer, Clerk and Manager roles issued by a dedicated Identity service.
- **One-command deployment** — `docker compose up` starts SQL Server, RabbitMQ and all six apps.

---

## 3. Architecture

### 3.1 Overview

```
        ┌────────────────────────┐
        │  Inventory.Web (MVC)   │   Razor server-rendered console, no JS framework
        └───────────┬────────────┘
                    │ HTTP / SignalR
   ┌────────────────┼────────────────┬─────────────────┬──────────────┐
   ▼                ▼                ▼                 ▼              ▼
┌──────────┐  ┌───────────┐   ┌───────────┐    ┌────────────┐  ┌──────────┐
│ Product  │  │ Inventory │   │  Alert    │    │ Reporting  │  │ Identity │
│ Service  │  │  Service  │   │  Service  │    │  Service   │  │ Service  │
└────┬─────┘  └─────┬─────┘   └─────▲─────┘    └─────▲──────┘  └────┬─────┘
     │              │               │                │              │
 ProductDb     InventoryDb      AlertDb         ReportingDb     IdentityDb
                    │  events       │                │
                    ▼               │                │
              ┌───────────┐         │                │
              │ RabbitMQ  │─────────┴────────────────┘
              └───────────┘
```

**Guiding principle:** every service owns its database, and no service reads another service's
database. Data crosses service boundaries only through REST calls or RabbitMQ events.

### 3.2 The six applications

| # | Application | Owns | Dev port | Responsibility |
|---|-------------|------|----------|----------------|
| 1 | **Product** (`Product.Api`) | `ProductDb` | 5251 | The catalogue: products, categories, warehouses |
| 2 | **Identity** (`Identity.Api`) | `IdentityDb` | 5402 | Accounts, roles, sign-in, signed tokens |
| 3 | **Inventory** (`Inventory.Api`) | `InventoryDb` | 5102 | Stock levels, the four stock operations, the ledger, the live stock hub |
| 4 | **Alert** (`Alert.Api`) | `AlertDb` | 5212 | Projects stock events into low-stock alerts |
| 5 | **Reporting** (`Reporting.Api`) | `ReportingDb` | 5312 | Projects stock events into a read model; serves the four reports |
| 6 | **Console** (`Inventory.Web`) | — | 5159 | The web application people work in; every screen is a view over the five services |

### 3.3 Shared infrastructure

- **SQL Server 2022** — host port `1433`, one database per service.
- **RabbitMQ 3.13** — host ports `5672` (AMQP) and `15672` (management UI, default
  `inventory` / `inventory` unless `.env` says otherwise).

### 3.4 Internal structure of a service

Each service follows a clean, layered structure of four projects:

| Layer | Contents |
|-------|----------|
| `*.Domain` | Entities and business rules; no dependencies |
| `*.Application` | Use-case services, DTOs, abstractions |
| `*.Infrastructure` | EF Core persistence, repositories, migrations, messaging |
| `*.Api` | HTTP surface, middleware, `Program.cs` composition root |

(The Reporting service has no Domain project — it is a read model.)

### 3.5 How a stock movement travels

```
Clerk acts in the console
        │  (HTTP, after Identity sign-in)
        ▼
Inventory service ── verifies product/warehouse against ──► Product service (HTTP)
        │  commit: stock change + ledger + outbox in ONE transaction
        ▼
Outbox dispatcher ──► RabbitMQ (exchange inventory.events, routing keys stock.*)
        │
        ├──► alert.inventory     ──► Alert service     (low-stock projection)
        └──► reporting.inventory ──► Reporting service (read-model projection)

Inventory service ── SignalR hub (/hubs/stock) ──► console relay ──► browsers (live tables)
```

- **Transactional outbox.** The Inventory service never publishes before commit. It writes each event
  to an outbox table in the same transaction as the stock change, and a background dispatcher
  publishes it afterwards. A movement and its announcement either both happen or neither does; a
  broker outage delays events instead of losing them.
- **Topic exchange.** Events go to `inventory.events` with routing keys `stock.received`,
  `stock.issued`, `stock.transferred` and `stock.adjusted`.
- **Shared contracts.** Event shapes live in `InventorySystem.BuildingBlocks.Contracts`; the
  `CrossService.Contracts.Tests` suite fails the build if a publisher and a consumer drift apart.
- **Idempotent consumers.** Both consumers keep a processed-event store keyed by event id, so a
  redelivered message never creates a duplicate alert or double-counts a movement.
- **Dead-lettering.** A message that still fails after one retry lands in a `*.inventory.dead` queue,
  visible in the RabbitMQ UI — never dropped silently, never blocking the queue.

---

## 4. Users, roles and permissions

People never call the APIs directly; they sign in to the console. There are three human roles and one
machine account, all defined in the Identity domain:

| Role | Typical user | Permissions |
|------|--------------|-------------|
| **Viewer** | floor staff, auditors | Browse every screen, the live view and the reports; change nothing |
| **Clerk** | dock / warehouse clerks | Everything a Viewer can, **plus** move stock (receive, issue, transfer, adjust) and acknowledge alerts |
| **Manager** | office manager | Everything a Clerk can, **plus** maintain the catalogue (create/edit/deactivate products, categories, warehouses) |
| **Service** (`console`) | not a person — the console's relay | Follows the Inventory service's live feed on behalf of everyone signed in; internal only |

The console only draws the buttons a user's role can actually use. An account with an unknown role is
refused at sign-in rather than guessed at.


---

## 5. Functional modules

### 5.1 Catalogue — Product service

The reference data every other service trusts:

- **Products** — unique, upper-cased SKU (changed only through a dedicated endpoint), name,
  description, category, unit price, **minimum stock level** (the threshold the Alert service
  watches) and an `isActive` flag.
- **Categories** — unique names, with a per-category product count.
- **Warehouses** — unique codes, names and addresses.

Rules:

- Deleting a product or warehouse is a **soft delete** (deactivation); nothing disappears from a ledger
  or a report. A deactivated warehouse cannot take part in a new stock operation.
- Deleting a category is refused while products still reference it.
- List endpoints are paged (`page` / `pageSize`, clamped to 1..100) and most support search.

### 5.2 Stock — Inventory service

The Inventory service stores **quantities and the ledger only** — no names, prices or minimums. Every
operation first resolves the product and warehouse against the Product service; if it is unreachable
the request fails with `503` rather than being accepted on a guess.

| Operation | Effect on stock | Ledger entries | Event |
|-----------|-----------------|----------------|-------|
| **Receive** | Quantity increases; the stock row is created if new | `RECEIVE` | `stock.received` |
| **Issue** | Quantity decreases; refused if it exceeds what is available | `ISSUE` | `stock.issued` |
| **Transfer** | Moves stock between warehouses in one commit — both legs or neither | `TRANSFER_OUT` + `TRANSFER_IN` | `stock.transferred` |
| **Adjust** | Sets the quantity to the physically counted figure | `ADJUSTMENT` | `stock.adjusted` |

Every ledger entry records product, warehouse, quantity and an optional `reference` (e.g. `PO-1`,
`SO-77`, "Annual count"). Each operation returns the fresh stock level(s) so the operator sees the
result immediately.

Two protections:

- **No negative stock** — issuing more than is on hand is refused with `400`.
- **Optimistic concurrency** — if two operators race on the same stock row, the second is refused with
  `409` and asked to reload, instead of silently overwriting a colleague's change.

### 5.3 Alerts — Alert service

Alerts exist only as a projection of real stock events; there is deliberately **no create endpoint**.

- **One alert per situation, not per event.** When a product in a warehouse drops to or below its
  minimum, one alert becomes active (`LOW_STOCK`, or `OUT_OF_STOCK` at zero). Further events while
  the condition holds refresh that alert rather than adding new ones.
- **Replenishment resolves, history stays.** When stock rises above the minimum the alert is marked
  `RESOLVED` and kept; a later drop starts a new alert.
- **Transfers evaluate both warehouses**, so one transfer can raise an alert at the source and resolve
  one at the destination.
- **Read state survives.** Marking an alert read is idempotent, and refreshing the same condition never
  makes it unread again.

### 5.4 Reports — Reporting service

The Reporting service projects the event stream into a lightweight read model (`StockLevelSnapshot`,
one row per product and warehouse, and an append-only `StockMovement` ledger), de-duplicated on event id.

| Report | Answers |
|--------|---------|
| **Inventory** | What do we hold now, where, and what is it worth — with low/out-of-stock flags and totals |
| **Low stock** | What is at or below its minimum right now, with the shortfall, worst first |
| **Stock movement** | What moved, when and in which direction — paged, with a summary |
| **Valuation** | `quantity × unitPrice` per product and warehouse, plus a per-warehouse roll-up |

All reports share the same filters (date range, warehouse, product, SKU search) and can be downloaded
as CSV or `.xlsx` with exactly the rows, order and totals shown on screen (the export contains **every**
matching row, not just the current page).

Things to know:

- Because the read model is a projection, a just-executed movement may take a moment to appear in a
  report; the current-stock screens read the Inventory service directly.
- SKUs, names and unit prices come from the Product service, so value columns follow catalogue prices,
  and the reports return `503` rather than an empty report if that service is down.

### 5.5 The console — Inventory.Web

An ASP.NET Core MVC application (port 5159) that owns **no data and no database**; every screen is
built from the service APIs.

| Route | Screen | Source |
|-------|--------|--------|
| `/sign-in`, `/sign-in/denied` | Sign-in and refusal page | Identity |
| `/` | Overview: position per warehouse, shortages, last movements, open alerts | All services |
| `/products`, `/products/new`, `/products/{id}/edit` | Product list, creation, edit (SKU, deactivate/reactivate) | Product |
| `/categories`, `/warehouses` | Categories and warehouses with inline create/edit forms | Product |
| `/inventory`, `/inventory/history` | Stock levels (against the catalogue minimum) and the movement ledger | Inventory |
| `/inventory/receive`, `/issue`, `/transfer`, `/adjust` | The four stock operations, each ending on the changed row | Inventory |
| `/alerts` | Active/resolved alerts; acknowledge one or a batch | Alert |
| `/reports/inventory`, `/low-stock`, `/movement`, `/valuation` | The four reports with filters, totals, pager and CSV/Excel | Reporting |
| `/reports/{report}/export?format=csv\|xlsx` | Downloads, proxied so the browser only talks to the console | Reporting |

Defining behaviours:

- **Honest about outages.** A service that is down is shown as unavailable, never as an empty table.
  Only the Identity service must be healthy before the console starts, since every screen sits behind
  sign-in (a fallback policy protects any screen added later).
- **Live updates.** The overview and `/inventory` follow the Inventory service's SignalR hub
  (`/hubs/stock`) through the console's relay and re-read their server-rendered regions when stock
  moves; the alert count in the navigation rail stays current on every screen. Each browser carries an
  opaque station id, so a screen can say whose movement it was — *"your movement of 6 units is
  confirmed"* or *"another terminal moved 5 units"*.
- **Progressive enhancement.** Without JavaScript, or with the hub unreachable, every screen still works
  as served.
- **Session handling.** The console asks Identity to verify the username and password, then issues its
  own session cookie. The user's token is not kept in the browser, and the browser never talks to the
  Inventory service directly.

See [docs/web-ui.md](docs/web-ui.md) for details on downloads, timestamps and live regions.

---

## 6. API reference

In Development every service exposes Swagger UI at `/swagger`. All APIs return errors as RFC 7807
`application/problem+json`.

### Product service — `http://localhost:5251`

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/api/products` | List / search / filter / page |
| GET | `/api/products/{id}` | Product details |
| POST | `/api/products` | Create a product |
| PUT | `/api/products/{id}` | Update a product |
| PUT | `/api/products/{id}/sku` | Change a SKU |
| DELETE | `/api/products/{id}` | Deactivate (soft delete) |
| POST | `/api/products/{id}/activate` | Reactivate |
| GET/POST/PUT/DELETE | `/api/categories[/{id}]` | Category management |
| GET/POST/PUT/DELETE | `/api/warehouses[/{id}]` | Warehouse management |
| GET | `/health` | Health probe |

Examples: [docs/api.md](docs/api.md)

### Inventory service — `http://localhost:5102`

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/api/inventory` | Current stock (filter by product/warehouse, paged) |
| GET | `/api/inventory/{productId}` | Stock of one product across warehouses |
| GET | `/api/inventory/history` | Stock ledger (filter by product/warehouse/type/date, paged) |
| POST | `/api/inventory/receive` | Receive stock |
| POST | `/api/inventory/issue` | Issue stock (refused if it exceeds what is available) |
| POST | `/api/inventory/transfer` | Move stock between warehouses (records both legs) |
| POST | `/api/inventory/adjust` | Set an absolute counted quantity |
| GET | `/health` | Health probe |

Examples: [docs/api.md](docs/api.md), [docs/inventory-api.md](docs/inventory-api.md)

### Alert service — `http://localhost:5212`

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/api/alerts` | List alerts (filter by product/warehouse/type/read state/status; active by default; paged, newest first) |
| GET | `/api/alerts/{id}` | One alert |
| PUT | `/api/alerts/{id}/read` | Mark as read (idempotent) |
| GET | `/health` | Health probe |

Use `status=1` to see resolved alerts. Details: [docs/alerts-api.md](docs/alerts-api.md)

### Reporting service — `http://localhost:5312`

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/api/reports/inventory` | Current stock levels with value, totals over the filtered set |
| GET | `/api/reports/low-stock` | Stock at or below its minimum, with the shortfall, worst first |
| GET | `/api/reports/movement` | Movement ledger (product/warehouse/type/date filters, paged, with a summary) |
| GET | `/api/reports/valuation` | `quantity × unitPrice` per product/warehouse, plus a per-warehouse roll-up |
| GET | `/api/reports/{report}/export?format=csv\|xlsx` | The report as a file: same filters, every matching row |
| GET | `/health` | Health probe |

Details: [docs/reports-api.md](docs/reports-api.md)

### Identity service — `http://localhost:5402`

Accounts, roles, sign-in and token issuing. Browse the endpoints at `/swagger`.

---

## 7. Security and reliability

**Security**

- **Signed tokens.** The Identity service signs JWTs with `Jwt:SigningKey`; only the services that must
  validate those tokens hold the key.
- **Machine accounts have one ability.** The console relay has its own `service` role, never a copy of a
  person's permissions.
- **Server-side sessions.** Users hold a console session cookie, not a token.
- **Protected by default.** Every console screen requires sign-in through a fallback authorisation policy.

**Consistent errors**

| Situation | Status |
|-----------|--------|
| Malformed payload or violated business rule | `400` |
| Resource not found | `404` |
| Uniqueness or concurrency conflict | `409` |
| A dependency is unreachable | `503` (never reported as a bad request) |

**Reliability**

- Transactional outbox — no lost or premature events.
- Idempotent consumers — redeliveries are harmless.
- Dead-letter queues — poison messages are isolated and visible.
- Truthful health checks — each `/health` reflects the service's own database, and `docker compose`
  waits on those checks rather than on open ports.
- Graceful degradation — without RabbitMQ the services keep running and events wait in the outbox.

---

## 8. Getting started

### Prerequisites

- .NET SDK 8.0 or later (developed against the 8.0 / 9.0 SDK)
- Docker Desktop (optional for local development, required for `docker compose`)

### Option A — everything in containers

```bash
cp .env.example .env           # passwords, environment, token signing key
docker compose up -d --build   # SQL Server + RabbitMQ + five services + the console
docker compose ps              # wait until all six apps are healthy
```

A single [Dockerfile](Dockerfile) builds all six apps, with the project to publish passed as a build
argument. Each app migrates and seeds its own database on startup (the first start is usually the
slowest), so a fresh clone is ready to sign in as soon as the console is healthy.

| What | Where |
| ---- | ----- |
| Console | <http://localhost:5159> — `manager`, `clerk` or `viewer`, password `inventory-console-2026` |
| Product service | <http://localhost:5251/swagger> |
| Inventory service | <http://localhost:5102/swagger> |
| Alert service | <http://localhost:5212/swagger> |
| Reporting service | <http://localhost:5312/swagger> |
| Identity service | <http://localhost:5402/swagger> |
| RabbitMQ UI | <http://localhost:15672> — `inventory` / `inventory` |
| SQL Server | `localhost,1433` |

Useful commands:

```bash
docker compose logs -f console              # follow one app's output
docker compose up -d --build inventory-api  # rebuild a single app after a change
```

Ports are identical to a local run, so every URL in this README works either way. What a production run
must supply instead is described in [docs/development.md §4](docs/development.md).

### Option B — services on the host (easier debugging)

```bash
# 1. Optional: shared infrastructure
cp .env.example .env
docker compose up -d sqlserver rabbitmq

# 2. Product service (5251) — start first; Inventory and Reporting depend on it
dotnet run --project src/Services/Product/Product.Api

# 3. Inventory service (5102)
dotnet run --project src/Services/Inventory/Inventory.Api

# 4. Alert service (5212) — consumes stock events
dotnet run --project src/Services/Alert/Alert.Api

# 5. Reporting service (5312) — projects events and serves reports
dotnet run --project src/Services/Reporting/Reporting.Api

# 6. Identity service (5402) — accounts, roles and tokens
dotnet run --project src/Services/Identity/Identity.Api

# 7. The console (5159)
dotnet run --project src/Inventory.Web
```

Run each command in its own terminal.

### Running without any containers

Each service falls back to a local SQLite file (`product.dev.db`, `inventory.dev.db`, `alert.dev.db`,
`reporting.dev.db`, `identity.dev.db`), so everything runs immediately. The Product service seeds a
small sample catalogue and the Identity service seeds the development accounts. Without RabbitMQ the
services still run and events wait in the outbox (see `RabbitMq:Enabled` in
[docs/development.md](docs/development.md)).

To use SQL Server instead, start the container and migrate every database as described in
[docs/development.md §6](docs/development.md), or set `Database__Provider=SqlServer` for each service.

### Build and test

```bash
dotnet build InventorySystem.sln
dotnet test  InventorySystem.sln   # no broker, service or database needed
```

---

## 9. Verifying the system end to end

1. Sign in as **manager**; create a category, two warehouses and a product (note its SKU and minimum
   stock level).
2. Sign in as **clerk**; receive 20 units, issue 5, transfer the rest to the second warehouse, and
   adjust once.
3. Check `/inventory/history` for the exact ledger rows, and `/alerts` as the quantity crosses the
   minimum.
4. Open the console in a second browser or profile — movements should appear there within a second,
   without refreshing.
5. Open the four reports and download one as CSV.
6. In the RabbitMQ UI, confirm traffic on `inventory.events` and no messages in the `*.dead` queues.

---

## 10. Troubleshooting

| Symptom | Likely cause | Where to look |
|---------|--------------|---------------|
| Console is up but screens show a service as unavailable | That service is still starting or has crashed | `docker compose logs <service>` and its `/health` |
| Stock operation fails with `503 The Product service could not be reached` | `product-api` is not running | `docker compose ps` / logs |
| Sign-in always rejects | Identity service down, or Production mode with seeding off and no accounts | Identity logs, `Identity:Seed:*` settings |
| A queue stalls with old messages unhandled | A poison event | The `*.inventory.dead` queue in the RabbitMQ UI |
| Live tables do not update | The relay lost its hub connection | Console logs (`StockRelay`); pages reconnect, or reload |
| `409` on a stock operation | Two operators raced on the same row | Harmless — reload and retry |
| A report is missing a movement just made | The projection has not caught up yet | Wait a moment; check RabbitMQ queue depth |

---

## 11. Repository layout

```
InventorySystem.sln
Dockerfile                  one recipe for all six apps (project chosen by build argument)
docker-compose.yml          SQL Server + RabbitMQ + the six apps
.env.example                template for passwords, environment and signing key
src/
  BuildingBlocks/
    InventorySystem.BuildingBlocks.Contracts   integration events, RabbitMQ topology, serialization
    InventorySystem.BuildingBlocks.Messaging   RabbitMQ publisher, consumer, connection plumbing
    InventorySystem.BuildingBlocks.Security    shared JWT validation vocabulary
  Services/
    Product/     Product.Api / Application / Domain / Infrastructure
    Inventory/   Inventory.Api / Application / Domain / Infrastructure
    Alert/       Alert.Api / Application / Domain / Infrastructure
    Reporting/   Reporting.Api / Application / Infrastructure
    Identity/    Identity.Api / Application / Domain / Infrastructure
  Inventory.Web/            the console: pages, design system, service clients, SignalR relay
tests/                      *.Domain.Tests and *.Api.Tests per service, Inventory.Web.Tests,
                            CrossService.Contracts.Tests
docs/                       API references, architecture, development and UI guides
```

---

## 12. Glossary

| Term | Meaning |
|------|---------|
| **Catalogue** | Reference data — products, categories, warehouses (owned by the Product service) |
| **Stock level** | One product in one warehouse and its quantity |
| **Ledger / history** | Append-only transaction rows: `RECEIVE`, `ISSUE`, `TRANSFER_IN`, `TRANSFER_OUT`, `ADJUSTMENT` |
| **Movement** | Any ledger entry; a transfer produces two |
| **Event** | The JSON announcement of a movement, published with a `stock.*` routing key |
| **Outbox** | Events staged in the same transaction as the movement and published afterwards |
| **Projection** | What Alert or Reporting derives from the events — not a copy of another service's table |
| **Alert** | One low-stock *situation* at a warehouse, active until replenishment resolves it |
| **Minimum stock level** | The per-product threshold that turns an alert on and off |
| **Console** | `Inventory.Web`, the web app people work in |
| **Relay** | The console's machine account that keeps the live view open for everyone signed in |
