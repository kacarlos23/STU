# Phase 7.4 controlled onboarding baseline

Date: 2026-08-31  
Scope: pre-implantation controls and first read-only diagnosis of `UBS-PILOTO`

## Outcome

The controlled-onboarding control plane is implemented, tested, and deployed. The system now produces a reviewable per-UBS readiness report and refuses approval while any mandatory territorial, cadastral, staffing, import, or backup check is blocked.

No neighborhood, microregion, property, user, import, or backup record was changed during the baseline. Approval is an explicit manager/global-administrator action and creates an append-only audit entry containing the readiness snapshot hash and the declared encrypted off-host backup evidence.

## Implemented checks

- exactly three neighborhoods linked through active UBS microregions;
- at least one active microregion;
- valid neighborhood/microregion geometries in SRID 4326;
- microregion interior overlaps;
- uncovered neighborhood area and microregion area outside linked neighborhoods;
- microregions without an active neighborhood;
- microregions without an assigned agent;
- properties outside their assigned microregion;
- duplicate active family numbers;
- active agent, receptionist, doctor, and manager accounts;
- imports still processing or awaiting approval;
- weekly backup schedule;
- completed, unpruned backup no older than seven days.

The report also shows before/latest-import/current property counts, temporary-password count, the latest approval, whether that approval still matches the current snapshot, and the latest backup checksum used for off-host evidence confirmation.

## Safety controls

- Server-side UBS scope is enforced for reads and approvals.
- Only a UBS manager can assess their own UBS; a global administrator must explicitly select a UBS.
- Anonymous access returns HTTP 401.
- A report with any blocker returns conflict on approval.
- The SHA-256 entered during approval must match the latest completed backup.
- The off-host destination field explicitly forbids credentials or encryption keys.
- Approval does not activate, import, rewrite, or archive operational data.
- The evidence and actor are stored in append-only audit history.

## Current production baseline

The read-only diagnosis found:

- UBS: `UBS-PILOTO`;
- active microregions: 0;
- active properties: 0;
- active UBS users: 1;
- weekly backup: enabled for Sunday at 00:00 in `America/Bahia`;
- latest backup: completed on 2026-08-30, 92,383 bytes;
- public main portal, admin portal, gateway/API readiness, and direct API readiness: HTTP 200;
- database: healthy on the host-bound PostgreSQL port;
- anonymous onboarding endpoint: HTTP 401.

Four global neighborhood references currently exist (`Jardim Liberdade`, `Luiz Eduardo Magalhães`, `Nova Teixeira`, and `Redenção`), but none can count toward readiness until active microregions of the UBS link them. The initial pilot scope was confirmed as `Luiz Eduardo Magalhães`, `Nova Teixeira`, and `Redenção`; `Jardim Liberdade` is outside the first activation package.

## Automated evidence

- 23 unit tests passed.
- 21 integration tests passed, including manager UBS isolation and blocked-approval behavior.
- 16 operational portal tests passed.
- 7 global-administration portal tests passed, including the new readiness screen.
- Total: 67 passing tests.
- Both web linters and production builds passed.
- .NET Release build completed with zero warnings and zero errors.

## Remaining real-world inputs

The controlled rehearsal cannot be marked complete until the operator provides or confirms:

1. the exact three initial neighborhoods;
2. their reviewed OSM/GeoJSON boundaries and microregion distribution;
3. the real role-account roster or an approved account import list;
4. the encrypted off-host backup destination under municipal control.

After those inputs are available, the workflow is: import/draw territories, validate the report, stage properties, approve the property import, rehearse account reset/transfer/archive, create a manual backup, copy it encrypted off-host, confirm its SHA-256, and record manager approval.

## Gate decision

Controlled-onboarding software controls: **PASS and deployed**.  
Real three-neighborhood rehearsal: **WAITING FOR CONFIRMED TERRITORIAL AND OFF-HOST DESTINATION INPUTS**.
