/*
FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
CHANGE_ID: APPTECH-20261008-NHAP-XUAT-TON-004
PURPOSE: Read-only verification for independent Inventory Balance Report permission.
SAFETY: Read-only; do not modify catalog or mappings.
*/
SET NOCOUNT ON;

SELECT
    q.MaQuyen,
    COUNT_BIG(*) AS PermissionCount
FROM dbo.TblQuyen AS q
WHERE q.MaQuyen IN (N'Report_NhapXuatKho_View', N'Report_NhapXuatTon_View')
GROUP BY q.MaQuyen
ORDER BY q.MaQuyen;

SELECT
    cn.ID,
    cn.MaChucNang,
    cn.MaChucNangCha,
    cn.TenChucNang,
    cn.URL,
    cn.ThuTuHienThi,
    cn.TrangThaiSuDung,
    q.ID AS PermissionId,
    q.MaQuyen,
    q.TenQuyen
FROM dbo.TblChucNang AS cn
LEFT JOIN dbo.TblQuyen AS q ON q.IDChucNang = cn.ID
WHERE cn.MaChucNang IN (N'Report_NhapXuatKho', N'Report_NhapXuatTon')
ORDER BY cn.MaChucNang, q.MaQuyen;

-- Review only: migration 004 must not create or alter role mappings.
SELECT
    q.MaQuyen,
    COUNT_BIG(vtq.IDVaiTro) AS AssignedRoleCount
FROM dbo.TblQuyen AS q
LEFT JOIN dbo.TblVaiTroVaQuyen AS vtq ON vtq.IDQuyen = q.ID
WHERE q.MaQuyen IN (N'Report_NhapXuatKho_View', N'Report_NhapXuatTon_View')
GROUP BY q.MaQuyen
ORDER BY q.MaQuyen;
