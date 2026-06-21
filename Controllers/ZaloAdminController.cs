using ApptechDashboard.Configuration;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Controllers;

[Authorize]
[ApiController]
[Route("api/admin")]
public sealed class ZaloAdminController(
    IZaloAuthService zaloAuthService,
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration) : ControllerBase
{
    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");

    [HttpGet("zalo-token/status")]
    public async Task<IActionResult> TokenStatus(CancellationToken cancellationToken)
    {
        var status = await zaloAuthService.GetStatusAsync(cancellationToken);
        return Ok(status);
    }

    [HttpGet("zalo-webhook-events")]
    public async Task<IActionResult> WebhookEvents(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        if (!await TableExistsAsync(connection, "TblZaloWebhookEvents", cancellationToken))
        {
            return Ok(Array.Empty<object>());
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (100)
                Id, EventName, OaId, AppId, IsSignatureValid, ProcessedAtUtc, CreatedAtUtc
            FROM [TblZaloWebhookEvents]
            ORDER BY CreatedAtUtc DESC
            """;
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                id = reader.GetGuid(reader.GetOrdinal("Id")),
                eventName = reader["EventName"]?.ToString(),
                oaId = reader["OaId"]?.ToString(),
                appId = reader["AppId"]?.ToString(),
                isSignatureValid = Convert.ToBoolean(reader["IsSignatureValid"]),
                processedAtUtc = reader["ProcessedAtUtc"] == DBNull.Value ? null : reader["ProcessedAtUtc"],
                createdAtUtc = reader["CreatedAtUtc"]
            });
        }

        return Ok(rows);
    }

    [HttpGet("message-logs")]
    public async Task<IActionResult> MessageLogs(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        if (!await TableExistsAsync(connection, "TblZaloMessageLogs", cancellationToken))
        {
            return Ok(Array.Empty<object>());
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (100)
                Id, CustomerId, BookingId, ZaloUserId, PhoneNumber, MessageType,
                IsSuccess, ErrorMessage, CreatedAtUtc
            FROM [TblZaloMessageLogs]
            ORDER BY CreatedAtUtc DESC
            """;
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                id = reader.GetGuid(reader.GetOrdinal("Id")),
                customerId = reader["CustomerId"] == DBNull.Value ? null : reader["CustomerId"],
                bookingId = reader["BookingId"] == DBNull.Value ? null : reader["BookingId"],
                zaloUserId = reader["ZaloUserId"]?.ToString(),
                phoneNumber = reader["PhoneNumber"]?.ToString(),
                messageType = reader["MessageType"]?.ToString(),
                isSuccess = Convert.ToBoolean(reader["IsSuccess"]),
                errorMessage = reader["ErrorMessage"]?.ToString(),
                createdAtUtc = reader["CreatedAtUtc"]
            });
        }

        return Ok(rows);
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

    private static async Task<bool> TableExistsAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT OBJECT_ID(@TableName, 'U')";
        command.Parameters.Add(new SqlParameter("@TableName", $"dbo.{tableName}"));
        return await command.ExecuteScalarAsync(cancellationToken) is not null and not DBNull;
    }
}
