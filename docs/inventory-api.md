# Inventory Service API

Base URL in development: `http://localhost:5102`. Swagger UI: `/swagger` (Development only).

The service stores quantities and the stock ledger. It does **not** store product names, prices or
minimum stock levels — those live in the Product service, which must be reachable
(`Services:ProductApi:BaseUrl`, default `http://localhost:5251`). Every stock operation verifies its
product and warehouse against that service before touching a quantity.

## Error responses

All errors are RFC 7807 `application/problem+json`.

| Situation | Status | Example `detail` |
| --------- | ------ | ---------------- |
| malformed payload / non-positive quantity | 400 | model-validation `errors` dictionary |
| issue or transfer-out beyond what is available | 400 | `Cannot issue 100 unit(s): only 15 available for product 1 in warehouse 1.` |
| product or warehouse does not exist / is deactivated | 400 | `Product with id '9999' does not exist.` |
| transfer to the same warehouse, or source has no stock | 400 | `Source and destination warehouse must be different.` |
| two operations race on the same stock row | 409 | `The stock level was changed by another operation. Reload it and try again.` |
| the Product service cannot be reached | 503 | `The Product service could not be reached while resolving '/api/products/1'.` |

## Current stock

### `GET /api/inventory`

| Query parameter | Type | Default | Meaning |
| --------------- | ---- | ------- | ------- |
| `productId` | int | – | only this product |
| `warehouseId` | int | – | only this warehouse |
| `page` | int | `1` | 1-based page number |
| `pageSize` | int | `20` | clamped to 1..100 |

```json
{
  "items": [
    { "id": 1, "productId": 1, "warehouseId": 1, "quantity": 7, "updatedAt": "2026-10-15T13:01:45.9453188" }
  ],
  "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1,
  "hasNextPage": false, "hasPreviousPage": false
}
```

### `GET /api/inventory/{productId}`

Returns a **plain array** (one entry per warehouse) — not a paged envelope:

```json
[
  { "id": 1, "productId": 1, "warehouseId": 1, "quantity": 7, "updatedAt": "..." },
  { "id": 3, "productId": 1, "warehouseId": 2, "quantity": 6, "updatedAt": "..." }
]
```

An unknown product simply returns `[]`.

## Stock operations

All four operations return `200` with the affected stock level (`POST /transfer` returns both), and
`400`/`409`/`503` as described above. `reference` is optional (max 200 chars) and is stored on the
ledger entry.

### `POST /api/inventory/receive`

```json
{ "productId": 1, "warehouseId": 1, "quantity": 20, "reference": "PO-1" }
```

Creates the stock row when it does not exist yet. Records a `RECEIVE` transaction and publishes
`stock.received`.

### `POST /api/inventory/issue`

```json
{ "productId": 1, "warehouseId": 1, "quantity": 5, "reference": "SO-77" }
```

Refused when `quantity` exceeds what is on hand, or when the warehouse holds no stock for the
product. Records an `ISSUE` transaction and publishes `stock.issued`.

### `POST /api/inventory/transfer`

```json
{ "productId": 1, "sourceWarehouseId": 1, "destinationWarehouseId": 2, "quantity": 6 }
```

Creates the destination stock row if needed, records **two** transactions (`TRANSFER_OUT`,
`TRANSFER_IN`) in the same commit, and publishes `stock.transferred`:

```json
{
  "productId": 1,
  "quantity": 6,
  "source":      { "id": 1, "productId": 1, "warehouseId": 1, "quantity": 9, "updatedAt": "..." },
  "destination": { "id": 3, "productId": 1, "warehouseId": 2, "quantity": 6, "updatedAt": "..." }
}
```

If either leg fails, nothing is written: both quantities and both ledger entries share one
`SaveChanges`.

### `POST /api/inventory/adjust`

```json
{ "productId": 1, "warehouseId": 1, "newQuantity": 7, "reference": "Annual count" }
```

Sets the counted quantity (must be `>= 0`), records an `ADJUSTMENT` transaction carrying the
**signed delta** (`-2` when going from 9 to 7), and publishes `stock.adjusted` with the old and new
quantities. Default reference (`Adjusted from 9 to 7`) is supplied when none is given.

## History

### `GET /api/inventory/history`

| Query parameter | Type | Meaning |
| --------------- | ---- | ------- |
| `productId` | int | filter by product |
| `warehouseId` | int | filter by warehouse |
| `type` | enum name | `RECEIVE`, `ISSUE`, `TRANSFER_IN`, `TRANSFER_OUT`, `ADJUSTMENT` |
| `from`, `to` | ISO-8601 date-time | inclusive bounds on `createdAt` |
| `page`, `pageSize` | int | paging, default size 20 (max 200) |

Newest first. `quantity` is the magnitude of the movement for RECEIVE / ISSUE / TRANSFER_IN /
TRANSFER_OUT and the signed delta for ADJUSTMENT; `signedQuantity` normalises both into a signed
value (`+` in, `-` out) so a movement report can simply sum it.

```json
{
  "items": [
    {
      "id": 5, "productId": 1, "warehouseId": 1,
      "type": 4, "quantity": -2, "signedQuantity": -2,
      "reference": "Annual count", "createdAt": "2026-10-15T13:01:45.9539344"
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 5, "totalPages": 1,
  "hasNextPage": false, "hasPreviousPage": false
}
```

`type` serialises as its numeric enum value: `RECEIVE = 0`, `ISSUE = 1`, `TRANSFER_IN = 2`,
`TRANSFER_OUT = 3`, `ADJUSTMENT = 4`.

## Events published

Each successful operation publishes one event to the RabbitMQ topic exchange `inventory.events`
(routing key = event name). Payloads are defined in
`src/BuildingBlocks/InventorySystem.BuildingBlocks.Contracts/Events`. They carry
`currentQuantity` and `minimumStockLevel` so the Alert service can apply the low-stock rule
(`currentQuantity <= minimumStockLevel`) without any further lookup.

## Live updates (SignalR)

`/hubs/stock` is a SignalR hub that broadcasts one notification per **committed** stock change to
every connected client. It is the live view of stock: a broadcast, not a feed — there is no
subscription state and nothing to catch up on, so a client that connects late reads the current state
from the REST API and follows the notifications from there.

| When | What is sent |
| ---- | ------------ |
| a receive, issue or adjustment commits | one `StockChanged` carrying the change, the quantity that moved, the level it left and the product's minimum at that moment |
| a transfer commits | **one** `StockChanged` carrying both legs, not two |
| an operation is refused | nothing — the same rule as the outbox: only committed facts are announced |
| a client is slow, gone or never connected | logged and dropped; the operation has already been committed and answered |

The client method is `StockChanged`, and its argument is:

```json
{
  "change": "RECEIVE",
  "productId": 5,
  "warehouseId": 3,
  "quantity": 10,
  "currentQuantity": 14,
  "minimumStockLevel": 10,
  "destinationWarehouseId": null,
  "destinationCurrentQuantity": null,
  "origin": "4f2ab19c"
}
```

`change` is `RECEIVE`, `ISSUE`, `TRANSFER` or `ADJUSTMENT`; `quantity` is the magnitude of the
movement (the size of the correction, for an adjustment). A transfer fills the two destination fields
and leaves `warehouseId` as the source. This is deliberately not the integration event: a screen needs
the movement and the state it left behind, not the payload the Alert and Reporting services project.
The hub protocol's field casing is not part of this contract — read it case-insensitively.

### Who asked for it

A caller may identify itself with the request header **`X-Origin-Id`** on a stock operation, and the
value comes back as `origin` on the notification. Several people work the same system at once, and a
screen that says "something moved" is less useful than one that says which of those movements was
theirs; the console sends the station its browser belongs to, and a screen compares it with its own
to tell "your receipt was confirmed" from "another terminal moved something".

The label is sanitised before it is echoed: letters, digits, `-`, `_` and `.` are kept, up to 40
characters, and a value with nothing usable in it is dropped along with the rest — so `dock-3` travels
as `dock-3`, `  DOCK 7  ` as `DOCK7`, and `!!! ***` as no origin at all. **It is a label, not a
credential**: nothing is authorised by it, it is visible to every client connected to the hub, and an
operation without one is a perfectly ordinary operation (a script, a health check, another service).
Authentication is the thing that will make "who" a real question, and it is not built yet.

The hub is anonymous and read-only — there is no server method to call — and it is subject to the same
CORS policy as the REST API: any origin in `Development`, and `Cors:AllowedOrigins` everywhere
else, so a browser whose origin is not listed cannot connect. To check a hub is reachable:

```bash
curl -X POST "http://localhost:5102/hubs/stock/negotiate?negotiateVersion=1"
# {"negotiateVersion":1,"connectionId":"...","availableTransports":[...]}
```

## Health

`GET /health` → `200 {"status":"healthy","service":"inventory","database":"reachable"}`, or `503`
when InventoryDb cannot be reached.
