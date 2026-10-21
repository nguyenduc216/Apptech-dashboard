/* FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN
   CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-002
   PURPOSE: Append missing standard catalog rows and verify exact text mapping of existing work.
   GRAIN: one existing request-work row. Link remains TrangThaiCongViec -> StorageValue, not numeric ID.
   SAFETY: Run AFTER 20261020_work_progress_kanban.sql. Preview by default. No UPDATE of work,
   attendance, dates, rowversions, history or existing catalog customization. No trigger bypass.
   User-provided snapshot: Tạo mới = 39 (14 bytes), Hoàn thành = 14 (20 bytes).
   Snapshot counts are evidence at one point in time, not permanent business constraints. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ApplyChanges bit=0; -- 0 = read-only preview; 1 = append missing catalog rows and verify.
DECLARE @RequireOriginalSnapshot bit=0; -- Optional: require exactly the supplied 39 + 14 snapshot.

IF OBJECT_ID(N'dbo.TblTrangThaiCongViec',N'U') IS NULL
    THROW 51000,N'Run the reviewed schema migration 20261020_work_progress_kanban.sql first.',1;
IF OBJECT_ID(N'dbo.TR_WorkProgress',N'TR') IS NULL
    THROW 51000,N'Work progress schema is incomplete. Review the schema migration first.',1;

DECLARE @Seed TABLE (
    Code nvarchar(50) NOT NULL PRIMARY KEY,
    Name nvarchar(100) NOT NULL,
    StorageValue nvarchar(50) NOT NULL,
    Color varchar(7) NOT NULL,
    SortOrder int NOT NULL,
    Kind varchar(12) NOT NULL
);
INSERT @Seed VALUES
    (N'NEW',N'Tạo mới',N'Tạo mới','#64748b',10,'Normal'),
    (N'IN_PROGRESS',N'Đang thực hiện',N'Đang thực hiện','#2563eb',20,'Normal'),
    (N'REVIEW',N'Chờ đánh giá',N'Chờ đánh giá','#f59e0b',30,'Normal'),
    (N'COMPLETED',N'Hoàn thành',N'Hoàn thành','#16a34a',40,'Completed'),
    (N'CANCELLED',N'Hủy',N'Hủy','#dc2626',50,'Cancelled');

DECLARE @Snapshot TABLE (StorageValue nvarchar(50) NOT NULL,ExpectedCount bigint NOT NULL);
INSERT @Snapshot VALUES (N'Tạo mới',39),(N'Hoàn thành',14);
DECLARE @Observed TABLE (RawKey varbinary(100) NULL,StorageValue nvarchar(50) NULL,WorkCount bigint NOT NULL);
DECLARE @Inserted TABLE (ID int,Code nvarchar(50),StorageValue nvarchar(50));

BEGIN TRY
    BEGIN TRANSACTION;
    -- Freeze counts while previewing/applying; prevent catalog edits between conflict detection and INSERT.
    DECLARE @CatalogCount bigint;
    SELECT @CatalogCount=COUNT_BIG(*) FROM dbo.TblTrangThaiCongViec WITH(TABLOCKX,HOLDLOCK);
    INSERT @Observed
    SELECT CONVERT(varbinary(100),TrangThaiCongViec),MAX(TrangThaiCongViec),COUNT_BIG(*)
    FROM dbo.TblYeuCauCongViec WITH(TABLOCK,HOLDLOCK)
    GROUP BY CONVERT(varbinary(100),TrangThaiCongViec);

    -- Exact binary comparison also detects whitespace/Unicode differences hidden by database collation.
    SELECT seed.Code,seed.StorageValue,seed.Kind,
           existing.ID AS ExistingCatalogId,existing.Name AS ExistingDisplayName,occupied.Code AS ExistingStorageCode,
           CASE WHEN (existing.ID IS NULL AND occupied.ID IS NOT NULL)
                  OR CONVERT(varbinary(100),existing.StorageValue)<>CONVERT(varbinary(100),seed.StorageValue)
                  OR CONVERT(varbinary(12),existing.Kind)<>CONVERT(varbinary(12),seed.Kind) THEN N'CONFLICT_REVIEW_REQUIRED'
                WHEN existing.ID IS NULL THEN N'INSERT_MISSING'
                ELSE N'KEEP_EXISTING' END AS PlannedAction
    FROM @Seed seed LEFT JOIN dbo.TblTrangThaiCongViec existing ON existing.Code=seed.Code
    LEFT JOIN dbo.TblTrangThaiCongViec occupied ON occupied.StorageValue=seed.StorageValue
    ORDER BY seed.SortOrder;

    SELECT observed.StorageValue AS OriginalText,DATALENGTH(observed.StorageValue) AS OriginalBytes,
           observed.WorkCount,existing.ID AS ExistingCatalogId,
           COALESCE(existing.Code,seed.Code) AS MappedCode,
           CASE WHEN existing.ID IS NOT NULL THEN N'EXISTING_EXACT_MAPPING'
                WHEN seed.Code IS NOT NULL THEN N'STANDARD_SEED_REQUIRED'
                ELSE N'UNMAPPED_REVIEW_REQUIRED' END AS MappingResult
    FROM @Observed observed
    LEFT JOIN dbo.TblTrangThaiCongViec existing
        ON CONVERT(varbinary(100),existing.StorageValue)=observed.RawKey
    LEFT JOIN @Seed seed ON CONVERT(varbinary(100),seed.StorageValue)=observed.RawKey;

    SELECT snapshot.StorageValue,snapshot.ExpectedCount,ISNULL(observed.WorkCount,0) AS ActualCount,
           CAST(CASE WHEN ISNULL(observed.WorkCount,0)=snapshot.ExpectedCount THEN 1 ELSE 0 END AS bit) AS MatchesUserSnapshot
    FROM @Snapshot snapshot LEFT JOIN @Observed observed
        ON observed.RawKey=CONVERT(varbinary(100),snapshot.StorageValue);

    IF @ApplyChanges=0
    BEGIN
        COMMIT;
        PRINT N'PREVIEW ONLY: no catalog or work data changed. Review all result sets before ApplyChanges=1.';
        RETURN;
    END;

    IF @RequireOriginalSnapshot=1 AND (
        EXISTS (SELECT 1 FROM @Snapshot snapshot LEFT JOIN @Observed observed
                ON observed.RawKey=CONVERT(varbinary(100),snapshot.StorageValue)
                WHERE ISNULL(observed.WorkCount,0)<>snapshot.ExpectedCount)
        OR EXISTS (SELECT 1 FROM @Observed observed WHERE NOT EXISTS (
                   SELECT 1 FROM @Snapshot snapshot WHERE observed.RawKey=CONVERT(varbinary(100),snapshot.StorageValue))))
        THROW 51000,N'Counts differ from the supplied 39 new / 14 completed snapshot. Review current data; no changes committed.',1;

    IF EXISTS (
        SELECT 1 FROM @Seed seed JOIN dbo.TblTrangThaiCongViec existing ON existing.Code=seed.Code
        WHERE CONVERT(varbinary(100),existing.StorageValue)<>CONVERT(varbinary(100),seed.StorageValue)
           OR CONVERT(varbinary(12),existing.Kind)<>CONVERT(varbinary(12),seed.Kind)
    ) OR EXISTS (
        SELECT 1 FROM @Seed seed JOIN dbo.TblTrangThaiCongViec existing ON existing.StorageValue=seed.StorageValue
        WHERE existing.Code<>seed.Code
           OR CONVERT(varbinary(100),existing.StorageValue)<>CONVERT(varbinary(100),seed.StorageValue)
    )
        THROW 51000,N'Standard code/storage value/kind conflicts with the catalog. Existing data was not overwritten.',1;

    INSERT dbo.TblTrangThaiCongViec(Code,Name,StorageValue,Color,SortOrder,Active,Kind)
    OUTPUT inserted.ID,inserted.Code,inserted.StorageValue INTO @Inserted
    SELECT seed.Code,seed.Name,seed.StorageValue,seed.Color,seed.SortOrder,1,seed.Kind
    FROM @Seed seed WHERE NOT EXISTS (SELECT 1 FROM dbo.TblTrangThaiCongViec existing WHERE existing.Code=seed.Code);

    IF EXISTS (
        SELECT 1 FROM @Observed observed WHERE NOT EXISTS (
            SELECT 1 FROM dbo.TblTrangThaiCongViec existing
            WHERE CONVERT(varbinary(100),existing.StorageValue)=observed.RawKey))
        THROW 51000,N'Some work status text has no exact catalog mapping (including NULL/blank/Unicode variants). Review explicitly; no changes committed.',1;

    COMMIT;
    -- Save this list with the execution log. Rollback may remove ONLY these newly inserted, unreferenced rows.
    SELECT ID,Code,StorageValue FROM @Inserted ORDER BY ID;
    SELECT existing.ID,existing.Code,existing.Name,existing.StorageValue,existing.Kind,existing.Active,
           ISNULL(observed.WorkCount,0) AS MappedWorkCount
    FROM dbo.TblTrangThaiCongViec existing LEFT JOIN @Observed observed
        ON observed.RawKey=CONVERT(varbinary(100),existing.StorageValue)
    ORDER BY existing.SortOrder,existing.ID;
    PRINT N'APPLIED: catalog seed/mapping verified. Original work text, dates, rowversions and history were not modified.';
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
