/* FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN; CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-001
   Operator-only rollback. Prefer reverting application and retaining additive schema.
   DESTRUCTIVE: removes catalog configuration, progress dates, audit history and permissions.
   Export all new tables/columns and role mappings first. Never run automatically. */
SET XACT_ABORT ON;
DECLARE @AllowDataLoss bit=0;
IF @AllowDataLoss<>1 THROW 51000,N'Rollback deletes progress data. Export and review; set AllowDataLoss=1 explicitly.',1;
IF EXISTS(SELECT 1 FROM dbo.TblYeuCauCongViec w JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=w.TrangThaiCongViec WHERE s.Code NOT IN ('NEW','IN_PROGRESS','REVIEW','COMPLETED','CANCELLED') AND s.Code NOT LIKE 'LEGACY[_]%')
 THROW 51000,N'Custom statuses are in use. Legacy application cannot safely interpret them. Rollback stopped.',1;
IF EXISTS(SELECT 1 FROM dbo.TblVaiTroVaQuyen v JOIN dbo.TblQuyen q ON q.ID=v.IDQuyen WHERE q.MaQuyen LIKE N'TienDoCongViec[_]%' OR q.MaQuyen LIKE N'TrangThaiCongViec[_]%')
 THROW 51000,N'New permissions have role grants. Export and remove mappings explicitly first.',1;
BEGIN TRANSACTION;
DROP TRIGGER dbo.TR_WorkProgress;
IF OBJECT_ID(N'dbo.TblWorkProgressLegacyOrphan',N'U') IS NOT NULL DROP TABLE dbo.TblWorkProgressLegacyOrphan;
DROP TABLE dbo.TblYeuCauCongViecTrangThaiHistory;
ALTER TABLE dbo.TblYeuCauCongViec DROP CONSTRAINT FK_WorkProgress_Status,CK_WorkProgress_Dates;
DROP INDEX IX_WorkProgress_Board ON dbo.TblYeuCauCongViec;
DROP INDEX IX_WorkProgress_Assignment ON dbo.TblYeuCauCongViecNhanVien;
ALTER TABLE dbo.TblYeuCauCongViec DROP COLUMN StartDate,ExpectedCompletionDate,ActualCompletionDate,ProgressVersion;
DROP TABLE dbo.TblTrangThaiCongViec;
DELETE dbo.TblQuyen WHERE MaQuyen LIKE N'TienDoCongViec[_]%' OR MaQuyen LIKE N'TrangThaiCongViec[_]%';
DELETE dbo.TblChucNang WHERE MaChucNang IN(N'TienDoCongViec',N'TrangThaiCongViec');
COMMIT;
