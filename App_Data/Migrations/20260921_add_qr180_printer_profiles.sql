IF OBJECT_ID(N'[dbo].[TblQr180PrinterProfile]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TblQr180PrinterProfile] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_TblQr180PrinterProfile] PRIMARY KEY,
        [ProfileName] nvarchar(100) NOT NULL,
        [OffsetX] decimal(7,2) NOT NULL,
        [OffsetY] decimal(7,2) NOT NULL,
        [PitchX] decimal(7,2) NOT NULL,
        [PitchY] decimal(7,2) NOT NULL,
        [QrSize] decimal(7,2) NOT NULL,
        [IsDefault] bit NOT NULL CONSTRAINT [DF_TblQr180PrinterProfile_IsDefault] DEFAULT(0),
        [CreatedAt] datetime2(0) NOT NULL CONSTRAINT [DF_TblQr180PrinterProfile_CreatedAt] DEFAULT(SYSUTCDATETIME()),
        [UpdatedAt] datetime2(0) NOT NULL CONSTRAINT [DF_TblQr180PrinterProfile_UpdatedAt] DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT [UQ_TblQr180PrinterProfile_ProfileName] UNIQUE ([ProfileName])
    );
END;

IF NOT EXISTS (SELECT 1 FROM [dbo].[TblQr180PrinterProfile] WHERE IsDefault = 1)
    INSERT INTO [dbo].[TblQr180PrinterProfile] (ProfileName, OffsetX, OffsetY, PitchX, PitchY, QrSize, IsDefault)
    VALUES (N'Mặc định', 0, 0, 20, 15, 14.5, 1);
