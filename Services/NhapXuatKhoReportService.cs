using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface INhapXuatKhoReportService
{
    Task<NhapXuatKhoReportViewModel> GetReportAsync(
        string? loai,
        DateTime? fromDate,
        DateTime? toDate,
        string? vatTu,
        string? hangHoa,
        int? khoId,
        string? maPhieu,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NhapXuatKhoLookupOption>> GetKhoOptionsAsync(CancellationToken cancellationToken = default);
}

public sealed class NhapXuatKhoReportService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<NhapXuatKhoReportService> logger) : INhapXuatKhoReportService
{
    private const string NhapHeaderTableName = "TblPhieuNhapKho";
    private const string NhapDetailTableName = "TblPhieuNhapKhoChiTiet";
    private const string XuatHeaderTableName = "TblPhieuXuatKho";
    private const string XuatDetailTableName = "TblPhieuXuatKhoChiTiet";
    private const string VatTuTableName = "TblChiTietHangHoa";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<NhapXuatKhoReportService> _logger = logger;

    public async Task<NhapXuatKhoReportViewModel> GetReportAsync(
        string? loai,
        DateTime? fromDate,
        DateTime? toDate,
        string? vatTu,
        string? hangHoa,
        int? khoId,
        string? maPhieu,
        CancellationToken cancellationToken = default)
    {
        var model = new NhapXuatKhoReportViewModel
        {
            Filter = new NhapXuatKhoReportFilterState
            {
                Loai = NhapXuatKhoReportLoai.Normalize(loai),
                FromDate = fromDate?.Date,
                ToDate = toDate?.Date,
                VatTu = string.IsNullOrWhiteSpace(vatTu) ? null : vatTu.Trim(),
                HangHoa = string.IsNullOrWhiteSpace(hangHoa) ? null : hangHoa.Trim(),
                KhoId = khoId is null or <= 0 ? null : khoId,
                MaPhieu = string.IsNullOrWhiteSpace(maPhieu) ? null : maPhieu.Trim()
            }
        };

        model.KhoOptions = await GetKhoOptionsAsync(cancellationToken);
        model.Items = await LoadItemsAsync(model.Filter, cancellationToken);

        foreach (var item in model.Items)
        {
            if (item.IsNhap)
            {
                model.TotalNhap++;
                model.TongSoLuongNhap += item.SoLuong;
                model.TongTienNhap += item.ThanhTien;
            }
            else
            {
                model.TotalXuat++;
                model.TongSoLuongXuat += item.SoLuong;
                model.TongTienXuat += item.ThanhTien;
            }
        }

        return model;
    }


    public async Task<IReadOnlyList<NhapXuatKhoLookupOption>> GetKhoOptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ID, TenKho
                FROM [TblKho]
                ORDER BY TenKho ASC
                """;

            var items = new List<NhapXuatKhoLookupOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new NhapXuatKhoLookupOption
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    Label = GetNullableString(reader, "TenKho") ?? string.Empty
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load kho options for NhapXuatKho report.");
            return [];
        }
    }

    private async Task<IReadOnlyList<NhapXuatKhoReportItem>> LoadItemsAsync(
        NhapXuatKhoReportFilterState filter,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);

            var sqlParts = new List<string>();
            if (filter.Loai is NhapXuatKhoReportLoai.All or NhapXuatKhoReportLoai.Nhap)
            {
                sqlParts.Add(BuildNhapSql(BuildWhereClause(filter, "nhap")));
            }

            if (filter.Loai is NhapXuatKhoReportLoai.All or NhapXuatKhoReportLoai.Xuat)
            {
                sqlParts.Add(BuildXuatSql(BuildWhereClause(filter, "xuat")));
            }

            if (sqlParts.Count == 0)
            {
                return [];
            }

            await using var command = connection.CreateCommand();
            AddFilterParameters(filter, command, "nhap");
            AddFilterParameters(filter, command, "xuat");
            command.CommandText = $"""
                {string.Join($"{Environment.NewLine}UNION ALL{Environment.NewLine}", sqlParts)}
                ORDER BY Ngay ASC, Loai ASC, PhieuId ASC, ChiTietId ASC
                """;

            var items = new List<NhapXuatKhoReportItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load NhapXuatKho report items.");
            return [];
        }
    }


    private static string BuildWhereClause(NhapXuatKhoReportFilterState filter, string prefix)
    {
        var headerAlias = prefix == "nhap" ? "pn" : "px";
        var dateColumn = prefix == "nhap" ? "NgayNhapKho" : "NgayXuatKho";
        var filters = new List<string> { "1 = 1" };

        if (filter.FromDate.HasValue)
        {
            filters.Add($"{headerAlias}.{dateColumn} >= @{prefix}FromDate");
        }

        if (filter.ToDate.HasValue)
        {
            filters.Add($"{headerAlias}.{dateColumn} < @{prefix}ToDateExclusive");
        }

        if (!string.IsNullOrWhiteSpace(filter.MaPhieu))
        {
            filters.Add($"{headerAlias}.MaPhieu COLLATE {SearchCollation} LIKE @{prefix}MaPhieu");
        }

        if (!string.IsNullOrWhiteSpace(filter.VatTu))
        {
            filters.Add($"ct.TenChiTiet COLLATE {SearchCollation} LIKE @{prefix}VatTu");
        }

        if (!string.IsNullOrWhiteSpace(filter.HangHoa))
        {
            filters.Add($"(hh.TenHangHoa COLLATE {SearchCollation} LIKE @{prefix}HangHoa OR hh.MaHangHoa COLLATE {SearchCollation} LIKE @{prefix}HangHoa)");
        }

        if (filter.KhoId.HasValue)
        {
            filters.Add($"kho.ID = @{prefix}KhoId");
        }

        return string.Join(" AND ", filters);
    }

    private static void AddFilterParameters(NhapXuatKhoReportFilterState filter, SqlCommand command, string prefix)
    {
        if (filter.FromDate.HasValue)
        {
            command.Parameters.Add(new SqlParameter($"@{prefix}FromDate", SqlDbType.DateTime) { Value = filter.FromDate.Value });
        }

        if (filter.ToDate.HasValue)
        {
            command.Parameters.Add(new SqlParameter($"@{prefix}ToDateExclusive", SqlDbType.DateTime) { Value = filter.ToDate.Value.Date.AddDays(1) });
        }

        if (!string.IsNullOrWhiteSpace(filter.MaPhieu))
        {
            command.Parameters.Add(new SqlParameter($"@{prefix}MaPhieu", SqlDbType.NVarChar, 100) { Value = $"%{filter.MaPhieu}%" });
        }

        if (!string.IsNullOrWhiteSpace(filter.VatTu))
        {
            command.Parameters.Add(new SqlParameter($"@{prefix}VatTu", SqlDbType.NVarChar, 250) { Value = $"%{filter.VatTu}%" });
        }

        if (!string.IsNullOrWhiteSpace(filter.HangHoa))
        {
            command.Parameters.Add(new SqlParameter($"@{prefix}HangHoa", SqlDbType.NVarChar, 250) { Value = $"%{filter.HangHoa}%" });
        }

        if (filter.KhoId.HasValue)
        {
            command.Parameters.Add(new SqlParameter($"@{prefix}KhoId", SqlDbType.Int) { Value = filter.KhoId.Value });
        }
    }

    private static string BuildNhapSql(string whereClause)
    {
        return $"""
            SELECT
                CAST('nhap' AS NVARCHAR(10)) AS Loai,
                pn.NgayNhapKho AS Ngay,
                pn.ID AS PhieuId,
                pn.MaPhieu,
                pn.NoiDungNhapKho AS NoiDung,
                CAST(NULL AS NVARCHAR(100)) AS MucDich,
                pn.NguoiNhapKho AS NguoiThaoTac,
                ct.TenChiTiet,
                hh.TenHangHoa,
                hh.MaHangHoa,
                ct.MaSoLo,
                ct.QRCode,
                kho.TenKho,
                kho.MaKho,
                dvt.TenVietTat AS DonViTinh,
                CAST(pnct.SoLuongNhap AS DECIMAL(18, 4)) AS SoLuong,
                CAST(ISNULL(pnct.DonGiaNhap, 0) AS DECIMAL(18, 2)) AS DonGia,
                CAST(ISNULL(pnct.DonGiaNhap, 0) * ISNULL(pnct.SoLuongNhap, 0) AS DECIMAL(18, 2)) AS ThanhTien,
                pn.TrangThaiPhieu,
                pnct.ID AS ChiTietId
            FROM [{NhapHeaderTableName}] pn
            INNER JOIN [{NhapDetailTableName}] pnct ON pnct.IDPhieuNhapKho = pn.ID
            LEFT JOIN [{VatTuTableName}] ct ON ct.IDPhieuNhapChiTiet = pnct.ID
            LEFT JOIN [TblHangHoa] hh ON hh.ID = pnct.IDHangHoa
            LEFT JOIN [TblKho] kho ON kho.ID = pn.IDKho
            LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = pnct.IDDonViTinh
            WHERE {whereClause}
            """;
    }

    private static string BuildXuatSql(string whereClause)
    {
        return $"""
            SELECT
                CAST('xuat' AS NVARCHAR(10)) AS Loai,
                px.NgayXuatKho AS Ngay,
                px.ID AS PhieuId,
                px.MaPhieu,
                px.NoiDungXuatKho AS NoiDung,
                px.MucDichXuat AS MucDich,
                px.NguoiXuatKho AS NguoiThaoTac,
                ct.TenChiTiet,
                hh.TenHangHoa,
                hh.MaHangHoa,
                ct.MaSoLo,
                ct.QRCode,
                kho.TenKho,
                kho.MaKho,
                dvt.TenVietTat AS DonViTinh,
                CAST(pxct.SoLuongXuat AS DECIMAL(18, 4)) AS SoLuong,
                CAST(ISNULL(pxct.DonGiaXuat, 0) AS DECIMAL(18, 2)) AS DonGia,
                CAST(ISNULL(pxct.TongTienXuat, 0) AS DECIMAL(18, 2)) AS ThanhTien,
                px.TrangThaiPhieu,
                pxct.ID AS ChiTietId
            FROM [{XuatHeaderTableName}] px
            INNER JOIN [{XuatDetailTableName}] pxct ON pxct.IDPhieuXuatKho = px.ID
            LEFT JOIN [{VatTuTableName}] ct ON ct.ID = pxct.IDChiTietHangHoa
            LEFT JOIN [TblHangHoa] hh ON hh.ID = pxct.IDHangHoa
            LEFT JOIN [TblKho] kho ON kho.ID = ct.IDKho
            LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = ct.IDDonVinTinh
            WHERE {whereClause}
            """;
    }

    private static NhapXuatKhoReportItem MapItem(SqlDataReader reader)
    {
        return new NhapXuatKhoReportItem
        {
            Loai = GetNullableString(reader, "Loai") == NhapXuatKhoReportItem.LoaiXuat
                ? NhapXuatKhoReportItem.LoaiXuat
                : NhapXuatKhoReportItem.LoaiNhap,
            Ngay = GetNullableDateTime(reader, "Ngay"),
            PhieuId = GetNullableInt32(reader, "PhieuId") ?? 0,
            MaPhieu = GetNullableString(reader, "MaPhieu"),
            NoiDung = GetNullableString(reader, "NoiDung"),
            MucDich = GetNullableString(reader, "MucDich"),
            NguoiThaoTac = GetNullableString(reader, "NguoiThaoTac"),
            TenChiTiet = GetNullableString(reader, "TenChiTiet"),
            TenHangHoa = GetNullableString(reader, "TenHangHoa"),
            MaHangHoa = GetNullableString(reader, "MaHangHoa"),
            MaSoLo = GetNullableString(reader, "MaSoLo"),
            QrCode = GetNullableString(reader, "QRCode"),
            TenKho = GetNullableString(reader, "TenKho"),
            MaKho = GetNullableString(reader, "MaKho"),
            DonViTinh = GetNullableString(reader, "DonViTinh"),
            SoLuong = GetNullableDecimal(reader, "SoLuong") ?? 0,
            DonGia = GetNullableDecimal(reader, "DonGia") ?? 0,
            ThanhTien = GetNullableDecimal(reader, "ThanhTien") ?? 0,
            TrangThaiPhieu = GetNullableString(reader, "TrangThaiPhieu")
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

    private static string? GetNullableString(SqlDataReader reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetString(index);
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetDateTime(index);
    }

    private static int? GetNullableInt32(SqlDataReader reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetInt32(index);
    }

    private static decimal? GetNullableDecimal(SqlDataReader reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetDecimal(index);
    }
}
