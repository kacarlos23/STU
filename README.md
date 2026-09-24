# STU — Sistema Territorial das UBS

STU is a web-based territorial management system for Basic Health Units. The repository contains a modular ASP.NET Core backend, two React front-ends, PostGIS, MapLibre dependencies, a background worker, and container infrastructure.

## Current status

The repository has completed **Phase 6: operations and hardening** and prepared **Phase 7: capacity, accessibility, and controlled pilot readiness**. The platform supports UBS-scoped properties, structured visits, coverage alerts, operational indicators, notifications, asynchronous import/export, progressive MapLibre layers, audit, archive/restore workflows, and the isolated global administration portal. Backups, monitoring, an isolated recovery rehearsal, and the security release gate have been validated. The [independent family implementation](docs/families/IMPLEMENTATION.md) adds only a family number and responsible name, with optional residence, immutable link history and family-based visits. Individual members and clinical records are not collected. Its destructive data reset and production deployment have not been executed; see the [deployment procedure](docs/families/DEPLOYMENT.md).

## Published environment

- Main STU interface: `https://stu.laudaapp.com`;
- Isolated global administration interface: `https://stu-admin.laudaapp.com`;
- API through the main hostname: `https://stu.laudaapp.com/api`.

These addresses use the existing locally managed Cloudflare Tunnel on the development computer. The Docker stack and the Cloudflare tunnel supervisor must remain running for the environment to stay available.

## Stack

- ASP.NET Core 10 and Entity Framework Core;
- PostgreSQL 18 with PostGIS 3.6;
- React 19, TypeScript, Vite, and MapLibre GL JS;
- Martin for locally hosted OSM-derived basemap archives;
- Docker Compose and Nginx;
- xUnit, ASP.NET integration tests, Vitest, and Testing Library.

## First setup

Prerequisites: .NET SDK 10, Node.js 24+, npm 11+, Docker, and Git.

```powershell
Copy-Item .env.example .env
# Replace every change-me value in .env before starting containers.
.\scripts\bootstrap.ps1
.\scripts\dev-infra.ps1
```

Then run these in separate terminals:

```powershell
dotnet run --project src\STU.Api
npm run dev:app
npm run dev:admin
```

- API health: `http://localhost:5152/health/live` or the address shown by `dotnet run`;
- API reference in Development: `/docs`;
- Main app: `http://localhost:5173`;
- Global admin: `http://localhost:5174`.

To build the entire container stack:

```powershell
docker compose up --build
```

The local gateway listens on `http://localhost:8088` by default. Use the `Host: admin.localhost` name to exercise the separate admin virtual host.
PostGIS is exposed only to the local computer on port `55432`, avoiding common local PostgreSQL ports; containers still connect to it internally on port `5432`.

## Repository structure

```text
src/
  STU.Api/             HTTP entry point and authorization boundary
  STU.Application/     Use cases and application contracts
  STU.Domain/          Business rules and domain model
  STU.Infrastructure/  EF Core, PostGIS, Identity, and external adapters
  STU.Worker/          Imports, exports, notifications, and scheduled jobs
  web/stu-app/         UBS application and PWA shell
  web/stu-admin/       Isolated global administration interface
tests/                 Unit and integration tests
infra/                 Docker, Nginx, Martin, backup, and monitoring assets
docs/                  Architecture, decisions, and phased implementation plan
scripts/               Repeatable setup and verification commands
```

## Verification

```powershell
.\scripts\verify.ps1
```

## Security baseline

- No credentials or runtime data belong in Git;
- Private API responses and map layers must never be cached by the PWA;
- Every private action must be authorized and scoped to a UBS on the server;
- Operational records are archived and audited rather than silently deleted;
- Compose refuses to start PostgreSQL, the API, or the worker when database credentials are absent;
- The development passwords in examples are not valid production defaults.

Read [the architecture overview](docs/architecture/README.md) and [the implementation plan](docs/stu-platform/IMPLEMENTATION.md) before adding features.
The latest evidence is in [the recovery and security report](docs/recovery-security-validation/REPORT.md), and the next work is defined in [the Phase 7 plan](docs/pilot-readiness/IMPLEMENTATION.md).
