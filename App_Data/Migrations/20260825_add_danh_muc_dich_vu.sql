/*
    Add service catalog templates for request work presets.
    Idempotent: safe to run more than once.
*/

IF OBJECT_ID(N'dbo.TblDanhMucDichVu', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TblDanhMucDichVu] (
        [ID] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_TblDanhMucDichVu] PRIMARY KEY,
        [TenDichVu] NVARCHAR(250) NOT NULL,
        [MieuTa] NVARCHAR(1000) NULL,
        [TrangThaiSuDung] BIT NOT NULL CONSTRAINT [DF_TblDanhMucDichVu_TrangThaiSuDung] DEFAULT (1),
        [Created_Date] DATETIME NULL,
        [Created_By] NVARCHAR(50) NULL,
        [Updated_Date] DATETIME NULL,
        [Updated_By] NVARCHAR(50) NULL
    );
END;

IF OBJECT_ID(N'dbo.TblDanhMucDichVuCongViec', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TblDanhMucDichVuCongViec] (
        [ID] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_TblDanhMucDichVuCongViec] PRIMARY KEY,
        [IDDanhMucDichVu] INT NOT NULL,
        [IDCongViec] INT NOT NULL,
        [ThuTu] INT NOT NULL CONSTRAINT [DF_TblDanhMucDichVuCongViec_ThuTu] DEFAULT (0),
        [Created_Date] DATETIME NULL,
        [Created_By] NVARCHAR(50) NULL
    );
END;

IF COL_LENGTH(N'dbo.TblYeuCau', N'IDDanhMucDichVu') IS NULL
BEGIN
    ALTER TABLE [dbo].[TblYeuCau]
    ADD [IDDanhMucDichVu] INT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_TblDanhMucDichVuCongViec_TblDanhMucDichVu'
      AND parent_object_id = OBJECT_ID(N'dbo.TblDanhMucDichVuCongViec')
)
BEGIN
    ALTER TABLE [dbo].[TblDanhMucDichVuCongViec] WITH CHECK
    ADD CONSTRAINT [FK_TblDanhMucDichVuCongViec_TblDanhMucDichVu]
        FOREIGN KEY ([IDDanhMucDichVu]) REFERENCES [dbo].[TblDanhMucDichVu] ([ID]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_TblDanhMucDichVuCongViec_TblCongViec'
      AND parent_object_id = OBJECT_ID(N'dbo.TblDanhMucDichVuCongViec')
)
BEGIN
    ALTER TABLE [dbo].[TblDanhMucDichVuCongViec] WITH CHECK
    ADD CONSTRAINT [FK_TblDanhMucDichVuCongViec_TblCongViec]
        FOREIGN KEY ([IDCongViec]) REFERENCES [dbo].[TblCongViec] ([ID]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_TblYeuCau_TblDanhMucDichVu'
      AND parent_object_id = OBJECT_ID(N'dbo.TblYeuCau')
)
BEGIN
    ALTER TABLE [dbo].[TblYeuCau] WITH CHECK
    ADD CONSTRAINT [FK_TblYeuCau_TblDanhMucDichVu]
        FOREIGN KEY ([IDDanhMucDichVu]) REFERENCES [dbo].[TblDanhMucDichVu] ([ID]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_TblDanhMucDichVu_TenDichVu'
      AND object_id = OBJECT_ID(N'dbo.TblDanhMucDichVu')
)
BEGIN
    CREATE UNIQUE INDEX [UX_TblDanhMucDichVu_TenDichVu]
    ON [dbo].[TblDanhMucDichVu] ([TenDichVu]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_TblDanhMucDichVuCongViec_ServiceWork'
      AND object_id = OBJECT_ID(N'dbo.TblDanhMucDichVuCongViec')
)
BEGIN
    CREATE UNIQUE INDEX [UX_TblDanhMucDichVuCongViec_ServiceWork]
    ON [dbo].[TblDanhMucDichVuCongViec] ([IDDanhMucDichVu], [IDCongViec]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TblDanhMucDichVuCongViec_Service_Order'
      AND object_id = OBJECT_ID(N'dbo.TblDanhMucDichVuCongViec')
)
BEGIN
    CREATE INDEX [IX_TblDanhMucDichVuCongViec_Service_Order]
    ON [dbo].[TblDanhMucDichVuCongViec] ([IDDanhMucDichVu], [ThuTu], [ID]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TblDanhMucDichVuCongViec_Work'
      AND object_id = OBJECT_ID(N'dbo.TblDanhMucDichVuCongViec')
)
BEGIN
    CREATE INDEX [IX_TblDanhMucDichVuCongViec_Work]
    ON [dbo].[TblDanhMucDichVuCongViec] ([IDCongViec]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TblYeuCau_IDDanhMucDichVu'
      AND object_id = OBJECT_ID(N'dbo.TblYeuCau')
)
BEGIN
    CREATE INDEX [IX_TblYeuCau_IDDanhMucDichVu]
    ON [dbo].[TblYeuCau] ([IDDanhMucDichVu]);
END;

/*
-- Optional sample data, not executed automatically:
-- INSERT INTO dbo.TblDanhMucDichVu (TenDichVu, MieuTa, TrangThaiSuDung, Created_Date, Created_By)
-- VALUES (N'Sửa nhà thông minh chung cư', N'Template kiểm tra và cấu hình căn hộ chung cư.', 1, GETDATE(), N'system');
*/
