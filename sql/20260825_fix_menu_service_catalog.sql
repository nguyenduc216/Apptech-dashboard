SET NOCOUNT ON;

DECLARE @ParentCode nvarchar(250);
DECLARE @ParentId int;
DECLARE @WorkSort decimal(10,2);
DECLARE @NextSort decimal(10,2);
DECLARE @ServiceSort decimal(10,2);
DECLARE @ServiceSortText nvarchar(50);
DECLARE @FunctionId int;

SELECT TOP (1)
    @ParentId = ID,
    @ParentCode = MaChucNang
FROM dbo.TblChucNang
WHERE TrangThaiSuDung = 1
  AND (MaChucNangCha IS NULL OR LTRIM(RTRIM(MaChucNangCha)) = N'')
  AND (
        MaChucNang = N'Category'
     OR TenChucNang COLLATE Latin1_General_100_CI_AI = N'Danh mục'
  )
ORDER BY
    CASE WHEN MaChucNang = N'Category' THEN 0 ELSE 1 END,
    TRY_CONVERT(decimal(10,2), ThuTuHienThi),
    ID;

IF @ParentCode IS NULL
BEGIN
    INSERT INTO dbo.TblChucNang (MaChucNang, MaChucNangCha, TenChucNang, MieuTa, URL, ThuTuHienThi, CssClass, TrangThaiSuDung)
    VALUES (N'Category', NULL, N'Danh mục', N'Nhóm danh mục quản trị', NULL, N'3', N'fa-solid fa-table-list', 1);

    SET @ParentId = CONVERT(int, SCOPE_IDENTITY());
    SET @ParentCode = N'Category';
END;

SELECT TOP (1) @WorkSort = TRY_CONVERT(decimal(10,2), ThuTuHienThi)
FROM dbo.TblChucNang
WHERE TrangThaiSuDung = 1
  AND MaChucNangCha = @ParentCode
  AND (
        MaChucNang = N'Cat_Cong_viec'
     OR URL = N'/cong-viec'
     OR TenChucNang COLLATE Latin1_General_100_CI_AI LIKE N'%Công việc%'
  )
ORDER BY
    CASE
        WHEN MaChucNang = N'Cat_Cong_viec' THEN 0
        WHEN URL = N'/cong-viec' THEN 1
        ELSE 2
    END,
    TRY_CONVERT(decimal(10,2), ThuTuHienThi),
    ID;

SET @WorkSort = ISNULL(@WorkSort, 3.40);
SET @ServiceSort = @WorkSort + 0.01;

SELECT @NextSort = MIN(TRY_CONVERT(decimal(10,2), ThuTuHienThi))
FROM dbo.TblChucNang
WHERE TrangThaiSuDung = 1
  AND MaChucNangCha = @ParentCode
  AND MaChucNang <> N'DanhMucDichVu'
  AND TRY_CONVERT(decimal(10,2), ThuTuHienThi) > @WorkSort;

IF @NextSort IS NOT NULL AND @NextSort <= @ServiceSort
BEGIN
    UPDATE dbo.TblChucNang
    SET ThuTuHienThi = CONVERT(nvarchar(50), CAST(TRY_CONVERT(decimal(10,2), ThuTuHienThi) + 0.01 AS decimal(10,2)))
    WHERE TrangThaiSuDung = 1
      AND MaChucNangCha = @ParentCode
      AND MaChucNang <> N'DanhMucDichVu'
      AND TRY_CONVERT(decimal(10,2), ThuTuHienThi) >= @NextSort;

    SET @ServiceSort = @NextSort;
END;

SET @ServiceSortText = CONVERT(nvarchar(50), CAST(@ServiceSort AS decimal(10,2)));

UPDATE dbo.TblChucNang
SET TrangThaiSuDung = 0
WHERE TrangThaiSuDung = 1
  AND (MaChucNangCha IS NULL OR LTRIM(RTRIM(MaChucNangCha)) = N'')
  AND (
        MaChucNang = N'YeuCau'
     OR URL = N'/YeuCau'
  );

SELECT TOP (1) @FunctionId = ID
FROM dbo.TblChucNang
WHERE MaChucNang = N'DanhMucDichVu'
ORDER BY
    CASE WHEN MaChucNangCha = @ParentCode THEN 0 ELSE 1 END,
    TrangThaiSuDung DESC,
    ID;

IF @FunctionId IS NULL
BEGIN
    INSERT INTO dbo.TblChucNang (MaChucNang, MaChucNangCha, TenChucNang, MieuTa, URL, ThuTuHienThi, CssClass, TrangThaiSuDung)
    VALUES (N'DanhMucDichVu', @ParentCode, N'Dịch vụ', N'Template nhóm công việc cho phiếu yêu cầu', N'/danh-muc-dich-vu', @ServiceSortText, N'fa-solid fa-layer-group', 1);

    SET @FunctionId = CONVERT(int, SCOPE_IDENTITY());
END
ELSE
BEGIN
    UPDATE dbo.TblChucNang
    SET MaChucNangCha = @ParentCode,
        TenChucNang = N'Dịch vụ',
        MieuTa = N'Template nhóm công việc cho phiếu yêu cầu',
        URL = N'/danh-muc-dich-vu',
        ThuTuHienThi = @ServiceSortText,
        CssClass = N'fa-solid fa-layer-group',
        TrangThaiSuDung = 1
    WHERE ID = @FunctionId;
END;

UPDATE dbo.TblChucNang
SET TrangThaiSuDung = 0
WHERE MaChucNang = N'DanhMucDichVu'
  AND ID <> @FunctionId;

UPDATE dbo.TblChucNang
SET TrangThaiSuDung = 0
WHERE TrangThaiSuDung = 1
  AND MaChucNang = N'MasterData'
  AND (MaChucNangCha IS NULL OR LTRIM(RTRIM(MaChucNangCha)) = N'')
  AND NOT EXISTS (
        SELECT 1
        FROM dbo.TblChucNang AS child
        WHERE child.TrangThaiSuDung = 1
          AND child.MaChucNangCha = N'MasterData'
  );

UPDATE dbo.TblQuyen
SET IDChucNang = @FunctionId
WHERE MaQuyen IN (
    N'DanhMucDichVu_View',
    N'DanhMucDichVu_Create',
    N'DanhMucDichVu_Update',
    N'DanhMucDichVu_Delete'
);

SELECT
    ID,
    MaChucNang,
    MaChucNangCha,
    TenChucNang,
    ThuTuHienThi,
    URL,
    TrangThaiSuDung
FROM dbo.TblChucNang
WHERE MaChucNang IN (N'YeuCau', N'DanhMucDichVu', N'MasterData', N'Category', N'Cat_Cong_viec')
   OR URL IN (N'/YeuCau', N'/danh-muc-dich-vu', N'/cong-viec')
ORDER BY
    MaChucNangCha,
    TRY_CONVERT(decimal(10,2), ThuTuHienThi),
    ID;
