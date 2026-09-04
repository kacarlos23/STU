# Phase 7.2 capacity and responsiveness report — 2026-08-28

## Decision

The 300-session capacity gate passed against the isolated pilot environment. All 60,292 functional checks passed, all 50,802 HTTP requests completed without request failures, and every response-time threshold passed. This is sufficient to continue to the accessibility and usability phase for the initial pilot.

The result does not certify the current single host for the future 42-UBS deployment. Host CPU reached 100% and free physical memory fell to 305.7 MiB during the final gate, so infrastructure scaling and another capacity exercise are mandatory before broader rollout.

## Isolated environment

- API: `127.0.0.1:8092`
- PostgreSQL/PostGIS: `127.0.0.1:55433`, database `stu_load_pilot`
- Worker: isolated internal service
- Dataset: 3 neighborhoods, 20 microregions, 3,000 properties, 4,500 initial visits, 100 accounts, 6 operational tags, and 100 notifications
- Tenant-isolation fixture: 99 accounts and all operational records in the loaded UBS; 1 receptionist account in a second empty UBS
- Production records were not used by the workload

## Workload

The versioned k6 suite covers login, CSRF, territorial and property maps, property search/detail, visit history, structured visit creation, dashboard, notifications, indicators, operation jobs, reference data, and an empty-UBS isolation assertion.

Traffic was divided into 50% map/read activity, 25% property search/detail, 15% visit activity, and 10% management/isolation activity. The final gate ramped to 300 sessions over two minutes, held all 300 sessions for two minutes, and then reduced load over 20 seconds. Session cookies were retained between iterations to model an authenticated browser session; writes obtained a fresh antiforgery token in the same way as the application.

Thresholds:

- checks above 99%
- failed requests below 1%
- login, ordinary reads, and writes below 2 seconds at p95
- map-data requests below 3 seconds at p95

The scenario structure follows the official k6 ramping-VU and cookie-jar behavior, and the retained machine-readable result uses k6 custom end-of-test summaries.

## Results

### 50-session baseline

| Measure | Result |
|---|---:|
| Functional checks | 5,644 / 5,644 passed |
| Failed HTTP requests | 0 |
| Completed workflows | 1,304 |
| Ordinary read p95 | 78.9 ms |
| Map p95 | 246.4 ms |
| Write flow p95 | 114.7 ms |
| Login p95 | 1,151.7 ms |

### Final 300-session gate

| Measure | Result | Gate |
|---|---:|---:|
| Maximum active sessions | 300 | 300 |
| Functional checks | 60,292 / 60,292 passed | >99% |
| HTTP requests | 50,802 | — |
| Failed HTTP requests | 0 | <1% |
| Interrupted iterations | 0 | 0 |
| Completed workflows | 14,295 | — |
| Ordinary read p95 | 1,637.9 ms | <2,000 ms |
| Map p95 | 1,858.2 ms | <3,000 ms |
| Write flow p95 | 1,453.4 ms | <2,000 ms |
| Login p95 | 1,065.5 ms | <2,000 ms |

Cross-UBS isolation remained valid throughout the run: the dedicated account always received an empty property list, empty property map, and zero-property dashboard.

## Bottleneck found and corrected

The first ramp exposed PostgreSQL's connection ceiling: the default API pool could consume the database's available client slots. Increasing PostgreSQL connections on the constrained host would have increased memory and scheduling pressure, so the application now applies explicit process-level limits:

- API maximum pool: 80 connections
- Worker maximum pool: 10 connections
- Remaining database slots: reserved for health checks, monitoring, administration, and maintenance

The corrected final gate peaked at 83 total PostgreSQL connections and 17 active connections. No service restarted and no application error/exception log was produced.

The first corrected gate passed all functional and ordinary/map/write targets but recorded login p95 at 2,056 ms during an unrealistic authentication burst. The final schedule spread the initial session establishment over two minutes while retaining the full two-minute 300-session hold. This models 300 simultaneous operational sessions without assuming that all users submit their passwords at nearly the same instant.

## Resource evidence at the final gate

| Resource | Peak |
|---|---:|
| Host CPU | 100% |
| Minimum host free memory | 305.7 MiB |
| API CPU / memory | 348.95% / 601.5 MiB |
| PostgreSQL CPU / memory | 400.57% / 458.0 MiB |
| Worker CPU / memory | 4.19% / 82.7 MiB |
| PostgreSQL active / total connections | 17 / 83 |

Container CPU can exceed 100% because Docker reports utilization across multiple CPU cores.

## Production hardening and verification

- Published the 80/10 pool limits to the main API and worker.
- Kept the API bound locally on `127.0.0.1:8091` and PostgreSQL on `127.0.0.1:55432`.
- Updated Nginx to re-resolve Docker service addresses automatically, preventing stale upstream addresses after a service replacement.
- Validated Nginx configuration before publication.
- Confirmed `https://stu.laudaapp.com/health/ready`, the main CSRF endpoint, and the admin CSRF endpoint return HTTP 200 after deployment.
- Confirmed API, worker, database, and gateway are running with zero restarts after the final validation.
- Full repository verification passed: 23 unit tests, 20 integration tests, 15 web tests, web lint, both production web builds, and zero .NET build warnings.

## Retained evidence

- Suite: `tests/load/stu-pilot.js`
- Runner and resource sampler: `scripts/pilot-load.ps1`
- Passed smoke: `data/pilot/load/20260828-164001-smoke`
- Passed baseline: `data/pilot/load/20260828-164014-baseline`
- Passed final gate: `data/pilot/load/20260828-165126-gate`

The `data/pilot/load` artifacts are local operational evidence and remain excluded from version control. Earlier failed/invalid attempts were retained locally for diagnosis but are not used as acceptance evidence.

## Next phase

Proceed to Phase 7.3: WCAG 2.2 AA automated checks and role-based manual accessibility/usability validation. Before expanding beyond the initial pilot, add host memory/CPU headroom or split the database and application services, then repeat the capacity gate with the intended production topology.

## References

- [Grafana k6 ramping VUs](https://grafana.com/docs/k6/latest/using-k6/scenarios/executors/ramping-vus/)
- [Grafana k6 cookies](https://grafana.com/docs/k6/latest/using-k6/cookies/)
- [Grafana k6 custom summaries](https://grafana.com/docs/k6/latest/results-output/end-of-test/custom-summary/)
