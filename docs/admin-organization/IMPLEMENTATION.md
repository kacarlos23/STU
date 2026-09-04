# Admin organization implementation plan

## Overview

Finish STU Phase 2 organization management through protected APIs, append-only audit persistence and functional global-administration screens.

## Prerequisites

- Identity and first-access flow deployed.
- Global administration policy and isolated hostname working.
- PostGIS database and automatic migrations healthy.

## Phase summary

1. Organization domain and audit persistence.
2. Global administration API.
3. Functional administration interface.
4. Verification and deployment.

## Phase 1: Organization domain and audit persistence

### Objective

Add safe lifecycle operations and append-only audit storage.

### Tasks

- [x] Add update/archive/restore behavior to UBS, users and custom roles.
- [x] Add the custom-role permission catalog.
- [x] Add audit entity, configuration and migration.

### Success criteria

The database represents all required state without permanent-delete endpoints and preserves immutable audit events.

## Phase 2: Global administration API

### Objective

Provide protected CRUD-like workflows for organization management.

### Tasks

- [x] Implement UBS list/create/update/archive/restore.
- [x] Implement user list/create/update/transfer/reset/archive/restore.
- [x] Implement role list/create/update/archive/restore.
- [x] Implement audit listing and permission catalog.
- [x] Enforce dependencies, protected defaults, self-lockout guards, antiforgery and auditing.

### Success criteria

Only a global administrator with a definitive password can execute mutations; invalid lifecycle transitions return actionable errors.

## Phase 3: Functional administration interface

### Objective

Turn the dashboard cards and navigation into usable management screens.

### Tasks

- [x] Add a typed administration API client.
- [x] Add UBS management UI.
- [x] Add user, transfer and password-reset UI.
- [x] Add role and permission UI.
- [x] Add read-only audit UI.

### Success criteria

The complete workflow can be performed in the browser without database or command-line access.

## Phase 4: Verification and deployment

### Objective

Prove behaviors and publish the phase safely.

### Tasks

- [x] Add integration tests against isolated PostGIS.
- [x] Add front-end component tests.
- [x] Run builds, lint and dependency audits.
- [x] Apply migration, rebuild containers and verify public HTTPS flows.

### Success criteria

Automated and deployed smoke tests pass, existing login behavior remains intact, and the public admin portal manages real persisted data.

## Post-implementation

- [x] Update platform progress and README.
- [x] Record the next phase as territory and map.

## Notes

- All custom roles are operational and therefore require UBS membership.
- System roles remain visible but immutable.
- The audit UI is read-only and ordered newest first.
