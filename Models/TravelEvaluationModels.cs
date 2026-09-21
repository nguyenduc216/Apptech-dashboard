namespace ApptechDashboard.Models;

public sealed record TravelRouteResult(decimal DistanceKm, decimal ExpectedMinutes);

public sealed record TravelEvaluationCalculation(
    decimal ExpectedMinutes,
    decimal ActualMinutes,
    decimal DeviationMinutes,
    bool IsWarning);

public sealed class ChamCongTravelEvaluation
{
    public int CurrentAttendanceId { get; set; }
    public int PreviousAttendanceId { get; set; }
    public int EmployeeId { get; set; }
    public decimal FromLatitude { get; set; }
    public decimal FromLongitude { get; set; }
    public decimal ToLatitude { get; set; }
    public decimal ToLongitude { get; set; }
    public decimal DistanceKm { get; set; }
    public decimal ExpectedTravelMinutes { get; set; }
    public decimal ActualTravelMinutes { get; set; }
    public decimal DeviationMinutes { get; set; }
    public int AllowedDeviationMinutes { get; set; }
    public bool IsWarning { get; set; }
}
