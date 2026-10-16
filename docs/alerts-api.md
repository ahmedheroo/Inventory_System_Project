# Alert Service API

Base URL in development: `http://localhost:5212`. Swagger UI: `/swagger` (Development only).

The Alert service owns **AlertDb** and has no create endpoint: alerts exist only as the projection of
inventory events. A stock event that drops a quantity to/below the product's minimum stock level
raises an alert here; see [architecture.md](architecture.md) for the flow.

All errors use RFC 7807 `application/problem+json`:

| Situation | Status |
| --------- | ------ |
| malformed query parameter | 400 |
| alert id does not exist (`NotFoundException`) | 404 |
| unexpected failure | 500 |

## Enumerations

| `type` | Meaning |
| ------ | ------- |
| `0` `LOW_STOCK` | quantity at or below the minimum, but still above zero |
| `1` `OUT_OF_STOCK` | quantity has reached zero |

| `status` | Meaning |
| -------- | ------- |
| `0` `ACTIVE` | the condition still holds |
| `1` `RESOLVED` | a later event showed the quantity is back above the minimum |

Both are serialized as **numbers** (their enum values); the `message` carries the human-readable
description.

## `GET /api/alerts`

Newest first, paged.

| Query parameter | Type | Default | Meaning |
| --------------- | ---- | ------- | ------- |
| `productId` | int | – | restrict to one product |
| `warehouseId` | int | – | restrict to one warehouse |
| `type` | enum | – | `0` low stock, `1` out of stock |
| `isRead` | bool | – | `true` read, `false` unread |
| `status` | enum | `0` (ACTIVE) | `0` active, `1` resolved |
| `page` | int | `1` | 1-based page number |
| `pageSize` | int | `20` | clamped to 1..100 |

The default `status` filter is `ACTIVE`, which is what a dashboard wants; pass `status=1` to read the
resolved history (an alert that was resolved by a replenishment is kept, not deleted).

```json
{
  "items": [
    {
      "id": 1,
      "productId": 5,
      "warehouseId": 3,
      "type": 0,
      "message": "Product 5 is low on stock in warehouse 3: 8 left (minimum stock level 10).",
      "currentQuantity": 8,
      "minimumStockLevel": 10,
      "isRead": false,
      "status": 0,
      "createdAt": "2026-10-15T15:51:51.6216263",
      "updatedAt": "2026-10-15T15:51:51.6216263",
      "readAt": null,
      "resolvedAt": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

## `GET /api/alerts/{id}`

`200` with a single alert, or `404` when the id is unknown.

## `PUT /api/alerts/{id}/read`

Marks an alert as read and returns the updated alert. **Idempotent:** reading an already-read alert
is a success and keeps the original `readAt`, so the endpoint is safe to retry. `404` for an unknown
id.

```json
{
  "id": 1,
  "isRead": true,
  "readAt": "2026-10-15T15:56:02.1130000",
  "updatedAt": "2026-10-15T15:56:02.1130000"
}
```

Read state survives a refresh: a newer event for the same condition updates `currentQuantity`,
`type` and `message` but never marks an alert unread again.

## Health

`GET /health` → `200 {"status":"healthy","service":"alert","database":"reachable"}`, or `503` when
AlertDb cannot be reached.

## How an alert changes over time

| Event the consumer receives | Alert state |
| --------------------------- | ----------- |
| quantity at/below the minimum, no active alert | new `ACTIVE` alert (`LOW_STOCK`, or `OUT_OF_STOCK` at zero) |
| quantity at/below the minimum, an active alert exists | that alert is **refreshed** — one alert per condition, not one per event |
| quantity back above the minimum, an active alert exists | the alert is `RESOLVED` |
| quantity back above the minimum, then low again | a new alert; the resolved one stays in the history |
| a transfer | each affected warehouse is evaluated independently, so one transfer can raise, refresh or resolve alerts in one or both warehouses |
| the same event delivered twice | ignored (de-duplicated on the event id), so no duplicate alert |
