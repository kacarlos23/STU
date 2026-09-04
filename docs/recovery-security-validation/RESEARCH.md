# Recovery and security validation research

## Objective

Demonstrate that a real STU archive can be restored into an isolated database and that the published application enforces its authentication, authorization, tenant isolation, input, session, transport, and operational controls.

## Safety boundaries

- Never restore over the production database.
- Use the existing completed archive and compare its recorded SHA-256 before restoration.
- Use only synthetic accounts and records for active security tests.
- Keep denial-of-service, destructive production mutations, credential attacks, and data extraction out of scope.
- Prefer automated integration tests for role and UBS-isolation attacks; use passive or low-volume checks against the published endpoints.

## Recovery approach

Restore the custom-format PostgreSQL archive into a new database created from `template0`, with ownership and ACL restoration disabled and immediate exit on error. Validate migrations, extensions, constraints, spatial indexes, core row counts, and application startup against the clone. Record restore duration and remove the isolated database only after the evidence is captured.

## Security approach

Use the OWASP ASVS as a control checklist and the OWASP Web Security Testing Guide for targeted negative tests. Cover anonymous access, portal separation, role permissions, horizontal UBS isolation, CSRF, session termination, archived-account behavior, rate limiting, input/file validation, CORS, security headers, cache policy, error sanitization, dependency vulnerabilities, secret exposure, container exposure, and backup authorization.

## Known configuration risk to verify

The Compose file currently permits a development database password when no external configuration is supplied. The database port is bound only to loopback, which limits exposure, but the pilot configuration must fail closed or use a separately managed secret.

## References

- PostgreSQL 18 `pg_restore`: https://www.postgresql.org/docs/current/app-pgrestore.html
- OWASP ASVS: https://owasp.org/www-project-application-security-verification-standard/
- OWASP WSTG: https://owasp.org/www-project-web-security-testing-guide/latest/README

