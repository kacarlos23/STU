# STU platform progress

## Status: Phase 7 preparation implemented; operational pilot acceptance pending

Current review: [08/09/2026](../functional-review/PROJECT_REVIEW_2026-09-08.md). Historical test counts below refer to their original implementation sessions.

## Completed

- Initial repository and solution structure;
- Backend layer references and essential packages;
- Main and global administration React shells;
- PostGIS, Martin, Nginx, worker, and container definitions;
- Initial tests, scripts, and architecture documentation.
- Release builds for all .NET and web projects;
- Unit, integration, and front-end tests;
- Dependency audits for NuGet and npm with no known vulnerabilities;
- Container images for the API, worker, main app, and global admin;
- Healthy PostgreSQL 18 / PostGIS 3.6 runtime;
- End-to-end gateway routing for the app, isolated admin, and API.
- Cloudflare Tunnel publication with HTTPS for `stu.laudaapp.com` and `stu-admin.laudaapp.com`.
- Persistent users, protected roles and UBS membership with ASP.NET Core Identity;
- secure cookie authentication, antiforgery protection, lockout and rate limiting;
- mandatory replacement of temporary passwords on first access;
- functional main and isolated global-administration login screens;
- initial protected dashboards backed by live API data;
- end-to-end authentication tests against an isolated PostGIS database.
- global administration of UBS with update, archive and restore rules;
- server account creation, UBS transfer, password reset and account lifecycle;
- protected default roles and global-admin configurable custom roles;
- append-only administrative audit with before/after snapshots;
- functional management screens in the isolated administration portal.
- territory drawing, OSM import, versioned limits, map visualization, and property layers;
- property, family-number, tag, coverage-rule, and structured-visit workflows;
- operational indicators, notifications, asynchronous imports, and CSV/GeoJSON/KML/GeoPackage exports;
- configurable weekly and manual full-database backups in the global administration portal;
- backup execution history, retention, SHA-256 integrity metadata, protected downloads, and restore runbook;
- real PostgreSQL 18 archive generated and validated in the published environment;
- separate public liveness and readiness checks for the API, database, operations storage, and worker;
- persisted worker heartbeat and protected global-administration monitoring with actionable backup and asynchronous-operation alerts;
- controlled worker failure and recovery validated locally and through Cloudflare.
- real backup restored into an isolated PostgreSQL/PostGIS database and smoke-tested;
- authentication, portal separation, CSRF, session revocation, CORS, rate limiting, headers, malformed input, dependencies, containers, and secret exposure validated;
- confirmed security findings corrected, published, and covered by regression tests;
- 49 automated tests and the final release verification passing;
- Phase 7 capacity, accessibility, and controlled-pilot plan prepared.

## Decisions

- Use npm workspaces for the two front-ends;
- Keep Martin isolated from private PostGIS tables;
- Keep PWA runtime caching empty in the foundation;
- Pin the development PostGIS and Martin major/minor images.

## Blockers

- Real territory rehearsal, agent assignments and representative role accounts are incomplete.
- Encrypted off-host transfer, supervised accessibility/training acceptance and the final pilot decision remain pending.
