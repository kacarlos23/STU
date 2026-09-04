# Identity and access research

## Overview

This feature makes the STU entry flow operational for the UBS and global administration portals. It introduces individual accounts, secure browser sessions, mandatory temporary-password replacement, UBS membership, protected default roles, and a first authenticated dashboard.

## Problem statement

The current web shells are public and the database has no identity schema. Before territorial or operational records can be implemented, every request must have an authenticated actor, the actor's UBS scope, and a server-enforced permission boundary.

## User stories

- A UBS employee signs in with an individual username and password and sees their name, role, and UBS.
- A user with a temporary password must replace it before accessing operational screens.
- A global administrator signs in through the isolated administration hostname.
- A non-global account cannot enter the global administration portal.
- A signed-in user can close the session explicitly.
- Locked, archived, or incorrectly scoped accounts cannot start a session.

## Technical research

### Approach options

1. Built-in Identity API endpoints: quick, but exposes registration and account-management flows that STU does not want in the MVP.
2. Custom Identity-backed endpoints: keeps ASP.NET Core Identity password hashing, lockout, cookies, roles, and security stamps while exposing only STU workflows.
3. External identity provider: useful for future institutional login, but not approved for the MVP.

### Recommended approach

Use custom minimal API endpoints backed by ASP.NET Core Identity and PostgreSQL. Use secure, HTTP-only cookies for browser sessions and antiforgery tokens for every state-changing request. Store role permissions as Identity role claims so custom roles can be added later without replacing the authentication model.

### Data requirements

- Application user: display name, username, UBS, mandatory password-change flag, archive timestamp.
- Application role: display metadata, protected-system flag, archive timestamp.
- UBS: existing domain entity linked to users.
- Role claims: permission identifiers for future configurable RBAC.
- Data protection keys: persisted in PostgreSQL so sessions survive API restarts.

## UI/UX considerations

- Desktop-first, responsive login with clear field labels and password visibility control.
- Generic invalid-credential response to avoid account enumeration.
- Dedicated mandatory-password-change screen.
- Authenticated dashboard with user context, session exit, and first operational summary.
- Separate visual identity and authorization check for the global admin portal.

## Integration points

- `STU.Infrastructure` owns Identity persistence and initialization.
- `STU.Api` exposes session and protected summary endpoints.
- Both React applications use same-origin `/api` requests with credentials.
- Nginx and Cloudflare continue routing each hostname to its correct portal.

## Risks and mitigations

- CSRF on cookie-authenticated writes: antiforgery cookie plus request token.
- Brute-force login: Identity lockout and a strict login rate limit.
- Session invalidation on restart: database-persisted data-protection keys.
- Privilege leakage: global admin endpoint and portal login enforce the protected role on the server.
- Weak bootstrap credential: one-time random passwords, never committed, with mandatory replacement.

## References

- https://learn.microsoft.com/aspnet/core/security/authentication/identity-api-authorization
- https://learn.microsoft.com/aspnet/core/security/anti-request-forgery
- https://learn.microsoft.com/aspnet/core/security/authentication/api-endpoint-auth

