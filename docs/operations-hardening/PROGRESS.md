# Operations and hardening progress

- [x] Existing append-only administrative audit reviewed
- [x] Existing API rate limits, health endpoint, HTTPS headers, and private-cache policy reviewed
- [x] Browser security headers enforced at the public gateway and verified through Cloudflare
- [x] Worker heartbeat persisted
- [x] Liveness and readiness probes separated
- [x] Protected monitoring snapshot and operational alerts implemented
- [x] Global-administration monitoring screen implemented
- [x] Monitoring failure and recovery paths tested
- [x] Monitoring published and validated through Cloudflare
- [x] Backup settings and execution history persisted
- [x] Global-administrator backup API implemented
- [x] Weekly scheduler and PostgreSQL archive execution implemented
- [x] Integrity metadata and retention implemented
- [x] Administration backup interface implemented
- [x] Restore runbook completed
- [x] Automated tests passing
- [x] Production deployment and real backup validated
- [x] Real backup restored and smoke-tested in an isolated database
- [x] Security validation, remediation, and regression pass completed
- [x] Phase 7 pilot-readiness plan prepared

## Production validation

- PostgreSQL client: 18.6;
- first scheduled archive: completed successfully on 2026-08-24;
- archive size: 83,279 bytes;
- recorded SHA-256 matched the file checksum;
- `pg_restore --list` read the archive successfully;
- local gateway and both Cloudflare hostnames returned HTTP 200 after deployment.
- public responses now include CSP, HSTS, frame blocking, MIME sniffing protection, referrer policy, and permissions policy.
- the worker now reports its presence every 15 seconds and readiness rejects heartbeats older than 45 seconds;
- liveness remained HTTP 200 while the worker was stopped and readiness changed to HTTP 503 locally and through Cloudflare;
- readiness returned to HTTP 200 locally and through Cloudflare after the worker restarted;
- the protected monitoring snapshot rejects unauthenticated requests and the administration portal exposes database, worker, storage, backup, and asynchronous-operation status;
- the complete verification passed 49 automated tests, both web builds, lint checks, and the .NET release build with zero warnings;
- the real archive restored without error in 2.44 seconds and the restored API passed health, authentication, and administration smoke checks;
- the final passive web baseline produced 62 passes, no failures, and five accepted design warnings.
