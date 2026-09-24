IF OBJECT_ID(N'dbo.TblZaloSettings', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.TblZaloSettings', N'EnableAutomaticCustomerNotifications') IS NULL
BEGIN
    ALTER TABLE dbo.TblZaloSettings
    ADD EnableAutomaticCustomerNotifications BIT NOT NULL
        CONSTRAINT DF_TblZaloSettings_EnableAutomaticCustomerNotifications DEFAULT (0) WITH VALUES;
END;
