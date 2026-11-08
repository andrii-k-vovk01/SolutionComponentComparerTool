# Changelog

## 1.2026.11.1 - 2026-11-08

Initial version.

### Added

- Added comparison of source and target solutions by component, with the statuses Missing in target, Target has less, Only in target, Covered by table, In both and Listed.
- Added the "Also in other solutions" check for components missing in the targets.
- Added handling of root component behavior (all subcomponents, no subcomponents, shell only).
- Added the Duplicates view for components contained in two or more selected solutions.
- Added the Where used lookup for a selected component.
- Added name resolution for components through table metadata, global choices and solution component definitions.
- Added filters by status, component type and free text.
- Added export to Excel (.xlsx) and to a self-contained HTML report.
- Added per-connection persistence of selected source and target solutions.
- Added a console test runner for the comparison engine, report writers and repository paging.
- Added the Release build script (build.cmd).
- Added the user guide, screenshots and a demo animation.
- Added cancellation of a running comparison.
- Added GitHub and Help links in the XrmToolBox tool menu.
- Added NuGet packaging for the XrmToolBox Tool Library (pack.cmd), the MIT license and the security policy.
