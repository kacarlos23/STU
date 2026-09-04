# STU platform implementation plan

## Overview

Implementation is divided into independently verifiable phases. The current repository establishes Phase 1 only.

## Phase summary

1. Foundation and executable architecture;
2. Identity, UBS boundaries, and configurable RBAC;
3. Neighborhoods, microregions, maps, and territorial versioning;
4. Properties, identifiers, structured visits, tags, and coverage;
5. Indicators, notifications, imports, and exports;
6. Audit, backup controls, monitoring, and hardening;
7. Load validation, accessibility, data onboarding, and pilot.

## Phase 1: Foundation

### Objective

Create a reproducible repository that builds, tests, and starts locally.

### Tasks

- [x] Create .NET solution and layer references;
- [x] Create the two React applications;
- [x] Add PostGIS and Identity persistence foundations;
- [x] Add unit, integration, and front-end tests;
- [x] Add Docker Compose, Nginx, Martin, and scripts;
- [x] Document architecture and repository conventions;
- [x] Validate all builds, tests, and container configuration.

### Success criteria

All projects restore and build; automated tests pass; Compose configuration validates; PostgreSQL/PostGIS starts locally; no known dependency vulnerability is reported by package managers.

## Phase 2: Identity and organization

### Objective

Implement accounts, password reset, UBS membership, transfers, protected default roles, and globally configurable roles.

### Success criteria

Every protected request is authenticated, permission checked, and scoped to the current UBS; the global admin uses an isolated interface and can manage roles without weakening protected defaults.

## Phase 3: Territory and map

### Objective

Implement neighborhoods, microregions, assignments, spatial queries, progressive map layers, drawing, and before/after territorial versions.

### Success criteria

Users only receive authorized map data; changes are previewed, transactional, versioned, and reconstructable by date.

## Phase 4: Properties and visits

### Objective

Implement property points and footprints, family-number uniqueness, structured visits, operational tags, coverage periods, and history.

### Success criteria

Reassignments cannot create duplicates or lose history; agents edit only their assigned areas; visit notes remain short and operational.

## Phase 5: Operational workflows

### Objective

Implement indicators, in-app notifications, staged imports, spreadsheets, GeoJSON, KML, and GeoPackage exports.

### Success criteria

Long operations run in the worker, are retryable and audited, and never create partial official data.

## Phase 6: Operations and hardening

### Objective

Implement append-only audit, configurable weekly and manual backups, restore runbooks, alerts, headers, rate limits, and security validation.

### Success criteria

Restore succeeds within eight hours, private data is never cached by the PWA, and critical operations are visible in audit.

## Phase 7: Pilot

### Objective

Load real data, validate 300 concurrent sessions, complete accessibility checks, train representative users, and release the three-neighborhood pilot.

### Success criteria

Operational users complete their real flows without critical blockers and response, availability, isolation, and recovery goals are demonstrated.
