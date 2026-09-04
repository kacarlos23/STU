# STU repository guidance

- Keep the backend as a modular monolith. Domain code must not depend on Infrastructure or API.
- Enforce UBS scope and permissions on the server for every private query and command.
- Never cache authenticated responses, private map layers, visits, or property data in the PWA.
- Preserve audit history and archive records instead of deleting operational data.
- Store geographic data in PostGIS and use SRID 4326 unless a documented decision says otherwise.
- Add or update tests with each behavior change.
- Do not commit credentials, generated exports, backups, map archives, or local runtime data.
