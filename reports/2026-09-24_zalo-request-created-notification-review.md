# Báo cáo review thông báo Zalo tự động khi tạo Phiếu yêu cầu

## Phạm vi và phiên bản

- Nhánh: `main`.
- HEAD sau khi đồng bộ: `e1b1e42a6109e04c218b5361bef5595c978ed4ed`.
- Đã xác nhận đủ các commit `e1b1e42`, `8c7970a`, `9cd5c21` và `fd91bfb` trong lịch sử hiện tại.
- Review tập trung vào luồng tự động gửi Zalo sau khi tạo Phiếu yêu cầu; không thay đổi schema, migration, luồng chấm công hoặc luồng gửi thủ công.

## Kết quả review

Luồng chính đáp ứng yêu cầu:

1. `YeuCauService.CreateAsync()` hoàn tất trước khi gọi gửi thông báo Zalo.
2. Chỉ action `Create` tự gửi; action `Update` và thao tác gửi thủ công không bị thay đổi.
3. `SendRequestCreatedNotificationAsync()` tải lại phiếu đã lưu, tạo hoặc tái sử dụng request link còn hiệu lực, rồi gửi với `MessageType = RequestCreatedNotification`.
4. Nội dung có tên khách hàng, mã phiếu, thời gian thực hiện, địa điểm và URL landing page `/zalo/request/{token}`.
5. Nhánh này đặt `requireZaloUserId: true`: recipient chỉ dùng `user_id`; không fallback sang số điện thoại.
6. Zalo user ID được tìm từ `TblZaloUserMappings`, sau đó fallback sang `TblKhachHang.ZaloID`. Luồng webhook request hiện tại đồng bộ cả mapping tương thích và `TblKhachHang.ZaloID`, nên khách vừa kết nối từ Phiếu yêu cầu có thể nhận tin.
7. `ZaloRequestService.CreateLinkAsync()` tái sử dụng link còn hạn của cùng phiếu; chỉ tạo link mới khi chưa có hoặc link cũ hết hạn.
8. Request tạo thành công vẫn được giữ nguyên khi Zalo không gửi được.

## Lỗi phát hiện và sửa

### 1. Ngoại lệ Zalo có thể làm action Create thất bại sau khi dữ liệu đã lưu

Trước sửa, controller gọi trực tiếp `SendRequestCreatedNotificationAsync()`. Lỗi database, tạo link hoặc lỗi khác phát sinh trước phần bắt lỗi HTTP có thể thoát khỏi action, khiến người dùng nhận trang lỗi dù phiếu đã được tạo.

Đã bọc riêng bước gửi thông báo bằng `try/catch`, ghi structured error log kèm `RequestId`, trả thông báo “lưu thành công nhưng chưa gửi được Zalo”, và vẫn redirect về phiếu vừa tạo. Cancellation do request bị hủy vẫn được truyền tiếp đúng semantics.

### 2. Log thiếu Zalo ID ghi sai MessageType

Trước sửa, nhánh bắt buộc Zalo user ID nhưng khách chưa kết nối ghi `PendingSendZaloMessage`, làm mất loại nghiệp vụ thật và không thể lọc đúng `RequestCreatedNotification`.

Đã đổi sang ghi đúng `messageType` truyền vào. Chi tiết chờ gửi vẫn nằm trong trường lỗi, không thay đổi schema.

## Tests bổ sung

Thêm test hành vi controller cho các trường hợp:

1. Phiếu phải tạo thành công trước khi bắt đầu gửi Zalo.
2. Gửi Zalo thành công chỉ được gọi đúng một lần và redirect về phiếu mới.
3. Dịch vụ Zalo trả kết quả thất bại không làm Create thất bại.
4. Dịch vụ Zalo ném ngoại lệ không làm Create thất bại.
5. Kết quả lưu không có ID không gọi gửi Zalo.

## Build và test

- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 104/104 test passed.
- `git diff --check`: không có whitespace error; chỉ có cảnh báo LF/CRLF theo quy ước worktree Windows.

## Migration và triển khai

- Không có thay đổi database và không cần migration.
- Có thay đổi application code; cần publish/deploy application và recycle IIS App Pool.
- Giữ nguyên cấu hình AppSecret theo phạm vi task này.
- Sau deploy nên tạo một phiếu cho khách đã kết nối Zalo, một phiếu cho khách chưa kết nối, rồi kiểm tra `TblZaloMessageLogs.MessageType = 'RequestCreatedNotification'` ở cả hai trường hợp.
