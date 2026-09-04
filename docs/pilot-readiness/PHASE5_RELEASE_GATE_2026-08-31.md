# Phase 7.5 — Pilot release gate

## Outcome

The final pilot release control plane is implemented. Managers can assess their own UBS and global administrators can select any active UBS. A `go` decision fails closed while mandatory automatic or human evidence is missing; a `no-go` decision can be recorded at any time with a reason. Both decisions are append-only audit events and do not activate, import, edit, or archive operational data.

## Automatic evidence

- the retained 300-session capacity result;
- the WCAG 2.2 AA engineering result;
- a current approved onboarding snapshot;
- encrypted off-host backup evidence tied to that onboarding approval;
- a recent unpruned backup and no stalled backup run;
- worker heartbeat and writable operational storage;
- no stalled or recently failed operation requiring intervention.

The release snapshot hash contains stable evidence state rather than observation timestamps. A material change makes the prior decision visibly outdated.

## Human confirmations required for `go`

- supervised accessibility acceptance;
- role-based training completed;
- representative-user acceptance completed;
- no critical or high defect remains open;
- named support, incident, and rollback owners.

## Interfaces

- Main portal: managers receive `Liberação do piloto` in the management navigation.
- Global portal: administrators receive the same workspace with an active-UBS selector.
- Both portals include printable quick guides and support, incident, and restoration procedures.

## Security and scope

- password-change policy and API rate limiting remain mandatory;
- manager requests are scoped to the account's UBS on the server;
- anonymous access returns `401` and cross-UBS manager access returns `403`;
- all decisions require antiforgery validation;
- audit evidence contains operational names and hashes, never credentials or encryption keys.

## Current pilot state

The intended neighborhoods are `Luiz Eduardo Magalhães`, `Nova Teixeira`, and `Redenção`. The final gate remains blocked until their real microregions and properties are rehearsed and approved, the encrypted copy is transferred to the external Windows computer, and the supervised training and acceptance activities are completed.

## Verification and publication

- .NET release build: zero warnings and zero errors;
- 23 unit, 22 integration, 16 main-portal, and 8 administration-portal tests passed (69 total);
- main portal, administration portal, gateway readiness, and direct API readiness returned HTTP 200;
- anonymous final-gate access returned HTTP 401;
- local gateway, API, and PostgreSQL ports were listening and PostgreSQL accepted connections;
- both public login pages loaded in the integrated browser without console errors.
