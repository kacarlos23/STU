# Recovery and security validation implementation

## Phase 1: Evidence setup

- Inventory endpoints, roles, protections, containers, and the selected backup.
- Capture production-safe baseline health and backup metadata.
- Add repeatable security regression tests where current coverage is missing.

## Phase 2: Isolated restore rehearsal

- Verify archive SHA-256 and table of contents.
- Restore into a new database created from `template0`.
- Validate PostGIS, migrations, essential counts, constraints, and indexes.
- Start an isolated API process against the clone and perform health/authentication smoke checks.
- Record elapsed time and clean up the rehearsal resources.

## Phase 3: Security validation

- Run dependency and secret/configuration scans.
- Test anonymous access, portal isolation, UBS isolation, roles, CSRF, logout, archived accounts, rate limiting, malformed input, CORS, headers, cache, error handling, imports, downloads, and audit.
- Run a passive web baseline scan against the local gateway.

## Phase 4: Remediation and release gate

- Correct confirmed findings that can be resolved safely in the current scope.
- Repeat the full automated verification and the affected security tests.
- Publish changed components, revalidate through Cloudflare, and produce the final report.
- Prepare Phase 7 entry criteria without starting real-data onboarding or load generation.

## Success criteria

- A real archive restores without error into an isolated database.
- The restored application starts and answers smoke checks.
- No critical or high unresolved vulnerability remains in the tested scope.
- Access and UBS boundaries are covered by repeatable negative tests.
- The report records evidence, limitations, findings, remediation, recovery time, and Phase 7 readiness.

