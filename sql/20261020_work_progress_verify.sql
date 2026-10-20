/* FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN; CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-001; read-only postchecks */
SET NOCOUNT ON;
SELECT s.ID,s.Code,s.Name,s.Kind,s.Active,s.Color,COUNT_BIG(w.ID) WorkCount
FROM dbo.TblTrangThaiCongViec s LEFT JOIN dbo.TblYeuCauCongViec w ON w.TrangThaiCongViec=s.StorageValue
GROUP BY s.ID,s.Code,s.Name,s.Kind,s.Active,s.Color ORDER BY s.ID;
SELECT w.ID,w.TrangThaiCongViec FROM dbo.TblYeuCauCongViec w LEFT JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=w.TrangThaiCongViec WHERE s.ID IS NULL;
SELECT ID FROM dbo.TblYeuCauCongViec WHERE ExpectedCompletionDate<StartDate;
SELECT TOP(100) * FROM dbo.TblYeuCauCongViecTrangThaiHistory ORDER BY ID DESC;
SELECT q.MaQuyen,COUNT(v.IDVaiTro) AssignedRoles FROM dbo.TblQuyen q LEFT JOIN dbo.TblVaiTroVaQuyen v ON v.IDQuyen=q.ID
WHERE q.MaQuyen LIKE N'TienDoCongViec[_]%' OR q.MaQuyen LIKE N'TrangThaiCongViec[_]%' GROUP BY q.MaQuyen;
SELECT name,is_disabled FROM sys.triggers WHERE name='TR_WorkProgress';

/* CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-002 — exact mapping, independent of collation aliases. */
SELECT w.TrangThaiCongViec AS OriginalText,DATALENGTH(w.TrangThaiCongViec) AS OriginalBytes,
       s.Code,s.Name AS DisplayName,s.Kind,COUNT_BIG(*) AS MappedWorkCount,
       CASE WHEN s.ID IS NULL THEN N'UNMAPPED_REVIEW_REQUIRED' ELSE N'EXACT_MAPPING_OK' END AS MappingResult
FROM dbo.TblYeuCauCongViec w LEFT JOIN dbo.TblTrangThaiCongViec s
 ON CONVERT(varbinary(100),s.StorageValue)=CONVERT(varbinary(100),w.TrangThaiCongViec)
GROUP BY CONVERT(varbinary(100),w.TrangThaiCongViec),w.TrangThaiCongViec,DATALENGTH(w.TrangThaiCongViec),s.ID,s.Code,s.Name,s.Kind;
