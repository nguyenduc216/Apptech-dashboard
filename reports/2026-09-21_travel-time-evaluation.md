# Báo cáo đánh giá thời gian di chuyển chấm công

## 1. Business rule

Chỉ đánh giá check-in nằm trong ca sáng/chiều đã cấu hình, có GPS, không phải lần đầu ca và có lần chấm công trước trong cùng ngày/cùng ca. Thời điểm xuất phát ưu tiên checkout và GPS checkout của lần trước; nếu chưa checkout thì dùng check-in. Khoảng thực tế vượt cấu hình tối đa, thiếu GPS hoặc không lấy được route sẽ không đánh giá và không ảnh hưởng kết quả check-in.

Cảnh báo khi `ActualMinutes - ExpectedMinutes > AllowedTravelDeviationMinutes`. Bằng ngưỡng hoặc đi nhanh hơn dự kiến không cảnh báo.

## 2. Database schema

Thêm `dbo.TblChamCongTravelEvaluation` lưu current/previous attendance, nhân viên, tọa độ hai đầu, khoảng cách, thời gian dự kiến/thực tế, chênh lệch, ngưỡng áp dụng, cờ cảnh báo và thời điểm tạo. Bảng có FK về `TblCheckinHistory`, unique index theo current attendance và index phục vụ tra cứu nhân viên/cảnh báo.

## 3. Configuration added

Màn hình Cài đặt → Cấu hình giờ chấm công bổ sung:

- `AllowedTravelDeviationMinutes`, mặc định 15 phút;
- `MaxTravelEvaluationGapMinutes`, mặc định 120 phút.

Giá trị dùng chung `TblCauHinhHeThong`; không tạo bảng ca mới.

## 4. Travel calculation flow

Sau khi check-in văn phòng, ngoài hoặc công việc được lưu thành công, `TravelEvaluationService` chạy best-effort. Service lấy cấu hình ca, tìm lần trước của đúng nhân viên, kiểm tra ca/khoảng thời gian/GPS, rồi gọi cùng OSRM provider đang dùng bởi map (`router.project-osrm.org`). Kết quả được upsert theo current attendance. Lỗi route hoặc lỗi đánh giá chỉ ghi warning log, không rollback và không chặn check-in.

## 5. Report UI changes

Báo cáo tháng đọc dữ liệu đã lưu bằng `LEFT JOIN`, không gọi route khi mở báo cáo. Ngày có lượt bất thường hiển thị nền cam và icon cảnh báo. Chi tiết check-in có tooltip gồm khoảng cách, thời gian dự kiến, thực tế và chênh lệch. Model chi tiết cung cấp `ExpectedTravelMinutes`, `ActualTravelMinutes`, `DeviationMinutes`, `DistanceKm`, `IsTravelWarning`.

## 6. Test result

68/68 tests passed. Test mới bao phủ ngoài ca/lần đầu ca, đúng ngưỡng, vượt ngưỡng, đi nhanh hơn, khoảng cách thời gian vượt tối đa và nhận diện ca sáng/chiều.

## 7. Build result

- `dotnet restore apptech-dashboard.sln`: thành công.
- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 68/68 passed.
- `git diff --check`: không có whitespace error; chỉ có cảnh báo LF/CRLF trên Windows.

## 8. Migration script

`App_Data/Migrations/20260921_add_cham_cong_travel_evaluation.sql`

Database schema changed: **YES**.

Migration required: **YES**. Phải chạy migration trước khi deploy ứng dụng. Script idempotent và có rollback note.

## 9. Commit SHA

Feature commit: `71dd0a42c11cfccc1a998d7d7a44982e9467ff75`.

Message: `feat(attendance): add travel time evaluation for checkin report`.

## 10. Push result

Push thành công lên `origin/main`.

Deploy status: **Not deployed**. Chưa có IIS server/path triển khai được xác nhận.
