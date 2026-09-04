# Admin organization research

## Overview

Complete the organizational part of STU Phase 2 through the isolated global administration portal: health units, users, transfers, password resets, custom roles, permissions and append-only audit records.

## Problem statement

The platform already authenticates users and seeds protected roles, but operational growth still depends on manual database/bootstrap work. Expansion to additional UBS requires safe self-service administration with no permanent deletion and no weakening of protected defaults.

## User stories / use cases

- A global administrator registers and updates a UBS.
- A global administrator creates an individual server account with one role and one UBS.
- A server is transferred to another UBS without losing identity or history.
- A forgotten password is replaced by a one-time temporary password.
- A global administrator creates a custom operational role and selects its permissions.
- In-use UBS and roles cannot be archived until dependencies are resolved.
- Every addition or modification is visible in an append-only audit trail.

## Technical research

### Approach options

1. Direct Identity and EF Core administration endpoints: uses the existing user, role and database managers and keeps one authorization boundary.
2. Separate identity service: adds operational complexity and eventual consistency without value at the current scale.
3. Direct database administration: bypasses Identity validation, security stamps and password-reset tokens and is therefore rejected.

### Recommended approach

Keep administration inside the modular monolith. Use ASP.NET Core Identity managers for user/role mutation, EF Core for UBS and audit persistence, secure cookie policies for authorization, and antiforgery tokens for every browser mutation. Use unique database indexes for stable identifiers and optimistic concurrency stamps already provided by Identity.

### Required technologies

- ASP.NET Core Identity `UserManager` and `RoleManager`;
- EF Core and PostgreSQL;
- React forms and same-origin JSON APIs;
- cryptographically secure temporary-password generation.

### Data requirements

- UBS: code, name, creation/update/archive timestamps.
- User: username, display name, UBS, temporary-password flag, archive state and one assigned role.
- Role: stable name, display name, description, system/custom flag, archive state and permission claims.
- Audit: actor, action, entity type/id, summary, before/after JSON, IP and timestamp.

## UI/UX considerations

- Keep the overview and three management areas inside the existing isolated shell.
- Show active/archived state and prevent invalid actions with clear messages.
- Display generated temporary passwords once, with an explicit copy action.
- Use constrained permission checkboxes rather than free text.
- Keep tables usable on desktop and stack forms on smaller screens.

## Integration points

- Existing `GlobalAdministration` authorization policy.
- Existing antiforgery endpoint and secure session cookie.
- Existing Identity tables, health-unit aggregate and role permission claims.
- Existing admin counts and dashboard navigation.

## Risks and challenges

- Role permission changes can leave active cookies stale: update security stamps for affected users.
- Archiving referenced records can break operations: block archive while active dependencies exist.
- Administrators can lock themselves out: prohibit self-archive, self-role replacement and self-password reset in this workflow.
- Password exposure: return generated temporary passwords only in create/reset responses and never persist plaintext.
- Audit tampering: expose read-only audit APIs and no delete/update endpoint.

## Open questions

- None for this phase. Institutional login and two-factor authentication remain future work as previously approved.

## References

- https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.usermanager-1.resetpasswordasync?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- https://learn.microsoft.com/en-us/ef/core/modeling/indexes
