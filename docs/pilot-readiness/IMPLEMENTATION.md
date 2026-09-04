# Phase 7 pilot-readiness implementation plan

## Phase 1: Repeatable synthetic environment

- Build a deterministic generator for at least 3,000 properties, 20 microregions, 100 operational accounts, visits, tags, and notifications.
- Preserve geographic realism around Teixeira de Freitas without using resident or clinical data.
- Record database size, tile/layer volume, seed duration, and cleanup procedure.
- Define read-only and write-capable test accounts for each role and UBS-isolation cases.

## Phase 2: Capacity and responsiveness

- Add k6 scenarios for login, overview, territorial map, property search/detail, visit registration, notifications, and management operations.
- Ramp gradually to 300 simultaneous sessions, hold the planned concurrency, and finish with a short sustained-load interval.
- Use a realistic traffic mix: 50% map/read, 25% property search/detail, 15% visit operations, and 10% management/operations.
- Gate ordinary API flows at p95 below 2 seconds, map-data flows at p95 below 3 seconds, and failed requests below 1%.
- Monitor API, PostgreSQL, worker, host resources, rate limiting, connection pools, and cross-UBS isolation throughout the run.
- Tune only after a baseline has been retained for comparison, then repeat the exact scenario.

## Phase 3: Accessibility and usability

- Run automated accessibility checks on login and the primary pages for agents, receptionists, doctors, managers, and global administrators.
- Manually validate keyboard operation, visible focus, logical focus order, 200% zoom, form errors, map alternatives, dialogs, notifications, and status announcements.
- Test representative workflows with a screen reader and document issues by WCAG 2.2 AA criterion and severity.
- Add regression checks for corrected high-impact accessibility defects.

## Phase 4: Controlled onboarding rehearsal

- Prepare the three initial neighborhoods and their microregions in a reviewable staging package.
- Validate OSM imports, coordinate systems, polygon validity, overlaps, gaps, duplicates, family-number rules, and agent assignments.
- Produce a before/import/after count report and require manager approval before activation.
- Create the real-role accounts, issue temporary passwords, and rehearse transfer, reset, archive, and recovery workflows.
- Confirm the weekly schedule, complete a manual pre-pilot backup, copy it encrypted off-host, and verify its checksum.

## Phase 5: Pilot release gate

- Provide concise role-based quick guides and a supervised acceptance script.
- Record capacity, accessibility, import, backup, monitoring, and user-acceptance evidence.
- Approve the pilot only when no critical/high defect is open and every mandatory threshold has passed.
- Publish the support, incident, rollback, and responsibility runbooks before enabling operational use.

## Deliverables

- Synthetic dataset generator and cleanup routine;
- versioned k6 load suite and results;
- WCAG 2.2 AA accessibility report and regression coverage;
- onboarding validation report and approved territorial package;
- encrypted off-host backup evidence;
- signed pilot go/no-go checklist and operations runbooks.
