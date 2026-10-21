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
-- CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-003 — inspect exact tuples before any approved retention.
SELECT w.ID AS YeuCauCongViecId,w.IDYeuCau,w.IDCongViec,w.TrangThaiCongViec
FROM dbo.TblYeuCauCongViec w LEFT JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau
WHERE r.ID IS NULL ORDER BY w.ID;
SELECT c.name PossibleEquivalentDate FROM sys.columns c WHERE c.object_id=OBJECT_ID('dbo.TblYeuCauCongViec') AND
 (c.name LIKE '%Date%' OR c.name LIKE '%Ngay%' OR c.name LIKE '%Time%');
SELECT MaChucNang,MaChucNangCha,TenChucNang,URL FROM dbo.TblChucNang ORDER BY ThuTuHienThi;

/* CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-002
   Preview exact text mapping BEFORE schema migration. Snapshot supplied by user, not a live query result. */
DECLARE @KnownStatus TABLE(StorageValue nvarchar(50),Code nvarchar(50),Kind varchar(12),SnapshotCount bigint NULL);
INSERT @KnownStatus VALUES
 (N'Tạo mới',N'NEW','Normal',39),
 (N'Đang thực hiện',N'IN_PROGRESS','Normal',NULL),
 (N'Chờ đánh giá',N'REVIEW','Normal',NULL),
 (N'Hoàn thành',N'COMPLETED','Completed',14),
 (N'Hủy',N'CANCELLED','Cancelled',NULL);
;WITH observed AS (
 SELECT CONVERT(varbinary(100),TrangThaiCongViec) RawKey,MAX(TrangThaiCongViec) OriginalText,COUNT_BIG(*) WorkCount
 FROM dbo.TblYeuCauCongViec GROUP BY CONVERT(varbinary(100),TrangThaiCongViec)
)
SELECT observed.OriginalText,DATALENGTH(observed.OriginalText) OriginalBytes,observed.WorkCount,
       known.Code AS ProposedCode,known.Kind AS ProposedKind,
       CASE WHEN known.Code IS NULL THEN N'REVIEW_UNKNOWN_OR_BLANK' ELSE N'EXACT_STANDARD_TEXT' END AS MappingAction
FROM observed LEFT JOIN @KnownStatus known ON CONVERT(varbinary(100),known.StorageValue)=observed.RawKey;
SELECT known.Code,known.StorageValue,known.SnapshotCount AS UserProvidedCount,COUNT_BIG(w.ID) AS CurrentCount,
       CASE WHEN known.SnapshotCount IS NULL THEN NULL
            WHEN COUNT_BIG(w.ID)=known.SnapshotCount THEN 1 ELSE 0 END AS MatchesUserSnapshot
FROM @KnownStatus known LEFT JOIN dbo.TblYeuCauCongViec w
 ON CONVERT(varbinary(100),w.TrangThaiCongViec)=CONVERT(varbinary(100),known.StorageValue)
GROUP BY known.Code,known.StorageValue,known.SnapshotCount;
