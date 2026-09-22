using System.Data;
using System.Globalization;
using System.Text.Json;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface ITravelEvaluationService
{
    Task EvaluateAsync(int currentAttendanceId, CancellationToken cancellationToken = default);
}

public sealed class TravelEvaluationService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    IAttendanceSettingsService attendanceSettingsService,
    IHttpClientFactory httpClientFactory,
    ILogger<TravelEvaluationService> logger) : ITravelEvaluationService
{
    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");

    public async Task EvaluateAsync(int currentAttendanceId, CancellationToken cancellationToken = default)
    {
        if (currentAttendanceId <= 0) return;

        try
        {
            var settings = await attendanceSettingsService.GetScheduleAsync(cancellationToken);
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var current = await LoadAttendanceAsync(connection, currentAttendanceId, cancellationToken);
            if (current is null || current.EmployeeId <= 0 || !current.CheckinTime.HasValue || !current.Latitude.HasValue || !current.Longitude.HasValue)
            {
                return;
            }

            var shift = ResolveShift(current.CheckinTime.Value.TimeOfDay, settings);
            if (shift is null) return;

            var shiftStart = current.CheckinTime.Value.Date.Add(shift.Value.Start);
            var previous = await LoadPreviousInShiftAsync(
                connection,
                current,
                shiftStart,
                current.CheckinTime.Value,
                cancellationToken);
            if (previous is null) return;
            if (!IsPreviousAttendanceInShift(previous.CheckinTime, current.CheckinTime.Value, shift.Value)) return;

            var previousTime = previous.CheckoutTime ?? previous.CheckinTime;
            var fromLatitude = previous.CheckoutTime.HasValue ? previous.CheckoutLatitude : previous.Latitude;
            var fromLongitude = previous.CheckoutTime.HasValue ? previous.CheckoutLongitude : previous.Longitude;
            if (!previousTime.HasValue || !fromLatitude.HasValue || !fromLongitude.HasValue) return;

            var actualMinutes = (decimal)(current.CheckinTime.Value - previousTime.Value).TotalMinutes;
            if (actualMinutes < 0) return;

            var route = await GetRouteAsync(
                fromLatitude.Value, fromLongitude.Value,
                current.Latitude.Value, current.Longitude.Value,
                cancellationToken);
            if (route is null) return;

            var calculation = Calculate(route.ExpectedMinutes, actualMinutes, settings.AllowedTravelDeviationMinutes);
            await SaveAsync(connection, new ChamCongTravelEvaluation
            {
                CurrentAttendanceId = current.Id,
                PreviousAttendanceId = previous.Id,
                EmployeeId = current.EmployeeId,
                FromLatitude = fromLatitude.Value,
                FromLongitude = fromLongitude.Value,
                ToLatitude = current.Latitude.Value,
                ToLongitude = current.Longitude.Value,
                DistanceKm = route.DistanceKm,
                ExpectedTravelMinutes = calculation.ExpectedMinutes,
                ActualTravelMinutes = calculation.ActualMinutes,
                DeviationMinutes = calculation.DeviationMinutes,
                AllowedDeviationMinutes = settings.AllowedTravelDeviationMinutes,
                IsWarning = calculation.IsWarning
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Travel evaluation failed for attendance {AttendanceId}; check-in remains successful.", currentAttendanceId);
        }
    }

    public static TravelEvaluationCalculation Calculate(decimal expectedMinutes, decimal actualMinutes, int allowedDeviationMinutes)
    {
        var deviation = actualMinutes - expectedMinutes;
        return new(expectedMinutes, actualMinutes, deviation, deviation > allowedDeviationMinutes);
    }

    public static bool ShouldCountEarlyCheckout(bool isBeforeShiftEnd, bool hasValidNextTravel)
    {
        return isBeforeShiftEnd && !hasValidNextTravel;
    }

    public static (TimeSpan Start, TimeSpan End)? ResolveShift(TimeSpan time, AttendanceScheduleSettingsForm settings)
    {
        if (time >= settings.MorningStart && time <= settings.MorningEnd) return (settings.MorningStart, settings.MorningEnd);
        if (time >= settings.AfternoonStart && time <= settings.AfternoonEnd) return (settings.AfternoonStart, settings.AfternoonEnd);
        return null;
    }

    public static bool IsPreviousAttendanceInShift(
        DateTime? previousCheckinTime,
        DateTime currentCheckinTime,
        (TimeSpan Start, TimeSpan End) shift)
    {
        if (!previousCheckinTime.HasValue || previousCheckinTime.Value.Date != currentCheckinTime.Date)
        {
            return false;
        }

        var shiftStart = currentCheckinTime.Date.Add(shift.Start);
        var shiftEnd = currentCheckinTime.Date.Add(shift.End);
        return previousCheckinTime.Value >= shiftStart &&
               previousCheckinTime.Value <= shiftEnd &&
               previousCheckinTime.Value < currentCheckinTime;
    }

    private async Task<TravelRouteResult?> GetRouteAsync(decimal fromLat, decimal fromLng, decimal toLat, decimal toLng, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("AttendanceRoute");
        var url = FormattableString.Invariant($"route/v1/driving/{fromLng},{fromLat};{toLng},{toLat}?overview=false");
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0) return null;
        var route = routes[0];
        var distanceKm = route.GetProperty("distance").GetDecimal() / 1000m;
        var expectedMinutes = route.GetProperty("duration").GetDecimal() / 60m;
        return new(decimal.Round(distanceKm, 2), decimal.Round(expectedMinutes, 2));
    }

    private static async Task<AttendancePoint?> LoadAttendanceAsync(SqlConnection connection, int id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ID, IDNhanVien, ThoiDiem, ThoiDiemCheckOut, LatAddress, LongAddress, LatAddressCheckOut, LongAddressCheckOut FROM dbo.TblCheckinHistory WHERE ID = @Id";
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapPoint(reader) : null;
    }

    private static async Task<AttendancePoint?> LoadPreviousInShiftAsync(
        SqlConnection connection,
        AttendancePoint current,
        DateTime shiftStart,
        DateTime currentCheckinTime,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) ID, IDNhanVien, ThoiDiem, ThoiDiemCheckOut, LatAddress, LongAddress, LatAddressCheckOut, LongAddressCheckOut FROM dbo.TblCheckinHistory WHERE IDNhanVien = @EmployeeId AND ID <> @Id AND ThoiDiem >= @ShiftStart AND ThoiDiem < @CurrentTime ORDER BY ThoiDiem DESC, ID DESC";
        command.Parameters.Add(new SqlParameter("@EmployeeId", SqlDbType.Int) { Value = current.EmployeeId });
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = current.Id });
        command.Parameters.Add(new SqlParameter("@ShiftStart", SqlDbType.DateTime) { Value = shiftStart });
        command.Parameters.Add(new SqlParameter("@CurrentTime", SqlDbType.DateTime) { Value = currentCheckinTime });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapPoint(reader) : null;
    }

    private static AttendancePoint MapPoint(SqlDataReader reader) => new(
        reader.GetInt32(reader.GetOrdinal("ID")),
        reader.IsDBNull(reader.GetOrdinal("IDNhanVien")) ? 0 : reader.GetInt32(reader.GetOrdinal("IDNhanVien")),
        GetDateTime(reader, "ThoiDiem"), GetDateTime(reader, "ThoiDiemCheckOut"),
        GetDecimal(reader, "LatAddress"), GetDecimal(reader, "LongAddress"),
        GetDecimal(reader, "LatAddressCheckOut"), GetDecimal(reader, "LongAddressCheckOut"));

    private static async Task SaveAsync(SqlConnection connection, ChamCongTravelEvaluation value, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.TblChamCongTravelEvaluation SET PreviousAttendanceId=@PreviousAttendanceId, EmployeeId=@EmployeeId, FromLatitude=@FromLatitude, FromLongitude=@FromLongitude, ToLatitude=@ToLatitude, ToLongitude=@ToLongitude, DistanceKm=@DistanceKm, ExpectedTravelMinutes=@ExpectedTravelMinutes, ActualTravelMinutes=@ActualTravelMinutes, DeviationMinutes=@DeviationMinutes, AllowedDeviationMinutes=@AllowedDeviationMinutes, IsWarning=@IsWarning, CreatedDate=SYSUTCDATETIME() WHERE CurrentAttendanceId=@CurrentAttendanceId;
            IF @@ROWCOUNT = 0 INSERT INTO dbo.TblChamCongTravelEvaluation (CurrentAttendanceId, PreviousAttendanceId, EmployeeId, FromLatitude, FromLongitude, ToLatitude, ToLongitude, DistanceKm, ExpectedTravelMinutes, ActualTravelMinutes, DeviationMinutes, AllowedDeviationMinutes, IsWarning) VALUES (@CurrentAttendanceId, @PreviousAttendanceId, @EmployeeId, @FromLatitude, @FromLongitude, @ToLatitude, @ToLongitude, @DistanceKm, @ExpectedTravelMinutes, @ActualTravelMinutes, @DeviationMinutes, @AllowedDeviationMinutes, @IsWarning);
            """;
        command.Parameters.AddWithValue("@CurrentAttendanceId", value.CurrentAttendanceId);
        command.Parameters.AddWithValue("@PreviousAttendanceId", value.PreviousAttendanceId);
        command.Parameters.AddWithValue("@EmployeeId", value.EmployeeId);
        command.Parameters.AddWithValue("@FromLatitude", value.FromLatitude); command.Parameters.AddWithValue("@FromLongitude", value.FromLongitude);
        command.Parameters.AddWithValue("@ToLatitude", value.ToLatitude); command.Parameters.AddWithValue("@ToLongitude", value.ToLongitude);
        command.Parameters.AddWithValue("@DistanceKm", value.DistanceKm); command.Parameters.AddWithValue("@ExpectedTravelMinutes", value.ExpectedTravelMinutes);
        command.Parameters.AddWithValue("@ActualTravelMinutes", value.ActualTravelMinutes); command.Parameters.AddWithValue("@DeviationMinutes", value.DeviationMinutes);
        command.Parameters.AddWithValue("@AllowedDeviationMinutes", value.AllowedDeviationMinutes); command.Parameters.AddWithValue("@IsWarning", value.IsWarning);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(!string.IsNullOrWhiteSpace(_connectionString) ? _connectionString : _sqlOptions.BuildConnectionString());
        await connection.OpenAsync(cancellationToken); return connection;
    }
    private static DateTime? GetDateTime(SqlDataReader r, string n) { var i=r.GetOrdinal(n); return r.IsDBNull(i)?null:r.GetDateTime(i); }
    private static decimal? GetDecimal(SqlDataReader r, string n) { var i=r.GetOrdinal(n); return r.IsDBNull(i)?null:Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture); }
    private sealed record AttendancePoint(int Id, int EmployeeId, DateTime? CheckinTime, DateTime? CheckoutTime, decimal? Latitude, decimal? Longitude, decimal? CheckoutLatitude, decimal? CheckoutLongitude);
}
