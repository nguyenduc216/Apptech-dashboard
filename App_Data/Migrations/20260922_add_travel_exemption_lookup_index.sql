/*
  Support checkout travel exemption lookup by previous attendance and warning status.
  Idempotent: does not change table, columns, foreign keys, or existing indexes.
*/
IF OBJECT_ID(N'dbo.TblChamCongTravelEvaluation', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.TblChamCongTravelEvaluation')
         AND name = N'IX_TblChamCongTravelEvaluation_PreviousAttendance_IsWarning'
   )
BEGIN
    CREATE INDEX IX_TblChamCongTravelEvaluation_PreviousAttendance_IsWarning
        ON dbo.TblChamCongTravelEvaluation(PreviousAttendanceId, IsWarning);
END;
