using System.Data;
using System.Text;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IHangHoaService
{
    Task<(IReadOnlyList<HangHoaListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        bool? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<HangHoaListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HangHoaLookupOption>> GetDonViTinhOptionsAsync(CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        HangHoaFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        HangHoaFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<HangHoaImportResult> ImportAsync(
        IReadOnlyList<HangHoaImportRow> rows,
        string currentUser,
        CancellationToken cancellationToken = default);
}

public sealed class HangHoaService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<HangHoaService> logger) : IHangHoaService
{
    private const string TableName = "TblHangHoa";
    private const string DonViTinhTableName = "TblDonViTinh";
    private const string DefaultDonViTinhType = "DV";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<HangHoaService> _logger = logger;

    public async Task<(IReadOnlyList<HangHoaListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        bool? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 5, 100);
        page = Math.Max(page, 1);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var normalizedKeyword = NormalizeKeyword(keyword);
            var whereClause = BuildWhereClause(normalizedKeyword, statusFilter, "hh");
            var donViTinhColumnName = await ResolveDonViTinhColumnNameAsync(connection, null, cancellationToken);
            var donViTinhSelect = donViTinhColumnName is null ? "CAST(NULL AS int) AS IDDonViTinh," : $"hh.[{donViTinhColumnName}] AS IDDonViTinh,";
            var tenDonViSelect = donViTinhColumnName is null ? "CAST(NULL AS nvarchar(300)) AS TenDonVi," : "dvt.TenDonVi,";
            var tenVietTatSelect = donViTinhColumnName is null ? "CAST(NULL AS nvarchar(40)) AS TenVietTat," : "dvt.TenVietTat,";
            var donViTinhJoin = donViTinhColumnName is null
                ? string.Empty
                : $"LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = hh.[{donViTinhColumnName}]";

            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"""
                SELECT COUNT(1)
                FROM [{TableName}] hh
                WHERE {whereClause}
                """;
            AddFilterParameters(countCommand, normalizedKeyword, statusFilter);

            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            var currentPage = totalPages == 0 ? 1 : Math.Min(page, totalPages);
            var offset = (currentPage - 1) * pageSize;

            await using var listCommand = connection.CreateCommand();
            listCommand.CommandText = $"""
                SELECT
                    hh.ID,
                    hh.TenHangHoa,
                    hh.MaHangHoa,
                    {donViTinhSelect}
                    {tenDonViSelect}
                    {tenVietTatSelect}
                    hh.Image,
                    CAST(ISNULL(hh.TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    hh.Created_Date,
                    hh.Created_By,
                    hh.Updated_Date,
                    hh.Updated_By
                FROM [{TableName}] hh
                {donViTinhJoin}
                WHERE {whereClause}
                ORDER BY hh.ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword, statusFilter);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<HangHoaListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblHangHoa list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<HangHoaListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var donViTinhColumnName = await ResolveDonViTinhColumnNameAsync(connection, null, cancellationToken);
            var donViTinhSelect = donViTinhColumnName is null ? "CAST(NULL AS int) AS IDDonViTinh," : $"hh.[{donViTinhColumnName}] AS IDDonViTinh,";
            var tenDonViSelect = donViTinhColumnName is null ? "CAST(NULL AS nvarchar(300)) AS TenDonVi," : "dvt.TenDonVi,";
            var tenVietTatSelect = donViTinhColumnName is null ? "CAST(NULL AS nvarchar(40)) AS TenVietTat," : "dvt.TenVietTat,";
            var donViTinhJoin = donViTinhColumnName is null
                ? string.Empty
                : $"LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = hh.[{donViTinhColumnName}]";
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1)
                    hh.ID,
                    hh.TenHangHoa,
                    hh.MaHangHoa,
                    {donViTinhSelect}
                    {tenDonViSelect}
                    {tenVietTatSelect}
                    hh.Image,
                    CAST(ISNULL(hh.TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    hh.Created_Date,
                    hh.Created_By,
                    hh.Updated_Date,
                    hh.Updated_By
                FROM [{TableName}] hh
                {donViTinhJoin}
                WHERE hh.ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapItem(reader) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblHangHoa item {Id}.", id);
            return null;
        }
    }

    public async Task<IReadOnlyList<HangHoaLookupOption>> GetDonViTinhOptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    ID,
                    TenDonVi,
                    TenVietTat
                FROM [TblDonViTinh]
                WHERE ISNULL(TrangThaiSuDung, 1) = 1
                ORDER BY TenDonVi ASC, ID ASC
                """;

            var items = new List<HangHoaLookupOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tenDonVi = GetNullableString(reader, "TenDonVi") ?? $"#{reader.GetInt32(reader.GetOrdinal("ID"))}";
                var tenVietTat = GetNullableString(reader, "TenVietTat");
                items.Add(new HangHoaLookupOption
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    Label = string.IsNullOrWhiteSpace(tenVietTat) ? tenDonVi : $"{tenDonVi} ({tenVietTat})"
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load HangHoa don vi tinh lookup.");
            return [];
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        HangHoaFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var duplicateError = await ValidateDuplicateNameAsync(connection, model.TenHangHoa, null, cancellationToken);
            if (duplicateError is not null)
            {
                return (false, duplicateError, null);
            }

            var donViTinhColumnName = await ResolveDonViTinhColumnNameAsync(connection, null, cancellationToken);
            var donViTinhColumn = donViTinhColumnName is null ? string.Empty : $"[{donViTinhColumnName}],";
            var donViTinhValue = donViTinhColumnName is null ? string.Empty : "@IDDonViTinh,";

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    TenHangHoa,
                    MaHangHoa,
                    {donViTinhColumn}
                    Image,
                    TrangThaiSuDung,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_By
                )
                VALUES (
                    @TenHangHoa,
                    @MaHangHoa,
                    {donViTinhValue}
                    @Image,
                    @TrangThaiSuDung,
                    GETDATE(),
                    @CreatedBy,
                    GETDATE(),
                    @UpdatedBy
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            FillSaveParameters(command, model, donViTinhColumnName is not null);
            command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            return newId > 0
                ? (true, null, newId)
                : (false, "Không thể thêm mới hàng hóa.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblHangHoa.");
            return (false, "Không thể thêm mới hàng hóa lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        HangHoaFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được hàng hóa cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var duplicateError = await ValidateDuplicateNameAsync(connection, model.TenHangHoa, model.Id, cancellationToken);
            if (duplicateError is not null)
            {
                return (false, duplicateError);
            }

            var donViTinhColumnName = await ResolveDonViTinhColumnNameAsync(connection, null, cancellationToken);
            var donViTinhSet = donViTinhColumnName is null ? string.Empty : $"[{donViTinhColumnName}] = @IDDonViTinh,";

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE [{TableName}]
                SET
                    TenHangHoa = @TenHangHoa,
                    MaHangHoa = @MaHangHoa,
                    {donViTinhSet}
                    Image = @Image,
                    TrangThaiSuDung = @TrangThaiSuDung,
                    Updated_Date = GETDATE(),
                    Updated_By = @UpdatedBy
                WHERE ID = @Id
                """;

            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillSaveParameters(command, model, donViTinhColumnName is not null);
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0
                ? (true, null)
                : (false, "Không tìm thấy hàng hóa để cập nhật.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblHangHoa {Id}.", model.Id);
            return (false, "Không thể cập nhật hàng hóa lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được hàng hóa cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DELETE FROM [{TableName}]
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0
                ? (true, null)
                : (false, "Không tìm thấy hàng hóa để xóa.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblHangHoa {Id}.", id);
            return (false, "Không thể xóa hàng hóa lúc này.");
        }
    }

    public async Task<HangHoaImportResult> ImportAsync(
        IReadOnlyList<HangHoaImportRow> rows,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return new HangHoaImportResult();
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var donViTinhColumnName = await ResolveDonViTinhColumnNameAsync(connection, transaction, cancellationToken);
            var currentAuditUser = TrimToLength(currentUser, 50);
            var donViTinhLookup = donViTinhColumnName is null
                ? null
                : await LoadDonViTinhLookupAsync(connection, transaction, cancellationToken);
            var (existingByCode, existingByName) = await LoadHangHoaImportLookupAsync(connection, transaction, cancellationToken);
            var failedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var result = new HangHoaImportResult();
            foreach (var row in rows)
            {
                var tenHangHoa = NormalizeComparisonKey(row.TenHangHoa);
                if (tenHangHoa is null)
                {
                    result.SkippedCount++;
                    continue;
                }

                var maHangHoa = NormalizeComparisonKey(row.MaHangHoa);
                var donViTinhId = donViTinhColumnName is null || donViTinhLookup is null
                    ? null
                    : await ResolveOrCreateDonViTinhIdAsync(connection, transaction, donViTinhLookup, row.DonViTinh, currentUser, cancellationToken);
                var (matchedItem, hasCodeConflict, shouldSkip) = FindExistingImportItem(existingByCode, existingByName, maHangHoa, tenHangHoa);

                if (hasCodeConflict)
                {
                    if (maHangHoa is not null)
                    {
                        failedCodes.Add(maHangHoa);
                    }

                    result.SkippedCount++;
                    continue;
                }

                if (shouldSkip)
                {
                    result.SkippedCount++;
                    continue;
                }

                if (matchedItem is not null)
                {
                    var previousNameKey = matchedItem.NameKey;
                    var previousCodeKey = matchedItem.CodeKey;
                    var updateCode = maHangHoa is not null &&
                        (matchedItem.CodeKey is null ||
                         string.Equals(matchedItem.CodeKey, maHangHoa, StringComparison.OrdinalIgnoreCase));

                    await UpdateImportedHangHoaAsync(
                        connection,
                        transaction,
                        donViTinhColumnName,
                        matchedItem.Id,
                        tenHangHoa,
                        maHangHoa,
                        updateCode,
                        donViTinhId,
                        currentAuditUser,
                        cancellationToken);

                    matchedItem.NameKey = tenHangHoa;
                    if (updateCode)
                    {
                        matchedItem.CodeKey = maHangHoa;
                    }

                    RefreshHangHoaImportLookup(existingByCode, previousCodeKey, matchedItem.CodeKey, matchedItem);
                    RefreshHangHoaImportLookup(existingByName, previousNameKey, matchedItem.NameKey, matchedItem);
                    result.ImportedCount++;
                    continue;
                }

                var newId = await InsertImportedHangHoaAsync(
                    connection,
                    transaction,
                    donViTinhColumnName,
                    tenHangHoa,
                    maHangHoa,
                    donViTinhId,
                    currentAuditUser,
                    cancellationToken);

                var newItem = new HangHoaImportExistingItem
                {
                    Id = newId,
                    NameKey = tenHangHoa,
                    CodeKey = maHangHoa
                };

                AddImportLookupItem(existingByCode, newItem.CodeKey, newItem);
                AddImportLookupItem(existingByName, newItem.NameKey, newItem);
                result.ImportedCount++;
            }

            result.FailedCodes = failedCodes.OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToList();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import TblHangHoa from Excel.");
            return new HangHoaImportResult();
        }
    }

    private async Task<string?> ValidateDuplicateNameAsync(
        SqlConnection connection,
        string tenHangHoa,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) ID
            FROM [{TableName}]
            WHERE UPPER(LTRIM(RTRIM(TenHangHoa))) = UPPER(@TenHangHoa)
            {(excludeId.HasValue ? "AND ID <> @ExcludeId" : string.Empty)}
            """;
        command.Parameters.Add(new SqlParameter("@TenHangHoa", SqlDbType.NVarChar, 250) { Value = tenHangHoa.Trim() });

        if (excludeId.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@ExcludeId", SqlDbType.Int) { Value = excludeId.Value });
        }

        var existingId = await command.ExecuteScalarAsync(cancellationToken);
        return existingId is null ? null : "Tên hàng hóa đã tồn tại.";
    }

    private static async Task<(Dictionary<string, List<HangHoaImportExistingItem>> ByCode, Dictionary<string, List<HangHoaImportExistingItem>> ByName)> LoadHangHoaImportLookupAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ID,
                TenHangHoa,
                MaHangHoa
            FROM [{TableName}]
            WHERE
                (TenHangHoa IS NOT NULL AND LTRIM(RTRIM(TenHangHoa)) <> '') OR
                (MaHangHoa IS NOT NULL AND LTRIM(RTRIM(MaHangHoa)) <> '')
            ORDER BY ID ASC
            """;

        var byCode = new Dictionary<string, List<HangHoaImportExistingItem>>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, List<HangHoaImportExistingItem>>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = new HangHoaImportExistingItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("ID")),
                NameKey = NormalizeComparisonKey(GetNullableString(reader, "TenHangHoa")),
                CodeKey = NormalizeComparisonKey(GetNullableString(reader, "MaHangHoa"))
            };

            AddImportLookupItem(byCode, item.CodeKey, item);
            AddImportLookupItem(byName, item.NameKey, item);
        }

        return (byCode, byName);
    }

    private static (HangHoaImportExistingItem? Item, bool HasCodeConflict, bool ShouldSkip) FindExistingImportItem(
        IReadOnlyDictionary<string, List<HangHoaImportExistingItem>> existingByCode,
        IReadOnlyDictionary<string, List<HangHoaImportExistingItem>> existingByName,
        string? maHangHoa,
        string tenHangHoa)
    {
        if (maHangHoa is not null &&
            existingByCode.TryGetValue(maHangHoa, out var codeMatches) &&
            codeMatches.Count > 0)
        {
            var codeNameMatches = codeMatches
                .Where(item => string.Equals(item.NameKey, tenHangHoa, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return codeNameMatches.Count == 1
                ? (codeNameMatches[0], false, false)
                : (null, true, true);
        }

        if (existingByName.TryGetValue(tenHangHoa, out var nameMatches) &&
            nameMatches.Count > 0)
        {
            if (nameMatches.Count == 1)
            {
                return (nameMatches[0], false, false);
            }

            return (null, false, true);
        }

        return (null, false, false);
    }

    private static async Task<int> InsertImportedHangHoaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string? donViTinhColumnName,
        string tenHangHoa,
        string? maHangHoa,
        int? donViTinhId,
        string currentAuditUser,
        CancellationToken cancellationToken)
    {
        var donViTinhColumn = donViTinhColumnName is null ? string.Empty : $"[{donViTinhColumnName}],";
        var donViTinhValue = donViTinhColumnName is null ? string.Empty : "@IDDonViTinh,";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO [{TableName}] (
                TenHangHoa,
                MaHangHoa,
                {donViTinhColumn}
                Image,
                TrangThaiSuDung,
                Created_Date,
                Created_By,
                Updated_Date,
                Updated_By
            )
            VALUES (
                @TenHangHoa,
                @MaHangHoa,
                {donViTinhValue}
                NULL,
                1,
                GETDATE(),
                @CreatedBy,
                GETDATE(),
                @UpdatedBy
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        command.Parameters.Add(new SqlParameter("@TenHangHoa", SqlDbType.NVarChar, 250) { Value = tenHangHoa });
        command.Parameters.Add(new SqlParameter("@MaHangHoa", SqlDbType.NVarChar, 50) { Value = ToDbValue(maHangHoa) });
        if (donViTinhColumnName is not null)
        {
            command.Parameters.Add(new SqlParameter("@IDDonViTinh", SqlDbType.Int)
            {
                Value = donViTinhId.HasValue && donViTinhId.Value > 0 ? donViTinhId.Value : DBNull.Value
            });
        }
        command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = currentAuditUser });
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = currentAuditUser });

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task UpdateImportedHangHoaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string? donViTinhColumnName,
        int id,
        string tenHangHoa,
        string? maHangHoa,
        bool updateCode,
        int? donViTinhId,
        string currentAuditUser,
        CancellationToken cancellationToken)
    {
        var maHangHoaSet = updateCode ? "MaHangHoa = @MaHangHoa," : string.Empty;
        var donViTinhSet = donViTinhColumnName is null ? string.Empty : $"[{donViTinhColumnName}] = @IDDonViTinh,";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE [{TableName}]
            SET
                TenHangHoa = @TenHangHoa,
                {maHangHoaSet}
                {donViTinhSet}
                Updated_Date = GETDATE(),
                Updated_By = @UpdatedBy
            WHERE ID = @Id
            """;

        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        command.Parameters.Add(new SqlParameter("@TenHangHoa", SqlDbType.NVarChar, 250) { Value = tenHangHoa });
        if (updateCode)
        {
            command.Parameters.Add(new SqlParameter("@MaHangHoa", SqlDbType.NVarChar, 50) { Value = ToDbValue(maHangHoa) });
        }
        if (donViTinhColumnName is not null)
        {
            command.Parameters.Add(new SqlParameter("@IDDonViTinh", SqlDbType.Int)
            {
                Value = donViTinhId.HasValue && donViTinhId.Value > 0 ? donViTinhId.Value : DBNull.Value
            });
        }
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = currentAuditUser });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, int>> LoadDonViTinhLookupAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ID,
                TenDonVi,
                TenVietTat
            FROM [{DonViTinhTableName}]
            WHERE
                (TenDonVi IS NOT NULL AND LTRIM(RTRIM(TenDonVi)) <> '') OR
                (TenVietTat IS NOT NULL AND LTRIM(RTRIM(TenVietTat)) <> '')
            ORDER BY ID ASC
            """;

        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(reader.GetOrdinal("ID"));
            AddLookupKey(lookup, GetNullableString(reader, "TenDonVi"), id);
            AddLookupKey(lookup, GetNullableString(reader, "TenVietTat"), id);
        }

        return lookup;
    }

    private static async Task<int?> ResolveOrCreateDonViTinhIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IDictionary<string, int> donViTinhLookup,
        string? donViTinh,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var normalizedDonViTinh = NormalizeComparisonKey(donViTinh);
        if (normalizedDonViTinh is null)
        {
            return null;
        }

        if (donViTinhLookup.TryGetValue(normalizedDonViTinh, out var existingId))
        {
            return existingId;
        }

        var newId = await InsertDonViTinhAsync(connection, transaction, normalizedDonViTinh, currentUser, cancellationToken);
        donViTinhLookup[normalizedDonViTinh] = newId;
        return newId;
    }

    private static async Task<int> InsertDonViTinhAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tenDonVi,
        string currentUser,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO [{DonViTinhTableName}] (
                TenDonVi,
                TenVietTat,
                TrangThaiSuDung,
                NguoiTao,
                NgayTao,
                NguoiCapNhap,
                NgayCapNhat,
                [Type]
            )
            VALUES (
                @TenDonVi,
                NULL,
                1,
                @NguoiTao,
                GETDATE(),
                @NguoiCapNhap,
                GETDATE(),
                @Type
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        command.Parameters.Add(new SqlParameter("@TenDonVi", SqlDbType.NVarChar, 300) { Value = tenDonVi });
        command.Parameters.Add(new SqlParameter("@NguoiTao", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
        command.Parameters.Add(new SqlParameter("@NguoiCapNhap", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
        command.Parameters.Add(new SqlParameter("@Type", SqlDbType.NVarChar, 100) { Value = DefaultDonViTinhType });

        var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        if (newId <= 0)
        {
            throw new InvalidOperationException("Không thể tạo mới đơn vị tính khi import hàng hóa.");
        }

        return newId;
    }

    private static HangHoaListItem MapItem(SqlDataReader reader)
    {
        return new HangHoaListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TenHangHoa = GetNullableString(reader, "TenHangHoa") ?? string.Empty,
            MaHangHoa = GetNullableString(reader, "MaHangHoa"),
            DonViTinhId = GetNullableInt32(reader, "IDDonViTinh"),
            TenDonViTinh = GetNullableString(reader, "TenDonVi"),
            TenVietTatDonViTinh = GetNullableString(reader, "TenVietTat"),
            ImageUrl = GetNullableString(reader, "Image"),
            TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung")),
            CreatedDate = GetNullableDateTime(reader, "Created_Date"),
            CreatedBy = GetNullableString(reader, "Created_By"),
            UpdatedDate = GetNullableDateTime(reader, "Updated_Date"),
            UpdatedBy = GetNullableString(reader, "Updated_By")
        };
    }

    private static string BuildWhereClause(string? keyword, bool? statusFilter, string? tableAlias = null)
    {
        var prefix = string.IsNullOrWhiteSpace(tableAlias) ? string.Empty : $"{tableAlias}.";
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"""
                (
                    {prefix}TenHangHoa COLLATE {SearchCollation} LIKE @Keyword OR
                    {prefix}MaHangHoa COLLATE {SearchCollation} LIKE @Keyword
                )
                """);
        }

        if (statusFilter.HasValue)
        {
            filters.Add($"{prefix}TrangThaiSuDung = @TrangThaiSuDung");
        }

        return string.Join(" AND ", filters);
    }

    private static void AddFilterParameters(SqlCommand command, string? keyword, bool? statusFilter)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250)
            {
                Value = $"%{keyword}%"
            });
        }

        if (statusFilter.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit)
            {
                Value = statusFilter.Value
            });
        }
    }

    private static void FillSaveParameters(SqlCommand command, HangHoaFormModel model, bool includeDonViTinh)
    {
        command.Parameters.Add(new SqlParameter("@TenHangHoa", SqlDbType.NVarChar, 250)
        {
            Value = model.TenHangHoa.Trim()
        });
        command.Parameters.Add(new SqlParameter("@MaHangHoa", SqlDbType.NVarChar, 50)
        {
            Value = ToDbValue(model.MaHangHoa)
        });
        if (includeDonViTinh)
        {
            command.Parameters.Add(new SqlParameter("@IDDonViTinh", SqlDbType.Int)
            {
                Value = model.DonViTinhId.HasValue && model.DonViTinhId.Value > 0 ? model.DonViTinhId.Value : DBNull.Value
            });
        }
        command.Parameters.Add(new SqlParameter("@Image", SqlDbType.NVarChar, 550)
        {
            Value = ToDbValue(model.ImageUrl)
        });
        command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit)
        {
            Value = model.TrangThaiSuDung
        });
    }

    private static int? GetNullableInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static async Task<string?> ResolveDonViTinhColumnNameAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (await HasColumnAsync(connection, transaction, TableName, "IDDonViTinh", cancellationToken))
        {
            return "IDDonViTinh";
        }

        if (await HasColumnAsync(connection, transaction, TableName, "IDDonVinTinh", cancellationToken))
        {
            return "IDDonVinTinh";
        }

        return null;
    }

    private static async Task<bool> HasColumnAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE
                WHEN COL_LENGTH(@TableName, @ColumnName) IS NULL THEN 0
                ELSE 1
            END
            """;
        command.Parameters.Add(new SqlParameter("@TableName", SqlDbType.NVarChar, 128) { Value = $"dbo.{tableName}" });
        command.Parameters.Add(new SqlParameter("@ColumnName", SqlDbType.NVarChar, 128) { Value = columnName });
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
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

    private static string? NormalizeKeyword(string? keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
    }

    private static string? NormalizeComparisonKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var parts = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : string.Join(' ', parts);
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static string TrimToLength(string value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "system" : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
    }

    private static void AddLookupKey(ISet<string> values, string? value)
    {
        var normalizedValue = NormalizeComparisonKey(value);
        if (normalizedValue is not null)
        {
            values.Add(normalizedValue);
        }
    }

    private static void AddLookupKey(IDictionary<string, int> values, string? value, int id)
    {
        var normalizedValue = NormalizeComparisonKey(value);
        if (normalizedValue is not null && !values.ContainsKey(normalizedValue))
        {
            values[normalizedValue] = id;
        }
    }

    private static void AddImportLookupItem(
        IDictionary<string, List<HangHoaImportExistingItem>> lookup,
        string? key,
        HangHoaImportExistingItem item)
    {
        if (key is null)
        {
            return;
        }

        if (!lookup.TryGetValue(key, out var items))
        {
            items = [];
            lookup[key] = items;
        }

        items.Add(item);
    }

    private static void RefreshHangHoaImportLookup(
        IDictionary<string, List<HangHoaImportExistingItem>> lookup,
        string? previousKey,
        string? nextKey,
        HangHoaImportExistingItem item)
    {
        if (previousKey is not null &&
            lookup.TryGetValue(previousKey, out var previousItems))
        {
            previousItems.Remove(item);
            if (previousItems.Count == 0)
            {
                lookup.Remove(previousKey);
            }
        }

        AddImportLookupItem(lookup, nextKey, item);
    }

    private sealed class HangHoaImportExistingItem
    {
        public int Id { get; set; }
        public string? NameKey { get; set; }
        public string? CodeKey { get; set; }
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            DateTime typedDate => typedDate,
            string typedString when DateTime.TryParse(typedString, out var parsedDate) => parsedDate,
            _ => null
        };
    }
}
