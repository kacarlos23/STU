# STU recovery and security validation report

Date: 2026-08-24  
Result: **Approved for Phase 7 preparation, with low-risk observations recorded**

## Executive result

The selected real STU backup was verified, restored into an isolated PostgreSQL/PostGIS database, and exercised through a temporary API instance. The restore completed successfully in 2.44 seconds and did not touch the production database. The security validation identified concrete issues in portal separation, CSRF enforcement for JSON requests, malformed-request handling, session revocation, default credentials, container footprint, and response headers. They were corrected, retested, and published.

No critical or high applicable vulnerability remains unresolved in the tested scope. The environment is ready for the next phase: capacity, accessibility, and controlled pilot preparation.

## Recovery exercise

### Archive used

- File: `stu-20260824-140455-a49deae06d344a949ab38eca6eb84066.dump`
- Format: PostgreSQL custom archive, created by PostgreSQL 18.6
- Size: 83,279 bytes
- SHA-256: `55338bf82fda876dd7047834c01a4660664d7fecb207b1615a4818c786f9a141`
- Table-of-contents entries: 171

The calculated checksum exactly matched the value stored by STU. The archive was restored with ownership and privileges excluded into the isolated database `stu_restore_20260824`, created from `template0`. Restore exit status was zero.

### Integrity and functional checks

- PostGIS, topology, and fuzzy-string extensions were present.
- No invalid index or unvalidated constraint was found.
- All application geometries used SRID 4326.
- The restored API reported the STU system identity and healthy dependencies.
- Anonymous administration access returned 401.
- A synthetic global administrator could authenticate in the correct portal, replace the temporary password, and load the administration overview.
- The API applied the one migration created after the backup, demonstrating forward compatibility of this archive with the current release.

The expected count differences were documented: production had one newer neighborhood, four newer audit records, and the monitoring table added after the archive was created. No unexplained loss was found.

The temporary API, isolated database, and temporary files were removed after validation. The original operations-volume archive was preserved.

### Recovery objectives

This exercise demonstrates a technically short database restore, but the configured operational recovery target remains up to eight business hours because it also includes incident assessment, infrastructure preparation, validation, and controlled service return. With weekly backups, the current maximum data-loss window can approach seven days. An encrypted off-host copy is still required before a real pilot to protect against loss of the host or its Docker volume.

## Security validation

The checks followed the control categories in the OWASP ASVS and the test approach in the OWASP Web Security Testing Guide. Active tests against the published service were deliberately low volume; destructive testing and production-data mutation were excluded.

### Corrections applied

1. Global administrators are now rejected by the regular UBS portal and must use the isolated administration portal.
2. JSON-changing endpoints now actively reject missing or invalid antiforgery tokens with HTTP 400.
3. Malformed JSON returns a sanitized HTTP 400 response rather than an internal-server error.
4. Archived accounts lose active sessions immediately through per-request security-stamp validation.
5. Compose no longer accepts a fallback database password. The live database credential was rotated to a randomly generated secret stored in the Windows user environment without being printed or committed.
6. Nginx runtimes were replaced by upgraded Alpine slim images, substantially reducing the installed package surface.
7. Duplicate upstream security and cache headers were removed so the public gateway emits one authoritative policy.
8. Versioned web assets were republished so Cloudflare no longer serves the older header set.

### Automated and published checks

- 49 automated tests passed: 15 unit, 19 integration, 11 main-interface, and 4 administration-interface tests.
- The .NET release build finished with zero warnings and zero errors.
- Both web applications passed lint and production builds.
- NuGet and npm audits found no known actionable package vulnerability.
- Gitleaks scanned approximately 7.87 MB and found no leaked secret.
- Anonymous private API routes consistently returned 401.
- Missing CSRF and malformed JSON returned safe 400 responses.
- Cookies were Secure, HttpOnly, and SameSite Strict.
- A disallowed origin received no CORS permission.
- TRACE returned 405.
- Rate limiting returned 429 after the permitted burst while readiness remained healthy.
- Public application, administration, and readiness endpoints returned HTTP 200 after publication.

### Passive web baseline

The final passive ZAP baseline examined nine URLs and produced 62 passes, no failures, and five warnings accepted by design:

- private and application-shell responses intentionally use no-store/no-cache;
- content-hashed static assets intentionally use immutable caching;
- `style-src 'unsafe-inline'` remains necessary for the current MapLibre and dynamic-style integration;
- the “modern web application” result is informational;
- Cross-Origin-Embedder-Policy is not enabled because STU does not require cross-origin isolation and it could block external OSM/MapLibre resources.

The scanner also reported OpenSSL CVE-2026-14456 in two Alpine libraries. OpenSSL classifies it as low severity and limits the affected path to an OpenSSL QUIC server listener. STU's Nginx containers serve internal HTTP only; HTTPS and QUIC terminate at Cloudflare. It is therefore not applicable to the deployed path, but the Alpine package should be upgraded when 3.5.8 or newer becomes available.

## Limitations

- This was an internal engineering validation, not an independent penetration test.
- There was no destructive production test, denial-of-service test, credential attack, or full authenticated crawler against real data.
- A complete production failover was intentionally not performed; the restore was isolated.
- Horizontal UBS and role boundaries were exercised through repeatable integration tests with synthetic data.
- Performance under 300 simultaneous users and WCAG 2.2 accessibility remain Phase 7 gates.

## Release decision and next actions

The current release is approved to enter Phase 7 preparation. Before pilot approval, STU must complete the 3,000-property/20-microregion/100-account dataset rehearsal, the 300-concurrent-user capacity test, WCAG 2.2 AA review, encrypted off-host backup procedure, and controlled real-data onboarding checklist.

## Primary references

- PostgreSQL `pg_restore`: https://www.postgresql.org/docs/current/app-pgrestore.html
- OWASP ASVS: https://owasp.org/www-project-application-security-verification-standard/
- OWASP Web Security Testing Guide: https://owasp.org/www-project-web-security-testing-guide/latest/README
- ASP.NET Core antiforgery guidance: https://learn.microsoft.com/aspnet/core/security/anti-request-forgery
- OpenSSL CVE-2026-14456: https://www.openssl-library.org/news/vulnerabilities-3.6/
