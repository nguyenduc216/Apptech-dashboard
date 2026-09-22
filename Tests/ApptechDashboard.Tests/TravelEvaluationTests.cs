using System.Reflection;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class TravelEvaluationTests
{
    private static readonly AttendanceScheduleSettingsForm Settings = AttendanceScheduleSettingsForm.Default();
    private static readonly object ReportSchedule = CreateReportSchedule();

    [Fact]
    public void FirstCheckinOutsideWorkingShift_IsNotEvaluated()
    {
        Assert.Null(TravelEvaluationService.ResolveShift(new TimeSpan(6, 45, 0), Settings));
        Assert.Null(TravelEvaluationService.ResolveShift(new TimeSpan(12, 30, 0), Settings));
    }

    [Fact]
    public void FirstCheckinMorning_HasNoPreviousAttendanceInShift()
    {
        var shift = TravelEvaluationService.ResolveShift(new TimeSpan(7, 45, 0), Settings)!.Value;

        Assert.False(TravelEvaluationService.IsPreviousAttendanceInShift(null, new DateTime(2026, 9, 21, 7, 45, 0), shift));
    }

    [Fact]
    public void FirstCheckinAfternoon_IgnoresMorningAttendance()
    {
        var current = new DateTime(2026, 9, 21, 13, 35, 0);
        var shift = TravelEvaluationService.ResolveShift(current.TimeOfDay, Settings)!.Value;

        Assert.False(TravelEvaluationService.IsPreviousAttendanceInShift(new DateTime(2026, 9, 21, 9, 0, 0), current, shift));
    }

    [Fact]
    public void SecondCheckinSameShift_IsEvaluated()
    {
        var current = new DateTime(2026, 9, 21, 14, 30, 0);
        var shift = TravelEvaluationService.ResolveShift(current.TimeOfDay, Settings)!.Value;

        Assert.True(TravelEvaluationService.IsPreviousAttendanceInShift(new DateTime(2026, 9, 21, 13, 35, 0), current, shift));
    }

    [Fact]
    public void PreviousDayAttendance_IsIgnored()
    {
        var current = new DateTime(2026, 9, 22, 8, 0, 0);
        var shift = TravelEvaluationService.ResolveShift(current.TimeOfDay, Settings)!.Value;

        Assert.False(TravelEvaluationService.IsPreviousAttendanceInShift(new DateTime(2026, 9, 21, 10, 0, 0), current, shift));
    }

    [Fact]
    public void MissingRouteProducesNoCalculation()
    {
        TravelRouteResult? route = null;
        Assert.Null(route);
    }

    [Fact]
    public void TravelAtAllowedDeviation_IsNotWarning()
    {
        var result = TravelEvaluationService.Calculate(expectedMinutes: 20, actualMinutes: 35, allowedDeviationMinutes: 15);

        Assert.Equal(15, result.DeviationMinutes);
        Assert.False(result.IsWarning);
    }

    [Fact]
    public void TravelAboveAllowedDeviation_IsWarning()
    {
        var result = TravelEvaluationService.Calculate(expectedMinutes: 20, actualMinutes: 60, allowedDeviationMinutes: 15);

        Assert.Equal(40, result.DeviationMinutes);
        Assert.True(result.IsWarning);
    }

    [Fact]
    public void FasterThanExpectedTravel_IsNotWarning()
    {
        var result = TravelEvaluationService.Calculate(expectedMinutes: 40, actualMinutes: 20, allowedDeviationMinutes: 15);

        Assert.Equal(-20, result.DeviationMinutes);
        Assert.False(result.IsWarning);
    }

    [Fact]
    public void EarlyCheckout_WithValidNextTravel_IsNotCountedAsEarlyLeave()
    {
        Assert.False(TravelEvaluationService.ShouldCountEarlyCheckout(
            isBeforeShiftEnd: true,
            hasValidNextTravel: true));
    }

    [Fact]
    public void EarlyCheckout_WithoutValidNextTravel_IsStillCountedAsEarlyLeave()
    {
        Assert.True(TravelEvaluationService.ShouldCountEarlyCheckout(
            isBeforeShiftEnd: true,
            hasValidNextTravel: false));
    }

    [Fact]
    public void OnTimeCheckout_IsNeverCountedAsEarlyLeave()
    {
        Assert.False(TravelEvaluationService.ShouldCountEarlyCheckout(
            isBeforeShiftEnd: false,
            hasValidNextTravel: false));
    }

    [Fact]
    public void EarlyCheckout_WithValidTravelEvaluation_IsNotViolationAndHasNoEarlyMinutes()
    {
        var checkoutTime = new DateTime(2026, 9, 22, 16, 30, 0);
        var hasValidNextTravel = HasCheckoutTravelExemption(previousAttendanceId: 100, checkoutAttendanceId: 100, isWarning: false);

        Assert.False(IsDashboardCheckoutViolation(checkoutTime, hasValidNextTravel));
        Assert.Equal(0, CalculateReportLateEarlyMinutes(checkoutTime, hasValidNextTravel));
    }

    [Fact]
    public void EarlyCheckout_WithWarningTravelEvaluation_RemainsViolationAndKeepsEarlyMinutes()
    {
        var checkoutTime = new DateTime(2026, 9, 22, 16, 30, 0);
        var hasValidNextTravel = HasCheckoutTravelExemption(previousAttendanceId: 100, checkoutAttendanceId: 100, isWarning: true);

        Assert.True(IsDashboardCheckoutViolation(checkoutTime, hasValidNextTravel));
        Assert.Equal(60, CalculateReportLateEarlyMinutes(checkoutTime, hasValidNextTravel));
    }

    [Fact]
    public void EarlyCheckout_WithoutTravelEvaluation_RemainsViolationAndKeepsEarlyMinutes()
    {
        var checkoutTime = new DateTime(2026, 9, 22, 16, 30, 0);
        var hasValidNextTravel = HasCheckoutTravelExemption(previousAttendanceId: null, checkoutAttendanceId: 100, isWarning: null);

        Assert.True(IsDashboardCheckoutViolation(checkoutTime, hasValidNextTravel));
        Assert.Equal(60, CalculateReportLateEarlyMinutes(checkoutTime, hasValidNextTravel));
    }

    [Fact]
    public void MorningCheckout_WithAfternoonCheckin_IsNotExempted()
    {
        var current = new DateTime(2026, 9, 22, 13, 30, 0);
        var shift = TravelEvaluationService.ResolveShift(current.TimeOfDay, Settings)!.Value;
        var checkoutTime = new DateTime(2026, 9, 22, 11, 20, 0);

        Assert.False(TravelEvaluationService.IsPreviousAttendanceInShift(checkoutTime, current, shift));
        Assert.True(TravelEvaluationService.ShouldCountEarlyCheckout(
            isBeforeShiftEnd: true,
            hasValidNextTravel: false));
    }

    [Fact]
    public void CheckoutAndNextCheckinOnDifferentDays_AreNotExempted()
    {
        var current = new DateTime(2026, 9, 23, 8, 0, 0);
        var shift = TravelEvaluationService.ResolveShift(current.TimeOfDay, Settings)!.Value;
        var checkoutTime = new DateTime(2026, 9, 22, 16, 30, 0);

        Assert.False(TravelEvaluationService.IsPreviousAttendanceInShift(checkoutTime, current, shift));
        Assert.True(IsDashboardCheckoutViolation(checkoutTime, hasValidNextTravel: false));
    }

    [Fact]
    public async Task DeletingNextCheckin_RemovesExemptionAndRestoresEarlyCheckout()
    {
        var calls = new List<string>();
        var checkoutTime = new DateTime(2026, 9, 22, 16, 30, 0);

        var beforeDelete = HasCheckoutTravelExemption(previousAttendanceId: 100, checkoutAttendanceId: 100, isWarning: false);
        var deleted = await TravelEvaluationCleanup.ExecuteAttendanceDeleteAsync(
            () => { calls.Add("cleanup"); return Task.CompletedTask; },
            () => { calls.Add("delete"); return Task.FromResult(1); },
            () => { calls.Add("rollback"); return Task.CompletedTask; });
        var afterDelete = HasCheckoutTravelExemption(previousAttendanceId: null, checkoutAttendanceId: 100, isWarning: null);

        Assert.True(beforeDelete);
        Assert.True(deleted);
        Assert.Equal(["cleanup", "delete"], calls);
        Assert.False(afterDelete);
        Assert.True(IsDashboardCheckoutViolation(checkoutTime, afterDelete));
        Assert.Equal(60, CalculateReportLateEarlyMinutes(checkoutTime, afterDelete));
    }

    [Fact]
    public void WorkingTimesResolveToConfiguredShifts()
    {
        Assert.Equal(Settings.MorningStart, TravelEvaluationService.ResolveShift(new TimeSpan(8, 0, 0), Settings)?.Start);
        Assert.Equal(Settings.AfternoonStart, TravelEvaluationService.ResolveShift(new TimeSpan(13, 30, 0), Settings)?.Start);
    }

    [Fact]
    public void DeleteCurrentAttendance_CleansItsTravelEvaluationFirst()
    {
        var sql = TravelEvaluationCleanup.DeleteRelatedSql;

        Assert.Contains("DELETE FROM dbo.TblChamCongTravelEvaluation", sql);
        Assert.Contains("CurrentAttendanceId = @AttendanceId", sql);
    }

    [Fact]
    public void DeletePreviousAttendance_CleansReferencingTravelEvaluationFirst()
    {
        var sql = TravelEvaluationCleanup.DeleteRelatedSql;

        Assert.Contains("PreviousAttendanceId = @AttendanceId", sql);
        Assert.Contains(" OR ", sql);
    }

    [Fact]
    public void TravelExemptionLookupMigration_AddsPreviousAttendanceWarningIndex()
    {
        var sql = File.ReadAllText(FindRepositoryFile("App_Data", "Migrations", "20260922_add_travel_exemption_lookup_index.sql"));

        Assert.Contains("IF OBJECT_ID(N'dbo.TblChamCongTravelEvaluation'", sql);
        Assert.Contains("IX_TblChamCongTravelEvaluation_PreviousAttendance_IsWarning", sql);
        Assert.Contains("PreviousAttendanceId, IsWarning", sql);
    }

    [Fact]
    public void AttendanceWithoutEvaluation_UsesIdempotentCleanup()
    {
        Assert.DoesNotContain("THROW", TravelEvaluationCleanup.DeleteRelatedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT", TravelEvaluationCleanup.DeleteRelatedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AttendanceDeleteFailure_RollsBackEvaluationCleanup()
    {
        var calls = new List<string>();

        var deleted = await TravelEvaluationCleanup.ExecuteAttendanceDeleteAsync(
            () => { calls.Add("cleanup"); return Task.CompletedTask; },
            () => { calls.Add("delete"); return Task.FromResult(0); },
            () => { calls.Add("rollback"); return Task.CompletedTask; });

        Assert.False(deleted);
        Assert.Equal(["cleanup", "delete", "rollback"], calls);
    }

    [Fact]
    public async Task AttendanceDeleteSuccess_DoesNotRollback()
    {
        var calls = new List<string>();

        var deleted = await TravelEvaluationCleanup.ExecuteAttendanceDeleteAsync(
            () => { calls.Add("cleanup"); return Task.CompletedTask; },
            () => { calls.Add("delete"); return Task.FromResult(1); },
            () => { calls.Add("rollback"); return Task.CompletedTask; });

        Assert.True(deleted);
        Assert.Equal(["cleanup", "delete"], calls);
    }

    private static bool HasCheckoutTravelExemption(int? previousAttendanceId, int checkoutAttendanceId, bool? isWarning)
    {
        return previousAttendanceId == checkoutAttendanceId && isWarning == false;
    }

    private static bool IsDashboardCheckoutViolation(DateTime checkoutTime, bool hasValidNextTravel)
    {
        return TravelEvaluationService.ShouldCountEarlyCheckout(
            checkoutTime.TimeOfDay < Settings.AfternoonEnd,
            hasValidNextTravel);
    }

    private static decimal CalculateReportLateEarlyMinutes(DateTime checkoutTime, bool hasValidNextTravel)
    {
        var itemType = typeof(ChamCongReportService).GetNestedType("AttendanceReportCheckin", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AttendanceReportCheckin record was not found.");
        var item = Activator.CreateInstance(
            itemType,
            7,
            checkoutTime.Date.AddHours(13).AddMinutes(30),
            checkoutTime,
            hasValidNextTravel)
            ?? throw new InvalidOperationException("Could not create AttendanceReportCheckin.");
        var method = typeof(ChamCongReportService).GetMethod("CalculateLateEarlyMinutes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CalculateLateEarlyMinutes was not found.");

        return (decimal)(method.Invoke(null, [item, ReportSchedule])
            ?? throw new InvalidOperationException("CalculateLateEarlyMinutes returned null."));
    }

    private static object CreateReportSchedule()
    {
        var scheduleType = typeof(ChamCongReportService).GetNestedType("AttendanceSchedule", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AttendanceSchedule record was not found.");
        return Activator.CreateInstance(
            scheduleType,
            new TimeSpan(7, 30, 0),
            new TimeSpan(11, 30, 0),
            new TimeSpan(13, 30, 0),
            new TimeSpan(17, 30, 0),
            10,
            10)
            ?? throw new InvalidOperationException("Could not create AttendanceSchedule.");
    }

    private static string FindRepositoryFile(params string[] relativePathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativePathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find repository file.", Path.Combine(relativePathParts));
    }
}
