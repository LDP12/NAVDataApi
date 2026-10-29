# NAVDataApi: Bug-Fix Exercise

## Overview

Net Asset Value (NAV) represents the total net worth of an investment fund, such as a mutual fund or an ETF. In simple terms, it acts as the "price tag" for a single unit or share of that fund, telling you exactly what it is worth based on its underlying holdings

## Background

NAVDataApi serves fund Net Asset Value (NAV) data. The read endpoints work as intended. The create (`POST`) and update (`PUT`) endpoints were delivered incomplete and contain defects.

Some defects are obvious (errors or unexpected status codes). Others are **silent**: the API reports success but the data is wrong.

## Your task

1. Find and fix the defects in the create and update functionality so that both behave as specified below.
2. For each defect, briefly note:
   - what was wrong
   - how you found it
   - how you fixed it
3. Do not change the read (`GET`) endpoints' behaviour or the request/response contracts.
4. Add a visual reporting interface that consumes the API - This can be done in your language of choice.

Writing automated tests is optional but welcome.

## Getting started

```powershell
dotnet run --launch-profile http
```

- Every request needs an `X-Api-Key` header. Development keys are in `appsettings.Development.json`.
- Swagger UI: `http://localhost:5225/swagger` (click **Authorize** and enter your API key)
- Ready-made requests: `NAVDataApi.http`
- `POST /api/admin/reset` restores your data to its original state at any time.

## Expected behaviour

### Record fields

| Field | Rule |
|---|---|
| `fundCode` | Required, 2–20 characters; stored upper-case |
| `fundName` | Required, max 200 characters |
| `shareClass` | Required, max 50 characters |
| `isin` | Required; 2 letters, 9 alphanumerics, 1 digit (e.g. `ZZ0000000011`) |
| `currency` | Required; 3-letter upper-case ISO code (e.g. `USD`) |
| `navDate` | Required; `yyyy-MM-dd` |
| `navPerShare` | Required; greater than zero; stored with full precision (NAVs are quoted to 4 decimal places) |
| `sharesOutstanding` | Zero or greater |

Only one NAV may exist per share class (`isin`) per `navDate`.

### `POST /api/navs` (create)

| Situation | Response |
|---|---|
| Valid, new ISIN + date | `201 Created`. The body is the stored record with a new, unique `id`. The `Location` header points to `/api/navs/{id}`. |
| Invalid payload | `400 Bad Request` with validation details |
| ISIN + date already exists | `409 Conflict` |

- Creating a record must never change or replace any other record.
- A subsequent `GET /api/navs/{id}` must return exactly what was created.

### `PUT /api/navs/{id}` (update)

| Situation | Response |
|---|---|
| Valid payload, record exists | `200 OK`. The body is the updated record. All fields are replaced with the values sent; `id` is unchanged. |
| Record does not exist | `404 Not Found` |
| Invalid payload | `400 Bad Request` with validation details |
| A **different** record already has the same ISIN + date | `409 Conflict` |

- Updating a record without changing its ISIN or date (for example, correcting the NAV) must succeed.
- A subsequent `GET /api/navs/{id}` must return the updated values.

Neither endpoint should ever return `500 Internal Server Error` for client input.
