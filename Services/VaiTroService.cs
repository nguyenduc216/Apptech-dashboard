using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IVaiTroService
{
    Task<(IReadOnlyList<VaiTroListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(string? keyword, bool? statusFilter, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<VaiTroListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(VaiTroFormModel model, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(VaiTroFormModel model, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<VaiTroPermissionMatrix?> GetPermissionMatrixAsync(int roleId, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> SavePermissionsAsync(int roleId, IReadOnlyCollection<int> selectedPermissionIds, CancellationToken cancellationToken = default);
}

public sealed class VaiTroService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<VaiTroService> logger) : IVaiTroService
{
    private const string RoleTableName = "TblVaiTro";
    private const string FunctionTableName = "TblChucNang";
    private const string PermissionTableName = "TblQuyen";
    private const string RolePermissionTableName = "TblVaiTroVaQuyen";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<VaiTroService> _logger = logger;

    public async Task<(IReadOnlyList<VaiTroListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
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
            var whereClause = BuildWhereClause(normalizedKeyword, statusFilter);

            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"""
                SELECT COUNT(1)
                FROM [{RoleTableName}]
                WHERE {whereClause}
                """;
            AddFilterParameters(countCommand, normalizedKeyword, statusFilter);

            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            var currentPage = totalPages == 0 ? 1 : Math.Min(page, totalPages);
            var offset = (currentPage - 1) * pageSize;

            await using var listCommand = connection.CreateCommand();
            listCommand.CommandText = $"""
                SELECT ID, TenVaiTro, MieuTa, CAST(ISNULL(TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung
                FROM [{RoleTableName}]
                WHERE {whereClause}
                ORDER BY ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword, statusFilter);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<VaiTroListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapRole(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblVaiTro list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<VaiTroListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1) ID, TenVaiTro, MieuTa, CAST(ISNULL(TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung
                FROM [{RoleTableName}]
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapRole(reader) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblVaiTro item {Id}.", id);
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(VaiTroFormModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO [{RoleTableName}] (TenVaiTro, MieuTa, TrangThaiSuDung)
                VALUES (@TenVaiTro, @MieuTa, @TrangThaiSuDung);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;
            FillRoleParameters(command, model);

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            return newId > 0 ? (true, null, newId) : (false, "Không thể thêm mới vai trò.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblVaiTro.");
            return (false, "Không thể thêm mới vai trò lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(VaiTroFormModel model, CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được vai trò cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE [{RoleTableName}]
                SET TenVaiTro = @TenVaiTro,
                    MieuTa = @MieuTa,
                    TrangThaiSuDung = @TrangThaiSuDung
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillRoleParameters(command, model);

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0 ? (true, null) : (false, "Không tìm thấy vai trò để cập nhật.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblVaiTro {Id}.", model.Id);
            return (false, "Không thể cập nhật vai trò lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được vai trò cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var deleteMapCommand = connection.CreateCommand())
            {
                deleteMapCommand.Transaction = (SqlTransaction)transaction;
                deleteMapCommand.CommandText = $"DELETE FROM [{RolePermissionTableName}] WHERE IDVaiTro = @IDVaiTro";
                deleteMapCommand.Parameters.Add(new SqlParameter("@IDVaiTro", SqlDbType.Int) { Value = id });
                await deleteMapCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = (SqlTransaction)transaction;
            command.CommandText = $"DELETE FROM [{RoleTableName}] WHERE ID = @Id";
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return affectedRows > 0 ? (true, null) : (false, "Không tìm thấy vai trò để xóa.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblVaiTro {Id}.", id);
            return (false, "Không thể xóa vai trò lúc này.");
        }
    }

    public async Task<VaiTroPermissionMatrix?> GetPermissionMatrixAsync(int roleId, CancellationToken cancellationToken = default)
    {
        var role = await GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var selectedIds = await LoadSelectedPermissionIdsAsync(connection, roleId, cancellationToken);
            var flatNodes = await LoadPermissionNodesAsync(connection, selectedIds, cancellationToken);
            return new VaiTroPermissionMatrix
            {
                RoleId = role.Id,
                RoleName = role.TenVaiTro,
                Nodes = BuildTree(flatNodes)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load permission matrix for role {RoleId}.", roleId);
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> SavePermissionsAsync(
        int roleId,
        IReadOnlyCollection<int> selectedPermissionIds,
        CancellationToken cancellationToken = default)
    {
        if (roleId <= 0)
        {
            return (false, "Không xác định được vai trò cần phân quyền.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var currentIds = await LoadSelectedPermissionIdsAsync(connection, roleId, cancellationToken, (SqlTransaction)transaction);
            var selected = selectedPermissionIds.Where(id => id > 0).Distinct().ToHashSet();
            var toInsert = selected.Except(currentIds).ToArray();
            var toDelete = currentIds.Except(selected).ToArray();

            foreach (var permissionId in toInsert)
            {
                await using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = (SqlTransaction)transaction;
                insertCommand.CommandText = $"""
                    INSERT INTO [{RolePermissionTableName}] (IDVaiTro, IDQuyen)
                    VALUES (@IDVaiTro, @IDQuyen)
                    """;
                insertCommand.Parameters.Add(new SqlParameter("@IDVaiTro", SqlDbType.Int) { Value = roleId });
                insertCommand.Parameters.Add(new SqlParameter("@IDQuyen", SqlDbType.Int) { Value = permissionId });
                await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var permissionId in toDelete)
            {
                await using var deleteCommand = connection.CreateCommand();
                deleteCommand.Transaction = (SqlTransaction)transaction;
                deleteCommand.CommandText = $"""
                    DELETE FROM [{RolePermissionTableName}]
                    WHERE IDVaiTro = @IDVaiTro AND IDQuyen = @IDQuyen
                    """;
                deleteCommand.Parameters.Add(new SqlParameter("@IDVaiTro", SqlDbType.Int) { Value = roleId });
                deleteCommand.Parameters.Add(new SqlParameter("@IDQuyen", SqlDbType.Int) { Value = permissionId });
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save permissions for role {RoleId}.", roleId);
            return (false, "Không thể lưu phân quyền vai trò lúc này.");
        }
    }

    private async Task<HashSet<int>> LoadSelectedPermissionIdsAsync(
        SqlConnection connection,
        int roleId,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT IDQuyen FROM [{RolePermissionTableName}] WHERE IDVaiTro = @IDVaiTro";
        command.Parameters.Add(new SqlParameter("@IDVaiTro", SqlDbType.Int) { Value = roleId });

        var ids = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(Convert.ToInt32(reader.GetValue(0)));
        }

        return ids;
    }

    private async Task<List<VaiTroPermissionNode>> LoadPermissionNodesAsync(
        SqlConnection connection,
        HashSet<int> selectedIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                cn.ID AS ChucNangId,
                cn.MaChucNang,
                cn.MaChucNangCha,
                cn.TenChucNang,
                cn.MieuTa AS ChucNangMieuTa,
                cn.ThuTuHienThi,
                q.ID AS QuyenId,
                q.TenQuyen,
                q.MieuTa AS QuyenMieuTa
            FROM [{FunctionTableName}] AS cn
            INNER JOIN [{PermissionTableName}] AS q ON q.IDChucNang = cn.ID
            WHERE ISNULL(cn.TrangThaiSuDung, 0) = 1
            ORDER BY
                CASE WHEN cn.MaChucNangCha IS NULL OR LTRIM(RTRIM(cn.MaChucNangCha)) = '' THEN 0 ELSE 1 END,
                TRY_CONVERT(decimal(10,2), cn.ThuTuHienThi),
                cn.ThuTuHienThi,
                cn.ID,
                q.ID
            """;

        var nodesByCode = new Dictionary<string, VaiTroPermissionNode>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var code = GetNullableString(reader, "MaChucNang") ?? $"ID:{reader.GetInt32(reader.GetOrdinal("ChucNangId"))}";
            if (!nodesByCode.TryGetValue(code, out var node))
            {
                node = new VaiTroPermissionNode
                {
                    ChucNangId = reader.GetInt32(reader.GetOrdinal("ChucNangId")),
                    MaChucNang = code,
                    MaChucNangCha = GetNullableString(reader, "MaChucNangCha"),
                    TenChucNang = GetNullableString(reader, "TenChucNang") ?? "Chức năng",
                    MieuTa = GetNullableString(reader, "ChucNangMieuTa"),
                    ThuTuHienThi = GetNullableString(reader, "ThuTuHienThi")
                };
                nodesByCode.Add(code, node);
            }

            var permissionId = GetNullableInt32(reader, "QuyenId");
            if (permissionId.HasValue)
            {
                node.Permissions.Add(new VaiTroPermissionItem
                {
                    QuyenId = permissionId.Value,
                    ChucNangId = node.ChucNangId,
                    TenQuyen = GetNullableString(reader, "TenQuyen") ?? "Quyền",
                    MieuTa = GetNullableString(reader, "QuyenMieuTa"),
                    IsSelected = selectedIds.Contains(permissionId.Value)
                });
            }
        }

        return nodesByCode.Values.ToList();
    }

    private static IReadOnlyList<VaiTroPermissionNode> BuildTree(IEnumerable<VaiTroPermissionNode> flatNodes)
    {
        var ordered = flatNodes
            .OrderBy(node => GetSortValue(node.ThuTuHienThi))
            .ThenBy(node => node.ChucNangId)
            .ToList();
        var byCode = ordered.ToDictionary(node => node.MaChucNang, StringComparer.OrdinalIgnoreCase);
        var roots = new List<VaiTroPermissionNode>();

        foreach (var node in ordered)
        {
            if (!string.IsNullOrWhiteSpace(node.MaChucNangCha) &&
                byCode.TryGetValue(node.MaChucNangCha, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return roots;
    }

    private static decimal GetSortValue(string? sortOrder)
    {
        return decimal.TryParse(sortOrder, out var value) ? value : decimal.MaxValue;
    }

    private static VaiTroListItem MapRole(SqlDataReader reader)
    {
        return new VaiTroListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TenVaiTro = GetNullableString(reader, "TenVaiTro") ?? "",
            MieuTa = GetNullableString(reader, "MieuTa"),
            TrangThaiSuDung = GetNullableBoolean(reader, "TrangThaiSuDung") ?? true
        };
    }

    private static string BuildWhereClause(string? keyword, bool? statusFilter)
    {
        var filters = new List<string> { "1 = 1" };
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"""
                (
                    TenVaiTro COLLATE {SearchCollation} LIKE @Keyword
                    OR MieuTa COLLATE {SearchCollation} LIKE @Keyword
                )
                """);
        }

        if (statusFilter.HasValue)
        {
            filters.Add("ISNULL(TrangThaiSuDung, 0) = @StatusFilter");
        }

        return string.Join(" AND ", filters);
    }

    private static void AddFilterParameters(SqlCommand command, string? keyword, bool? statusFilter)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250) { Value = $"%{keyword}%" });
        }

        if (statusFilter.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@StatusFilter", SqlDbType.Bit) { Value = statusFilter.Value });
        }
    }

    private static void FillRoleParameters(SqlCommand command, VaiTroFormModel model)
    {
        command.Parameters.Add(new SqlParameter("@TenVaiTro", SqlDbType.NVarChar, 200) { Value = model.TenVaiTro.Trim() });
        command.Parameters.Add(new SqlParameter("@MieuTa", SqlDbType.NVarChar, 500) { Value = ToDbValue(model.MieuTa) });
        command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit) { Value = model.TrangThaiSuDung });
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

    private static string? NormalizeKeyword(string? keyword) => string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
    private static object ToDbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
    }

    private static int? GetNullableInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static bool? GetNullableBoolean(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            bool flag => flag,
            byte number => number != 0,
            short number => number != 0,
            int number => number != 0,
            long number => number != 0,
            string text => bool.TryParse(text, out var parsedBool)
                ? parsedBool
                : int.TryParse(text, out var parsedInt) && parsedInt != 0,
            _ => null
        };
    }
}
