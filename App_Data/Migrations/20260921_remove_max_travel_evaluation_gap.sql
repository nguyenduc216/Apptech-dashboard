/*
    Xóa cấu hình giới hạn khoảng thời gian đánh giá di chuyển đã ngừng sử dụng.
    Idempotent, không thay đổi các cấu hình chấm công khác.
*/
IF OBJECT_ID(N'dbo.TblCauHinhHeThong', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.TblCauHinhHeThong
    WHERE MaCauHinh = N'MaxTravelEvaluationGapMinutes';
END;
