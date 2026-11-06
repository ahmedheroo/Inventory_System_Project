# Reporting Service API

Base URL in development: `http://localhost:5312`. Swagger UI: `/swagger` (Development only).

The Reporting service owns **ReportingDb** and is read-only over HTTP: it consumes the stock events
the Inventory service publishes and projects them into a read model that the four report endpoints
serve (specification section 7.4).

| Read-model table | What it holds |
| ---------------- | ------------- |
| `StockLevels` | one row per (product, warehouse): the **current** quantity, the minimum stock level of the latest event, and that event's id and timestamp |
| `StockMovements` | an append-only ledger: one row per movement (a transfer contributes two — `TRANSFER_OUT` and `TRANSFER_IN`) |
| `ProcessedEvents` | one row per consumed event id, which makes redelivery impossible to double-count |

The read model stores **ids only**, because that is what an event carries. Sku, names, categories and
unit prices are resolved from the Product service when a report is rendered (one catalogue read per
request, not one call per row) — which is what makes the valuation report possible and why an
unreachable Product service answers `503`.

All errors use RFC 7807 `application/problem+json`:

| Situation | Status |
| --------- | ------ |
| a date range that cannot match anything (`from` after `to`) | 400 |
| the Product service cannot be reached | 503 |
| unexpected failure | 500 |

## Enumerations

`type` in the movement report is serialized as a **number** (the value of the shared
`StockTransactionType` contract):

| `type` | Meaning | Ledger direction |
| ------ | ------- | ---------------- |
| `0` `RECEIVE` | stock received | `+quantity` |
| `1` `ISSUE` | stock issued | `-quantity` |
| `2` `TRANSFER_IN` | stock arrived from another warehouse | `+quantity` |
| `3` `TRANSFER_OUT` | stock left for another warehouse | `-quantity` |
| `4` `ADJUSTMENT` | stock count corrected | the signed delta (`newQuantity - oldQuantity`) |

`status` in the low-stock report:

| `status` | Meaning |
| -------- | ------- |
| `0` `LOW_STOCK` | at or below the minimum, still above zero |
| `1` `OUT_OF_STOCK` | quantity has reached zero |

## `GET /api/reports/inventory`

The current inventory report: every projected stock level with its catalogue data and value.

| Query parameter | Type | Default | Meaning |
| --------------- | ---- | ------- | ------- |
| `productId` | int | – | restrict to one product |
| `warehouseId` | int | – | restrict to one warehouse |
| `page` | int | `1` | 1-based page number |
| `pageSize` | int | `20` | clamped to 1..100 |

`summary` covers the **whole filtered set**, not just the returned page. `value` is
`quantity × unitPrice`, and `isLowStock` is the specification's rule
(`quantity <= minimumStockLevel`) — an out-of-stock row counts as low stock as well.

```json
{
  "items": [
    {
      "productId": 6,
      "sku": "SKU-1001",
      "productName": "Mechanical Keyboard",
      "warehouseId": 5,
      "warehouseCode": "WH-CAI",
      "warehouseName": "Cairo Main",
      "quantity": 9,
      "minimumStockLevel": 5,
      "unitPrice": 12.5,
      "value": 112.5,
      "isLowStock": false,
      "isOutOfStock": false,
      "updatedAt": "2026-10-16T15:17:38.2493508"
    }
  ],
  "summary": {
    "productCount": 2,
    "warehouseCount": 2,
    "stockLevelCount": 3,
    "totalQuantity": 17,
    "totalValue": 136.5,
    "lowStockCount": 1,
    "outOfStockCount": 1
  },
  "page": 1,
  "pageSize": 20,
  "totalCount": 3,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

## `GET /api/reports/low-stock`

Every stock level at or below its minimum, **worst first**: out of stock, then the largest shortfall.
`shortfall` is `minimumStockLevel - quantity`.

| Query parameter | Type | Default | Meaning |
| --------------- | ---- | ------- | ------- |
| `productId` | int | – | restrict to one product |
| `warehouseId` | int | – | restrict to one warehouse |
| `includeOutOfStock` | bool | `true` | `false` leaves the zero-quantity rows out |
| `page` / `pageSize` | int | `1` / `20` | as above |

```json
{
  "items": [
    {
      "productId": 7,
      "sku": "SKU-1002",
      "productName": "Wireless Mouse",
      "warehouseId": 6,
      "warehouseCode": "WH-ALX",
      "warehouseName": "Alexandria",
      "quantity": 0,
      "minimumStockLevel": 2,
      "shortfall": 2,
      "status": 1,
      "updatedAt": "2026-10-16T15:13:04.7517824"
    },
    {
      "productId": 6,
      "sku": "SKU-1001",
      "productName": "Mechanical Keyboard",
      "warehouseId": 5,
      "warehouseCode": "WH-CAI",
      "warehouseName": "Cairo Main",
      "quantity": 4,
      "minimumStockLevel": 5,
      "shortfall": 1,
      "status": 0,
      "updatedAt": "2026-10-16T15:13:04.3484683"
    }
  ],
  "summary": {
    "lowStockCount": 1,
    "outOfStockCount": 1,
    "totalShortfall": 3,
    "affectedProducts": 2,
    "affectedWarehouses": 2
  },
  "page": 1,
  "pageSize": 20,
  "totalCount": 2,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

## `GET /api/reports/movement`

The movement ledger, newest first, with the movements the Alert service never sees: transfers are two
rows, and an adjustment carries its signed delta.

| Query parameter | Type | Default | Meaning |
| --------------- | ---- | ------- | ------- |
| `productId` | int | – | restrict to one product |
| `warehouseId` | int | – | restrict to one warehouse |
| `type` | enum | – | `0`…`4`, see the table above |
| `from` | datetime | – | **inclusive** lower bound on when the stock moved |
| `to` | datetime | – | **inclusive** upper bound; `from` after `to` is a `400` |
| `page` | int | `1` | 1-based page number |
| `pageSize` | int | `20` | clamped to 1..200 |

`occurredAt` is the timestamp of the stock operation (the event's own timestamp), not the moment the
projection ran, so the report and its date filter describe when the stock actually moved. `summary`
aggregates the whole filtered set: `netChange` is `+received +transferred in +adjustments
-issued -transferred out`, which is why it matches the total quantity change over the same period.

```json
{
  "items": [
    {
      "id": 8,
      "productId": 6,
      "sku": "SKU-1001",
      "productName": "Mechanical Keyboard",
      "warehouseId": 5,
      "warehouseCode": "WH-CAI",
      "warehouseName": "Cairo Main",
      "type": 0,
      "quantity": 5,
      "signedQuantity": 5,
      "quantityAfter": 9,
      "occurredAt": "2026-10-16T15:17:38.2493508"
    }
  ],
  "summary": {
    "totalMovements": 8,
    "totalReceived": 35,
    "totalIssued": 17,
    "totalTransferred": 16,
    "totalAdjustedUp": 0,
    "totalAdjustedDown": 1,
    "netChange": 17
  },
  "page": 1,
  "pageSize": 20,
  "totalCount": 8,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

## `GET /api/reports/valuation`

`quantity × unitPrice` per (product, warehouse), most valuable first, plus a per-warehouse roll-up.

| Query parameter | Type | Default | Meaning |
| --------------- | ---- | ------- | ------- |
| `warehouseId` | int | – | restrict to one warehouse |
| `categoryId` | int | – | restrict to the products of one category (resolved from the catalogue) |
| `page` / `pageSize` | int | `1` / `20` | as above |

`unitPrice` is the product's **current** price: the read model does not keep a price history, so a
valuation always reflects today's catalogue.

```json
{
  "items": [
    {
      "productId": 6,
      "sku": "SKU-1001",
      "productName": "Mechanical Keyboard",
      "categoryId": 1,
      "warehouseId": 5,
      "warehouseCode": "WH-CAI",
      "warehouseName": "Cairo Main",
      "quantity": 9,
      "unitPrice": 12.5,
      "value": 112.5
    }
  ],
  "byWarehouse": [
    {
      "warehouseId": 5,
      "warehouseCode": "WH-CAI",
      "warehouseName": "Cairo Main",
      "quantity": 17,
      "value": 136.5
    }
  ],
  "summary": {
    "productCount": 2,
    "warehouseCount": 2,
    "stockLevelCount": 3,
    "totalQuantity": 17,
    "totalValue": 136.5
  },
  "page": 1,
  "pageSize": 20,
  "totalCount": 3,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

## Downloads

Each report is also served as a file, with the same filters and **every** matching row — a download
is the report on screen, not the page of it:

| Method | Route |
| ------ | ----- |
| GET | `/api/reports/inventory/export?format=csv\|xlsx` |
| GET | `/api/reports/low-stock/export?format=csv\|xlsx` |
| GET | `/api/reports/movement/export?format=csv\|xlsx` |
| GET | `/api/reports/valuation/export?format=csv\|xlsx` |

`format` defaults to `csv`; anything else is a `400`. Every other query parameter is the report's own
(`productId`, `warehouseId`, `type`, `from`, `to`, `categoryId`, `includeOutOfStock`) and `page`/
`pageSize` are accepted and ignored, so a caller can forward the query it already has.

| | |
| --- | --- |
| `Content-Type` | `text/csv`, or `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` |
| `Content-Disposition` | `attachment; filename=inventory-report-20261016-1600.csv` (the generation stamp keeps two downloads apart) |
| CSV | RFC 4180: `CRLF` rows, every field that holds a comma, a quote or a line break quoted, **UTF-8 with a BOM** so Excel detects the encoding, timestamps as `yyyy-MM-dd HH:mm:ss`, decimals with a dot. The totals follow the table after a blank line as label/value rows, so the file is complete on its own. |
| XLSX | A real Open XML workbook — one sheet per table plus a summary sheet, numbers written as numbers (a quantity column can be summed), built with `System.IO.Compression` and no spreadsheet dependency. |
| Valuation | two tables: the rows, and the per-warehouse roll-up |

```bash
# the whole inventory report, filters included
curl -OJ 'http://localhost:5312/api/reports/inventory/export?format=xlsx&warehouseId=2'
```

The console proxies these endpoints (`/reports/{report}/export?format=csv|xlsx`) so the browser only
ever talks to the web app; see [web-ui.md](web-ui.md).

## Health

`GET /health` → `200 {"status":"healthy","service":"reporting","database":"reachable"}`, or `503`
when ReportingDb cannot be reached.

## How a report changes

| Event the consumer receives | Read model |
| --------------------------- | ---------- |
| `stock.received` | the (product, warehouse) level becomes `currentQuantity`; one `RECEIVE` movement is appended |
| `stock.issued` | the level becomes `currentQuantity`; one `ISSUE` movement |
| `stock.transferred` | **two** levels (source and destination) and **two** movements, `TRANSFER_OUT` then `TRANSFER_IN` |
| `stock.adjusted` | the level becomes `newQuantity`; one `ADJUSTMENT` movement holding the signed delta |
| the same event delivered twice | ignored — the event id is already in `ProcessedEvents`, so neither the level nor the ledger changes |
| an event for an unknown product or warehouse | still projected (ids), and the report labels the row `Unknown product 12` / `#12` with a value of `0` |

Two properties follow from the design and are worth remembering when reading the endpoints:

- **the read model is current state plus history, never a log of levels** — `StockLevels` is an
  upsert per (product, warehouse) pair, so a fluctuating quantity produces one row and many
  movements;
- **the movement report is paged and aggregated by the database** (the ledger grows without bound),
  while the stock-level reports load their filtered set and page after the catalogue has been applied
  — one row per pair is a bounded set, and a value column cannot be summed in SQL.
