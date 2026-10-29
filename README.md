# NAVDataApi

A small NAV reporting proof of concept built on the supplied .NET 10 sandbox API. Create/update defects are covered by regression tests; the browser dashboard consumes the existing authenticated API.

## Quick start

### Native .NET 10

Install the **.NET 10 SDK**, then run:

```bash
dotnet run --launch-profile http
```

Open **http://localhost:5225/** for the dashboard and enter one of the supplied development keys from `appsettings.Development.json`. The dashboard does not embed or persist keys. The supplied launch profile still opens Swagger automatically.

On this machine the SDK is installed under `$HOME/.dotnet`. If your terminal cannot find it, run `export PATH="$HOME/.dotnet:$PATH"` first, or use `$HOME/.dotnet/dotnet` directly. No Docker is needed.

This is a **local development demo**, not a production deployment. Use private keys and HTTPS for a real deployment, and replace the in-memory store if persistence is needed.

## Verify

```bash
# .NET 10 SDK and Node.js (verified with Node 26)
dotnet test NAVDataApi.slnx --configuration Release
node --test tests/frontend/*.test.mjs

# Convenience runner (also locates $HOME/.dotnet on this machine):
bash scripts/verify.sh
```

The verification script requires Node.js for frontend tests and the native .NET 10 SDK for backend tests. Test runners use isolated datasets, not the running demo's data. The browser needs modern JavaScript support (modules, BigInt and AbortSignal). Decimal JSON tokens are preserved as strings before arithmetic; there is no dependency on `JSON.parse` source-context support.

## Assessment deliverables

- [Scope and considerations](docs/SCOPE.md)
- [Architecture](docs/ARCHITECTURE.md) · [SVG diagram](docs/architecture.svg)
- [Task breakdown and estimates](docs/TASK-BREAKDOWN.md)
- [Defect log](docs/DEFECTS.md)
- [Code and demo walkthrough](docs/WALKTHROUGH.md)
- [AI tools, prompts and demonstration](docs/AI-USAGE.md) · [selected verbatim prompt record](docs/AI-PROMPTS.md)
- [Quality and verification evidence](docs/QUALITY.md) · [final observed results](docs/verification/FINAL.md)

The original requirements remain in [TASK.md](TASK.md). Time estimates are planning estimates, not a fabricated development log. No git history has been invented for the supplied directory.

## Sandbox background

A sandbox API for fund Net Asset Value (NAV) data. Consumers can freely test create and update operations; **data is never persisted** and always returns to the original seed state. Each API key has its own isolated dataset, so testers never see each other's changes.

## Run

```powershell
dotnet run --launch-profile http
```

- API: `http://localhost:5225/api/navs`
- Swagger UI (Development only): `http://localhost:5225/swagger` — opens automatically; click **Authorize** and enter your API key
- Alternative docs (Development only): `http://localhost:5225/scalar`
- Sample requests: `NAVDataApi.http`

## Project structure

ASP.NET Core Web API (.NET 10) using controllers.

| Folder | Contents |
|---|---|
| `Controllers/` | `NavsController` (NAV read/create/update), `AdminController` (reset, status) |
| `Models/` | `NavRecord` (response), `NavRecordRequest` (create/update payload with validation), admin response types |
| `Data/` | In-memory repository, seed data, per-client sandbox store, background reset and cleanup services |
| `Security/` | API key authentication handler |
| `Program.cs` | Service registration and request pipeline |

## Authentication

Every endpoint requires an `X-Api-Key` header. A missing or unknown key returns `401`.

Keys are configured by client name under `ApiKeys:Clients`:

```json
"ApiKeys": {
  "Clients": [
    { "Name": "client-a", "Key": "<long random value>" }
  ]
}
```

- `appsettings.Development.json` holds two development keys (`dev-tester-1`, `dev-tester-2`).
- `appsettings.json` ships with no keys. Supply real keys outside source control, e.g. user secrets, environment variables (`ApiKeys__Clients__0__Name`, `ApiKeys__Clients__0__Key`) or a key vault.
- The client **name** identifies the dataset, so rotating a client's key keeps its data.

## Endpoints

| Method | Route | Description |
|---|---|---|
| GET | `/api/navs?fundCode=&isin=&from=&to=` | List NAVs with optional filters |
| GET | `/api/navs/latest?fundCode=` | Latest NAV per share class |
| GET | `/api/navs/{id}` | Single NAV record |
| POST | `/api/navs` | Create (409 if ISIN + date already exists) |
| PUT | `/api/navs/{id}` | Replace |
| POST | `/api/admin/reset` | Restore **your** dataset to seed data immediately |
| GET | `/api/admin/status` | Your client name, record count, last reset time and dataset expiry |

## Data isolation and reset

`Data/NavSandboxStore.cs` holds one in-memory dataset (`Data/InMemoryNavRepository.cs`) per client, created from `Data/SeedData.cs` on that client's first request.

### Capacity and expiry

Configured under `Sandbox` in `appsettings.json`:

| Setting | Default | Behaviour |
|---|---|---|
| `MaxDatasets` | `5` | Maximum datasets held at once. A new client beyond the cap receives `503` with a `Retry-After` header (seconds until the oldest dataset expires). Existing datasets are never evicted early. |
| `LifetimeDays` | `7` | A dataset is removed this many days after **creation**; resets do not extend it. The client's next call creates a fresh dataset. |

Expired datasets are removed on the next request and by an hourly background cleanup. Invalid values stop the API at startup.

### Reset

1. On application restart (all clients)
2. On demand via `POST /api/admin/reset` (calling client only)
3. On a schedule, configured in `appsettings.json` (`DataReset:IntervalMinutes`, `0` disables) (all clients)

Seed data is deterministic: 6 fictional share classes across 4 funds, 10 business days of NAVs ending 30 Sep 2026 (60 records). ISINs use the unassigned `ZZ` prefix.
