using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface INhapXuatTonReportService
{
    Task<NhapXuatTonReportViewModel> GetReportAsync(
        DateTime? fromDate,
        DateTime? toDate,
        string? hangHoa,
        int? khoId,
        CancellationToken cancellationToken = default);
}

public sealed class NhapXuatTonReportService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    INhapXuatKhoReportService nhapXuatKhoReportService,
    ILogger<NhapXuatTonReportService> logger) : INhapXuatTonReportService
{
    private const string SearchCollation = "Latin1_General_100_CI_AI";
    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly INhapXuatKhoReportService _nhapXuatKhoReportService = nhapXuatKhoReportService;
    private readonly ILogger<NhapXuatTonReportService> _logger = logger;

    // FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
    // CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-002
    // Báo cáo tổng hợp luôn có business grain IDHangHoa + IDKho.
    public async Task<NhapXuatTonReportViewModel> GetReportAsync(
        DateTime? fromDate,
        DateTime? toDate,
        string? hangHoa,
        int? khoId,
        CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        var effectiveFrom = fromDate?.Date ?? new DateTime(today.Year, today.Month, 1);
        var effectiveTo = toDate?.Date ?? today;

        var model = new NhapXuatTonReportViewModel
        {
            Filter = new NhapXuatTonReportFilterState
            {
                FromDate = effectiveFrom,
                ToDate = effectiveTo,
                HangHoa = string.IsNullOrWhiteSpace(hangHoa) ? null : hangHoa.Trim(),
                KhoId = khoId is null or <= 0 ? null : khoId
            },
            KhoOptions = await _nhapXuatKhoReportService.GetKhoOptionsAsync(cancellationToken)
        };

        if (effectiveFrom > effectiveTo)
        {
            model.StatusMessage = "Từ ngày không được lớn hơn đến ngày.";
            model.StatusType = "error";
            return model;
        }

        try
        {
            model.Items = await LoadItemsAsync(model.Filter, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load inventory balance report from {FromDate} to {ToDate}.",
                effectiveFrom,
                effectiveTo);
            model.StatusMessage = "Không thể tải báo cáo nhập xuất tồn.";
            model.StatusType = "error";
        }

        return model;
    }

    // FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
    // CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-002
    // Tính lịch sử từ phiếu hoàn tất; không dựa vào SoLuongTon và không nhân dòng PNCT qua vật tư.
    private async Task<IReadOnlyList<NhapXuatTonReportItem>> LoadItemsAsync(
        NhapXuatTonReportFilterState filter,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = BuildSql();
        command.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.DateTime) { Value = filter.FromDate });
        command.Parameters.Add(new SqlParameter("@ToDateExclusive", SqlDbType.DateTime) { Value = filter.ToDate.AddDays(1) });
        command.Parameters.Add(new SqlParameter("@KhoId", SqlDbType.Int)
        {
            Value = filter.KhoId.HasValue ? filter.KhoId.Value : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@HangHoa", SqlDbType.NVarChar, 250)
        {
            Value = string.IsNullOrWhiteSpace(filter.HangHoa) ? DBNull.Value : $"%{filter.HangHoa}%"
        });

        var items = new List<NhapXuatTonReportItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new NhapXuatTonReportItem
            {
                HangHoaId = GetNullableInt32(reader, "HangHoaId"),
                KhoId = GetNullableInt32(reader, "KhoId"),
                MaHangHoa = GetNullableString(reader, "MaHangHoa"),
                TenHangHoa = GetNullableString(reader, "TenHangHoa"),
                MaKho = GetNullableString(reader, "MaKho"),
                TenKho = GetNullableString(reader, "TenKho"),
                TonDau = GetNullableDecimal(reader, "TonDau") ?? 0,
                NhapTrongKy = GetNullableDecimal(reader, "NhapTrongKy") ?? 0,
                XuatTrongKy = GetNullableDecimal(reader, "XuatTrongKy") ?? 0
            });
        }

        return items;
    }

    // FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
    // CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-002
    // Query cố định theo IDHangHoa + IDKho; nhánh nhập tổng hợp trực tiếp từ PNCT để tránh double-count loại 2.
    private static string BuildSql()
    {
        return $"""
            /*
            FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
            CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-002
            PURPOSE: Báo cáo Nhập - Xuất - Tồn theo Hàng hóa × Kho
            BUSINESS GRAIN: IDHangHoa + IDKho
            BUSINESS RULES:
            - TonDau = NhapTruocKy - XuatTruocKy
            - TonCuoi = TonDau + NhapTrongKy - XuatTrongKy
            SAFETY / IMPORTANT:
            - Không join PNCT với nhiều TblChiTietHangHoa để SUM số nhập.
            - Chỉ tính phiếu hoàn tất.
            */
            WITH Movements AS
            (
                SELECT
                    pnct.IDHangHoa,
                    pn.IDKho,
                    pn.NgayNhapKho AS Ngay,
                    CAST(ISNULL(pnct.SoLuongNhap, 0) AS decimal(18,4)) AS SoLuongNhap,
                    CAST(0 AS decimal(18,4)) AS SoLuongXuat
                FROM [TblPhieuNhapKho] pn
                INNER JOIN [TblPhieuNhapKhoChiTiet] pnct
                    ON pnct.IDPhieuNhapKho = pn.ID
                WHERE pn.TrangThaiPhieu = N'{NhapKhoPhieuStatus.Imported}'

                UNION ALL

                SELECT
                    COALESCE(hhXuat.ID, ct.IDHangHoa) AS IDHangHoa,
                    ct.IDKho,
                    px.NgayXuatKho AS Ngay,
                    CAST(0 AS decimal(18,4)) AS SoLuongNhap,
                    CAST(ISNULL(pxct.SoLuongXuat, 0) AS decimal(18,4)) AS SoLuongXuat
                FROM [TblPhieuXuatKho] px
                INNER JOIN [TblPhieuXuatKhoChiTiet] pxct
                    ON pxct.IDPhieuXuatKho = px.ID
                LEFT JOIN [TblChiTietHangHoa] ct
                    ON ct.ID = pxct.IDChiTietHangHoa
                LEFT JOIN [TblHangHoa] hhXuat
                    ON hhXuat.ID = pxct.IDHangHoa
                WHERE px.TrangThaiPhieu = N'{XuatKhoPhieuStatus.Exported}'
            )
            SELECT
                m.IDHangHoa AS HangHoaId,
                m.IDKho AS KhoId,
                hh.MaHangHoa,
                hh.TenHangHoa,
                kho.MaKho,
                kho.TenKho,
                CAST(SUM(
                    CASE
                        WHEN m.Ngay < @FromDate
                            THEN m.SoLuongNhap - m.SoLuongXuat
                        ELSE 0
                    END
                ) AS decimal(18,4)) AS TonDau,
                CAST(SUM(
                    CASE
                        WHEN m.Ngay >= @FromDate AND m.Ngay < @ToDateExclusive
                            THEN m.SoLuongNhap
                        ELSE 0
                    END
                ) AS decimal(18,4)) AS NhapTrongKy,
                CAST(SUM(
                    CASE
                        WHEN m.Ngay >= @FromDate AND m.Ngay < @ToDateExclusive
                            THEN m.SoLuongXuat
                        ELSE 0
                    END
                ) AS decimal(18,4)) AS XuatTrongKy
            FROM Movements m
            LEFT JOIN [TblHangHoa] hh ON hh.ID = m.IDHangHoa
            LEFT JOIN [TblKho] kho ON kho.ID = m.IDKho
            WHERE
                m.Ngay < @ToDateExclusive
                AND (@KhoId IS NULL OR m.IDKho = @KhoId)
                AND (
                    @HangHoa IS NULL
                    OR hh.TenHangHoa COLLATE {SearchCollation} LIKE @HangHoa
                    OR hh.MaHangHoa COLLATE {SearchCollation} LIKE @HangHoa
                )
            GROUP BY m.IDHangHoa, m.IDKho, hh.MaHangHoa, hh.TenHangHoa, kho.MaKho, kho.TenKho
            ORDER BY hh.TenHangHoa ASC, kho.TenKho ASC;
            """;
    }

    // CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-001 - Mở kết nối theo cùng convention SQL của project.
    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = !string.IsNullOrWhiteSpace(_connectionString)
            ? _connectionString
            : _sqlOptions.BuildConnectionString();

        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    // CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-001 - Mapper nullable dùng cho result set báo cáo.
    private static string? GetNullableString(SqlDataReader reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetString(index);
    }

    // CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-001 - Mapper nullable dùng cho result set báo cáo.
    private static int? GetNullableInt32(SqlDataReader reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetInt32(index);
    }

    // CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-001 - Mapper nullable dùng cho result set báo cáo.
    private static decimal? GetNullableDecimal(SqlDataReader reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetDecimal(index);
    }
}
