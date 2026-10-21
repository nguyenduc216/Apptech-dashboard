/* FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN; CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-001
   Grain: request work, never catalog work. Operator-only migration; no application DDL.
   Backup first. Run preflight and confirm date semantics/keys/menu parents before setting approval.
   Unknown nonblank states are retained verbatim as disabled catalog entries. No invented historical dates. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @SchemaReviewed bit=0; -- Operator sets to 1 ONLY after reviewing preflight and equivalent date fields.
-- CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-003
-- User explicitly chose to retain the exact 13 orphan works below as legacy history.
DECLARE @PreserveReviewedOrphans bit=1;
IF @SchemaReviewed<>1 THROW 51000,N'Run preflight, confirm schema/date semantics and menu parents, then set SchemaReviewed=1.',1;
IF OBJECT_ID('dbo.TblTrangThaiCongViec','U') IS NOT NULL
BEGIN
    IF OBJECT_ID('dbo.TR_WorkProgress','TR') IS NULL THROW 51000,N'Partial migration detected: restore or review manually.',1;
    IF EXISTS(SELECT 1 FROM dbo.TblYeuCauCongViec w LEFT JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau WHERE r.ID IS NULL)
    BEGIN
        IF @PreserveReviewedOrphans<>1 OR OBJECT_ID(N'dbo.TblWorkProgressLegacyOrphan',N'U') IS NULL
            THROW 51000,N'Orphans exist without an approved retention register. Review before continuing.',1;
        EXEC(N'IF EXISTS(SELECT 1 FROM dbo.TblYeuCauCongViec w LEFT JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau
          WHERE r.ID IS NULL AND NOT EXISTS(SELECT 1 FROM dbo.TblWorkProgressLegacyOrphan approved
          WHERE approved.WorkId=w.ID AND approved.MissingRequestId=w.IDYeuCau AND approved.WorkCatalogId=w.IDCongViec
          AND CONVERT(varbinary(100),approved.OriginalStatus)=CONVERT(varbinary(100),w.TrangThaiCongViec)))
          THROW 51000,N''Unreviewed or changed orphan works detected after installation.'',1;');
    END;
    PRINT 'Work progress migration already installed.';
    RETURN;
END;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.TblYeuCauCongViec') AND name IN ('StartDate','ExpectedCompletionDate','ActualCompletionDate','ProgressVersion'))
    THROW 51000,N'Equivalent/new columns already exist: review and adapt rather than creating duplicate fields.',1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.TblYeuCauCongViec') AND system_type_id=189)
    THROW 51000,N'Existing rowversion found. Adapt application/migration to reuse it.',1;
IF COL_LENGTH('dbo.TblYeuCauCongViec','TrangThaiCongViec')<>100
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.TblYeuCauCongViec') AND name='TrangThaiCongViec' AND system_type_id=231)
    THROW 51000,N'Expected existing status NVARCHAR(50); schema mismatch.',1;
IF EXISTS(SELECT 1 FROM dbo.TblYeuCauCongViec WHERE TrangThaiCongViec IS NULL OR LTRIM(RTRIM(TrangThaiCongViec))=N'')
    THROW 51000,N'Blank legacy states need explicit operator-reviewed mapping. Migration stopped.',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id=OBJECT_ID('dbo.TblYeuCauCongViec') AND is_disabled=0)
    THROW 51000,N'Existing work triggers require manual compatibility review.',1;
DECLARE @ReviewedOrphans TABLE(WorkId int PRIMARY KEY,MissingRequestId int,WorkCatalogId int,OriginalStatus nvarchar(50));
INSERT @ReviewedOrphans VALUES
 (5036,4030,2030,N'Tạo mới'),(5037,4031,2030,N'Tạo mới'),(5038,4032,2030,N'Tạo mới'),
 (5048,4042,2026,N'Hoàn thành'),(5049,4042,2021,N'Hoàn thành'),
 (5050,4042,2019,N'Tạo mới'),(5051,4042,2025,N'Tạo mới'),(5052,4042,2027,N'Tạo mới'),
 (5055,4044,2028,N'Tạo mới'),(5056,4045,2028,N'Tạo mới'),
 (6026,5024,2022,N'Tạo mới'),(6027,5024,2024,N'Tạo mới'),(6028,5025,2034,N'Tạo mới');
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @LockedWorkCount bigint,@LockedRequestCount bigint;
SELECT @LockedWorkCount=COUNT_BIG(*) FROM dbo.TblYeuCauCongViec WITH(TABLOCKX,HOLDLOCK);
SELECT @LockedRequestCount=COUNT_BIG(*) FROM dbo.TblYeuCau WITH(TABLOCK,HOLDLOCK);
IF EXISTS(
 SELECT 1 FROM dbo.TblYeuCauCongViec w LEFT JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau
 WHERE r.ID IS NULL AND (@PreserveReviewedOrphans<>1 OR NOT EXISTS(
  SELECT 1 FROM @ReviewedOrphans approved WHERE approved.WorkId=w.ID
   AND approved.MissingRequestId=w.IDYeuCau AND approved.WorkCatalogId=w.IDCongViec
   AND CONVERT(varbinary(100),approved.OriginalStatus)=CONVERT(varbinary(100),w.TrangThaiCongViec))))
 THROW 51000,N'Unreviewed or changed orphan works detected. Only the exact 13 approved legacy work/request/catalog/status tuples may be retained.',1;
CREATE TABLE dbo.TblWorkProgressLegacyOrphan(
 WorkId int NOT NULL PRIMARY KEY,
 MissingRequestId int NOT NULL,
 WorkCatalogId int NOT NULL,
 OriginalStatus nvarchar(50) NOT NULL,
 ReviewedAt datetime2 NOT NULL DEFAULT SYSDATETIME(),
 Reason nvarchar(250) NOT NULL,
 CONSTRAINT FK_WorkProgress_LegacyOrphan FOREIGN KEY(WorkId) REFERENCES dbo.TblYeuCauCongViec(ID)
);
INSERT dbo.TblWorkProgressLegacyOrphan(WorkId,MissingRequestId,WorkCatalogId,OriginalStatus,Reason)
SELECT w.ID,w.IDYeuCau,w.IDCongViec,w.TrangThaiCongViec,N'User approved retaining legacy history for a missing request; excluded from normal Kanban.'
FROM dbo.TblYeuCauCongViec w LEFT JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau WHERE r.ID IS NULL;
-- All work/employee/checklist/image rows stay in place. Never manufacture a parent or completion date.
CREATE TABLE dbo.TblTrangThaiCongViec(
 ID int IDENTITY PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE, Name nvarchar(100) NOT NULL,
 StorageValue nvarchar(50) NOT NULL UNIQUE, Color varchar(7) NOT NULL,
 SortOrder int NOT NULL, Active bit NOT NULL, Kind varchar(12) NOT NULL,
 CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME(), UpdatedAt datetime2 NOT NULL DEFAULT SYSDATETIME(), Version rowversion,
 CONSTRAINT CK_WorkStatus_Kind CHECK(Kind IN ('Normal','Completed','Cancelled')),
 CONSTRAINT CK_WorkStatus_Color CHECK(Color LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]')
);
INSERT dbo.TblTrangThaiCongViec(Code,Name,StorageValue,Color,SortOrder,Active,Kind) VALUES
 (N'NEW',N'Tạo mới',N'Tạo mới','#64748b',10,1,'Normal'),
 (N'IN_PROGRESS',N'Đang thực hiện',N'Đang thực hiện','#2563eb',20,1,'Normal'),
 (N'REVIEW',N'Chờ đánh giá',N'Chờ đánh giá','#f59e0b',30,1,'Normal'),
 (N'COMPLETED',N'Hoàn thành',N'Hoàn thành','#16a34a',40,1,'Completed'),
 (N'CANCELLED',N'Hủy',N'Hủy','#dc2626',50,1,'Cancelled');
;WITH legacy AS(SELECT DISTINCT w.TrangThaiCongViec FROM dbo.TblYeuCauCongViec w WHERE NOT EXISTS(SELECT 1 FROM dbo.TblTrangThaiCongViec s WHERE s.StorageValue=w.TrangThaiCongViec))
INSERT dbo.TblTrangThaiCongViec(Code,Name,StorageValue,Color,SortOrder,Active,Kind)
 SELECT CONCAT(N'LEGACY_',ROW_NUMBER() OVER(ORDER BY TrangThaiCongViec)),TrangThaiCongViec,TrangThaiCongViec,'#64748b',1000,0,
 CASE REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(TrangThaiCongViec)),N' ',N''),N'-',N''),N'_',N'') COLLATE Latin1_General_100_CI_AI
 WHEN N'hoanthanh' THEN 'Completed' WHEN N'huy' THEN 'Cancelled' ELSE 'Normal' END FROM legacy;
ALTER TABLE dbo.TblYeuCauCongViec ADD StartDate datetime2 NULL, ExpectedCompletionDate datetime2 NULL, ActualCompletionDate datetime2 NULL, ProgressVersion rowversion;
ALTER TABLE dbo.TblYeuCauCongViec WITH CHECK ADD CONSTRAINT FK_WorkProgress_Status FOREIGN KEY(TrangThaiCongViec) REFERENCES dbo.TblTrangThaiCongViec(StorageValue);
-- No automatic backfill from attendance: these are independent business dates.
EXEC(N'ALTER TABLE dbo.TblYeuCauCongViec ADD CONSTRAINT CK_WorkProgress_Dates CHECK(ExpectedCompletionDate IS NULL OR StartDate IS NULL OR ExpectedCompletionDate>=StartDate)');
CREATE TABLE dbo.TblYeuCauCongViecTrangThaiHistory(
 ID bigint IDENTITY PRIMARY KEY, YeuCauCongViecId int NOT NULL,
 OldStatus nvarchar(50) NULL, NewStatus nvarchar(50) NOT NULL,
 PreviousCompletionDate datetime2 NULL, CompletionDate datetime2 NULL,
 ChangedAt datetime2 NOT NULL DEFAULT SYSDATETIME(), ChangedBy nvarchar(50) NULL,
 CONSTRAINT FK_WorkProgress_History FOREIGN KEY(YeuCauCongViecId) REFERENCES dbo.TblYeuCauCongViec(ID)
);
CREATE INDEX IX_WorkProgress_History ON dbo.TblYeuCauCongViecTrangThaiHistory(YeuCauCongViecId,ID);
EXEC(N'CREATE INDEX IX_WorkProgress_Board ON dbo.TblYeuCauCongViec(TrangThaiCongViec,ExpectedCompletionDate,ID) INCLUDE(IDYeuCau,IDCongViec,StartDate,ActualCompletionDate)');
CREATE INDEX IX_WorkProgress_Assignment ON dbo.TblYeuCauCongViecNhanVien(IDNhanVien,IDYeuCauCongViec);
EXEC(N'CREATE TRIGGER dbo.TR_WorkProgress ON dbo.TblYeuCauCongViec AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF TRIGGER_NESTLEVEL(OBJECT_ID(''dbo.TR_WorkProgress''))>1 RETURN;
 DECLARE @Now datetime2=SYSDATETIME();
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.ID=i.ID
   JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=i.TrangThaiCongViec
   WHERE s.Active=0 AND (d.ID IS NULL OR i.TrangThaiCongViec<>d.TrangThaiCongViec))
   THROW 51000,N''Trạng thái đích đã bị vô hiệu hóa.'',1;
 INSERT dbo.TblYeuCauCongViecTrangThaiHistory(YeuCauCongViecId,OldStatus,NewStatus,PreviousCompletionDate,CompletionDate,ChangedBy)
 SELECT i.ID,d.TrangThaiCongViec,i.TrangThaiCongViec,d.ActualCompletionDate,
   CASE WHEN s.Kind=''Completed'' THEN CASE WHEN old.Kind=''Completed'' THEN d.ActualCompletionDate ELSE @Now END ELSE NULL END,i.Updated_By
 FROM inserted i LEFT JOIN deleted d ON d.ID=i.ID JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=i.TrangThaiCongViec
 LEFT JOIN dbo.TblTrangThaiCongViec old ON old.StorageValue=d.TrangThaiCongViec
 WHERE d.ID IS NULL OR i.TrangThaiCongViec<>d.TrangThaiCongViec;
 UPDATE w SET ActualCompletionDate=CASE WHEN s.Kind=''Completed'' THEN
   CASE WHEN old.Kind=''Completed'' THEN d.ActualCompletionDate ELSE @Now END ELSE NULL END
 FROM dbo.TblYeuCauCongViec w JOIN inserted i ON i.ID=w.ID LEFT JOIN deleted d ON d.ID=i.ID
 JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=i.TrangThaiCongViec
 LEFT JOIN dbo.TblTrangThaiCongViec old ON old.StorageValue=d.TrangThaiCongViec
 WHERE d.ID IS NULL OR i.TrangThaiCongViec<>d.TrangThaiCongViec;
END');
-- Menu parents are resolved by existing routes/codes; no role grants are created.
DECLARE @CatalogParent nvarchar(250),@TechnicalParent nvarchar(250);
SELECT TOP(1) @CatalogParent=MaChucNangCha FROM dbo.TblChucNang WHERE MaChucNang=N'CongViec' OR URL IN(N'/CongViec',N'/cong-viec');
SELECT TOP(1) @TechnicalParent=MaChucNangCha FROM dbo.TblChucNang WHERE MaChucNang=N'YeuCau' OR URL IN(N'/YeuCau',N'/yeu-cau');
IF @CatalogParent IS NULL OR @TechnicalParent IS NULL THROW 51000,N'Menu parents not verified. Adapt route lookup from preflight.',1;
INSERT dbo.TblChucNang(MaChucNang,MaChucNangCha,TenChucNang,URL,ThuTuHienThi,TrangThaiSuDung)
SELECT N'TrangThaiCongViec',@CatalogParent,N'Trạng thái công việc',N'/TrangThaiCongViec',N'90',1
WHERE NOT EXISTS(SELECT 1 FROM dbo.TblChucNang WHERE MaChucNang=N'TrangThaiCongViec');
INSERT dbo.TblChucNang(MaChucNang,MaChucNangCha,TenChucNang,URL,ThuTuHienThi,TrangThaiSuDung)
SELECT N'TienDoCongViec',@TechnicalParent,N'Tiến độ công việc',N'/TienDoCongViec',N'90',1
WHERE NOT EXISTS(SELECT 1 FROM dbo.TblChucNang WHERE MaChucNang=N'TienDoCongViec');
INSERT dbo.TblQuyen(IDChucNang,TenQuyen,MaQuyen)
SELECT cn.ID,p.Label,p.Code FROM (VALUES
 (N'TrangThaiCongViec',N'Xem trạng thái',N'TrangThaiCongViec_View'),(N'TrangThaiCongViec',N'Thêm trạng thái',N'TrangThaiCongViec_Create'),
 (N'TrangThaiCongViec',N'Sửa trạng thái',N'TrangThaiCongViec_Update'),(N'TrangThaiCongViec',N'Bật/tắt trạng thái',N'TrangThaiCongViec_Toggle'),
 (N'TienDoCongViec',N'Xem tiến độ',N'TienDoCongViec_View'),(N'TienDoCongViec',N'Cập nhật tiến độ',N'TienDoCongViec_Update')) p(FunctionCode,Label,Code)
JOIN dbo.TblChucNang cn ON cn.MaChucNang=p.FunctionCode
WHERE NOT EXISTS(SELECT 1 FROM dbo.TblQuyen q WHERE q.MaQuyen=p.Code);
COMMIT;
SELECT WorkId,MissingRequestId,WorkCatalogId,OriginalStatus,ReviewedAt,Reason FROM dbo.TblWorkProgressLegacyOrphan ORDER BY WorkId;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
