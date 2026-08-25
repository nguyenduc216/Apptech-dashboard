using System.Data;
using System.Text.Json;
using ApptechDashboard.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface ICommonAuditService
{
    Task WriteAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CommonAuditEntry entry,
        CancellationToken cancellationToken = default);

    Task WriteAsync(
        CommonAuditEntry entry,
        CancellationToken cancellationToken = default);
}

public sealed record CommonAuditEntry(
    string FunctionCode,
    string ActionCode,
    string ObjectType,
    string ObjectId,
    string? ObjectCode,
    string Description,
    string UserName,
    object? Data = null,
    object? OldData = null,
    object? NewData = null);

public sealed class CommonAuditService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<CommonAuditService> logger) : ICommonAuditService
{
    private const string TableName = "TblCommonLogging";
    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<CommonAuditService> _logger = logger;

    public async Task WriteAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CommonAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var columns = await LoadExistingColumnsAsync(connection, transaction, cancellationToken);
            if (columns.Count == 0)
            {
                return;
            }

            var values = BuildValues(entry);
            var insertColumns = values.Keys
                .Where(column => columns.Contains(column))
                .ToArray();

            if (insertColumns.Length == 0)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    {string.Join(", ", insertColumns.Select(column => $"[{column}]"))}
                )
                VALUES (
                    {string.Join(", ", insertColumns.Select(column => $"@{column}"))}
                )
                """;

            foreach (var column in insertColumns)
            {
                command.Parameters.Add(new SqlParameter($"@{column}", values[column] ?? DBNull.Value));
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write common audit log {FunctionCode}/{ActionCode}/{ObjectType}/{ObjectId}.", entry.FunctionCode, entry.ActionCode, entry.ObjectType, entry.ObjectId);
        }
    }

    public async Task WriteAsync(CommonAuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await WriteAsync(connection, null, entry, cancellationToken);
    }

    private static async Task<HashSet<string>> LoadExistingColumnsAsync(SqlConnection connection, SqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @TableName
            """;
        command.Parameters.Add(new SqlParameter("@TableName", SqlDbType.NVarChar, 128) { Value = TableName });

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static Dictionary<string, object?> BuildValues(CommonAuditEntry entry)
    {
        var now = DateTime.Now;
        var dataJson = ToJson(entry.Data);
        var oldJson = ToJson(entry.OldData);
        var newJson = ToJson(entry.NewData);
        var searchKey = $"{entry.FunctionCode}|{entry.ActionCode}|{entry.ObjectType}|{entry.ObjectId}|{entry.ObjectCode}".Trim('|');

        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["FunctionCode"] = entry.FunctionCode,
            ["ActionCode"] = entry.ActionCode,
            ["ObjectType"] = entry.ObjectType,
            ["ObjectId"] = entry.ObjectId,
            ["ObjectCode"] = entry.ObjectCode,
            ["ReferenceCode"] = entry.ObjectCode,
            ["SearchKey"] = searchKey,
            ["Description"] = entry.Description,
            ["Content"] = entry.Description,
            ["LogContent"] = entry.Description,
            ["Data"] = dataJson,
            ["DataJson"] = dataJson,
            ["OldData"] = oldJson,
            ["OldValue"] = oldJson,
            ["NewData"] = newJson,
            ["NewValue"] = newJson,
            ["Created_Date"] = now,
            ["CreatedDate"] = now,
            ["Created_By"] = entry.UserName,
            ["CreatedBy"] = entry.UserName,
            ["UserName"] = entry.UserName,
            ["CreatedUser"] = entry.UserName
        };
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

    private static string? ToJson(object? value)
    {
        return value is null
            ? null
            : JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
