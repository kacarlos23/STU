# Operations and hardening implementation

## Goal

Give the global administrator practical control over weekly and manual backups while keeping execution safe, traceable, and recoverable.

## Backend

1. Add singleton backup settings and append-only backup run entities.
2. Expose global-admin endpoints to read and update settings, list runs, request a manual run, and download completed archives.
3. Register audit entries for settings changes and manual backup requests.
4. Add database constraints and indexes that prevent duplicate weekly runs.

## Worker

1. Evaluate the configured weekly slot during each worker cycle.
2. Queue at most one scheduled run for each slot.
3. Recover runs interrupted by a worker restart.
4. Execute PostgreSQL 18 `pg_dump` in custom format without exposing credentials.
5. Calculate size and SHA-256 after successful completion.
6. Apply retention to files while keeping the database history.

## Administration portal

1. Enable the existing Backups navigation item.
2. Show schedule status, weekday, local time, and retention count.
3. Allow saving configuration and requesting an immediate backup.
4. Show recent runs, status, origin, timestamps, size, integrity hash, errors, and download action.

## Verification

- domain tests for settings and run state transitions;
- integration tests for authorization, validation, audit, request, and listing;
- worker tests for schedule calculation;
- administration UI test for opening the section and requesting a backup;
- production build, migration, container health check, and one real manual archive.

## Restore target

The operational target remains recovery within eight business hours. The runbook in `infra/postgres/README.md` documents archive verification and restoration into a fresh database before any production replacement.

## Delivery 2: operational monitoring

### Backend and worker

1. Persist a worker heartbeat no more than once every 15 seconds.
2. Keep `/health/live` process-only and add `/health/ready` checks tagged for database, operations storage, and worker freshness.
3. Add a global-administrator monitoring snapshot with component status and computed alerts.
4. Detect missing/stale workers, overdue or failed backups, stalled jobs, and recent asynchronous failures.

### Administration portal

1. Add a Monitoring section to the isolated portal.
2. Show overall state, last check, component cards, and active alerts.
3. Refresh automatically without requiring a page reload.
4. Explain the difference between a warning and a critical incident in plain language.

### Success criteria

- liveness remains HTTP 200 while the API process is running;
- readiness becomes HTTP 503 when a required component is unavailable and returns to HTTP 200 after recovery;
- the worker heartbeat appears in the protected panel within 45 seconds;
- backup and operation problems produce actionable alerts;
- no health response exposes credentials, paths, record contents, or personal data.
