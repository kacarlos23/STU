# Identity and access implementation plan

## Overview

Deliver the first complete Phase 2 slice: persisted Identity schema, protected system roles, functional browser sessions, password replacement, two login experiences, and authenticated summaries.

## Prerequisites

- Phase 1 executable foundation complete.
- PostgreSQL/PostGIS and Docker stack healthy.
- Main and global administration hostnames routed through Cloudflare.

## Phase summary

1. Identity persistence and protected defaults.
2. Secure authentication API.
3. Main portal login and dashboard.
4. Global administration login and dashboard.
5. Verification, bootstrap, and deployment.

## Phase 1: Identity persistence

### Tasks

- [ ] Extend users and roles with STU metadata.
- [ ] Persist data-protection keys.
- [ ] Add the initial EF Core migration.
- [ ] Seed protected roles, permissions, UBS, and optional bootstrap accounts.

### Success criteria

The schema migrates automatically in the configured environment, survives restarts, and creates no hard-coded password.

## Phase 2: Authentication API

### Tasks

- [ ] Add antiforgery token, login, current-session, password-change, and logout endpoints.
- [ ] Add main dashboard and global administration summary endpoints.
- [ ] Enforce lockout, archive, UBS, role, and forced-password rules.
- [ ] Add integration coverage for unauthorized and authenticated flows.

### Success criteria

A browser can establish and close a secure session; unauthorized calls return 401/403 and password change is mandatory when flagged.

## Phase 3: Main portal

### Tasks

- [ ] Build accessible login and loading/error states.
- [ ] Build mandatory-password-change flow.
- [ ] Build protected dashboard and navigation shell.

### Success criteria

A UBS user can log in, replace a temporary password, see their account/UBS context, and log out.

## Phase 4: Global administration portal

### Tasks

- [ ] Build isolated login and password flow.
- [ ] Reject accounts without the global administrator role.
- [ ] Display the first global counts and administrative modules.

### Success criteria

Only a global administrator can establish a session through the admin portal and view its protected summary.

## Phase 5: Verification and deployment

### Tasks

- [ ] Pass backend and frontend builds, tests, lint, and dependency audits.
- [ ] Create random one-time bootstrap credentials.
- [ ] Rebuild containers, migrate the live development database, and validate both HTTPS hostnames.

### Success criteria

Both public portals complete their real login flows against the deployed API without console, network, or server errors.

