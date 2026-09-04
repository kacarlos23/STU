# Phase 7 pilot-readiness research

## Objective

Prove that STU remains responsive and safe at the planned initial scale, is usable by the four operational roles, and can receive the first real territorial dataset through a controlled and reversible procedure.

## Capacity model

The planning baseline is at least 3,000 properties, about 20 microregions, 100 registered workers, and 300 simultaneous sessions. Tests must use synthetic data and realistic mixes rather than 300 users repeatedly calling one endpoint. Grafana k6 scenarios and thresholds provide repeatable workload stages and an objective release gate.

The user-facing targets are ordinary screens within two seconds, a usable map within three seconds, fluid map movement with thousands of properties, and an error rate below one percent during the agreed test window.

## Accessibility model

The main desktop workflows will be checked against WCAG 2.2 Level AA. Automation can catch missing semantics, contrast, and labeling defects, but keyboard-only navigation, focus order, error recovery, zoom, and screen-reader announcements require manual checks for each role.

## Pilot model

Real data should enter through a staged import into one UBS, followed by geographic validation, duplicate review, permission review, user acceptance, training, backup, and a documented go/no-go decision. The existing archive and audit mechanisms form the rollback foundation, but an encrypted off-host copy must be operational before pilot authorization.

## References

- Grafana k6 thresholds: https://grafana.com/docs/k6/latest/using-k6/thresholds/
- Grafana k6 scenarios: https://grafana.com/docs/k6/latest/using-k6/scenarios/
- W3C WCAG overview: https://www.w3.org/WAI/standards-guidelines/wcag/
- WCAG 2.2 quick reference: https://www.w3.org/WAI/WCAG22/quickref/
