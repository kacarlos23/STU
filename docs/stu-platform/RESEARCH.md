# STU platform research summary

## Overview

STU provides a secure visual record of UBS coverage across neighborhoods, microregions, and properties. It supports progressive map detail, structured operational visits, territorial history, role-based access, imports, exports, alerts, and audit.

## Recommended approach

- Modular ASP.NET Core backend on .NET 10 LTS;
- React and TypeScript for the UBS and global administration interfaces;
- PostgreSQL with PostGIS for spatial and operational data;
- MapLibre for browser rendering and Martin for local OSM-derived basemaps;
- Cookie-based authentication with server-enforced UBS scope and configurable roles;
- Docker-based Linux deployment behind Nginx.

## Important constraints

- No resident, family-person, or clinical records;
- The family number identifies a property and is unique within a UBS;
- No permanent deletion during ordinary use;
- Desktop-first online MVP; PWA caches static application assets only;
- Public Nominatim autocomplete and route suggestions are outside the MVP;
- The first pilot covers three neighborhoods and more than 3,000 properties.

## Risks

- Initial OSM and municipal data quality;
- Unknown production server capacity;
- Incorrect custom permission configuration;
- Accidental sensitive content in optional visit notes;
- Missing permanent infrastructure ownership before production.
