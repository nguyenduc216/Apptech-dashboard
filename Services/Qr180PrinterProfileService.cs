using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IQr180PrinterProfileService
{
    Task<IReadOnlyList<Qr180PrinterProfile>> GetProfilesAsync(CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage, int? ProfileId)> SaveAsync(Qr180ProfileSaveRequest request, CancellationToken cancellationToken = default);
}

public sealed class Qr180PrinterProfileService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<Qr180PrinterProfileService> logger) : IQr180PrinterProfileService
{
    private const string TableName = "TblQr180PrinterProfile";
    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<Qr180PrinterProfileService> _logger = logger;

    public async Task<IReadOnlyList<Qr180PrinterProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT Id, ProfileName, OffsetX, OffsetY, PitchX, PitchY, QrSize, IsDefault, CreatedAt, UpdatedAt FROM [dbo].[{TableName}] ORDER BY IsDefault DESC, ProfileName";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var profiles = new List<Qr180PrinterProfile>();
            while (await reader.ReadAsync(cancellationToken))
            {
                profiles.Add(new Qr180PrinterProfile
                {
                    Id = reader.GetInt32(0),
                    ProfileName = reader.GetString(1),
                    OffsetX = reader.GetDecimal(2),
                    OffsetY = reader.GetDecimal(3),
                    PitchX = reader.GetDecimal(4),
                    PitchY = reader.GetDecimal(5),
                    QrSize = reader.GetDecimal(6),
                    IsDefault = reader.GetBoolean(7),
                    CreatedAt = new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
                    UpdatedAt = new DateTimeOffset(reader.GetDateTime(9), TimeSpan.Zero)
                });
            }

            return profiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load QR 180 printer profiles; using the built-in default profile.");
            return [CreateFallbackDefault()];
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? ProfileId)> SaveAsync(
        Qr180ProfileSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF @Id IS NOT NULL AND EXISTS (SELECT 1 FROM [dbo].[{TableName}] WHERE Id = @Id AND IsDefault = 0)
                BEGIN
                    UPDATE [dbo].[{TableName}]
                    SET ProfileName = @ProfileName, OffsetX = @OffsetX, OffsetY = @OffsetY,
                        PitchX = @PitchX, PitchY = @PitchY, QrSize = @QrSize, UpdatedAt = SYSUTCDATETIME()
                    WHERE Id = @Id;
                    SELECT CAST(@Id AS int);
                END
                ELSE
                BEGIN
                    INSERT INTO [dbo].[{TableName}] (ProfileName, OffsetX, OffsetY, PitchX, PitchY, QrSize, IsDefault)
                    VALUES (@ProfileName, @OffsetX, @OffsetY, @PitchX, @PitchY, @QrSize, 0);
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                END
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = request.Id is > 0 ? request.Id.Value : DBNull.Value });
            command.Parameters.Add(new SqlParameter("@ProfileName", SqlDbType.NVarChar, 100) { Value = request.ProfileName.Trim() });
            AddDecimal(command, "@OffsetX", request.OffsetX);
            AddDecimal(command, "@OffsetY", request.OffsetY);
            AddDecimal(command, "@PitchX", request.PitchX);
            AddDecimal(command, "@PitchY", request.PitchY);
            AddDecimal(command, "@QrSize", request.QrSize);
            var id = (int?)await command.ExecuteScalarAsync(cancellationToken);
            return (true, null, id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return (false, "Tên cấu hình máy in đã tồn tại.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save QR 180 printer profile.");
            return (false, "Không thể lưu cấu hình máy in lúc này.", null);
        }
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = !string.IsNullOrWhiteSpace(_connectionString) ? _connectionString : _sqlOptions.BuildConnectionString();
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID(N'[dbo].[{TableName}]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{TableName}] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_{TableName}] PRIMARY KEY,
                    [ProfileName] nvarchar(100) NOT NULL,
                    [OffsetX] decimal(7,2) NOT NULL,
                    [OffsetY] decimal(7,2) NOT NULL,
                    [PitchX] decimal(7,2) NOT NULL,
                    [PitchY] decimal(7,2) NOT NULL,
                    [QrSize] decimal(7,2) NOT NULL,
                    [IsDefault] bit NOT NULL CONSTRAINT [DF_{TableName}_IsDefault] DEFAULT(0),
                    [CreatedAt] datetime2(0) NOT NULL CONSTRAINT [DF_{TableName}_CreatedAt] DEFAULT(SYSUTCDATETIME()),
                    [UpdatedAt] datetime2(0) NOT NULL CONSTRAINT [DF_{TableName}_UpdatedAt] DEFAULT(SYSUTCDATETIME()),
                    CONSTRAINT [UQ_{TableName}_ProfileName] UNIQUE ([ProfileName])
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM [dbo].[{TableName}] WHERE IsDefault = 1)
                INSERT INTO [dbo].[{TableName}] (ProfileName, OffsetX, OffsetY, PitchX, PitchY, QrSize, IsDefault)
                VALUES (N'Mặc định', 0, 0, 20, 15, 14.5, 1);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddDecimal(SqlCommand command, string name, decimal value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Decimal) { Precision = 7, Scale = 2, Value = value });

    private static Qr180PrinterProfile CreateFallbackDefault() => new()
    {
        ProfileName = "Mặc định",
        IsDefault = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
