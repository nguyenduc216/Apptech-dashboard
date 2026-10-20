# APPTECH-WORK-PROGRESS-KANBAN

Purpose: configurable request-work states, independent progress dates and a permission-scoped Kanban.

Architecture baseline: extends existing `YeuCauService.SyncAssignedWorksAsync` and `YeuCauCongViecTrangThaiCatalog`; the latter resolves request-scoped SQL metadata through `WorkStatusContext`. `WorkProgressService` owns catalog persistence, board filtering/paging and status-only moves. Existing `IUserAccountService`/`IUserPermissionService` provide permission/scope decisions; menu/permission seeds are operator migration-owned.

Data invariants: one card per `TblYeuCauCongViec.ID`; immutable storage key with FK to `TblTrangThaiCongViec`; semantic kind defines completion/cancellation; unknown history never silently becomes new. Start/expected dates are independent of check-in/out. Trigger writes history and current actual completion date in the same transaction; reopening retains old completion in history. Rowversions guard editor/card/catalog writes. Staff queries are batched. Historical inactive states stay visible; no hard-delete status API. History FK prevents deleting a work row with status history.

Routes: `/TrangThaiCongViec`, POST `/TrangThaiCongViec/Save` and `/Toggle`; `/TienDoCongViec`, POST `/TienDoCongViec/Move`. Mutations require anti-forgery and explicit permission. Non-admin scope is request assignment, including filter options and writes.

Constraints: existing configured SQL database could not be reached. Migration requires operator preflight approval and exact schema/date/menu review; never auto-run. Existing date/rowversion contracts require adaptation rather than duplicate columns. Codes/semantic kinds are immutable after creation. Historical completion dates remain null unless a new transition is observed. Request/progress/home/report screens require installed catalog schema. No production/IIS deployment, no auto-merge for this DB/security change.

Change index:

- 2026-10-20 — `APPTECH-20261020-WORK-PROGRESS-001`: implementation, guarded SQL scripts, 209-test suite and local SQL/Playwright evidence; production validation pending. See `../20261020_APPTECH-20261020-WORK-PROGRESS-001_kanban-v1.md`.
- 2026-10-20 — `APPTECH-20261020-WORK-PROGRESS-002`: user approved retaining text/StorageValue mapping and supplied a snapshot of 39 new / 14 completed work rows. Added preview-first, append-only catalog seed/mapping verification, optional exact snapshot gate and guarded removal of newly inserted, unreferenced catalog rows. Original work text/dates/rowversions/history are untouched; no numeric status ID column. See `../20261020_APPTECH-20261020-WORK-PROGRESS-002_status-data-mapping.md` and `../../reports/work-progress/status-data-mapping.md`.
- 2026-10-20 — `APPTECH-20261020-WORK-PROGRESS-003`: user approved retaining 13 exact orphan work/request/catalog/status tuples as history. Schema migration records `TblWorkProgressLegacyOrphan`, preserves all original rows and rejects additional/changed orphan tuples on initial install and reruns. Existing board/move request joins exclude missing parents; snapshot remains 53 total / 40 board-eligible. See `../20261020_APPTECH-20261020-WORK-PROGRESS-003_retain-reviewed-orphans.md`.
