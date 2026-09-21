# Báo cáo fix Travel Evaluation FK khi xóa check-in

## 1. Root cause

`TblChamCongTravelEvaluation.CurrentAttendanceId` và `PreviousAttendanceId` cùng tham chiếu `TblCheckinHistory(ID)`. Hai luồng xóa check-in cũ xóa trực tiếp attendance mà không dọn dữ liệu đánh giá liên quan, nên SQL Server từ chối thao tác do foreign key.

## 2. Delete flows updated

- `ChamCongService.DeletePurchaseCheckinAsync`: dọn mọi evaluation có attendance ở vai trò current hoặc previous trước khi xóa lượt mua hàng.
- `YeuCauService.DeleteCheckinAsync`: thực hiện cùng cleanup trước khi xóa lượt check-in yêu cầu/công việc.
- Đã tìm toàn repository; đây là hai flow production xóa `TblCheckinHistory`.
- Logic cleanup dùng chung tại `TravelEvaluationCleanup`:

```sql
DELETE FROM dbo.TblChamCongTravelEvaluation
WHERE CurrentAttendanceId = @AttendanceId
   OR PreviousAttendanceId = @AttendanceId
```

## 3. Transaction handling

Cleanup evaluation và delete attendance sử dụng cùng `SqlConnection` và cùng `SqlTransaction` vốn đã có trong từng service.

- Nếu attendance được xóa: service commit như flow cũ.
- Nếu attendance không được xóa: coordinator gọi rollback, nên evaluation không bị mất.
- Nếu cleanup hoặc delete phát sinh exception: transaction chưa commit được dispose/rollback và service giữ cách trả lỗi cũ.
- Cleanup không có row vẫn thành công, nên attendance không có evaluation tiếp tục được xóa như trước.

Không sử dụng cascade delete.

## 4. Tests added

Bổ sung service/contract tests cho:

- Cleanup theo `CurrentAttendanceId`.
- Cleanup theo `PreviousAttendanceId`.
- Cleanup idempotent khi attendance không có evaluation.
- Delete attendance thất bại sẽ gọi rollback sau cleanup.
- Delete attendance thành công không rollback và giữ đúng thứ tự cleanup → delete.

## 5. Regression check

Không thay đổi:

- `TravelEvaluationService` và `EvaluateAsync`.
- Route OSRM, timeout và công thức deviation.
- Report query và UI warning.
- Luồng tạo check-in/check-out, GPS, map và popup công việc.
- Điều kiện nghiệp vụ xóa của từng service.

## 6. Build/test result

- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 73/73 test passed.
- `git diff --check`: không có whitespace error; chỉ có cảnh báo quy ước LF/CRLF của worktree Windows.

## 7. Migration status

**NO SCHEMA CHANGE.** Không tạo migration mới và không sửa migration Travel Evaluation hiện tại. Hai foreign key được giữ nguyên; lifecycle được xử lý trong transaction của application service.

## 8. Deploy recommendation

Regression chặn xóa check-in đã được xử lý. Có thể deploy sau khi chạy migration Travel Evaluation hiện tại trước khi khởi động phiên bản ứng dụng mới và thực hiện smoke test xóa một lượt mua hàng cùng một lượt công việc có evaluation.
