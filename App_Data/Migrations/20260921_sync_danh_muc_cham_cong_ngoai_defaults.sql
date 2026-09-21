/*
    Đồng bộ 10 nội dung chấm công ngoài mặc định.
    Script idempotent: cập nhật mục mặc định đã có, thêm mục còn thiếu,
    không xóa hoặc thay đổi các danh mục tùy chỉnh.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.TblDanhMucChamCongNgoai', N'U') IS NULL
BEGIN
    THROW 50001, N'Chưa có bảng dbo.TblDanhMucChamCongNgoai. Hãy chạy migration tạo bảng trước.', 1;
END;

DECLARE @Defaults TABLE
(
    TenNoiDung NVARCHAR(250) NOT NULL,
    ThuTuHienThi INT NOT NULL
);

INSERT INTO @Defaults (TenNoiDung, ThuTuHienThi)
VALUES
    (N'Mua vật tư', 1),
    (N'Mua thiết bị', 2),
    (N'Mua linh kiện', 3),
    (N'Giao hàng', 4),
    (N'Nhận hàng', 5),
    (N'Làm việc với nhà cung cấp', 6),
    (N'Đi ngân hàng', 7),
    (N'Đi bảo hành', 8),
    (N'Đi kho', 9),
    (N'Công việc khác', 10);

BEGIN TRANSACTION;

UPDATE target
SET target.ThuTuHienThi = source.ThuTuHienThi,
    target.IsActive = 1,
    target.UpdatedAt = SYSUTCDATETIME()
FROM dbo.TblDanhMucChamCongNgoai AS target
INNER JOIN @Defaults AS source
    ON target.TenNoiDung COLLATE Latin1_General_100_CI_AI
        = source.TenNoiDung COLLATE Latin1_General_100_CI_AI
WHERE target.ThuTuHienThi <> source.ThuTuHienThi
   OR target.IsActive = 0;

INSERT INTO dbo.TblDanhMucChamCongNgoai
    (TenNoiDung, ThuTuHienThi, IsActive)
SELECT source.TenNoiDung, source.ThuTuHienThi, 1
FROM @Defaults AS source
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.TblDanhMucChamCongNgoai AS target
    WHERE target.TenNoiDung COLLATE Latin1_General_100_CI_AI
        = source.TenNoiDung COLLATE Latin1_General_100_CI_AI
);

COMMIT TRANSACTION;

SELECT ID, TenNoiDung, ThuTuHienThi, IsActive
FROM dbo.TblDanhMucChamCongNgoai
WHERE TenNoiDung COLLATE Latin1_General_100_CI_AI IN
(
    SELECT TenNoiDung COLLATE Latin1_General_100_CI_AI
    FROM @Defaults
)
ORDER BY ThuTuHienThi, ID;
