# ADR 0001: Start as a modular monolith

**Status:** Accepted  
**Date:** 2026-08-21

## Context

STU must begin with one UBS and scale to 42 units while being maintained on municipal Linux infrastructure by a team that is not yet defined.

## Decision

Use one ASP.NET Core API organized into explicit modules, one PostgreSQL/PostGIS database, a separate background worker process, and two independently built React front-ends.

## Consequences

- Deployment and transactions remain straightforward;
- Module boundaries must be enforced through project references and tests;
- Worker tasks reuse application and infrastructure code without becoming a separate business service;
- Splitting a module into a service remains possible only when measured scale or ownership requires it.
