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

> Until Sprint 4 wires up RabbitMQ, the registered publisher writes the routed event and its payload
> to the service log.

## Health

`GET /health` → `200 {"status":"healthy","service":"inventory","database":"reachable"}`, or `503`
when InventoryDb cannot be reached.
