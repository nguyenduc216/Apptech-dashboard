/* FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN
   CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-004
   Read-only SQL verification: one result set, executable SELECT per check, final PASS/FAIL.
   No application data changes. Missing objects/query errors become FAIL, never a false PASS.
   SQL Server 2014 compatible. Run the entire file in the intended database. */
SET NOCOUNT ON;
DECLARE @CheckOriginalSnapshot bit=1; -- 1: require supplied 53/39/14/13/40 snapshot; 0: current counts are INFO.
DECLARE @Checks TABLE (
    StepNo int IDENTITY PRIMARY KEY, CheckName nvarchar(200), SqlQuery nvarchar(max), Expected bigint NULL
);
DECLARE @Report TABLE (
    StepNo int, CheckName nvarchar(200), SqlQuery nvarchar(max), Actual nvarchar(2000), Expected nvarchar(200), Result varchar(4), Detail nvarchar(2000)
);
INSERT @Checks(CheckName,SqlQuery,Expected) VALUES
(N'Bảng cần thiết',N'SELECT COUNT_BIG(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N''dbo'') AND name IN(N''TblYeuCauCongViec'',N''TblYeuCau'',N''TblTrangThaiCongViec'',N''TblYeuCauCongViecTrangThaiHistory'',N''TblWorkProgressLegacyOrphan'',N''TblChucNang'',N''TblQuyen'')',7),
(N'Cột trạng thái/ngày/rowversion',N'SELECT COUNT_BIG(*) FROM sys.columns WHERE object_id=OBJECT_ID(N''dbo.TblYeuCauCongViec'') AND ((name IN(N''StartDate'',N''ExpectedCompletionDate'',N''ActualCompletionDate'') AND system_type_id=42) OR (name=N''ProgressVersion'' AND system_type_id=189) OR (name=N''TrangThaiCongViec'' AND system_type_id=231 AND max_length=100))',5),
(N'5 mã chuẩn, StorageValue và loại nghiệp vụ',N'SELECT COUNT_BIG(*) FROM dbo.TblTrangThaiCongViec s JOIN (VALUES (N''NEW'',N''Tạo mới'',''Normal''),(N''IN_PROGRESS'',N''Đang thực hiện'',''Normal''),(N''REVIEW'',N''Chờ đánh giá'',''Normal''),(N''COMPLETED'',N''Hoàn thành'',''Completed''),(N''CANCELLED'',N''Hủy'',''Cancelled'')) expected(Code,StorageValue,Kind) ON s.Code=expected.Code AND CONVERT(varbinary(100),s.StorageValue)=CONVERT(varbinary(100),expected.StorageValue) AND CONVERT(varbinary(12),s.Kind)=CONVERT(varbinary(12),expected.Kind)',5),
(N'Trigger tiến độ đang bật',N'SELECT COUNT_BIG(*) FROM sys.triggers WHERE object_id=OBJECT_ID(N''dbo.TR_WorkProgress'') AND parent_id=OBJECT_ID(N''dbo.TblYeuCauCongViec'') AND is_disabled=0',1),
(N'3 khóa ngoại bật và trusted',N'SELECT COUNT_BIG(*) FROM sys.foreign_keys WHERE name IN(N''FK_WorkProgress_Status'',N''FK_WorkProgress_History'',N''FK_WorkProgress_LegacyOrphan'') AND schema_id=SCHEMA_ID(N''dbo'') AND is_disabled=0 AND is_not_trusted=0',3),
(N'Ràng buộc ngày bật và trusted',N'SELECT COUNT_BIG(*) FROM sys.check_constraints WHERE name=N''CK_WorkProgress_Dates'' AND parent_object_id=OBJECT_ID(N''dbo.TblYeuCauCongViec'') AND is_disabled=0 AND is_not_trusted=0',1),
(N'Công việc chưa mapping chính xác',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViec w WHERE NOT EXISTS(SELECT 1 FROM dbo.TblTrangThaiCongViec s WHERE CONVERT(varbinary(100),s.StorageValue)=CONVERT(varbinary(100),w.TrangThaiCongViec))',0),
(N'Ngày dự kiến trước ngày bắt đầu',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViec WHERE ExpectedCompletionDate<StartDate',0),
(N'Ngày hoàn thành còn ở trạng thái chưa hoàn thành',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViec w JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=w.TrangThaiCongViec WHERE s.Kind<>''Completed'' AND w.ActualCompletionDate IS NOT NULL',0),
(N'Orphan mới hoặc khác hồ sơ đã duyệt',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViec w LEFT JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau WHERE r.ID IS NULL AND NOT EXISTS(SELECT 1 FROM dbo.TblWorkProgressLegacyOrphan a WHERE a.WorkId=w.ID AND a.MissingRequestId=w.IDYeuCau AND a.WorkCatalogId=w.IDCongViec AND CONVERT(varbinary(100),a.OriginalStatus)=CONVERT(varbinary(100),w.TrangThaiCongViec))',0),
(N'Hồ sơ orphan mất công việc gốc',N'SELECT COUNT_BIG(*) FROM dbo.TblWorkProgressLegacyOrphan a LEFT JOIN dbo.TblYeuCauCongViec w ON w.ID=a.WorkId WHERE w.ID IS NULL',0),
(N'Lịch sử mất công việc gốc',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViecTrangThaiHistory h LEFT JOIN dbo.TblYeuCauCongViec w ON w.ID=h.YeuCauCongViecId WHERE w.ID IS NULL',0),
(N'6 quyền tính năng',N'SELECT COUNT_BIG(*) FROM dbo.TblQuyen WHERE MaQuyen IN(N''TrangThaiCongViec_View'',N''TrangThaiCongViec_Create'',N''TrangThaiCongViec_Update'',N''TrangThaiCongViec_Toggle'',N''TienDoCongViec_View'',N''TienDoCongViec_Update'')',6),
(N'2 menu tính năng đang bật',N'SELECT COUNT_BIG(*) FROM dbo.TblChucNang WHERE TrangThaiSuDung=1 AND MaChucNangCha IS NOT NULL AND ((MaChucNang=N''TrangThaiCongViec'' AND URL=N''/TrangThaiCongViec'') OR (MaChucNang=N''TienDoCongViec'' AND URL=N''/TienDoCongViec''))',2),
(N'Tổng công việc được bảo toàn',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViec',CASE WHEN @CheckOriginalSnapshot=1 THEN 53 ELSE NULL END),
(N'Tổng trạng thái Tạo mới',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViec WHERE CONVERT(varbinary(100),TrangThaiCongViec)=CONVERT(varbinary(100),N''Tạo mới'')',CASE WHEN @CheckOriginalSnapshot=1 THEN 39 ELSE NULL END),
(N'Tổng trạng thái Hoàn thành',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViec WHERE CONVERT(varbinary(100),TrangThaiCongViec)=CONVERT(varbinary(100),N''Hoàn thành'')',CASE WHEN @CheckOriginalSnapshot=1 THEN 14 ELSE NULL END),
(N'Công việc thiếu phiếu cha giữ lịch sử',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViec w LEFT JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau WHERE r.ID IS NULL',CASE WHEN @CheckOriginalSnapshot=1 THEN 13 ELSE NULL END),
(N'Công việc có phiếu cha hợp lệ',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViec w JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau',CASE WHEN @CheckOriginalSnapshot=1 THEN 40 ELSE NULL END),
(N'Hồ sơ orphan đã ghi nhận',N'SELECT COUNT_BIG(*) FROM dbo.TblWorkProgressLegacyOrphan',CASE WHEN @CheckOriginalSnapshot=1 THEN 13 ELSE NULL END),
(N'Lịch sử chuyển trạng thái hiện có (thông tin)',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViecTrangThaiHistory',NULL),
(N'Hoàn thành lịch sử chưa rõ ngày (được phép)',N'SELECT COUNT_BIG(*) FROM dbo.TblYeuCauCongViec w JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=w.TrangThaiCongViec WHERE s.Kind=''Completed'' AND w.ActualCompletionDate IS NULL',NULL);

DECLARE @Step int,@Name nvarchar(200),@Sql nvarchar(max),@Expected bigint,@Actual bigint,@Command nvarchar(max),@Result varchar(4),@Detail nvarchar(2000);
PRINT N'Database: '+DB_NAME()+N' | Snapshot check: '+CONVERT(nvarchar(1),@CheckOriginalSnapshot);
DECLARE check_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT StepNo,CheckName,SqlQuery,Expected FROM @Checks ORDER BY StepNo;
OPEN check_cursor;
FETCH NEXT FROM check_cursor INTO @Step,@Name,@Sql,@Expected;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @Actual=NULL;
    BEGIN TRY
        SET @Command=N'SELECT @Observed=('+@Sql+N');';
        EXEC sys.sp_executesql @Command,N'@Observed bigint OUTPUT',@Observed=@Actual OUTPUT;
        SET @Result=CASE WHEN @Actual IS NULL THEN 'FAIL' WHEN @Expected IS NULL THEN 'INFO' WHEN @Actual=@Expected THEN 'PASS' ELSE 'FAIL' END;
        SET @Detail=CASE WHEN @Actual IS NULL THEN N'Không nhận được kết quả kiểm tra.' WHEN @Expected IS NULL THEN N'Chỉ thông tin; không tính vào PASS/FAIL.' WHEN @Result='PASS' THEN N'Đạt.' ELSE N'Không khớp kỳ vọng; cần kiểm tra.' END;
        INSERT @Report VALUES(@Step,@Name,@Sql,COALESCE(CONVERT(nvarchar(30),@Actual),N'NULL'),COALESCE(CONVERT(nvarchar(30),@Expected),N'INFO'),@Result,@Detail);
    END TRY
    BEGIN CATCH
        INSERT @Report VALUES(@Step,@Name,@Sql,N'ERROR',COALESCE(CONVERT(nvarchar(30),@Expected),N'INFO'),'FAIL',ERROR_MESSAGE());
    END CATCH;
    PRINT CONVERT(nvarchar(10),@Step)+N'. '+@Name;
    PRINT @Sql+N';';
    SELECT @Detail=N'['+Result+N'] Actual='+Actual+N'; Expected='+Expected+N'; '+Detail FROM @Report WHERE StepNo=@Step;
    PRINT @Detail;
    FETCH NEXT FROM check_cursor INTO @Step,@Name,@Sql,@Expected;
END;
CLOSE check_cursor;
DEALLOCATE check_cursor;
DECLARE @Failed int=(SELECT COUNT(*) FROM @Report WHERE Result='FAIL');
DECLARE @Final varchar(4)=CASE WHEN @Failed=0 THEN 'PASS' ELSE 'FAIL' END;
DECLARE @Summary nvarchar(2000)=N'Database='+DB_NAME()+N'; PASS='+CONVERT(nvarchar(10),(SELECT COUNT(*) FROM @Report WHERE Result='PASS'))+N'; FAIL='+CONVERT(nvarchar(10),@Failed)+N'; INFO='+CONVERT(nvarchar(10),(SELECT COUNT(*) FROM @Report WHERE Result='INFO'))+N'. Kết luận chỉ áp dụng cho các kiểm tra SQL trên, không thay thế kiểm thử UI/phân quyền thực tế.';
INSERT @Report VALUES(999,N'KẾT LUẬN CUỐI CÙNG',N'-- FAIL nếu bất kỳ kiểm tra SQL nào lỗi hoặc không đạt; INFO không làm thất bại.',@Final,N'PASS',@Final,@Summary);
SELECT StepNo AS STT,CheckName AS KiemTra,SqlQuery AS CauLenhSQL,Actual AS ThucTe,Expected AS KyVong,Result AS KetQua,Detail AS GhiChu FROM @Report ORDER BY StepNo;
PRINT N'KẾT LUẬN CUỐI CÙNG: '+@Final+N' | '+@Summary;
