# Checkout Travel Exemption Hardening

## 1. Tests added

Added focused coverage for the checkout travel exemption flow:

- Early checkout with valid Travel Evaluation is not marked as early leave and report early minutes are `0`.
- Early checkout with warning Travel Evaluation remains early leave and keeps early minutes.
- Early checkout without Travel Evaluation remains early leave.
- Morning checkout followed by afternoon check-in is not exempted.
- Checkout and next check-in on different days are not exempted.
- Deleting the next check-in removes the exemption and restores early leave status.
- Migration SQL contains the expected idempotent lookup index.

The tests keep the current business logic unchanged and verify the same runtime contract used by Dashboard/report: `PreviousAttendanceId = checkout attendance ID` and `IsWarning = false`.

## 2. Database index added

Added migration:

`App_Data/Migrations/20260922_add_travel_exemption_lookup_index.sql`

Index:

```sql
IX_TblChamCongTravelEvaluation_PreviousAttendance_IsWarning
ON dbo.TblChamCongTravelEvaluation(PreviousAttendanceId, IsWarning)
```

The migration is idempotent and guarded by table/index existence checks.

## 3. Query improvement

Dashboard and report already use `EXISTS` lookup by:

```sql
PreviousAttendanceId = ch.ID
AND IsWarning = 0
```

The new index supports that lookup directly. No OSRM call, route calculation, new API, or report recalculation was added.

## 4. Regression check

Unchanged:

- `TravelEvaluationService.EvaluateAsync`.
- Travel formula and `AllowedTravelDeviationMinutes`.
- Shift resolution.
- Dashboard behavior.
- Report calculation.
- Delete lifecycle.
- Check-in, checkout, GPS, camera and map flows.

## 5. Migration required

Yes. Deploy should run:

1. `20260921_add_cham_cong_travel_evaluation.sql` if the environment does not yet have Travel Evaluation.
2. `20260921_remove_max_travel_evaluation_gap.sql`.
3. `20260922_add_travel_exemption_lookup_index.sql`.
4. Deploy application build.

## 6. Build result

Completed:

- `dotnet build apptech-dashboard.sln -c Release --no-restore`: succeeded, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: succeeded, 85/85 tests passed.
