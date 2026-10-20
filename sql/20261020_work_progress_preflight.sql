/* FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN; CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-001
   Read only. Run against intended database; inspect results before approving migration. */
SET NOCOUNT ON;
SELECT OBJECT_NAME(c.object_id) TableName,c.name,t.name DataType,c.max_length,c.is_nullable
FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
WHERE c.object_id IN (OBJECT_ID('dbo.TblYeuCauCongViec'),OBJECT_ID('dbo.TblYeuCau'),OBJECT_ID('dbo.TblYeuCauCongViecNhanVien'),OBJECT_ID('dbo.TblNhanVien'),OBJECT_ID('dbo.TblKhachHang'))
ORDER BY TableName,c.column_id;
SELECT TrangThaiCongViec,COUNT_BIG(*) WorkCount FROM dbo.TblYeuCauCongViec GROUP BY TrangThaiCongViec;
SELECT name,OBJECT_DEFINITION(object_id) Definition FROM sys.triggers WHERE parent_id=OBJECT_ID('dbo.TblYeuCauCongViec');
SELECT name,delete_referential_action_desc FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('dbo.TblYeuCauCongViec') OR referenced_object_id=OBJECT_ID('dbo.TblYeuCauCongViec');
SELECT COUNT_BIG(*) OrphanRequests FROM dbo.TblYeuCauCongViec w LEFT JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau WHERE r.ID IS NULL;
SELECT c.name PossibleEquivalentDate FROM sys.columns c WHERE c.object_id=OBJECT_ID('dbo.TblYeuCauCongViec') AND
 (c.name LIKE '%Date%' OR c.name LIKE '%Ngay%' OR c.name LIKE '%Time%');
SELECT MaChucNang,MaChucNangCha,TenChucNang,URL FROM dbo.TblChucNang ORDER BY ThuTuHienThi;
