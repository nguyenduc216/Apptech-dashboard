# Apptech Work Progress Kanban V1 — 20/10/2026

FEATURE_ID: `APPTECH-WORK-PROGRESS-KANBAN`
CHANGE_ID: `APPTECH-20261020-WORK-PROGRESS-001`
Branch: `feature/work-progress-kanban-v1`, based on clean `main` / `b113c8cd14f2cf9620619c76529f245faed56ca3`.
Commit: see Git commit containing this report. Integration status: `MERGE_NEEDS_REVIEW`.

## Delivered behavior

- `/TrangThaiCongViec`: SQL-backed status catalog; create, rename, HEX picker/preview with contrasting text, reorder, activate/deactivate, audit timestamps, duplicate-code rejection and optimistic concurrency. Codes, storage keys and semantic types are immutable after creation; create another status when a different semantic type is required. No hard-delete endpoint.
- Request editor uses active catalog options plus the current inactive historical value. Display names/colors come from metadata. Unknown values no longer silently normalize to “Tạo mới”. Existing completion/attendance locks use semantic type.
- Independent work start/expected dates are editable in the request; expected date cannot precede start. Attendance timestamps retain their existing meaning and behavior. The actual completion timestamp is generated only on a transition into a completed type. Unrelated saves preserve it; reopening clears the current date while history retains the previous completion date.
- `/TienDoCongViec`: one card per request-work ID; ordered status columns, counts, customer/request/work data, staff initials with three avatars and `+N`, dates, overdue/late labels, search/customer/staff/start-date/overdue filters, refresh, empty states and horizontal scrolling. Inactive statuses with matching work remain visible. Each column returns at most 30 cards per page; counts use the whole filtered set.
- Drag/drop and keyboard/mobile select share an anti-forgery protected, narrowly scoped status endpoint. SQL transaction + rowversion prevent stale/double updates. Failed requests restore the card. Successful moves refresh only the board area, including dates and counts.
- Server permission checks use existing account/permission services and request assignment scope. Non-admin reads/moves are limited to requests assigned to their employee ID; filter option lists use the same scope. Request editor GET/POST also enforce that request scope; POST requires the existing request-update permission.
- Completion counts, request filtering and work reports use semantic type. The central SQL trigger records changes from both the existing request save path and the new move endpoint, including bulk completion.
- Progress HTML bypasses service-worker caching. Timestamped no-store refreshes also tolerate a client still running the old worker.

## Impact and data decisions

`YeuCauService.SyncAssignedWorksAsync` remains the request-work persistence owner. Existing checklist, employee, images and check-in/out code is retained. Request form rowversions are checked with update/serializable row locks before synchronizing child records. `WorkProgressService` owns catalog operations and the bounded board query/status-only operation; it never submits an entire request for a card move.

`TblYeuCauCongViec.TrangThaiCongViec` remains the immutable storage key and receives a catalog foreign key. This preserves existing integrations without rewriting stored labels. The numeric status ID is used by the new UI/API. Known accent/spacing aliases of completed/cancelled states retain their semantic kind. Other legacy strings receive disabled catalog records with their original text; their classification must be reviewed, especially mojibake data. Blank legacy states stop migration for explicit mapping.

The new date fields are separate from attendance; historical dates are intentionally left null. Before migration, the operator must inspect equivalent date fields and adapt/reuse them if they already exist. An existing rowversion or matching date field stops the default script. Do not manufacture completion dates for historical rows.

The history foreign key prevents deleting a work row once it has status history. Retain that audit trail; review any operational need to remove such work before rollout. Status type is immutable to avoid retroactively changing completion rules for referenced historical data.

## SQL artifacts and deployment gate

1. `sql/20261020_work_progress_preflight.sql` — read-only columns, legacy status distribution, triggers, foreign keys, orphans, date candidates and menu inspection.
2. `App_Data/Migrations/20261020_work_progress_kanban.sql` — transactional migration, seeds, preserved legacy entries, date/rowversion columns, status/history constraints, indexes, history/completion trigger, menus and six permissions. No role mappings are granted. It intentionally refuses to run until the operator sets `@SchemaReviewed=1` after review.
3. `sql/20261020_work_progress_verify.sql` — read-only integrity, history, trigger and role-grant checks.
4. `sql/20261020_work_progress_rollback.sql` — destructive, explicitly gated rollback; refuses custom statuses in use or remaining role grants. Prefer retaining the additive schema and deploying a compatible prior application. Export new data before any destructive rollback.

The configured database connection could not be reached for read-only schema inspection. **Production schema, menu-parent matching and historical date equivalence are unverified.** No migration or data change was executed against that configured database. The scripts were executed only on isolated, generated local SQL databases with a synthetic schema derived from current source contracts. This validates the implementation, not the production database contract. Do not deploy the binary before the reviewed migration: authenticated request/progress/home/report screens load catalog metadata.

## Verification

- Release build: PASS, zero warnings/errors.
- Full xUnit suite, including explicit local SQL integration: **209 passed, 0 failed, 0 skipped**. Set `APPTECH_KANBAN_TEST_SQL` to an explicitly authorized local server before running; without it the SQL test is visibly skipped.
- SQL checks exercise migration refusal gate/idempotency, known aliases and unknown legacy data, catalog insert/update/duplicates/stale versions, disabled states, historical cards, staff/name/color refresh, scope, date validation, real request child synchronization, retained checklist/attendance values, completed/reopened/recompleted dates/history, custom completed types and completion percentage, transactional failure and simultaneous moves, rollback refusal/approved rollback.
- Playwright: PASS at 1440×900, 768×1024 and 390×844 using actual MVC views/controllers and isolated SQL, with a test-only administrator identity. Real drag/drop, select completion, simulated HTTP 409 rollback, catalog create/preview/deactivate, avatars, overdue/late labels and document width were checked; zero JavaScript page errors. Screenshots and machine-readable results are beside this report.
- Real IIS deployment/login and full request-editor browser flow against an actual staging clone: NOT RUN. The actual request child persistence method was exercised against SQL; complete production schema and authorization/session behavior still require the postdeploy checks below.

Reproduce SQL tests from repository root:

```powershell
$env:APPTECH_KANBAN_TEST_SQL='Server=.;Integrated Security=true;TrustServerCertificate=true;Encrypt=false'
dotnet test Tests/ApptechDashboard.Tests/ApptechDashboard.Tests.csproj -c Release
```

Reproduce browser checks in separate terminals. The harness refuses non-local SQL server names, creates a uniquely named synthetic DB, binds only to loopback and drops its DB when shut down. It is excluded from the production app.

```powershell
dotnet run --project Tests/WorkProgress.UiSmoke/WorkProgress.UiSmoke.csproj -c Release
# With playwright installed, or APPTECH_PLAYWRIGHT_MODULE pointing at its local package:
node Tests/WorkProgress.UiSmoke/smoke.cjs
Invoke-WebRequest http://127.0.0.1:5188/__shutdown
```

## IIS / SQL operator procedure

1. Back up the intended staging database and current IIS files/configuration. Work on a staging clone first. Run preflight, inspect exact types/lengths/foreign keys/triggers/date semantics/status aliases/menu parent codes, and resolve any blocking differences. Never run the synthetic fixture on an application database.
2. Stop the staging app or place `app_offline.htm` to prevent writes during migration. Review and approve the script locally, then run it against that staging clone. Preserve result logs and run verification. Existing status strings and counts must remain unchanged; role mappings must not be added automatically.
3. Assign `TrangThaiCongViec_View/Create/Update/Toggle` and `TienDoCongViec_View/Update` through the existing role UI only to intended roles. Existing request editing uses its existing update permission. Sign out/in to refresh the permission session/menu.
4. Publish output is `D:\Working\T.A\Tech\Projects\apptech-dashboard\deployed-Apptech-dashboard`. It is a local package, not a running IIS deployment. Command: `dotnet publish ApptechDashboard.csproj -c Release -p:PublishProfile=WorkProgressStaging -o ..\deployed-Apptech-dashboard`.
5. The staging publish profile excludes local `appsettings*.json`, machine data-protection keys and report evidence. Preserve/configure the target IIS environment's own database, Zalo/provider settings and key directory; do not overwrite them with development files. Keep existing uploads/keys/data and verify file ACLs for the app-pool identity. Use the installed .NET 8 IIS hosting setup.
6. Copy reviewed application files, remove the offline marker, start staging and inspect logs. Verify service-worker update and refresh behavior. No production deployment or automatic merge is authorized by these local results.

Postdeploy acceptance checklist:

- Create/rename/recolor/reorder/toggle a custom status and verify catalog, request badge and Kanban all agree after refresh.
- Save two works in one request with different statuses/dates. Check only the intended work changes; inspect employees, completed checklist, images and attendance history.
- Verify inactive historical statuses remain visible and cannot be newly assigned.
- Complete, perform an unrelated save, reopen and complete again. Verify current completion date/history and request completion percentage, including a custom completed-type status.
- Drag/drop and use the dropdown on desktop/tablet/mobile; test stale tabs, simultaneous users and a failed request.
- Use view-only, request-edit and progress-edit accounts. Confirm forbidden POSTs and out-of-scope request IDs disclose/change no data. Confirm missing anti-forgery token is rejected.
- Verify customer/employee/date/overdue filters, pagination, counts, staff changes, custom colors and empty/error states.
- Run SQL postchecks and the existing attendance/checklist/image/report smoke checks. Keep production deployment pending until these pass on the actual schema.

## Changed files

Core: `Controllers/YeuCauController.cs`, `Controllers/TienDoCongViecController.cs`, `Models/YeuCauViewModels.cs`, `Models/WorkProgressModels.cs`, `Program.cs`, `Services/YeuCauService.cs`, `Services/CongViecReportService.cs`, `Services/WorkProgressService.cs`, `Services/WorkStatusContext.cs`.

UI: `Views/YeuCau/Detail.cshtml`, `Views/TienDoCongViec/Index.cshtml`, `Views/TrangThaiCongViec/Index.cshtml`, `wwwroot/css/work-progress.css`, `wwwroot/js/work-progress.js`, `wwwroot/js/work-status.js`, `wwwroot/service-worker.js`.

SQL/publish: the four SQL files above, `Properties/PublishProfiles/WorkProgressStaging.pubxml`.

Tests/evidence: `Tests/ApptechDashboard.Tests/WorkProgressTests.cs`, `Tests/ApptechDashboard.Tests/Fixtures/WorkProgress.sql`, `Tests/WorkProgress.UiSmoke/Program.cs`, `Tests/WorkProgress.UiSmoke/WorkProgress.UiSmoke.csproj`, `Tests/WorkProgress.UiSmoke/smoke.cjs`, this report, `ui-results.json` and four PNG screenshots.

History: `code-history/20261020_APPTECH-20261020-WORK-PROGRESS-001_kanban-v1.md`, `code-history/features/APPTECH-WORK-PROGRESS-KANBAN.md`.
