using System.Data;
using ApptechDashboard.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IZaloSettingsService
{
    ZaloOptions Current { get; }
    string Source { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ZaloOptions options, bool updateAppSecret, bool updateOaSecretKey, CancellationToken cancellationToken = default);
}

public sealed class ZaloSettingsService : IZaloSettingsService
{
    private const string TableName = "TblZaloSettings";
    private readonly SqlServerOptions _sqlOptions;
    private readonly string? _connectionString;
    private readonly IDataProtector _protector;
    private readonly ILogger<ZaloSettingsService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ZaloOptions _current;
    private string _source = "AppSettings";

    public ZaloSettingsService(
        IOptions<SqlServerOptions> sqlOptions,
        IOptions<ZaloOptions> zaloOptions,
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ZaloSettingsService> logger)
    {
        _sqlOptions = sqlOptions.Value;
        _connectionString = configuration.GetConnectionString("DefaultConnection");
        _protector = dataProtectionProvider.CreateProtector("ApptechDashboard.ZaloSettings.v1");
        _logger = logger;
        _configuration = configuration;
        _current = Clone(zaloOptions.Value);
    }

    public ZaloOptions Current => Clone(_current);
    public string Source => _source;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ZaloOptions? database = null;
            try
            {
                await using var connection = await OpenConnectionAsync(cancellationToken);
                await EnsureTableAsync(connection, cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT TOP (1)
                        AppId, AppSecret, OaId, OaSecretKey, OAuthRedirectUri,
                        ApiBaseUrl, OAuthBaseUrl, PublicBaseUrl,
                        RefreshBeforeExpiryMinutes, AccessTokenLifetimeHours,
                        TextMessageEndpoint, TokenEndpoint, OAuthAuthorizePath,
                        EnableSignatureValidation
                    FROM [{TableName}]
                    WHERE Id = 1
                    """;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    database = new ZaloOptions
                    {
                        AppId = GetString(reader, "AppId"),
                        AppSecret = Unprotect(GetString(reader, "AppSecret")),
                        OaId = GetString(reader, "OaId"),
                        OaSecretKey = Unprotect(GetString(reader, "OaSecretKey")),
                        OAuthRedirectUri = GetString(reader, "OAuthRedirectUri"),
                        ApiBaseUrl = GetString(reader, "ApiBaseUrl") ?? string.Empty,
                        OAuthBaseUrl = GetString(reader, "OAuthBaseUrl") ?? string.Empty,
                        PublicBaseUrl = GetString(reader, "PublicBaseUrl"),
                        RefreshBeforeExpiryMinutes = Convert.ToInt32(reader["RefreshBeforeExpiryMinutes"]),
                        AccessTokenLifetimeHours = Convert.ToInt32(reader["AccessTokenLifetimeHours"]),
                        TextMessageEndpoint = GetString(reader, "TextMessageEndpoint") ?? string.Empty,
                        TokenEndpoint = GetString(reader, "TokenEndpoint") ?? string.Empty,
                        OAuthAuthorizePath = GetString(reader, "OAuthAuthorizePath") ?? string.Empty,
                        EnableSignatureValidation = Convert.ToBoolean(reader["EnableSignatureValidation"])
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load Zalo settings from Database. Falling back to AppSettings/Environment.");
            }

            var appSettings = ReadProviderOptions("JsonConfigurationProvider");
            var environment = ReadProviderOptions("EnvironmentVariablesConfigurationProvider");
            _current = Merge(database, appSettings, environment);
            _source = ResolveCredentialSource(database, appSettings, environment);
            LogConfigurationStatus();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        ZaloOptions options,
        bool updateAppSecret,
        bool updateOaSecretKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var next = Clone(options);
            if (!updateAppSecret)
            {
                next.AppSecret = _current.AppSecret;
            }

            if (!updateOaSecretKey)
            {
                next.OaSecretKey = _current.OaSecretKey;
            }

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureTableAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                MERGE [{TableName}] AS target
                USING (SELECT CAST(1 AS INT) AS Id) AS source ON target.Id = source.Id
                WHEN MATCHED THEN UPDATE SET
                    AppId = @AppId, AppSecret = @AppSecret, OaId = @OaId, OaSecretKey = @OaSecretKey,
                    OAuthRedirectUri = @OAuthRedirectUri, ApiBaseUrl = @ApiBaseUrl,
                    OAuthBaseUrl = @OAuthBaseUrl, PublicBaseUrl = @PublicBaseUrl,
                    RefreshBeforeExpiryMinutes = @RefreshBeforeExpiryMinutes,
                    AccessTokenLifetimeHours = @AccessTokenLifetimeHours,
                    TextMessageEndpoint = @TextMessageEndpoint, TokenEndpoint = @TokenEndpoint,
                    OAuthAuthorizePath = @OAuthAuthorizePath,
                    EnableSignatureValidation = @EnableSignatureValidation,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN INSERT (
                    Id, AppId, AppSecret, OaId, OaSecretKey, OAuthRedirectUri,
                    ApiBaseUrl, OAuthBaseUrl, PublicBaseUrl, RefreshBeforeExpiryMinutes,
                    AccessTokenLifetimeHours, TextMessageEndpoint, TokenEndpoint,
                    OAuthAuthorizePath, EnableSignatureValidation, UpdatedAtUtc
                ) VALUES (
                    1, @AppId, @AppSecret, @OaId, @OaSecretKey, @OAuthRedirectUri,
                    @ApiBaseUrl, @OAuthBaseUrl, @PublicBaseUrl, @RefreshBeforeExpiryMinutes,
                    @AccessTokenLifetimeHours, @TextMessageEndpoint, @TokenEndpoint,
                    @OAuthAuthorizePath, @EnableSignatureValidation, SYSUTCDATETIME()
                );
                """;
            AddString(command, "@AppId", next.AppId);
            AddString(command, "@AppSecret", Protect(next.AppSecret));
            AddString(command, "@OaId", next.OaId);
            AddString(command, "@OaSecretKey", Protect(next.OaSecretKey));
            AddString(command, "@OAuthRedirectUri", next.OAuthRedirectUri);
            AddString(command, "@ApiBaseUrl", next.ApiBaseUrl);
            AddString(command, "@OAuthBaseUrl", next.OAuthBaseUrl);
            AddString(command, "@PublicBaseUrl", next.PublicBaseUrl);
            command.Parameters.Add(new SqlParameter("@RefreshBeforeExpiryMinutes", SqlDbType.Int) { Value = next.RefreshBeforeExpiryMinutes });
            command.Parameters.Add(new SqlParameter("@AccessTokenLifetimeHours", SqlDbType.Int) { Value = next.AccessTokenLifetimeHours });
            AddString(command, "@TextMessageEndpoint", next.TextMessageEndpoint);
            AddString(command, "@TokenEndpoint", next.TokenEndpoint);
            AddString(command, "@OAuthAuthorizePath", next.OAuthAuthorizePath);
            command.Parameters.Add(new SqlParameter("@EnableSignatureValidation", SqlDbType.Bit) { Value = next.EnableSignatureValidation });
            await command.ExecuteNonQueryAsync(cancellationToken);
            _current = next;
            _source = "Database";
            LogConfigurationStatus();
        }
        finally
        {
            _gate.Release();
        }
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

    private static async Task EnsureTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID('dbo.{TableName}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{TableName}] (
                    [Id] INT NOT NULL PRIMARY KEY,
                    [AppId] NVARCHAR(200) NULL,
                    [AppSecret] NVARCHAR(MAX) NULL,
                    [OaId] NVARCHAR(200) NULL,
                    [OaSecretKey] NVARCHAR(MAX) NULL,
                    [OAuthRedirectUri] NVARCHAR(1000) NULL,
                    [ApiBaseUrl] NVARCHAR(1000) NOT NULL,
                    [OAuthBaseUrl] NVARCHAR(1000) NOT NULL,
                    [PublicBaseUrl] NVARCHAR(1000) NULL,
                    [RefreshBeforeExpiryMinutes] INT NOT NULL,
                    [AccessTokenLifetimeHours] INT NOT NULL,
                    [TextMessageEndpoint] NVARCHAR(500) NOT NULL,
                    [TokenEndpoint] NVARCHAR(500) NOT NULL,
                    [OAuthAuthorizePath] NVARCHAR(500) NOT NULL,
                    [EnableSignatureValidation] BIT NOT NULL,
                    [UpdatedAtUtc] DATETIME2 NOT NULL
                );
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string? Protect(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : _protector.Protect(value.Trim());

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not decrypt a stored Zalo secret.");
            return null;
        }
    }

    private ZaloOptions ReadProviderOptions(string providerTypeName)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (_configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Where(item =>
                         item.GetType().Name.Contains(providerTypeName, StringComparison.Ordinal)))
            {
                foreach (var property in ZaloPropertyNames)
                {
                    if (provider.TryGet($"Zalo:{property}", out var value))
                    {
                        values[property] = value;
                    }
                }
            }
        }

        return new ZaloOptions
        {
            AppId = GetValue(values, nameof(ZaloOptions.AppId)),
            AppSecret = GetValue(values, nameof(ZaloOptions.AppSecret)),
            OaId = GetValue(values, nameof(ZaloOptions.OaId)),
            OaSecretKey = GetValue(values, nameof(ZaloOptions.OaSecretKey)),
            OAuthRedirectUri = GetValue(values, nameof(ZaloOptions.OAuthRedirectUri)),
            ApiBaseUrl = GetValue(values, nameof(ZaloOptions.ApiBaseUrl)) ?? string.Empty,
            OAuthBaseUrl = GetValue(values, nameof(ZaloOptions.OAuthBaseUrl)) ?? string.Empty,
            PublicBaseUrl = GetValue(values, nameof(ZaloOptions.PublicBaseUrl)),
            WebhookUrl = GetValue(values, nameof(ZaloOptions.WebhookUrl)),
            RefreshBeforeExpiryMinutes = ParseInt(GetValue(values, nameof(ZaloOptions.RefreshBeforeExpiryMinutes))),
            AccessTokenLifetimeHours = ParseInt(GetValue(values, nameof(ZaloOptions.AccessTokenLifetimeHours))),
            TextMessageEndpoint = GetValue(values, nameof(ZaloOptions.TextMessageEndpoint)) ?? string.Empty,
            TokenEndpoint = GetValue(values, nameof(ZaloOptions.TokenEndpoint)) ?? string.Empty,
            OAuthAuthorizePath = GetValue(values, nameof(ZaloOptions.OAuthAuthorizePath)) ?? string.Empty,
            EnableSignatureValidation = ParseBool(GetValue(values, nameof(ZaloOptions.EnableSignatureValidation)))
        };
    }

    private static ZaloOptions Merge(ZaloOptions? database, ZaloOptions appSettings, ZaloOptions environment)
    {
        return new ZaloOptions
        {
            AppId = FirstConfigured(database?.AppId, appSettings.AppId, environment.AppId),
            AppSecret = FirstConfigured(database?.AppSecret, appSettings.AppSecret, environment.AppSecret),
            OaId = FirstConfigured(database?.OaId, appSettings.OaId, environment.OaId),
            OaSecretKey = FirstConfigured(database?.OaSecretKey, appSettings.OaSecretKey, environment.OaSecretKey),
            OAuthRedirectUri = NormalizePublicUrl(FirstConfigured(database?.OAuthRedirectUri, appSettings.OAuthRedirectUri, environment.OAuthRedirectUri)),
            ApiBaseUrl = FirstConfigured(database?.ApiBaseUrl, appSettings.ApiBaseUrl, environment.ApiBaseUrl) ?? "https://openapi.zalo.me",
            OAuthBaseUrl = FirstConfigured(database?.OAuthBaseUrl, appSettings.OAuthBaseUrl, environment.OAuthBaseUrl) ?? "https://oauth.zaloapp.com",
            PublicBaseUrl = NormalizePublicUrl(FirstConfigured(database?.PublicBaseUrl, appSettings.PublicBaseUrl, environment.PublicBaseUrl)),
            WebhookUrl = NormalizePublicUrl(FirstConfigured(database?.WebhookUrl, appSettings.WebhookUrl, environment.WebhookUrl)),
            RefreshBeforeExpiryMinutes = FirstPositive(database?.RefreshBeforeExpiryMinutes, appSettings.RefreshBeforeExpiryMinutes, environment.RefreshBeforeExpiryMinutes, 120),
            AccessTokenLifetimeHours = FirstPositive(database?.AccessTokenLifetimeHours, appSettings.AccessTokenLifetimeHours, environment.AccessTokenLifetimeHours, 25),
            TextMessageEndpoint = FirstConfigured(database?.TextMessageEndpoint, appSettings.TextMessageEndpoint, environment.TextMessageEndpoint) ?? "/v3.0/oa/message/cs",
            TokenEndpoint = FirstConfigured(database?.TokenEndpoint, appSettings.TokenEndpoint, environment.TokenEndpoint) ?? "/v4/oa/access_token",
            OAuthAuthorizePath = FirstConfigured(database?.OAuthAuthorizePath, appSettings.OAuthAuthorizePath, environment.OAuthAuthorizePath) ?? "/v4/oa/permission",
            EnableSignatureValidation = database?.EnableSignatureValidation
                ?? appSettings.EnableSignatureValidation
        };
    }

    private static string ResolveCredentialSource(ZaloOptions? database, ZaloOptions appSettings, ZaloOptions environment)
    {
        var sources = new List<string>();
        AddSource(sources, ResolveValueSource(database?.AppId, appSettings.AppId, environment.AppId));
        AddSource(sources, ResolveValueSource(database?.AppSecret, appSettings.AppSecret, environment.AppSecret));
        return sources.Count == 0 ? "NotConfigured" : string.Join("/", sources);
    }

    private void LogConfigurationStatus()
    {
        _logger.LogInformation(
            "Zalo configuration loaded. AppId configured: {AppIdConfigured}; AppSecret configured: {AppSecretConfigured}; Source: {Source}",
            IsConfigured(_current.AppId),
            IsConfigured(_current.AppSecret),
            _source);
    }

    private static string? ResolveValueSource(string? database, string? appSettings, string? environment)
    {
        if (IsConfigured(database))
        {
            return "Database";
        }

        if (IsConfigured(appSettings))
        {
            return "AppSettings";
        }

        return IsConfigured(environment) ? "Environment" : null;
    }

    private static void AddSource(List<string> sources, string? source)
    {
        if (!string.IsNullOrWhiteSpace(source) && !sources.Contains(source, StringComparer.Ordinal))
        {
            sources.Add(source);
        }
    }

    private static string? FirstConfigured(params string?[] values) =>
        values.FirstOrDefault(IsConfigured)?.Trim();

    private static bool IsConfigured(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Trim().StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizePublicUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Trim().Replace(
            "http://apptech.ddns.net",
            "https://apptech.ddns.net",
            StringComparison.OrdinalIgnoreCase);
    }

    private static int FirstPositive(int? database, int appSettings, int environment, int fallback) =>
        database is > 0 ? database.Value :
        appSettings > 0 ? appSettings :
        environment > 0 ? environment :
        fallback;

    private static int ParseInt(string? value) => int.TryParse(value, out var result) ? result : 0;
    private static bool ParseBool(string? value) => bool.TryParse(value, out var result) && result;
    private static string? GetValue(IReadOnlyDictionary<string, string?> values, string name) =>
        values.TryGetValue(name, out var value) ? value : null;

    private static readonly string[] ZaloPropertyNames =
    [
        nameof(ZaloOptions.AppId), nameof(ZaloOptions.AppSecret), nameof(ZaloOptions.OaId),
        nameof(ZaloOptions.OaSecretKey), nameof(ZaloOptions.OAuthRedirectUri),
        nameof(ZaloOptions.ApiBaseUrl), nameof(ZaloOptions.OAuthBaseUrl),
        nameof(ZaloOptions.PublicBaseUrl), nameof(ZaloOptions.WebhookUrl),
        nameof(ZaloOptions.RefreshBeforeExpiryMinutes), nameof(ZaloOptions.AccessTokenLifetimeHours),
        nameof(ZaloOptions.TextMessageEndpoint), nameof(ZaloOptions.TokenEndpoint),
        nameof(ZaloOptions.OAuthAuthorizePath), nameof(ZaloOptions.EnableSignatureValidation)
    ];

    private static void AddString(SqlCommand command, string name, string? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, -1)
        {
            Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim()
        });

    private static string? GetString(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static ZaloOptions Clone(ZaloOptions source) => new()
    {
        AppId = source.AppId,
        AppSecret = source.AppSecret,
        OaId = source.OaId,
        OaSecretKey = source.OaSecretKey,
        OAuthRedirectUri = source.OAuthRedirectUri,
        ApiBaseUrl = source.ApiBaseUrl,
        OAuthBaseUrl = source.OAuthBaseUrl,
        PublicBaseUrl = source.PublicBaseUrl,
        WebhookUrl = source.WebhookUrl,
        RefreshBeforeExpiryMinutes = source.RefreshBeforeExpiryMinutes,
        AccessTokenLifetimeHours = source.AccessTokenLifetimeHours,
        TextMessageEndpoint = source.TextMessageEndpoint,
        TokenEndpoint = source.TokenEndpoint,
        OAuthAuthorizePath = source.OAuthAuthorizePath,
        EnableSignatureValidation = source.EnableSignatureValidation
    };
}
