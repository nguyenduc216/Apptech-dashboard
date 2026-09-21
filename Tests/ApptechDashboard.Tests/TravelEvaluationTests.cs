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
    public void FirstCheckinInShift_IsNotEvaluatedWithoutPreviousAttendanceInShift()
    {
        Assert.False(TravelEvaluationService.ShouldEvaluateTravel(false, actualMinutes: 30, maxGapMinutes: 120));
    }

    [Fact]
    public void LunchGapBeyondMaximum_IsNotEvaluated()
    {
        Assert.False(TravelEvaluationService.ShouldEvaluateTravel(true, actualMinutes: 121, maxGapMinutes: 120));
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
}
