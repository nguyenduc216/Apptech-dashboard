# APPTECH-20261020-WORK-PROGRESS-003

Date: 2026-10-20 (Asia/Bangkok). Agent: Codex. FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN.

Branch: `feature/work-progress-kanban-v1`, starting clean at `57b7c0ae79907d44f357d31d3fa1cde29a16f961`.

User supplied 13 orphan work rows and explicitly selected retaining them as historical data rather than restoring/deleting/reassigning them. The previous schema guard correctly refused all orphans but prevented this newly approved scenario.

Extended the existing schema migration owner with an exact work ID/missing request ID/work catalog ID/status allowlist. Initial install freezes work/request snapshots, rejects any unapproved or changed orphan, records retained rows in `TblWorkProgressLegacyOrphan`, and continues the existing schema/seed flow without updating original rows. Work FK protects the retention register. Installed/rerun guard also checks current orphans against the recorded snapshot. New TRY/CATCH rolls back migration failures. Existing Kanban/status-only UPDATE joins already require a real request, so orphan work remains excluded without a parallel application path. Full schema rollback drops the register, never the original work rows. Verification SQL now exposes retained/valid groups and unknown orphans.

Expected user snapshot: 53 works total; 13 retained (11 new / 2 completed), 40 eligible for normal board (28 new / 12 completed). These are supplied data counts, not a claim that the configured database was queried in this turn. No fabricated parent, date or status history; employee/checklist/image links remain in place.

Changed: schema migration, full schema rollback, preflight/verify SQL, SQL regression tests, mapping/implementation guides, living feature history and this record. Focused orphan SQL test PASS; full suite PASS (211 passed, 0 failed, 0 skipped) on explicitly authorized local SQL with disposable databases. Build/publish results are recorded in the completion message and implementation report. No UI change; prior Playwright evidence remains change 001 only.

Git: continue isolated feature branch/PR #1; do not auto-merge DB/security changes. Commit SHA: see Git commit containing this history entry. Publish remains sibling `deployed-Apptech-dashboard`, outside the index. No configured/production database migration, no IIS deployment. User will run the updated schema script after the existing schema review gate; the exact orphan-retention choice is already authorized. Integration status: MERGE_NEEDS_REVIEW.
