# NAV Data API — Implementation Summary

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-Web_API-512BD4)
![Tests](https://img.shields.io/badge/Tests-167_Passing-2EA44F)
![Build](https://img.shields.io/badge/Build-Passing-2EA44F)
![Warnings](https://img.shields.io/badge/Warnings-0-2EA44F)
![Errors](https://img.shields.io/badge/Errors-0-2EA44F)

## Overview

The solution retains the supplied ASP.NET Core and in-memory repository architecture while correcting the original API defects, strengthening validation and financial precision, and adding a responsive reporting dashboard.

The implementation deliberately avoids unnecessary architectural changes. The goal was to fix each problem at its source while preserving the existing API contract and keeping the proof of concept straightforward to run, review, and demonstrate.

---

## Table of Contents

1. [Create Endpoint](#1-create-endpoint)
2. [Update Endpoint](#2-update-endpoint)
3. [Validation and Decimal Precision](#3-validation-and-decimal-precision)
4. [Existing GET Behaviour](#4-existing-get-behaviour)
5. [Thread-Safe Writes](#5-thread-safe-writes)
6. [Reporting Dashboard](#6-reporting-dashboard)
7. [Accurate Financial Reporting](#7-accurate-financial-reporting)
8. [Browser Decimal Precision](#8-browser-decimal-precision)
9. [API-Key Safety](#9-api-key-safety)
10. [Stable Report Filters](#10-stable-report-filters)
11. [Swagger and OpenAPI Example](#11-swagger-and-openapi-example)
12. [Automated and Live Verification](#12-automated-and-live-verification)
13. [Assessment Documentation](#13-assessment-documentation)
14. [Interview Summary](#14-interview-summary)

---

## 1. Create Endpoint

### Problem

The original create operation contained several issues:

- `POST` was mapped to `/api/navs/{id}` instead of `/api/navs`.
- The response's `Location` header pointed to `/api/nav/{id}` instead of `/api/navs/{id}`.
- New IDs were not incremented correctly.
- Later create operations could overwrite earlier records.

### Changes Made

- Changed the route to:

  ```http
  POST /api/navs
  ```

- Corrected the successful response's `Location` header:

  ```http
  Location: /api/navs/{id}
  ```

- Incremented `_nextId` after every successful creation.
- Used `Dictionary.Add` so an accidental duplicate ID fails instead of silently replacing an existing record.
- Kept ID allocation, duplicate checking, and insertion inside the repository lock.

### Why This Approach

A create operation represents a request to add a new resource to a collection. The server should therefore generate the resource ID, rather than requiring the client to provide it in the URL.

This solution preserves the existing controller and repository structure while correcting the defects where they originated.

### Alternatives Considered

| Alternative | Reason It Was Not Used |
|---|---|
| Client-generated IDs | The server should remain responsible for identity generation. |
| GUID identifiers | GUIDs are valid, but unnecessary because the existing API contract uses integers. |
| Replacing the repository with a database | This would be excessive for an intentionally in-memory assessment. |

> [!NOTE]
> A successful create now returns `201 Created`, together with the newly created record and a valid `Location` header.

---

## 2. Update Endpoint

### Problem

The original update operation had three significant defects:

1. Updating an ID that did not exist could throw an exception and potentially return `500 Internal Server Error`.
2. A record could conflict with itself when its `ISIN` and `navDate` remained unchanged.
3. The API returned an updated object without actually saving the replacement in the repository.

The third issue was particularly dangerous because the response appeared successful even though a subsequent `GET` still returned the old values.

### Changes Made

- Check safely whether the requested ID exists.
- Return `404 Not Found` when the record does not exist.
- Exclude the current record's ID when checking for duplicate `ISIN + navDate` combinations.
- Store the replacement record back in the dictionary.
- Preserve the original record ID while replacing the other fields.

### Why This Approach

The requirements define `PUT` as a **full replacement** of an existing resource.

The corrected workflow is:

```text
Find the record
      ↓
Return 404 if missing
      ↓
Validate duplicate rules
      ↓
Build the replacement
      ↓
Store the replacement
      ↓
Return 200 OK
```

### Alternatives Considered

| Alternative | Reason It Was Not Used |
|---|---|
| Treating `PUT` as an upsert | The specification states that missing records must return `404`. |
| Implementing `PATCH` behaviour | This would change the existing request contract. |
| Mutating fields individually | Creating a replacement is clearer and better matches `PUT` semantics. |

> [!IMPORTANT]
> The update is now verified through a follow-up `GET`, proving that the new values were actually stored.

---

## 3. Validation and Decimal Precision

### Problem

The original validation and financial calculations allowed several unsafe situations:

- `navDate` could be omitted and later cause a null-related exception.
- Zero or negative NAV values were accepted.
- NAV values were rounded to two decimal places during storage.
- Large decimal values could overflow when calculating `totalNetAssets`.
- A padded fund code such as `" X "` could pass initial validation but become invalid after trimming.

### Changes Made

- Made `navDate` required.
- Required `navPerShare` to be greater than zero.
- Preserved the submitted NAV value without rounding it during storage.
- Validated the calculated decimal product before saving the record.
- Validated the fund-code length after normalisation.
- Continued using C# `decimal` for all financial values.

### Why This Approach

Validation now happens at the API boundary, before invalid data can reach the repository.

C# `decimal` was retained because it is designed for exact base-10 arithmetic and is better suited to financial values than binary floating-point types.

### Alternatives Considered

| Alternative | Reason It Was Not Used |
|---|---|
| Using `double` | Binary floating-point arithmetic can introduce precision errors. |
| Catching every exception and returning `400` | This would hide the actual validation rule and make failures harder to understand. |
| Rounding NAV to two decimal places | NAV values are unit prices and may legitimately require greater precision. |
| Changing the response DTO | The instructions required the existing request and response contract to be preserved. |

> [!TIP]
> Monetary totals are calculated with `decimal`, while the submitted NAV precision is retained exactly.

---

## 4. Existing GET Behaviour

### Changes Made

No functional changes were made to the existing `GET` endpoints.

Regression tests were added to confirm that:

- Filtering still works.
- Existing ordering remains unchanged.
- Latest-per-ISIN behaviour still works.
- Missing records still return `404 Not Found`.

### Why This Approach

The assessment explicitly allowed the existing `GET` behaviour to remain unchanged.

Refactoring functionality that was already working would have increased the risk of regression without adding meaningful value.

> [!NOTE]
> The working read behaviour was protected with tests rather than rewritten unnecessarily.

---

## 5. Thread-Safe Writes

### Changes Made

The repository lock remains responsible for protecting the complete write operations, including:

- Duplicate checking
- ID allocation
- Dictionary insertion
- Record replacement

Concurrency tests were added to verify this behaviour.

### Why This Approach

The duplicate check and write must be treated as one atomic operation:

```text
Check for duplicate
        ↓
Allocate an ID
        ↓
Write the record
```

If these steps were protected separately, concurrent requests could both pass the duplicate check before either one completed the write.

A concurrent collection by itself would not make the entire multi-step operation atomic.

### Alternative Considered

Moving to a database with transactions would be appropriate for a production system, but it would be unnecessarily complex for this deliberately in-memory sandbox.

> [!WARNING]
> Thread-safe collections do not automatically make a sequence of related business operations atomic.

---

## 6. Reporting Dashboard

### Features Added

A responsive browser interface was added with:

- API-key connection
- Fund, currency, ISIN, and date filters
- Latest and historical data views
- Currency-separated valuation summaries
- Per-share-class trend charts
- Sortable data table
- Create and edit forms
- CSV export
- Loading states
- Validation feedback
- Empty states
- Error handling
- Desktop and mobile layouts

### Why Plain HTML, CSS, and JavaScript Were Used

The assessment prioritises simplicity, usability, and ease of review.

Serving a static frontend from the existing ASP.NET Core application provides:

- One application to run
- No separate frontend server
- No CORS configuration
- No npm dependencies
- No additional build pipeline
- A straightforward review process for the assessor

### Alternatives Considered

| Alternative | Reason It Was Not Used |
|---|---|
| React, Vue, or Angular | Appropriate for a larger application, but unnecessary complexity for this scope. |
| Razor Pages or MVC views | Workable, but a JavaScript client demonstrates the API boundary more clearly. |
| A separate frontend project | Adds setup and deployment work without meaningful value for this proof of concept. |

---

## 7. Accurate Financial Reporting

### Changes Made

The reporting logic follows these rules:

- Historical NAV observations are not added together.
- Snapshot valuations use the latest eligible observation for each ISIN.
- Different currencies are displayed separately.
- Charts show one share class and currency at a time.
- Missing dates are not invented or interpolated.
- Observation-date ranges are clearly labelled.

### Why This Approach

Adding historical observations together would double-count the same share class across multiple dates.

Similarly, combining values from different currencies without exchange-rate data would produce a misleading total.

### Alternatives Considered

| Alternative | Reason It Was Not Used |
|---|---|
| A single cross-currency grand total | No foreign-exchange data is available. |
| Summing every historical row | Historical rows are observations over time, not separate holdings. |
| Interpolating missing chart points | The API did not provide those values. |

> [!CAUTION]
> A financial report should not produce totals that appear precise but are based on invalid assumptions.

---

## 8. Browser Decimal Precision

### Risk Identified

JavaScript normally converts JSON numbers into binary floating-point values.

That conversion can lose precision before financial calculations or CSV exports are performed.

### Changes Made

- Preserve financial number tokens from the API as strings.
- Use `BigInt`-based decimal arithmetic for sorting and summary calculations.
- Export the original decimal strings to CSV.
- Use ordinary JavaScript numbers only for visual chart coordinates.

### Why This Approach

This keeps the financial values exact without:

- Adding a large third-party decimal library
- Changing the API contract
- Returning all decimals as JSON strings

### Alternatives Considered

| Alternative | Reason It Was Not Used |
|---|---|
| Converting every value with `Number()` | This could lose financial precision. |
| Adding a decimal package | An additional dependency was unnecessary for this interface. |
| Returning decimals as JSON strings | This would alter the existing API response contract. |

---

## 9. API-Key Safety

### Changes Made

The frontend handles the API key as follows:

- The key is kept only in page memory.
- It is sent through the `X-Api-Key` request header.
- It is never placed in the URL.
- It is not written to browser storage.
- Disconnecting or leaving the page clears the in-memory session.

Authenticated API responses also include:

```http
Cache-Control: private, no-store
Vary: X-Api-Key
```

Application startup validates that configured client names and keys are:

- Not blank
- Unique

### Why This Approach

The implementation preserves the supplied API-key authentication design while reducing obvious browser-storage and caching risks.

### Alternatives Considered

| Alternative | Reason It Was Not Used |
|---|---|
| Saving keys in `localStorage` | Convenient, but increases the duration and surface of credential exposure. |
| Building a complete login and JWT system | Outside the assessment scope. |
| Embedding a development key in the frontend | Unsafe and incompatible with isolated client datasets. |

> [!IMPORTANT]
> Development API keys are configuration values and are never embedded in the public frontend assets.

---

## 10. Stable Report Filters

### Problem

After editing the only record belonging to a selected fund or currency, that option could disappear from the available values.

The interface could then silently change the selection to **All**, unexpectedly widening the report scope and changing its totals.

### Changes Made

The selected value remains visible and is clearly labelled:

```text
OLD · no longer in current data
```

The report then displays an honest empty result until the user explicitly changes the filter.

### Why This Approach

A financial report should never broaden its scope silently.

### Alternative Considered

Automatically selecting **All** was rejected because this could change the displayed totals without an explicit user decision.

---

## 11. Swagger and OpenAPI Example

### Problem

Swagger automatically generated an unrealistic request body for `POST /api/navs`.

One generated value was:

```json
{
  "sharesOutstanding": 7.922816251426434e+28
}
```

The floating-point representation was slightly larger than the maximum value supported by C# `decimal`, resulting in:

```text
400 Bad Request
```

Swagger also generated an invalid ISIN and several values that were technically possible but unsuitable for a demonstration.

### Changes Made

A realistic OpenAPI example was added for `NavRecordRequest`:

```json
{
  "fundCode": "TEST",
  "fundName": "Test Investment Fund",
  "shareClass": "A Acc",
  "isin": "ZZ9999999981",
  "currency": "USD",
  "navDate": "2026-10-21",
  "navPerShare": 125.4321,
  "sharesOutstanding": 1000000
}
```

The example now appears automatically when `POST /api/navs` is expanded in Swagger.

### Why This Approach

The example is attached to the OpenAPI schema using the native .NET OpenAPI functionality already included in the project.

This avoids:

- Weakening backend validation
- Adding an unnecessary Swagger package
- Hardcoding changes into the Swagger user interface
- Altering the request contract

A regression test verifies the generated `/openapi/v1.json` document and confirms that the realistic example remains present.

> [!NOTE]
> The first submission of a unique `ISIN + navDate` returns `201 Created`. Repeating the same request correctly returns `409 Conflict`.

---

## 12. Automated and Live Verification

### Verification Added

The project includes:

- ASP.NET Core integration tests
- Repository tests
- Concurrency tests
- OpenAPI example tests
- Frontend reporting tests
- Frontend session and network tests
- A native verification script
- A local HTTP smoke test
- Desktop browser checks
- Mobile browser checks

### Verification Strategy

Several defects in the original project were silent.

For example, the update endpoint could return a successful-looking response even though the updated record had not actually been stored.

Tests therefore use the following pattern:

```text
Write
  ↓
Inspect the response
  ↓
Read the record again
  ↓
Compare the stored values
```

Testing only HTTP status codes would not have detected the original persistence defect.

### AI-Assisted Test Development

I used **Hermes Agent** to assist with writing and expanding the automated tests. Its role was to help identify and exercise edge cases and realistic failure scenarios so that the implementation could be checked thoroughly for **reliability** and **accuracy**.

The AI-assisted test coverage includes:

- Successful `POST` and `PUT` operations with follow-up `GET` read-back
- Missing records and malformed or incomplete request bodies
- Duplicate `ISIN + navDate` conflicts and rejected-write side effects
- Decimal precision, boundary values, and calculated-value overflow
- Concurrent creates, duplicate creates, and competing updates
- API-key authentication, client isolation, reset behaviour, and cache headers
- Existing `GET` filtering, ordering, and latest-record behaviour
- OpenAPI request-example generation
- Frontend filtering, reporting calculations, CSV safety, request races, and error states

> [!IMPORTANT]
> Hermes Agent was used as a development and verification aid. The resulting tests were executed against the real application, their failures were investigated, and the final assertions and observed results were reviewed rather than accepted solely because they were AI-generated.

This use of AI improved the breadth of the test suite while keeping the verification evidence reproducible through the repository's standard test commands.

### Final Verified Result

| Check | Result |
|---|---:|
| .NET tests | **140 passed** |
| Frontend tests | **27 passed** |
| Total automated tests | **167 passed** |
| Build warnings | **0** |
| Build errors | **0** |

![Verification](https://img.shields.io/badge/Verification-Complete-2EA44F)
![Backend Tests](https://img.shields.io/badge/Backend_Tests-140_Passed-2EA44F)
![Frontend Tests](https://img.shields.io/badge/Frontend_Tests-27_Passed-2EA44F)

---

## 13. Assessment Documentation

### Documentation Added

The project documentation includes:

- Scope and implementation approach
- Architecture explanation
- Architecture diagram
- Estimated task breakdown
- Defect register
- Quality and reliability notes
- Demonstration walkthrough
- AI-tool disclosure
- Selected prompts
- Actual verification evidence

### Why This Approach

These documents address the assessment deliverables and make the implementation easier to:

- Review
- Demonstrate
- Maintain
- Explain during an interview

Estimated effort is clearly labelled as an estimate, while test logs and verification results are labelled as actual evidence.

No working hours or Git history were fabricated.

---

## 14. Summary

> I retained the existing architecture and corrected the defects at the controller, validation, and repository boundaries.
>
> The main problems were incorrect POST routing, reused IDs, an incorrect `Location` header, unsafe access to missing records, updates conflicting with themselves, updates not being persisted, and loss of decimal precision.
>
> I added read-back and concurrency tests because several of the original defects could return successful responses while storing incorrect data.
>
> I used Hermes Agent to assist with writing comprehensive tests and identifying edge cases so that the solution could be checked for reliability and accuracy. I reviewed and executed the resulting tests against the real application rather than treating AI output as verification by itself.
>
> For reporting, I built a same-origin vanilla JavaScript dashboard to keep the proof of concept simple. I kept currencies and historical observations separate to avoid producing misleading financial totals.
>
> I also replaced Swagger's generated boundary-value request with a realistic valid example so the API can be demonstrated cleanly.

---

## Final Outcome

The completed solution now provides:

- Correct REST behaviour for create and update operations
- Safe server-generated IDs
- Reliable duplicate detection
- Persisted update operations
- Accurate decimal handling
- Clear validation responses
- Thread-safe in-memory writes
- A responsive reporting interface
- Safer API-key handling
- A valid Swagger demonstration
- Comprehensive automated and live verification