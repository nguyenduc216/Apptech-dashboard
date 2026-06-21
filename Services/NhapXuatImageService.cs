using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface INhapXuatImageService
{
    Task<IReadOnlyList<NhapXuatImageItem>> GetImagesAsync(
        int phieuId,
        string loaiPhieu,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, NhapXuatImageItem? Image)> AddImageAsync(
        int phieuId,
        string loaiPhieu,
        string imagePath,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteImageAsync(
        int phieuId,
        string loaiPhieu,
        string imagePath,
        CancellationToken cancellationToken = default);
}

public sealed class NhapXuatImageService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<NhapXuatImageService> logger) : INhapXuatImageService
{
    public const string TableName = "TblNhapXuatImage";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<NhapXuatImageService> _logger = logger;

    public async Task<IReadOnlyList<NhapXuatImageItem>> GetImagesAsync(
        int phieuId,
        string loaiPhieu,
        CancellationToken cancellationToken = default)
    {
        if (phieuId <= 0 || string.IsNullOrWhiteSpace(loaiPhieu))
        {
            return [];
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, transaction: null, cancellationToken);

            var items = new List<NhapXuatImageItem>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                    ID,
                    IDPhieu,
                    LoaiPhieu,
                    ImagePath
                FROM [{TableName}]
                WHERE IDPhieu = @IDPhieu
                  AND LoaiPhieu = @LoaiPhieu
                ORDER BY ID ASC
                """;
            command.Parameters.Add(new SqlParameter("@IDPhieu", SqlDbType.Int) { Value = phieuId });
            command.Parameters.Add(new SqlParameter("@LoaiPhieu", SqlDbType.NVarChar, 20) { Value = loaiPhieu.Trim() });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var imagePath = GetNullableString(reader, "ImagePath");
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    continue;
                }

                items.Add(new NhapXuatImageItem
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    PhieuId = reader.GetInt32(reader.GetOrdinal("IDPhieu")),
                    LoaiPhieu = GetNullableString(reader, "LoaiPhieu") ?? string.Empty,
                    ImagePath = imagePath
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblNhapXuatImage for {LoaiPhieu} {PhieuId}.", loaiPhieu, phieuId);
            return [];
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, NhapXuatImageItem? Image)> AddImageAsync(
        int phieuId,
        string loaiPhieu,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (phieuId <= 0 || string.IsNullOrWhiteSpace(loaiPhieu) || string.IsNullOrWhiteSpace(imagePath))
        {
            return (false, "Thông tin hình ảnh không hợp lệ.", null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, transaction: null, cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    IDPhieu,
                    LoaiPhieu,
                    ImagePath
                )
                VALUES (
                    @IDPhieu,
                    @LoaiPhieu,
                    @ImagePath
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;
            command.Parameters.Add(new SqlParameter("@IDPhieu", SqlDbType.Int) { Value = phieuId });
            command.Parameters.Add(new SqlParameter("@LoaiPhieu", SqlDbType.NVarChar, 20) { Value = loaiPhieu.Trim() });
            command.Parameters.Add(new SqlParameter("@ImagePath", SqlDbType.NVarChar, 550) { Value = imagePath.Trim() });

            var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            if (id <= 0)
            {
                return (false, "Không thể lưu thông tin hình ảnh.", null);
            }

            return (true, null, new NhapXuatImageItem
            {
                Id = id,
                PhieuId = phieuId,
                LoaiPhieu = loaiPhieu.Trim(),
                ImagePath = imagePath.Trim()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add image for {LoaiPhieu} {PhieuId}: {ImagePath}.", loaiPhieu, phieuId, imagePath);
            return (false, "Không thể lưu thông tin hình ảnh.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteImageAsync(
        int phieuId,
        string loaiPhieu,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (phieuId <= 0 || string.IsNullOrWhiteSpace(loaiPhieu) || string.IsNullOrWhiteSpace(imagePath))
        {
            return (false, "Thông tin hình ảnh không hợp lệ.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, transaction: null, cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DELETE FROM [{TableName}]
                WHERE IDPhieu = @IDPhieu
                  AND LoaiPhieu = @LoaiPhieu
                  AND ImagePath = @ImagePath
                """;
            command.Parameters.Add(new SqlParameter("@IDPhieu", SqlDbType.Int) { Value = phieuId });
            command.Parameters.Add(new SqlParameter("@LoaiPhieu", SqlDbType.NVarChar, 20) { Value = loaiPhieu.Trim() });
            command.Parameters.Add(new SqlParameter("@ImagePath", SqlDbType.NVarChar, 550) { Value = imagePath.Trim() });

            await command.ExecuteNonQueryAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete image for {LoaiPhieu} {PhieuId}: {ImagePath}.", loaiPhieu, phieuId, imagePath);
            return (false, "Không thể xóa hình ảnh.");
        }
    }

    public static async Task EnsureSchemaAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var checkCommand = connection.CreateCommand();
        checkCommand.Transaction = transaction;
        checkCommand.CommandText = """
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = @TableName
            """;
        checkCommand.Parameters.Add(new SqlParameter("@TableName", SqlDbType.NVarChar, 128) { Value = TableName });

        var exists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync(cancellationToken) ?? 0) > 0;
        if (exists)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            CREATE TABLE [dbo].[{TableName}] (
                [ID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_{TableName}] PRIMARY KEY,
                [IDPhieu] int NOT NULL,
                [LoaiPhieu] nvarchar(20) NOT NULL,
                [ImagePath] nvarchar(550) NOT NULL
            );

            CREATE INDEX [IX_{TableName}_Phieu]
            ON [dbo].[{TableName}] ([LoaiPhieu], [IDPhieu]);
            """;
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

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
    }
}
