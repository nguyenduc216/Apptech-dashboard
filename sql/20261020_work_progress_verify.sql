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
