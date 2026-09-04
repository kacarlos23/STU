# Identity and access progress

## Status: Complete and deployed

## Quick reference

- Research: `docs/identity-access/RESEARCH.md`
- Implementation: `docs/identity-access/IMPLEMENTATION.md`

## Phase progress

### Phase 1: Identity persistence

**Status:** Complete

- ASP.NET Core Identity persisted in PostgreSQL;
- system and configurable roles represented in the database;
- UBS membership and mandatory temporary-password change supported;
- data-protection keys persisted in the STU database;
- initial migration applied.

### Phase 2: Authentication API

**Status:** Complete

- Cookie-based login, logout, current-session and password-change endpoints;
- antiforgery validation on every session-changing request;
- account lockout and IP rate limiting;
- explicit 401/403 API responses;
- main and global-administration portal separation.

### Phase 3: Main portal

**Status:** Complete

- Real login and first-access password flow;
- authenticated UBS dashboard;
- current user, role and health-unit context;
- protected operational summary;
- initial territory, property, visit and coverage navigation.

### Phase 4: Global administration portal

**Status:** Complete

- Isolated global-administrator login;
- protected platform overview;
- initial modules for UBS, users, configurable roles, backups, audit and imports.

### Phase 5: Verification and deployment

**Status:** Complete

- Unit, browser-component and API integration tests passing;
- clean production builds for both web applications and all .NET projects;
- Docker images rebuilt and services recreated;
- public HTTPS health, login, temporary-password enforcement and logout verified;
- deployed at `stu.laudaapp.com` and `stu-admin.laudaapp.com`.

## Session log

### 2026-08-22

- Defined the cookie, antiforgery, lockout, bootstrap, UBS-scope and protected-role approach.
- Confirmed custom endpoints without public registration for the MVP.
- Implemented, tested and deployed the first complete identity and access slice.

## Architectural decisions

- Browser authentication uses secure HTTP-only cookies rather than tokens stored in JavaScript.
- Role permissions use claims to preserve the approved future configurable-role model.
- Temporary bootstrap passwords are generated during deployment and never committed.
- Operational and global-administration routes require both authentication and completed password change.

## Blockers

- None.
