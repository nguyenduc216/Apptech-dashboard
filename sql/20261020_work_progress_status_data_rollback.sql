/* FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN; CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-002
   Optional operator-only rollback of catalog rows listed in the data migration INSERT output.
   Retaining additive catalog seeds is normally safer. Never remove pre-existing or referenced entries. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @AllowCatalogRemoval bit=0;
DECLARE @InsertedRows TABLE (ID int PRIMARY KEY,Code nvarchar(50),StorageValue nvarchar(50));
-- Paste ONLY ID/Code/StorageValue rows from this specific migration run's INSERT output.
-- No rows are prefilled: existing standard catalog entries are not owned by this migration.
IF @AllowCatalogRemoval<>1 OR NOT EXISTS(SELECT 1 FROM @InsertedRows)
    THROW 51000,N'Explicit removal approval and the exact INSERT output are required. Default is no changes.',1;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @CatalogCount bigint,@WorkCount bigint,@HistoryCount bigint;
    SELECT @CatalogCount=COUNT_BIG(*) FROM dbo.TblTrangThaiCongViec WITH(TABLOCKX,HOLDLOCK);
    SELECT @WorkCount=COUNT_BIG(*) FROM dbo.TblYeuCauCongViec WITH(TABLOCK,HOLDLOCK);
    SELECT @HistoryCount=COUNT_BIG(*) FROM dbo.TblYeuCauCongViecTrangThaiHistory WITH(TABLOCK,HOLDLOCK);
    IF EXISTS(SELECT 1 FROM @InsertedRows expected LEFT JOIN dbo.TblTrangThaiCongViec existing ON existing.ID=expected.ID
              WHERE existing.ID IS NULL OR existing.Code<>expected.Code
                 OR CONVERT(varbinary(100),existing.StorageValue)<>CONVERT(varbinary(100),expected.StorageValue))
        THROW 51000,N'Catalog identity differs from the recorded INSERT output. Removal stopped.',1;
    IF EXISTS(SELECT 1 FROM dbo.TblYeuCauCongViec w JOIN @InsertedRows expected ON expected.StorageValue=w.TrangThaiCongViec)
       OR EXISTS(SELECT 1 FROM dbo.TblYeuCauCongViecTrangThaiHistory h JOIN @InsertedRows expected
                 ON expected.StorageValue=h.OldStatus OR expected.StorageValue=h.NewStatus)
        THROW 51000,N'The catalog entry is referenced by current work or history. Retain it.',1;
    DELETE existing FROM dbo.TblTrangThaiCongViec existing JOIN @InsertedRows expected ON expected.ID=existing.ID;
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
