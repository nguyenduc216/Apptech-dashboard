using ApptechDashboard.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Controllers;

[AllowAnonymous]
public sealed class RatingController(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration) : Controller
{
    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");

    [HttpGet("rating/{token}")]
    public async Task<IActionResult> Index(string token, CancellationToken cancellationToken)
    {
        var link = await LoadRatingLinkAsync(token, cancellationToken);
        return Content(link is null ? RenderInvalid() : RenderForm(token), "text/html; charset=utf-8");
    }

    [HttpPost("api/ratings")]
    public async Task<IActionResult> Submit([FromForm] RatingSubmitRequest request, CancellationToken cancellationToken)
    {
        if (request.Score is < 1 or > 5 || string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new { message = "Du lieu danh gia khong hop le." });
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var link = await LoadRatingLinkAsync(connection, request.Token.Trim(), cancellationToken);
        if (link is null)
        {
            return BadRequest(new { message = "Link danh gia khong hop le hoac da het han." });
        }

        await using var duplicateCommand = connection.CreateCommand();
        duplicateCommand.CommandText = "SELECT COUNT(1) FROM [TblZaloRatings] WHERE Token = @Token";
        duplicateCommand.Parameters.Add(new SqlParameter("@Token", request.Token.Trim()));
        var duplicateCount = Convert.ToInt32(await duplicateCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
        if (duplicateCount > 0)
        {
            return BadRequest(new { message = "Danh gia nay da duoc ghi nhan." });
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [TblZaloRatings] (
                Id, BookingId, CustomerId, Token, Score, Comment, Source, CreatedAtUtc
            )
            VALUES (
                NEWID(), @BookingId, @CustomerId, @Token, @Score, @Comment, N'ZaloLink', SYSUTCDATETIME()
            )
            """;
        command.Parameters.Add(new SqlParameter("@BookingId", link.BookingId.HasValue ? link.BookingId.Value : DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CustomerId", link.CustomerId));
        command.Parameters.Add(new SqlParameter("@Token", request.Token.Trim()));
        command.Parameters.Add(new SqlParameter("@Score", request.Score));
        command.Parameters.Add(new SqlParameter("@Comment", string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim()));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return Ok(new { message = "Cam on anh/chi da gui danh gia." });
    }

    private async Task<RatingLink?> LoadRatingLinkAsync(string token, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return await LoadRatingLinkAsync(connection, token, cancellationToken);
    }

    private static async Task<RatingLink?> LoadRatingLinkAsync(SqlConnection connection, string token, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) CustomerId, BookingId, ExpiresAtUtc
            FROM [TblCustomerInteractionLinks]
            WHERE Token = @Token AND Purpose = N'Rating'
            """;
        command.Parameters.Add(new SqlParameter("@Token", token.Trim()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var expiresAt = reader["ExpiresAtUtc"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["ExpiresAtUtc"]);
        if (expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow)
        {
            return null;
        }

        return new RatingLink(
            Convert.ToInt32(reader["CustomerId"]),
            reader["BookingId"] == DBNull.Value ? null : Convert.ToInt32(reader["BookingId"]));
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

    private static async Task EnsureSchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID('dbo.TblZaloRatings', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[TblZaloRatings] (
                    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    [BookingId] INT NULL,
                    [CustomerId] INT NOT NULL,
                    [Token] NVARCHAR(200) NOT NULL,
                    [Score] INT NOT NULL,
                    [Comment] NVARCHAR(1000) NULL,
                    [Source] NVARCHAR(50) NOT NULL,
                    [CreatedAtUtc] DATETIME2 NOT NULL
                );
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string RenderInvalid() => """
        <!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Danh gia</title></head>
        <body style="font-family:Arial,sans-serif;background:#eef8f5;color:#063b35;display:grid;min-height:100vh;place-items:center"><main style="background:#fff;padding:28px;border-radius:16px;width:min(520px,calc(100% - 32px))"><h1>Link danh gia khong hop le</h1><p>Vui long lien he cong ty neu anh/chi can gui lai danh gia.</p></main></body></html>
        """;

    private static string RenderForm(string token) => $$"""
        <!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Danh gia</title>
        <style>body{font-family:Arial,sans-serif;background:#eef8f5;color:#063b35;display:grid;min-height:100vh;place-items:center}main{background:#fff;padding:28px;border-radius:16px;width:min(520px,calc(100% - 32px));box-shadow:0 18px 45px rgba(4,58,54,.16)}label{display:block;margin:14px 0}select,textarea{width:100%;box-sizing:border-box;border:1px solid #bddbd4;border-radius:10px;padding:12px;font:inherit}button{border:0;border-radius:12px;background:#15b894;color:#fff;font-weight:700;min-height:46px;padding:0 18px}</style></head>
        <body><main><h1>Danh gia trai nghiem</h1><form method="post" action="/api/ratings"><input type="hidden" name="Token" value="{{System.Net.WebUtility.HtmlEncode(token)}}"><label>Diem danh gia<select name="Score"><option value="5">5 - Rat hai long</option><option value="4">4 - Hai long</option><option value="3">3 - Binh thuong</option><option value="2">2 - Chua hai long</option><option value="1">1 - Khong hai long</option></select></label><label>Ghi chu<textarea name="Comment" rows="4"></textarea></label><button type="submit">Gui danh gia</button></form></main></body></html>
        """;

    private sealed record RatingLink(int CustomerId, int? BookingId);
}

public sealed class RatingSubmitRequest
{
    public string? Token { get; set; }
    public int Score { get; set; }
    public string? Comment { get; set; }
}
