using System.Data;
using System.Globalization;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IAttendanceSettingsService
{
    Task<AttendanceScheduleSettingsForm> GetScheduleAsync(CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> SaveScheduleAsync(
        AttendanceScheduleSettingsForm form,
        CancellationToken cancellationToken = default);
}

public sealed class AttendanceSettingsService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<AttendanceSettingsService> logger) : IAttendanceSettingsService
{
    private const string SystemConfigTableName = "TblCauHinhHeThong";
    private const string MorningStartKey = "Begin_1";
    private const string MorningEndKey = "End_1";
    private const string AfternoonStartKey = "Begin_2";
    private const string AfternoonEndKey = "End_2";
    private const string MorningLateGraceKey = "LateGraceMinutes_1";
    private const string AfternoonLateGraceKey = "LateGraceMinutes_2";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<AttendanceSettingsService> _logger = logger;

    public async Task<AttendanceScheduleSettingsForm> GetScheduleAsync(CancellationToken cancellationToken = default)
    {
        var form = AttendanceScheduleSettingsForm.Default();

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT MaCauHinh, GiaTri
                FROM [{SystemConfigTableName}]
                WHERE MaCauHinh IN (
                    N'{MorningStartKey}', N'{MorningEndKey}', N'{AfternoonStartKey}', N'{AfternoonEndKey}',
                    N'{MorningLateGraceKey}', N'{AfternoonLateGraceKey}'
                )
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = GetNullableString(reader, "MaCauHinh");
                var rawValue = GetNullableString(reader, "GiaTri");

                switch (key)
                {
                    case MorningStartKey:
                        form.MorningStart = ParseNullableTime(rawValue) ?? form.MorningStart;
                        break;
                    case MorningEndKey:
                        form.MorningEnd = ParseNullableTime(rawValue) ?? form.MorningEnd;
                        break;
                    case AfternoonStartKey:
                        form.AfternoonStart = ParseNullableTime(rawValue) ?? form.AfternoonStart;
                        break;
                    case AfternoonEndKey:
                        form.AfternoonEnd = ParseNullableTime(rawValue) ?? form.AfternoonEnd;
                        break;
                    case MorningLateGraceKey:
                        form.MorningLateGraceMinutes = ParseNullableInt(rawValue) ?? form.MorningLateGraceMinutes;
                        break;
                    case AfternoonLateGraceKey:
                        form.AfternoonLateGraceMinutes = ParseNullableInt(rawValue) ?? form.AfternoonLateGraceMinutes;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load attendance schedule settings.");
        }

        return form;
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> SaveScheduleAsync(
        AttendanceScheduleSettingsForm form,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await UpsertConfigAsync(connection, transaction, MorningStartKey, form.MorningStart, cancellationToken);
            await UpsertConfigAsync(connection, transaction, MorningEndKey, form.MorningEnd, cancellationToken);
            await UpsertConfigAsync(connection, transaction, AfternoonStartKey, form.AfternoonStart, cancellationToken);
            await UpsertConfigAsync(connection, transaction, AfternoonEndKey, form.AfternoonEnd, cancellationToken);
            await UpsertConfigAsync(connection, transaction, MorningLateGraceKey, form.MorningLateGraceMinutes, cancellationToken);
            await UpsertConfigAsync(connection, transaction, AfternoonLateGraceKey, form.AfternoonLateGraceMinutes, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save attendance schedule settings.");
            return (false, "Không thể lưu cấu hình giờ chấm công.");
        }
    }

    private static async Task UpsertConfigAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string key,
        TimeSpan value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE [{SystemConfigTableName}]
            SET GiaTri = @GiaTri
            WHERE MaCauHinh = @MaCauHinh;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO [{SystemConfigTableName}] (MaCauHinh, GiaTri)
                VALUES (@MaCauHinh, @GiaTri);
            END
            """;
        command.Parameters.Add(new SqlParameter("@MaCauHinh", SqlDbType.NVarChar, 100) { Value = key });
        command.Parameters.Add(new SqlParameter("@GiaTri", SqlDbType.NVarChar, 50) { Value = FormatTime(value) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Task UpsertConfigAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string key,
        int value,
        CancellationToken cancellationToken)
    {
        return UpsertConfigTextAsync(connection, transaction, key, Math.Clamp(value, 0, 240).ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    private static async Task UpsertConfigTextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE [{SystemConfigTableName}]
            SET GiaTri = @GiaTri
            WHERE MaCauHinh = @MaCauHinh;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO [{SystemConfigTableName}] (MaCauHinh, GiaTri)
                VALUES (@MaCauHinh, @GiaTri);
            END
            """;
        command.Parameters.Add(new SqlParameter("@MaCauHinh", SqlDbType.NVarChar, 100) { Value = key });
        command.Parameters.Add(new SqlParameter("@GiaTri", SqlDbType.NVarChar, 50) { Value = value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = !string.IsNullOrWhiteSpace(_connectionString)
            ? _connectionString
            : _sqlOptions.BuildConnectionString();

        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string FormatTime(TimeSpan value) => value.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
    }

    private static TimeSpan? ParseNullableTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out var parsedTime))
        {
            return parsedTime;
        }

        return DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var parsedDate)
            ? parsedDate.TimeOfDay
            : null;
    }

    private static int? ParseNullableInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0, 240)
            : null;
    }
}
