# APPTECH-20261020-WORK-PROGRESS-002

Date: 2026-10-20 (Asia/Bangkok). Agent: Codex. Project: Apptech Dashboard.

FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN. Branch: `feature/work-progress-kanban-v1`, clean starting commit `79095e941b8961048b1c83324f528bb97302888c`.

User approved retaining text catalog linkage instead of adding a numeric status foreign key and requested data migration. Supplied screenshot shows “Tạo mới”, 14 bytes, 39 rows; “Hoàn thành”, 20 bytes, 14 rows. This is user evidence, not a live schema inspection.

Preserved the existing `TrangThaiCongViec -> StorageValue` contract and schema migration owner. Added `App_Data/Migrations/20261020_work_progress_status_data.sql`: preview default, append missing standard catalog seeds only, exact-byte mapping validation, optional 39/14 snapshot enforcement, conflict/unmapped transaction rollback, execution output identifying inserted rows. Existing catalog customization and work text/dates/rowversions/history are unchanged. Added guarded catalog-only rollback, mapping previews/postchecks, Vietnamese operator guide and local SQL regression coverage.

Changed files: the new data migration and `sql/20261020_work_progress_status_data_rollback.sql`; existing preflight/verify SQL; `Tests/ApptechDashboard.Tests/WorkProgressTests.cs`; `reports/work-progress/status-data-mapping.md`, implementation report, this history and living feature index.

Verification commands: focused `dotnet test ... --filter FullyQualifiedName~StatusDataMigration`, full Release tests with explicitly set `APPTECH_KANBAN_TEST_SQL`, Release build and staging publish. Focused SQL test PASS; full suite PASS (210 passed, 0 failed, 0 skipped). Build/publish results are recorded in the implementation report and completion message. UI was not rerun for this script-only delta; prior UI evidence belongs to change 001.

Git/publish: continue existing isolated feature branch and PR #1; never merge this migration automatically. Publish remains sibling `deployed-Apptech-dashboard`, excludes local config/keys; no deployed artifacts staged. No production/configured database DDL or data writes. Production schema/date/menu verification remains pending. Commit SHA: see Git commit containing this history entry; actual pushed SHA is returned in the completion message. Integration: `MERGE_NEEDS_REVIEW`.
