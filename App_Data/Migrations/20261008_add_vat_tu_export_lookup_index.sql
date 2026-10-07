/*
FEATURE_ID: APPTECH-WAREHOUSE-MATERIAL
CHANGE_ID: APPTECH-20261008-VAT-TU-PHIEU-XUAT-001
PURPOSE: Optimize batch lookup of completed export vouchers by material.
SAFETY:
- Idempotent.
- Index only.
- No data mutation.
- Do not execute automatically.
*/
SET NOCOUNT ON;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.TblPhieuXuatKhoChiTiet')
      AND name = N'IX_TblPhieuXuatKhoChiTiet_IDChiTietHangHoa_IDPhieuXuatKho'
)
BEGIN
    CREATE INDEX [IX_TblPhieuXuatKhoChiTiet_IDChiTietHangHoa_IDPhieuXuatKho]
        ON [dbo].[TblPhieuXuatKhoChiTiet] ([IDChiTietHangHoa], [IDPhieuXuatKho]);
END;
