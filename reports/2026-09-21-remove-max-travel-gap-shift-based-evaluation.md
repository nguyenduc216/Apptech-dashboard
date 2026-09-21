# Báo cáo bỏ Max Travel Gap và chuyển sang đánh giá theo ca

## 1. Config removed

Đã loại bỏ hoàn toàn `MaxTravelEvaluationGapMinutes` khỏi runtime:

- `AttendanceScheduleSettingsForm`: bỏ property, validation và default 120.
- `AttendanceSettingsService`: bỏ key, query reader, parser riêng và upsert.
- `Views/Setting/Index.cshtml`: bỏ field “Khoảng thời gian tối đa giữa 2 lần chấm công để đánh giá di chuyển”.
- Migration tạo Travel Evaluation không còn seed key này.

`AllowedTravelDeviationMinutes` được giữ nguyên với default 15 phút.

## 2. Database cleanup

Thêm migration idempotent:

`App_Data/Migrations/20260921_remove_max_travel_evaluation_gap.sql`

Migration chỉ thực hiện:

```sql
DELETE FROM dbo.TblCauHinhHeThong
WHERE MaCauHinh = N'MaxTravelEvaluationGapMinutes';
```

Có guard kiểm tra `TblCauHinhHeThong` tồn tại. Không xóa hoặc cập nhật bất kỳ cấu hình nào khác.

## 3. New evaluation rule

Flow mới trong `TravelEvaluationService.EvaluateAsync`:

```text
Load current attendance
→ xác định ca của current check-in
→ tính ShiftStart theo ngày hiện tại
→ query previous gần nhất với ThoiDiem >= ShiftStart và < CurrentTime
→ không có previous: đây là lần đầu ca, bỏ qua
→ kiểm tra phòng thủ previous cùng ngày/cùng biên ca
→ ưu tiên previous checkout time/GPS, nếu không có dùng check-in time/GPS
→ route OSRM
→ deviation/warning
→ lưu evaluation
```

Query không còn lấy previous từ đầu ngày rồi lọc sau; nó chỉ đọc attendance trong ca hiện tại. Attendance ca sáng, ca trước và ngày trước không thể trở thành previous của ca chiều/ngày mới.

## 4. Before/after logic

| Nội dung | Trước | Sau |
|---|---|---|
| Điều kiện đánh giá | Có previous cùng ca và actual gap ≤ cấu hình max | Có previous trong cùng ca |
| Lần đầu ca | Bỏ qua | Bỏ qua |
| Khoảng thời gian dài trong cùng ca | Có thể bị bỏ qua bởi max gap | Vẫn đánh giá theo route/deviation |
| Previous ca trước | Lấy candidate rồi loại | Không được query |
| Previous ngày trước | Không lấy | Không lấy |
| Warning threshold | `Deviation > AllowedTravelDeviationMinutes` | Không đổi |

Giá trị actual âm do dữ liệu checkout bất thường vẫn được bỏ qua để tránh tạo evaluation sai; đây là validation dữ liệu, không phải max-gap rule.

## 5. Tests updated

- Bỏ các test phụ thuộc `MaxTravelEvaluationGapMinutes` và `ShouldEvaluateTravel`.
- Thêm/đổi test cho lần đầu ca sáng.
- Thêm test lần đầu ca chiều bỏ attendance buổi sáng.
- Thêm test check-in thứ hai trong cùng ca được chấp nhận.
- Thêm test attendance ngày trước bị bỏ qua.
- Giữ các test công thức warning, shift resolution và delete lifecycle.

## 6. Regression check

Không thay đổi:

- Route OSRM và timeout.
- Cách ưu tiên checkout time/GPS.
- Công thức actual, expected, deviation và warning.
- `TblChamCongTravelEvaluation`, index và foreign key.
- Report query, màu/icon/tooltip cảnh báo.
- Check-in/check-out, GPS, map và camera.

## 7. Migration required

**Có.** Khi deploy cần chạy migration cleanup để xóa key cũ khỏi `TblCauHinhHeThong`. Migration không thay đổi schema và an toàn khi chạy lặp lại.

Thứ tự deploy:

1. Chạy `20260921_add_cham_cong_travel_evaluation.sql` nếu môi trường chưa có feature.
2. Chạy `20260921_remove_max_travel_evaluation_gap.sql`.
3. Deploy application mới.

## 8. Build result

- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, toàn bộ test passed.
- `git diff --check`: không có whitespace error; cảnh báo LF/CRLF là quy ước worktree Windows.

## 9. Deploy recommendation

Có thể deploy sau khi build/test xanh và migration cleanup được đưa vào runbook. Nên smoke test bốn mốc 07:45, 09:00, 13:35 và 14:30 để xác nhận chỉ lượt thứ hai của từng ca tạo evaluation.
