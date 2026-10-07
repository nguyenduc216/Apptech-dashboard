/*
FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
CHANGE_ID: APPTECH-20261008-NHAP-XUAT-TON-004
PURPOSE: Add independent function/menu and permission for Inventory Balance Report.
SAFETY:
- Idempotent.
- Do not modify unrelated permissions or role/user mappings.
- Do not execute automatically.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.TblChucNang WHERE MaChucNang = N'Report')
BEGIN
    INSERT INTO dbo.TblChucNang
        (MaChucNang, MaChucNangCha, TenChucNang, MieuTa, URL, ThuTuHienThi, CssClass, TrangThaiSuDung)
    VALUES
        (N'Report', NULL, N'Thống kê - báo cáo', N'Nhóm báo cáo hệ thống', NULL, N'5', N'fa-solid fa-chart-column', 1);
END;

DECLARE @FunctionId int;

SELECT TOP (1) @FunctionId = ID
FROM dbo.TblChucNang
WHERE MaChucNang = N'Report_NhapXuatTon'
ORDER BY ID;

IF @FunctionId IS NULL
BEGIN
    INSERT INTO dbo.TblChucNang
        (MaChucNang, MaChucNangCha, TenChucNang, MieuTa, URL, ThuTuHienThi, CssClass, TrangThaiSuDung)
    VALUES
        (N'Report_NhapXuatTon', N'Report', N'Nhập xuất tồn', N'Báo cáo tổng hợp nhập xuất tồn theo kỳ', N'/bao-cao/nhap-xuat-ton', N'5.4', N'fa-solid fa-chart-column', 1);

    SET @FunctionId = CONVERT(int, SCOPE_IDENTITY());
END
ELSE
BEGIN
    UPDATE dbo.TblChucNang
    SET MaChucNangCha = N'Report',
        TenChucNang = N'Nhập xuất tồn',
        MieuTa = N'Báo cáo tổng hợp nhập xuất tồn theo kỳ',
        URL = N'/bao-cao/nhap-xuat-ton',
        ThuTuHienThi = N'5.4',
        CssClass = N'fa-solid fa-chart-column',
        TrangThaiSuDung = 1
    WHERE ID = @FunctionId;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.TblQuyen WHERE MaQuyen = N'Report_NhapXuatTon_View')
BEGIN
    INSERT INTO dbo.TblQuyen (IDChucNang, TenQuyen, MaQuyen, MieuTa)
    VALUES (@FunctionId, N'Xem báo cáo Nhập xuất tồn', N'Report_NhapXuatTon_View', N'Cho phép xem và xuất Excel báo cáo Nhập xuất tồn.');
END;

UPDATE dbo.TblQuyen
SET IDChucNang = @FunctionId,
    TenQuyen = N'Xem báo cáo Nhập xuất tồn',
    MieuTa = N'Cho phép xem và xuất Excel báo cáo Nhập xuất tồn.'
WHERE MaQuyen = N'Report_NhapXuatTon_View';

COMMIT TRANSACTION;
