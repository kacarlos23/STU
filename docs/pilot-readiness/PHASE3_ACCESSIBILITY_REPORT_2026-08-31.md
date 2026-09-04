# Phase 7.3 accessibility and usability report

Date: 2026-08-31  
Target: WCAG 2.2 level AA for the STU desktop MVP  
Public portals: `stu.laudaapp.com` and `stu-admin.laudaapp.com`

## Outcome

The accessibility engineering gate is complete. No critical or high-severity accessibility defect remains open in the reviewed scope. The corrected build is deployed and both public portals and readiness probes return HTTP 200.

The review combined automated axe checks, deterministic role rendering, keyboard and focus regression tests, semantic accessibility-tree inspection in the published portals, computed-style contrast checks on both public login pages, source review of the map and form workflows, and the complete repository verification suite.

A supervised acceptance session with a person using NVDA or another real assistive technology remains recommended before the pilot go/no-go decision. Browser accessibility-tree inspection verifies name, role, value, landmarks, and reading structure, but is not a substitute for a human screen-reader acceptance session.

## Reviewed scope

- Operational and global-administration login pages.
- Agent, receptionist, doctor, manager, and global-administrator overview rendering.
- Main and administrative navigation.
- Property and visit dialogs.
- Territory editor and territorial map alternative.
- Property map picker and coordinate feedback.
- Notifications, success messages, errors, filters, and configurable tags.
- Reduced-motion behavior and visible keyboard focus.

## Automated evidence

- Seven representative axe scans target `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, and `wcag22aa`.
- Zero axe violations were found in the two logins and five representative role views.
- JSDOM cannot calculate real rendered contrast, so `color-contrast` is disabled only in component tests and supplemented by computed-style checks in the two deployed login pages. Those live checks found zero failing visible text nodes.
- The full verification completed with 65 passing tests: 23 unit, 20 integration, 16 operational-web, and 6 administrative-web tests.
- TypeScript production builds and both web linters completed successfully.

## Findings and remediation

| WCAG 2.2 criterion | Initial issue | Severity | Remediation and result |
| --- | --- | --- | --- |
| 2.4.1 Bypass Blocks | Authenticated workspaces had no direct path past the sidebar. | Medium | Added keyboard-visible “Ir para o conteúdo principal” links to both portals. |
| 2.4.3 Focus Order | SPA section changes and dialogs did not consistently move or restore focus. | High | Section headings receive focus after navigation; dialogs set initial focus, trap Tab/Shift+Tab, close with Escape, and restore focus. Regression coverage confirms the behavior. |
| 2.4.7 Focus Visible / 2.4.11 Focus Not Obscured | Focus indication varied between components. | High | Added a strong, consistent focus ring across links, buttons, fields, summaries, and custom focus targets. |
| 1.3.1 Info and Relationships / 4.1.2 Name, Role, Value | Some dialogs, selected views, navigation icons, and maps lacked complete programmatic state. | High | Added dialog roles and labels, `aria-modal`, `aria-current`, `aria-pressed`, decorative-icon hiding, named map regions, and unique IDs. |
| 1.4.1 Use of Color | Map colors could be interpreted as the only territorial signal. | Medium | Preserved named keyboard-operable neighborhood/microregion lists as an equivalent nonvisual route; map regions now describe that alternative. |
| 3.3.2 Labels or Instructions | Search and configurable-tag fields relied partly on placeholder text. | Medium | Added explicit accessible names while preserving visible examples. |
| 4.1.3 Status Messages | Save, drawing, loading, and coordinate changes were not always announced. | Medium | Added polite status regions and alert semantics without stealing focus. |
| 2.1.1 Keyboard | Notification popover and editor overlays lacked complete keyboard closure. | Medium | Added Escape support, focus return for notifications, and keyboard-safe dialogs. |
| 2.3.3 Animation from Interactions | The application did not globally honor reduced-motion preference. | Low | Added reduced-motion rules to both portals. |

## Published validation

- Main portal: HTTP 200; readiness endpoint HTTP 200.
- Global administration: HTTP 200.
- Direct API readiness on `127.0.0.1:8091`: HTTP 200.
- PostgreSQL remains healthy on `127.0.0.1:55432`.
- API, app, admin, gateway, database, Martin, and worker containers are running; no recent critical/error/fatal/exception log entry was found in the redeployed web path.
- Published accessibility trees expose labeled username/password fields, password visibility and submit buttons, two ordered headings, main landmarks, and the named STU presentation region.

## Residual items

- Conduct a short supervised NVDA or equivalent acceptance session with one representative user before final pilot approval.
- Repeat contrast and keyboard checks on authenticated production data after real role accounts are issued in the controlled onboarding rehearsal.
- The MapLibre bundle remains intentionally large and produces a build-size warning; it is lazy-loaded and does not block this accessibility gate.

## Gate decision

Accessibility engineering: **PASS**.  
Human assistive-technology acceptance: **scheduled for the final pilot acceptance gate**.
