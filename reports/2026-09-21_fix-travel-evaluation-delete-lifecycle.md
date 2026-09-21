# Báo cáo fix Travel Evaluation Delete Lifecycle trước deploy

## 1. Root cause

Hai foreign key `CurrentAttendanceId` và `PreviousAttendanceId` của `TblChamCongTravelEvaluation` tham chiếu `TblCheckinHistory(ID)`. Các flow xóa check-in cũ chưa xóa dữ liệu evaluation dẫn xuất, khiến SQL Server chặn xóa attendance ở cả vai trò current và previous.

## 2. Delete flows updated

Đã tìm toàn bộ repository theo `DELETE FROM TblCheckinHistory`, table constant và các API `Remove`/`RemoveRange`. Có hai flow production cần cập nhật:

- `ChamCongService.DeletePurchaseCheckinAsync`.
- `YeuCauService.DeleteCheckinAsync`.

Cả hai gọi `TravelEvaluationCleanup.DeleteRelatedAsync` trước khi chạy logic xóa attendance cũ. Cleanup dùng điều kiện:

```sql
DELETE FROM dbo.TblChamCongTravelEvaluation
WHERE CurrentAttendanceId = @AttendanceId
   OR PreviousAttendanceId = @AttendanceId
```

Foreign key được giữ nguyên; không dùng `ON DELETE CASCADE`.

## 3. Transaction handling

- Cleanup evaluation và delete attendance dùng cùng connection/transaction hiện có của service.
- Thứ tự bắt buộc là cleanup evaluation rồi mới delete attendance.
- Nếu delete attendance trả về 0 row, coordinator rollback transaction nên evaluation không bị mất.
- Nếu phát sinh exception trước commit, transaction được dispose mà không commit và service trả lỗi theo flow cũ.
- Nếu attendance không có evaluation, cleanup ảnh hưởng 0 row và delete attendance vẫn hoạt động bình thường.

## 4. Seed config

Migration `20260921_add_cham_cong_travel_evaluation.sql` được bổ sung seed idempotent theo schema thực tế `TblCauHinhHeThong(MaCauHinh, GiaTri)`:

- `AllowedTravelDeviationMinutes = 15`.
- `MaxTravelEvaluationGapMinutes = 120`.

Mỗi key chỉ được insert khi chưa tồn tại. Giá trị người dùng đã cấu hình không bị ghi đè. Seed được guard bằng kiểm tra bảng cấu hình tồn tại.

## 5. Tests added

Service/contract tests xác nhận:

- Cleanup evaluation khi attendance là `CurrentAttendanceId`.
- Cleanup evaluation khi attendance là `PreviousAttendanceId`.
- Attendance không có evaluation vẫn dùng cleanup idempotent.
- Delete attendance thất bại gọi rollback sau cleanup.
- Delete attendance thành công giữ đúng thứ tự cleanup → delete và không rollback.

## 6. Regression result

Không thay đổi `TravelEvaluationService`, `EvaluateAsync`, route OSRM, công thức actual/deviation/warning, report query hoặc UI. Các luồng tạo check-in, check-out, GPS và map không bị sửa. Điều kiện xác thực/xóa attendance của hai service vẫn giữ nguyên.

## 7. Build result

- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 73/73 test passed.
- `git diff --check`: không có whitespace error; cảnh báo LF/CRLF là quy ước worktree Windows.

## 8. Migration status

- Không thay đổi table, column, index hoặc foreign key.
- Không tạo migration mới.
- Chỉ bổ sung hai seed config idempotent vào migration Travel Evaluation hiện tại.

## 9. Deploy recommendation

Có thể deploy sau khi chạy migration Travel Evaluation đã cập nhật trước application. Nên smoke test hai trường hợp: xóa current attendance có evaluation và xóa previous attendance đang được evaluation khác tham chiếu.
