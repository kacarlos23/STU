# Admin organization progress

## Status: Complete and deployed

## Quick reference

- Research: `docs/admin-organization/RESEARCH.md`
- Implementation: `docs/admin-organization/IMPLEMENTATION.md`

## Phase progress

### Phase 1: Organization domain and audit persistence

**Status:** Complete

- Added safe update/archive/restore methods for UBS, users and roles.
- Added the configurable permission catalog.
- Added append-only audit persistence and the `AdminOrganization` migration.

### Phase 2: Global administration API

**Status:** Complete

- Added protected UBS, user, role, permission and audit endpoints.
- Enforced antiforgery, active-dependency checks, protected defaults and self-lockout guards.
- Added cryptographically generated one-time passwords and audited transfer/reset workflows.

### Phase 3: Functional administration interface

**Status:** Complete

- Added functional navigation and typed administration API client.
- Added UBS, server, transfer, password-reset, custom-role and audit screens.
- Added one-time credential presentation and responsive management layouts.

### Phase 4: Verification and deployment

**Status:** Complete

- Added isolated PostGIS integration coverage for the full administrative lifecycle.
- Corrected test database replacement and verified zero writes to the development database.
- Passed .NET, TypeScript, lint and component test suites.
- Applied the migration and published the API and global administration interface.

## Session log

### 2026-08-22

- Selected direct Identity/EF Core administration inside the modular monolith.
- Protected system roles, one-role server accounts and append-only audit were confirmed as phase boundaries.

## Architectural decisions

- No permanent delete endpoint.
- Every custom role is UBS-scoped; global administration remains a protected system role.
- Temporary passwords are generated cryptographically and returned once.
- Role permission changes invalidate affected users' security stamps.
- Integration tests replace the production `StuDbContext` registration before application startup.

## Blockers

- None.
