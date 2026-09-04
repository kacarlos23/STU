# Monitoring

The STU exposes two public probes and one protected operational view:

- `GET /health/live` confirms that the API process is responding. It does not depend on the database, storage, or worker.
- `GET /health/ready` confirms that PostgreSQL is reachable, the operations directory is writable, and the worker reported activity in the last 45 seconds.
- `GET /api/admin/monitoring/status` is restricted to the global administrator and adds actionable backup and asynchronous-operation alerts.

The administration portal refreshes the protected snapshot every 30 seconds. It reports database, worker, operations storage, backup, and operation status without exposing credentials, file paths, or record contents.

## Suggested external checks

An infrastructure monitor can request `/health/live` and `/health/ready` every minute. Alert when liveness fails once or when readiness fails for two consecutive checks. Notification delivery by e-mail, webhook, or another municipal channel remains optional until an institutional destination is selected.

Readiness intentionally becomes unavailable when the worker heartbeat is older than 45 seconds. This catches a stopped background processor while keeping liveness available for diagnosis.
