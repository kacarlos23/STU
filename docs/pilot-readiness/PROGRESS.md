# Phase 7 pilot-readiness progress

## Status: Phase 5 — Release controls deployed; operational acceptance pending

### Entry criteria

- [x] Phase 6 backup and monitoring features published
- [x] Real archive restored successfully in isolation
- [x] Security findings remediated and regression-tested
- [x] Phase 7 scope, thresholds, and deliverables documented
- [x] Runtime, ports, public endpoints, database, tests, and dependencies revalidated on 2026-08-28
- [x] Encrypted off-host backup destination selected: external Windows computer in another location
- [x] Initial neighborhoods confirmed: Luiz Eduardo Magalhães, Nova Teixeira, and Redenção

### Execution

- [x] Synthetic 3,000-property environment generated
- [x] Baseline capacity result captured
- [x] 300-simultaneous-session gate passed
- [x] WCAG 2.2 AA automated review completed
- [x] Role-based keyboard, focus, semantic, and usability review completed
- [ ] Supervised human screen-reader acceptance session completed
- [ ] Three-neighborhood onboarding rehearsal completed
- [x] Per-UBS pre-implantation validation and approval gate deployed
- [x] Read-only production onboarding baseline captured
- [x] Account transfer, reset, archive, and recovery regression coverage passed
- [x] Weekly backup schedule and recent completed backup confirmed
- [x] Training and acceptance material completed
- [ ] Pilot go/no-go decision recorded

## Recommended next action

The [08/09/2026 review](../functional-review/PROJECT_REVIEW_2026-09-08.md) corrected territorial impact, restoration, coverage and stale onboarding evidence. The current linked neighborhoods do not yet match the approved three-neighborhood pilot; operational acceptance remains pending.

Execute the real three-neighborhood import/rehearsal, configure and verify the encrypted transfer to the external Windows computer, then conduct supervised accessibility, training, and user acceptance. The deployed release gate will refuse `go` until these mandatory inputs pass.

## Session log

### 2026-08-28 — Synthetic pilot environment

- Started an isolated pilot database design so production records are never used by load tests.
- Fixed the deterministic target at 3,000 properties, 20 microregions, 100 operational accounts, structured visits, tags, notifications, and three territorial neighborhoods around Teixeira de Freitas.
- Defined a fail-closed cleanup rule: the generator only accepts a database whose name ends in `_pilot`.
- Selected SRID 4326 and deterministic longitude/latitude placement, following the existing geographic model.
- Reserved the k6 workload for the next phase after dataset counts and repeatability are validated.
- Generated and validated the isolated database twice, with a successful cleanup between runs.
- Final scale: 3 neighborhoods, 20 microregions, 3,000 properties, 4,500 visits, 100 accounts, 6 tags, 1,300 property-tag links, 20 coverage rules, and 100 notifications.
- Confirmed eight microregions linked to more than one neighborhood, zero overlapping microregion interiors, zero properties outside their microregion, zero duplicate family numbers, and SRID 4326 for all geometries.
- Confirmed authenticated agent and manager flows against the pilot API on port 8092.
- Verified the production database contains no synthetic unit, neighborhood, account, or property.
- Full project verification passed with 58 automated tests and zero .NET build warnings.

The detailed evidence is recorded in [PHASE1_SYNTHETIC_REPORT_2026-08-28.md](PHASE1_SYNTHETIC_REPORT_2026-08-28.md).

### 2026-08-28 — Capacity and responsiveness

- Started the versioned k6 workload against the isolated API on port 8092.
- Reserved one of the 100 operational accounts for a second, empty UBS so every run verifies cross-UBS isolation.
- Defined the mandatory progression: smoke, 50-session baseline, then a gradual ramp and hold at 300 sessions.
- Kept the approved traffic target: 50% map/read, 25% property search/detail, 15% visit writes, and 10% management/protection.
- Recreated the pilot database with 2 UBS, assigning 99 accounts to the loaded unit and 1 account to an empty isolation unit; the 3,000 properties remain exclusive to the loaded unit.
- Passed the 10-session smoke gate with 93/93 checks, zero failed HTTP requests, p95 of 131 ms for ordinary reads, 238 ms for maps, 264 ms for the complete write flow, and 254 ms for login.
- Passed the corrected 50-session baseline for 60 seconds with 5,644/5,644 checks, zero failed HTTP requests, 1,304 completed workflows, p95 of 79 ms for ordinary reads, 246 ms for maps, 115 ms for writes, and 1,152 ms for login.
- Confirmed that API, database, and worker remained running with zero restarts; PostgreSQL peaked at 3 active connections out of 56 total during the baseline sample.
- Found PostgreSQL client exhaustion during the first ramp and corrected it with explicit pools of 80 API connections and 10 worker connections, preserving database slots for operations.
- Passed the final 300-session gate with 60,292/60,292 checks, zero failed requests across 50,802 HTTP requests, 14,295 completed workflows, and zero interrupted iterations.
- Final p95: 1,638 ms ordinary reads, 1,858 ms maps, 1,453 ms writes, and 1,066 ms login.
- Confirmed cross-UBS isolation on every dedicated isolation iteration, no container restarts, no service error log, and a database peak of 17 active / 83 total connections.
- Published the connection-pool limits and dynamic Nginx service resolution; both public portals and API probes returned HTTP 200 after deployment.
- Full repository verification passed with 58 automated tests and zero .NET build warnings.

The detailed capacity evidence is recorded in [PHASE2_CAPACITY_REPORT_2026-08-28.md](PHASE2_CAPACITY_REPORT_2026-08-28.md).

### 2026-08-31 — Accessibility and usability

- Added WCAG 2.2 AA axe regression scans for both logins and representative agent, receptionist, doctor, manager, and global-administrator views.
- Added consistent visible focus, skip links, active-navigation semantics, section-change focus, and reduced-motion support.
- Added reusable accessible-dialog behavior with initial focus, keyboard trapping, Escape closure, and focus restoration to administrative, property, visit, and territory editors.
- Added named map regions and list-based alternatives so territorial and property workflows do not depend only on visual color or pointer input.
- Added accessible field names and polite status announcements for saves, loading, drawing, and coordinate changes.
- Inspected both deployed login accessibility trees and ran computed-style contrast checks with zero failing visible text nodes.
- Published the corrected app and administration portal; public pages, gateway/API readiness, and direct API readiness returned HTTP 200.
- Full repository verification passed with 65 automated tests and zero .NET build warnings.

The detailed accessibility evidence is recorded in [PHASE3_ACCESSIBILITY_REPORT_2026-08-31.md](PHASE3_ACCESSIBILITY_REPORT_2026-08-31.md).

### 2026-08-31 — Controlled onboarding controls and baseline

- Added a manager/global-administrator pre-implantation workspace with per-UBS scope.
- Added automatic checks for the three-neighborhood target, geometry/SRID, overlaps, gaps, links, agent assignments, property containment, duplicate family numbers, role accounts, pending imports, weekly backup, and recent backup integrity.
- Added before/latest-import/current count comparison and snapshot hashing.
- Added fail-closed approval: any blocker prevents approval; successful approval requires the latest backup SHA-256 and a declared encrypted off-host destination.
- Kept approval non-mutating and stored its evidence in append-only audit history.
- Confirmed anonymous access is rejected and a manager cannot assess another UBS.
- Captured the production baseline without changing data: zero active microregions, zero properties, one active UBS account, weekly backup enabled, and a recent completed backup.
- Published both portals and the API; all public and local readiness probes returned HTTP 200 after stabilization.
- Full repository verification passed with 67 automated tests and zero .NET build warnings.

The detailed baseline is recorded in [PHASE4_ONBOARDING_BASELINE_2026-08-31.md](PHASE4_ONBOARDING_BASELINE_2026-08-31.md).

### 2026-08-31 — Pilot release gate

- Confirmed the initial territorial scope as Luiz Eduardo Magalhães, Nova Teixeira, and Redenção.
- Added a per-UBS final gate consolidating capacity, accessibility, onboarding, backup, worker, storage, and operational-job evidence.
- Added fail-closed `go`: automatic blockers and four explicit human confirmations prevent release.
- Allowed a reasoned `no-go` record even while blockers remain, so postponement is also documented.
- Added named support, incident, and restoration responsibility fields.
- Stored both decisions as append-only audit evidence with a stable release snapshot hash.
- Added printable role quick guides and support, incident, backup, and restoration runbooks to both portals.
- Kept the Windows workstation topology documented as provisional for the initial pilot.
- Passed the full release verification with 69 automated tests, zero .NET warnings, both web builds, lint, and compose validation.
- Published the API and both portals; public and local readiness probes returned HTTP 200, PostgreSQL accepted connections, and anonymous release-gate access returned HTTP 401.

The final-gate implementation evidence is recorded in [PHASE5_RELEASE_GATE_2026-08-31.md](PHASE5_RELEASE_GATE_2026-08-31.md).

The current readiness evidence is recorded in [READINESS_AUDIT_2026-08-28.md](READINESS_AUDIT_2026-08-28.md).

### 2026-08-31 — Functional review of properties and health-unit staff

- Added own-UBS staff management to the main portal for authorized managers.
- Added server-enforced UBS isolation and protection for manager/global-administrator accounts.
- Added operational account creation, profile/role update, temporary-password reset, archive, restore, and append-only audit coverage.
- Added an explicit active-microregion prerequisite to property creation with direct navigation to the territorial map.
- Kept the property map centered on Teixeira de Freitas and retained server-side point-in-microregion validation.
- Passed the full repository verification with 72 automated tests and zero .NET warnings.

The review evidence is recorded in [../functional-review/PROPERTY_AND_STAFF_REVIEW_2026-08-31.md](../functional-review/PROPERTY_AND_STAFF_REVIEW_2026-08-31.md).
