/* Danh mục nội dung chấm công ngoài. Idempotent và không thay đổi dữ liệu lịch sử. */
IF OBJECT_ID(N'dbo.TblDanhMucChamCongNgoai', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TblDanhMucChamCongNgoai] (
        [ID] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_TblDanhMucChamCongNgoai] PRIMARY KEY,
        [TenNoiDung] NVARCHAR(250) NOT NULL,
        [ThuTuHienThi] INT NOT NULL CONSTRAINT [DF_TblDanhMucChamCongNgoai_ThuTu] DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT [DF_TblDanhMucChamCongNgoai_IsActive] DEFAULT (1),
        [CreatedAt] DATETIME2(0) NOT NULL CONSTRAINT [DF_TblDanhMucChamCongNgoai_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] DATETIME2(0) NOT NULL CONSTRAINT [DF_TblDanhMucChamCongNgoai_UpdatedAt] DEFAULT (SYSUTCDATETIME())
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TblDanhMucChamCongNgoai') AND name = N'UX_TblDanhMucChamCongNgoai_TenNoiDung')
    CREATE UNIQUE INDEX [UX_TblDanhMucChamCongNgoai_TenNoiDung] ON [dbo].[TblDanhMucChamCongNgoai] ([TenNoiDung]);

DECLARE @Seed TABLE (TenNoiDung NVARCHAR(250), ThuTuHienThi INT);
INSERT INTO @Seed VALUES
(N'Mua vật tư',1),(N'Mua thiết bị',2),(N'Mua linh kiện',3),(N'Giao hàng',4),(N'Nhận hàng',5),
(N'Làm việc với nhà cung cấp',6),(N'Đi ngân hàng',7),(N'Đi bảo hành',8),(N'Đi kho',9),(N'Công việc khác',10);

INSERT INTO [dbo].[TblDanhMucChamCongNgoai] (TenNoiDung, ThuTuHienThi, IsActive)
SELECT seed.TenNoiDung, seed.ThuTuHienThi, 1
FROM @Seed AS seed
WHERE NOT EXISTS (
    SELECT 1 FROM [dbo].[TblDanhMucChamCongNgoai] AS existing
    WHERE existing.TenNoiDung COLLATE Latin1_General_100_CI_AI = seed.TenNoiDung COLLATE Latin1_General_100_CI_AI
);
