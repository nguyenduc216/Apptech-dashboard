/*
  Lưu kết quả đánh giá thời gian di chuyển giữa hai lần chấm công.
  Idempotent, không thay đổi TblCheckinHistory.
  Rollback (chỉ khi đã sao lưu/không cần dữ liệu): DROP TABLE dbo.TblChamCongTravelEvaluation;
*/
IF OBJECT_ID(N'dbo.TblChamCongTravelEvaluation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TblChamCongTravelEvaluation
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TblChamCongTravelEvaluation PRIMARY KEY,
        CurrentAttendanceId INT NOT NULL,
        PreviousAttendanceId INT NOT NULL,
        EmployeeId INT NOT NULL,
        FromLatitude DECIMAL(18,10) NOT NULL,
        FromLongitude DECIMAL(18,10) NOT NULL,
        ToLatitude DECIMAL(18,10) NOT NULL,
        ToLongitude DECIMAL(18,10) NOT NULL,
        DistanceKm DECIMAL(10,2) NOT NULL,
        ExpectedTravelMinutes DECIMAL(10,2) NOT NULL,
        ActualTravelMinutes DECIMAL(10,2) NOT NULL,
        DeviationMinutes DECIMAL(10,2) NOT NULL,
        AllowedDeviationMinutes INT NOT NULL,
        IsWarning BIT NOT NULL,
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_TblChamCongTravelEvaluation_CreatedDate DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_TravelEvaluation_CurrentAttendance FOREIGN KEY (CurrentAttendanceId) REFERENCES dbo.TblCheckinHistory(ID),
        CONSTRAINT FK_TravelEvaluation_PreviousAttendance FOREIGN KEY (PreviousAttendanceId) REFERENCES dbo.TblCheckinHistory(ID)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TblChamCongTravelEvaluation') AND name=N'UX_TravelEvaluation_CurrentAttendance')
    CREATE UNIQUE INDEX UX_TravelEvaluation_CurrentAttendance ON dbo.TblChamCongTravelEvaluation(CurrentAttendanceId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TblChamCongTravelEvaluation') AND name=N'IX_TravelEvaluation_EmployeeWarning')
    CREATE INDEX IX_TravelEvaluation_EmployeeWarning ON dbo.TblChamCongTravelEvaluation(EmployeeId, IsWarning, CreatedDate);
