# Architecture

This document records the architecture of the Real-Time Inventory Management System and the
decisions taken while implementing it. It complements the specification in `Documentation.docx`.

## 1. Service inventory

| Service | Owns (database) | Responsibility | State |
| ------- | --------------- | -------------- | ----- |
| Product | ProductDb | products, categories, warehouses | implemented |
| Inventory | InventoryDb | stock levels, stock ledger, transaction history | implemented |
| Alert | AlertDb | low-stock alerts, read/unread state, consumed-event ledger | implemented |
| Reporting | ReportingDb | current inventory, low-stock, movement and valuation read model | implemented |
| Identity | IdentityDb | users, roles, login, JWT | scaffold |
| Inventory.Web | none | MVC presentation layer, talks to services over HTTP and follows the Inventory hub in the browser | implemented |

Each service is four projects: `*.Api` (HTTP surface), `*.Application` (use cases), `*.Domain`
(entities and invariants) and `*.Infrastructure` (EF Core, repositories, messaging). The Reporting
service has no `Domain` project: its model *is* a projection of inventory events, so the read-model
types (`StockLevelSnapshot`, `StockMovement`) live in its Application layer, together with the
projection and the report use cases. The Alert service does have a domain layer, because the
low-stock rule is a business rule of its own (see section 5).

## 2. Communication

- **Synchronous (REST):** the MVC app and other services call service APIs when they need an
  immediate answer. The Inventory service depends on the Product service for every stock operation:
  before changing a quantity it resolves the product (existence, active state, minimum stock level)
  and both warehouses through `HttpProductCatalogClient`. A dependency failure is reported as HTTP
  503 rather than being mistaken for a validation problem, and the whole seam is covered by
  `tests/CrossService.Contracts.Tests`.
- **Asynchronous (RabbitMQ):** the Inventory service publishes stock events; the Alert and
  Reporting services consume them. Contract types and the exchange/queue topology live in
  `src/BuildingBlocks/InventorySystem.BuildingBlocks.Contracts` so publishers and consumers cannot
  drift apart:

  | Event | Routing key | Payload |
  | ----- | ----------- | ------- |
  | `StockReceivedEvent` | `stock.received` | productId, warehouseId, quantity, currentQuantity, minimumStockLevel |
  | `StockIssuedEvent` | `stock.issued` | productId, warehouseId, quantity, currentQuantity, minimumStockLevel |
  | `StockTransferredEvent` | `stock.transferred` | productId, source/destination warehouse, quantity, both current quantities |
  | `StockAdjustedEvent` | `stock.adjusted` | productId, warehouseId, oldQuantity, newQuantity |

  Publishes go to the `inventory.events` topic exchange; consumers bind `alert.inventory` and
  `reporting.inventory` queues with the `stock.*` routing pattern. The transport itself lives in
  `src/BuildingBlocks/InventorySystem.BuildingBlocks.Messaging` (`AddRabbitMqMessaging()`), so a
  service only has to say *what* it publishes or consumes.

  **Publishing — transactional outbox.** A stock operation writes the event into the
  `OutboxMessages` table in the *same* `SaveChanges` as the stock change, and answers the HTTP
  request without touching the broker. A background `OutboxDispatcher` then drains the outbox to
  RabbitMQ with publisher confirms, mandatory routing and exponential backoff (2s → 60s, capped,
  no attempt limit). Consequences:

  * an event cannot be lost: it is committed with the change it describes, so a crash, a broker
    outage or a service restart before delivery only delays it;
  * a broker outage cannot fail a stock change (the API stays available and the events queue up);
  * delivery is **at-least-once** — an event is marked processed only after the broker confirmed
    it, so a crash in between republishes it on the next start.

  **Consuming — ack, retry, dead-letter, de-duplicate.** `RabbitMqEventConsumer` binds the queue,
  its dead-letter exchange and the `<queue>.dead` queue on start (so a consumer may start before any
  publisher exists), acknowledges a message only after the handler succeeded, re-queues a failing
  message once, then dead-letters it, and dead-letters an event whose routing key is not in
  `EventRegistry` instead of crashing the loop. Because delivery is at-least-once, each consumer
  de-duplicates on `IntegrationEvent.EventId` in the same transaction as its own changes (the Alert
  service's alerts, the Reporting service's read model). Every consumer owns its queue
  (`alert.inventory`, `reporting.inventory`), so a slow projection in one service cannot hold up the
  other. Both read the same event and neither writes anything the other needs.

- **Real time (SignalR):** the Inventory service broadcasts every committed stock change on the hub
  at `/hubs/stock` (`StockHub`, client method `StockChanged`), and the console's pages follow it so
  an open screen updates without a refresh. The hub is a *broadcast*, not a feed: there is no
  subscription state and nothing to catch up on, so a client that connects late renders the current
  state from the REST API and follows the notifications from there. The notification is sent after
  the operation has been committed — the same rule as the outbox — and a client that is slow, absent
  or broken cannot affect the operation: the broadcaster logs and drops (`SignalRStockBroadcaster`).
  The payload is a small `StockChangeNotification` (change, product, warehouse, quantity, current
  quantity, minimum, and the destination leg for a transfer) rather than the integration event: a
  screen needs the movement and the state it left behind, not the payload the projections consume.
  See decision 21.

## 3. Database per service

No service reads another service's tables. Where a service needs another service's data it either
calls that service's API or consumes its events. `ProductDb` holds only product/catalog data;
`InventoryDb` will reference warehouses and products by id only.

### Provider selection

`Database:Provider` selects the EF Core provider for the Product service:

- `SqlServer` (default, matches the specification) — schema is applied with EF Core migrations.
- `Sqlite` (development fallback) — schema is created with `EnsureCreated`, because the SQL Server
  migrations are provider-specific. This keeps the system runnable and testable on a machine where
  the Docker environment is not available.

The provider is resolved lazily from `IConfiguration` when `ProductDbContext` is constructed, so
test overrides and environment variables take effect.

## 4. Design decisions and deviations

| # | Decision | Rationale |
| - | -------- | --------- |
| 1 | **Warehouse lives in the Product service (ProductDb).** | The specification is ambiguous: section 7.2 lists `Warehouse` among the Inventory service's entities, but the sprint plan puts "Warehouse CRUD" in Sprint 2 next to Product/Category, and warehouses are pure catalog data with no stock logic. Placing them with Product avoids an Inventory schema dependency in Sprint 2 and keeps `InventoryDb` focused on quantities. The Inventory service stores `WarehouseId` and validates through the Product API, exactly as section 10 requires. Moving the entity later is a contained change. |
| 2 | **Namespace convention `InventorySystem.<BoundedContext>.<Layer>`, where the bounded-context segment must not repeat an entity type name.** | A type named `Product` inside a `...Product...` namespace chain is shadowed by the namespace itself (`CS0118`, verified empirically) as soon as it is referenced outside its own namespace. The Product service therefore uses `InventorySystem.Products.*`, and the Inventory service — whose `Inventory` entity would collide the same way — uses `InventorySystem.Stock.*` (project names are unchanged: `Inventory.Api`, `Inventory.Domain`, …). |
| 3 | **Migrations are pinned to SQL Server by a design-time factory.** | `dotnet ef` defaults to the `Development` environment, which selects the SQLite fallback and would emit SQLite-flavoured migrations (no `IDENTITY`, `TEXT` columns). `ProductDbContextFactory` pins the SQL Server provider so migrations always match production. |
| 4 | **`DELETE /api/products/{id}` deactivates.** | The specification lists both "DELETE /api/products/{id}" and "Deactivate products"; hard-deleting a product would orphan inventory history, so the endpoint performs a soft delete. |
| 5 | **Errors are RFC 7807 ProblemDetails.** | `ExceptionHandlingMiddleware` maps `DomainException`/`ValidationException` → 400, `NotFoundException` → 404, unique-constraint violations → 409, and everything else → 500. Controllers stay free of error-handling boilerplate and clients get one consistent error shape. |
| 6 | **Repository + Unit of Work instead of `DbContext` in controllers.** | Keeps the Application layer persistence-ignorant and makes use cases testable without a database. |
| 7 | **Domain entities own their invariants.** | Validation (non-empty SKU/name, non-negative price and minimum stock level, length limits) lives in the entities and is enforced regardless of which caller reaches them; DataAnnotations on request DTOs only reject malformed HTTP payloads early. |
| 8 | **Development CORS policy is permissive, production is not.** | The MVC app runs on a different origin during development. The `web-client` policy is registered only when `IsDevelopment()`. |
| 9 | **Stock levels carry an optimistic-concurrency token (`UpdatedAt`).** | The over-issue rule must hold for simultaneous operators, not only sequential ones: without a token two operators could each read 10 units, both pass the check and over-issue. A stale write now fails with `DbUpdateConcurrencyException`, mapped to 409. Covered by `InventoryConcurrencyTests`. |
| 10 | **Events go through a transactional outbox rather than being published inline.** | Sprint 3 published after the commit and could only log a failure — a broker outage would have lost the event while the stock change stood. The outbox row commits with the stock change, so the event is as durable as the change itself and the HTTP request never waits for the broker. Supersedes the original "log the failure" decision. |
| 11 | **An `ADJUSTMENT` ledger entry stores the signed delta.** | The other four transaction types encode direction in `Type` with a positive magnitude, so an adjustment (which can go either way) stores its delta and `signedQuantity` normalises all types into a signed value that reporting can sum. |
| 12 | **Stock operations reject deactivated products and warehouses.** | Receiving into a deactivated warehouse, or issuing a discontinued product, is a data-quality bug rather than a legitimate operation; the check is done against the Product service's active state. |
| 13 | **The transport is a shared building block with an `IEventPublisher` / `IEventConsumer` seam.** | The Inventory and Alert services share one implementation of the RabbitMQ dialect (topology, confirms, ack/retry/dead-letter) instead of each writing its own, and tests drive the same seam with in-memory fakes — no broker needed for the suite. |
| 14 | **Delivery is at-least-once, so consumers de-duplicate on `EventId`.** | A broker can redeliver after a crash or a lost acknowledgement, and an exactly-once transport does not exist. The Alert service records processed event ids in `ProcessedEvents` (primary key on the event id) inside the same transaction as the alert changes, so a redelivery can neither raise a second alert nor resolve one twice. Verified live: a re-published event was ignored. |
| 15 | **A poison or unknown message is retried once, then dead-lettered.** | Neither silently dropping it nor requeueing forever is acceptable: infinite requeue blocks the queue, dropping loses data. After one retry the message moves to `<queue>.dead` (declared automatically), where it stays visible in the RabbitMQ management UI for inspection. Verified live with a malformed payload and an unregistered routing key. |
| 16 | **One alert per (product, warehouse) condition, refreshed instead of duplicated, resolved when replenished.** | The specification requires defined duplicate behaviour: a fluctuating quantity must not produce an alert per event. An active alert is updated by newer events and resolved when the quantity is back above the minimum; the resolved row is kept as history, and a later shortage raises a new alert. |
| 17 | **The Alert service evaluates low stock from the event payload, and `RabbitMq:Enabled=false` runs a service without a broker.** | Events carry `currentQuantity` and `minimumStockLevel`, so the consumer needs no lookup and no synchronous dependency on the Product service. The switch keeps the APIs runnable (and their tests honest) on a machine without RabbitMQ: the Inventory service then leaves events in the outbox, and the Alert/Reporting services simply do not consume. |
| 18 | **The Reporting service builds its own read model from the event stream instead of querying the Inventory service.** | The reports exist to answer questions the operational store is not shaped for (what is the value of the stock, what moved last week), and reusing the event stream keeps Reporting independent: no HTTP dependency on Inventory, no load on its database, and the same events the Alert service already consumes. The model is an upsert row per (product, warehouse) plus an append-only movement ledger, committed together with the event's de-duplication record. |
| 19 | **Report rows are enriched from the Product service over HTTP, once per request.** | The read model holds ids because that is what events carry, while a report needs sku, names and — for the valuation report — a *current* unit price. One catalogue read per request (paged at 100 products) is the price of that, and an unreachable Product service is reported as `503` rather than as an empty report, which would look like "no stock". |
| 20 | **Stock-level reports page in the application; the movement ledger pages and aggregates in SQL.** | Stock levels are one row per (product, warehouse) pair — a bounded set — and their value column only exists once the catalogue has been applied, so the filtered set is loaded and sliced afterwards. The movement ledger grows with every event, so it is filtered, paged and summed by the database and never loaded wholesale. |
| 21 | **The live view is owned by the Inventory service and followed by the browser; it is a convenience on top of the ledger, never a participant in it.** | Only one service writes stock, so only one service can say truthfully that a level moved: the hub lives there, next to the operation that commits the change, and the notification is broadcast after the commit. The browser connects to the hub directly (SignalR is a browser protocol) and, for everything else, still talks only to the console; the console's pages re-read *server-rendered fragments* rather than rendering data in JavaScript, so a live refresh and a reload cannot disagree about how a screen looks. Every region that can be refreshed is also correct without JavaScript: the client only ever replaces what the server already sent. |
| 22 | **A stock change invalidates the stock page immediately and the read-model screens after a delay.** | The stock page reads the Inventory service — the service that just wrote the change — so its refresh is exact at once. The overview and the rail read the Reporting and Alert read models, which are built from events the outbox dispatcher delivers on a five-second poll, so they are a moment behind *by design*. The live client therefore re-reads those regions again when the movement has had time to travel (1.5s and 6.5s after the notification, plus a slow poll for alert state that changes without any movement). Without that, a page would show a position that is correct about a moment that has already passed. |
| 23 | **A live notification carries the label of the terminal that asked for the operation, as an unverified header echoed through the service.** | Several people work the same system at once, and on a shared terminal "something moved" and "that was me" are different news — the second is a receipt. The console mints an opaque station id per browser and sends it as `X-Origin-Id` on writes; the Inventory service sanitises it (letters, digits, `.`, `-`, `_`, 40 characters) and puts it on the notification, where every client can compare it with its own station. It is deliberately *not* an authentication mechanism: it is unverified, it is visible to every subscriber, and nothing is authorised by it — that is what sign-in is for, and it is not built yet. The alternative (letting the console tell its own screens what it did, without the service knowing) would have meant the console rendering data in JavaScript, which decision 21 rules out. |

## 5. Low-stock rule

Per the specification: `currentQuantity <= minimumStockLevel` ⇒ low stock. Stock events carry
`currentQuantity` and `minimumStockLevel`, so the Alert service evaluates the rule without an extra
lookup. It is defined in two places, on purpose:

* `Product.IsLowStock(...)` in the Product service, where a user first sees the question;
* `LowStockDetector.Detect(...)` in `Alert.Domain`, which is the single source of truth for the
  Alert service and classifies the condition as `OUT_OF_STOCK` (zero) or `LOW_STOCK`. At the
  boundary it refuses to raise an alert whose quantity is above the minimum (`Alert.Raise` throws),
  so a bad projection cannot invent one.

The two are kept honest by the event contract: the Product service publishes the minimum stock level
with every stock event, and `CrossService.Contracts.Tests` asserts that the value the Alert service
receives is the value the Product service holds. The Reporting service applies the same rule to its
read model (`StockLevelSnapshot.IsLowStock`) for the low-stock report, without raising anything: it
reports the condition, the Alert service acts on it.
