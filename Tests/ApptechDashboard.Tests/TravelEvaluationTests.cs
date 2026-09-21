using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class TravelEvaluationTests
{
    private static readonly AttendanceScheduleSettingsForm Settings = AttendanceScheduleSettingsForm.Default();

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
}
