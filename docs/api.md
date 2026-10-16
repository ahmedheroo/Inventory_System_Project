# Product Service API

Base URL in development: `http://localhost:5251`. Swagger UI: `/swagger` (Development only).

All errors use RFC 7807 `application/problem+json`:

| Situation | Status | Notes |
| --------- | ------ | ----- |
| malformed payload (DataAnnotations) | 400 | `errors` dictionary with per-field messages |
| business rule violated (`DomainException`) | 400 | e.g. negative price |
| referenced data missing (`ValidationException`) | 400 | e.g. unknown category id, duplicate SKU |
| resource missing (`NotFoundException`) | 404 | |
| unique index race (`DbUpdateException`) | 409 | |

## Products

### `GET /api/products`

| Query parameter | Type | Default | Meaning |
| --------------- | ---- | ------- | ------- |
| `search` | string | – | case-insensitive match on SKU or name |
| `categoryId` | int | – | restrict to one category |
| `includeInactive` | bool | `false` | include deactivated products |
| `page` | int | `1` | 1-based page number |
| `pageSize` | int | `20` | clamped to 1..100 |

```json
{
  "items": [
    {
      "id": 2,
      "sku": "SKU-1002",
      "name": "Mechanical Keyboard",
      "description": "RGB backlit keyboard",
      "categoryId": 1,
      "categoryName": "Electronics",
      "unitPrice": 1200.0,
      "minimumStockLevel": 5,
      "isActive": true,
      "createdAt": "2026-10-15T12:22:23.6772077",
      "updatedAt": "2026-10-15T12:22:23.6772077"
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

### `GET /api/products/{id}`

`200` with a single product, or `404`.

### `POST /api/products`

```json
{
  "sku": "sku-5000",
  "name": "Monitor 27in",
  "description": "1440p",
  "categoryId": 1,
  "unitPrice": 8500.5,
  "minimumStockLevel": 3
}
```

- SKU is normalised to upper case and must be unique ⇒ `201 Created` with a `Location` header, or
  `400` (`SKU 'SKU-5000' is already used by another product.`).
- `categoryId` is optional but must exist.
- `unitPrice` and `minimumStockLevel` must be `>= 0`.

### `PUT /api/products/{id}`

Body: `name`, `description`, `categoryId`, `unitPrice`, `minimumStockLevel`. The SKU is immutable
here — use `PUT /api/products/{id}/sku` (`{ "sku": "NEW-SKU" }`).

### `DELETE /api/products/{id}`

Soft delete: marks the product inactive (`204 No Content`). Inactive products stay retrievable by
id and appear only with `includeInactive=true`. `POST /api/products/{id}/activate` reverses it.

## Categories

| Method | Route | Notes |
| ------ | ----- | ----- |
| GET | `/api/categories` | includes `productCount` per category |
| GET | `/api/categories/{id}` | |
| POST | `/api/categories` | `{ "name": "Electronics", "description": "Devices" }`, name unique (case-insensitive) |
| PUT | `/api/categories/{id}` | `{ "name": ..., "description": ... }` |
| DELETE | `/api/categories/{id}` | `204`; `400` if products still reference the category |

## Warehouses

| Method | Route | Notes |
| ------ | ----- | ----- |
| GET | `/api/warehouses` | `?includeInactive=true` to list deactivated warehouses |
| GET | `/api/warehouses/{id}` | |
| POST | `/api/warehouses` | `{ "code": "wh-cai", "name": "Cairo Main", "address": "Cairo" }`; code is upper-cased and unique |
| PUT | `/api/warehouses/{id}` | name/address only; the code is immutable |
| DELETE | `/api/warehouses/{id}` | deactivates |
| POST | `/api/warehouses/{id}/activate` | reactivates |

## Health

`GET /health` → `200 {"status":"healthy","service":"product","database":"reachable"}`,
or `503` when ProductDb cannot be reached.
