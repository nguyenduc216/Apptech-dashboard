# Construction checkin request loading fix

Date: 2026-09-17

## A. Root cause

Two independent failures produced the same empty-list symptom:

1. `getConstructionSelectedEmployeeId()` trusted `constructionEmployeeSelect.value` before the employee ID rendered by the server in `data-construction-employee-id`. A normal browser refresh can restore form control state, so the employee name, dataset and ID sent to the API could disagree.
2. Concurrent loads were not cancelled. A slower response from opening the popup, refreshing, or changing employee could overwrite the latest response.
3. `YeuCauService.GetConstructionCheckinRequestsAsync()` caught SQL exceptions and returned an empty list. The controller then returned `succeeded=true`, so the UI incorrectly displayed a genuine empty state.

## B. F5 compared with Ctrl+F5

The defect was not caused by HTTP response caching alone. The endpoint and fetch already disabled caching. The behavioral difference came from browser-restored `<select>` state during a normal refresh. A hard refresh was more likely to start with the server-rendered selected option, which made the request appear stable.

## C. Files changed

- `Views/Home/Index.cshtml`
- `Controllers/HomeController.cs`
- `Services/YeuCauService.cs`

## D. Before

- The browser-restored select value was the initial source of truth.
- Dataset, select value and employee label could represent different employees.
- All loads could complete and render, regardless of request order.
- SQL failure was converted to `[]` and rendered as "Khong co phieu yeu cau phu hop."
- An invalid requested employee could silently fall back to another employee.

## E. After

- `syncConstructionEmployeeStateFromServer()` applies the server employee ID during initialization, `pageshow`, and before opening the popup until the user explicitly changes employee.
- A user selection remains active for the current page session.
- Every load synchronizes selected employee ID, select value, dataset and displayed name.
- The request includes `_ts`, retains `cache: "no-store"`, and logs diagnostics with the `[ConstructionCheckin]` prefix.
- Backend logs requested/current/resolved employee IDs, permission, employee name and result count.
- An explicitly requested employee outside the selectable options returns HTTP 400 with a clear JSON error.

## F. Stale employee state protection

The initial source of truth is `data-construction-employee-id` rendered by the server. Browser-restored control state is overwritten when the page initializes or receives `pageshow`. Once the employee select fires a user `change`, subsequent popup opens preserve that user choice for the life of the page.

## G. Request race protection

Each load aborts the previous fetch through `AbortController`. A monotonically increasing request sequence is also checked before rendering and before re-enabling the refresh button, so an old response cannot replace the newest employee result.

## H. Empty and SQL error handling

- Empty state is rendered only for HTTP success, `succeeded === true`, and an empty `items` array.
- `YeuCauService` logs and rethrows query exceptions.
- `HomeController` logs the resolved context and returns HTTP 500 with `succeeded=false` and `message="Khong the tai danh sach phieu yeu cau."`.
- The UI renders the load-error message instead of the empty-state message.

## I. Build and publish

- `dotnet build -c Release`: PASS, 0 warnings, 0 errors.
- `dotnet publish -c Release -o publish/iis`: PASS.
- Publish path: `publish/iis` (ignored by Git).

## J. Test results

- Database target: Cao Minh Phap, employee ID `5288`.
- Direct DB query: PASS, 1 matching request.
- Service load repeated 5 times: PASS, every call returned count `1`, request ID `4020`.
- Simulated SQL connection failure: PASS, exception propagated instead of returning `[]`.
- Static browser-state review: PASS for server-state sync on init/pageshow/open, user-change preservation, ID/name synchronization, cache-busting and stale-response protection.
- CASE A/B/C browser F5 and Ctrl+F5: NOT RUN. The local database contains only password hashes and the repository/deployment notes do not provide a valid login credential.
- CASE D/E interaction through an authenticated browser: NOT RUN for the same credential limitation; the implemented AbortController and request-sequence guards were verified in source.
- CASE F: PASS at service boundary; UI HTTP 500 rendering was verified in source, not through an authenticated browser.
- CASE G: PASS by source-path verification: pageshow reapplies the server dataset value before a user-originated selection is accepted.

## K. Commit

Source fix commit: `b93a81c2a0f3151603d9448ce0b84084a393c83c`

## L. Push

- Branch: `main`
- Remote: `origin`
- Status: PASS

## M. Scope

No changes were made to Apptech checkin, purchasing, QR, warehouse receipt, database schema, map thumbnails, fullscreen popup, customer detail links, map links, telephone links, or current employee-selection permissions.
