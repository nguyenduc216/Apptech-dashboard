# APPTECH-20261020-WORK-PROGRESS-005

Date: 2026-10-20 (Asia/Bangkok). Agent: Codex. FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN.

Clean starting commit `e892d23b28590edc0b768e49563cb4a898a00618`, existing isolated branch `feature/work-progress-kanban-v1` / PR #1. User reported too many redirects at Login ReturnUrl=TienDoCongViec and confirmed an admin account.

Root cause verified in source: global cookie AccessDeniedPath was Login; an authenticated Login redirects back to the forbidden ReturnUrl, repeating indefinitely. New progress authorization also only considered the administrator bit, unlike established request module conventions for persisted admin username/group. Actual production account bit was not directly read, so the latter is a reproduced compatibility case rather than a claimed server inspection.

Changed shared cookie-denial configuration to a dedicated terminal 403 MVC view; AJAX/JSON receives 403 JSON, no Location. Anonymous login/challenge remains unchanged. Denial page ignores ReturnUrl and has no permission/sidebar dependencies. WorkProgressService uses one active-current-account helper for both Allowed/Scope, including legacy persisted admin identity/group; ordinary users still need explicit rights and retain assignment filtering. Fake display/role/group claims do not override a normal persisted account. No SQL/role mappings changed.

Tests: real Kestrel HTTP with actual cookie handler and MVC route/view, anonymous challenge, authenticated denial redirect terminates at 403, unsafe ReturnUrl ignored, AJAX/JSON 403, administrator-bit/legacy-name/group cases, inactive/missing accounts and ordinary permission/scope restrictions. Browser harness now uses persisted admin username with administrator flag false, exercising actual SQL board/moves/catalog plus 403 page. Final test/build/publish results are in implementation report/completion message.

Changes: Program, Configuration/CookieAuthorization.cs, Controllers/AccessController.cs, Views/Access/Denied.cshtml, Services/WorkProgressService.cs, focused tests, UI harness/results/screenshots and guides/history. Publish to sibling deployed-Apptech-dashboard excluding local config/keys, never stage artifacts. No server restart/deployment or configured database access performed. Preserve server config/keys when deploying the fix; no migration rerun needed. Commit SHA: see Git commit containing this history entry. No auto-merge; MERGE_NEEDS_REVIEW.
