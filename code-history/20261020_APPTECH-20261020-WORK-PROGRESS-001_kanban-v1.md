# APPTECH-20261020-WORK-PROGRESS-001

Date: 2026-10-20 (Asia/Bangkok). Agent: Codex. Project: Apptech Dashboard.
FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN. Original branch: clean/current main `b113c8cd14f2cf9620619c76529f245faed56ca3`. Feature branch: `feature/work-progress-kanban-v1` as explicitly requested.

Request: implement the supplied Work Progress Kanban V1 prompt end to end. Reused the existing request child persistence owner, account/permission services, theme and SQL Server stack. Added catalog, dates, scoped Kanban/status endpoint, history trigger, optimistic concurrency, migrations/tests and staging profile. Legacy unknown-state normalization and fixed completion comparisons were corrected. Browser testing uncovered cache-first service-worker responses reverting successful visual moves; no-store refresh and a worker bypass correct that behavior.

Changed paths, impact, operator procedure, limitations and evidence are recorded in `reports/work-progress/implementation.md`; module/data/API ownership is in `features/APPTECH-WORK-PROGRESS-KANBAN.md`.

Checks: Release build PASS (0 warnings/errors); full xUnit suite including isolated local SQL PASS (209/209, no skips); Playwright real MVC/SQL with test-only identity PASS at 1440×900, 768×1024, 390×844, zero page errors. Includes CRUD/duplicates/inactive data, known/unknown migration states, request child saving, staff refresh, history/date lifecycle, custom completed kind/percentage, scope, concurrent/stale updates and rollback. Actual IIS login/full browser request editing on a staging clone: NOT RUN.

Publish: sibling `D:\Working\T.A\Tech\Projects\apptech-dashboard\deployed-Apptech-dashboard`; `WorkProgressStaging` excludes local configuration/machine keys and reports. Local package only. Deployed output is never staged/committed. Configured database schema inspection could not connect; no DDL/data writes executed against that database. SQL migrations ran only in generated local synthetic test DBs. Operator schema/date/menu approval and actual-clone postchecks remain required.

Git safety: feature branch isolated from main; repository main reports unprotected/no rulesets, but this migration/security change requires review and cannot auto-merge. Stage only implementation/tests/evidence/history; no existing database config changes, logs, keys or deployed output. Commit SHA: see Git commit containing this history entry. Push/PR outcome and verified full SHA are returned in the task completion message. Integration state: MERGE_NEEDS_REVIEW; no production deployment.
