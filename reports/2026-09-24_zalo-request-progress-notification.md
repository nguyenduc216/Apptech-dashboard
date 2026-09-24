# Báo cáo thông báo Zalo khi cập nhật tiến độ Phiếu yêu cầu

## Phiên bản

- Nhánh: `main`.
- HEAD trước sửa: `87a5cf43d952793200783ff716da3c8edd16d248`.
- Giữ nguyên public route, token/QR flow, rating flow và transport Zalo đã sửa ở `af5b54e`.

## Files changed

- `Controllers/YeuCauController.cs`
- `Models/ZaloRequestViewModels.cs`
- `Services/YeuCauService.cs`
- `Services/RequestProgressChangeDetector.cs`
- `Services/ZaloIntegrationService.cs`
- `Tests/ApptechDashboard.Tests/RequestProgressNotificationTests.cs`
- `reports/2026-09-24_zalo-request-progress-notification.md`

## Snapshot trạng thái cũ

Thêm `IYeuCauService.GetProgressStateAsync()` để đọc một snapshot nhẹ gồm:

- Request ID và mã phiếu;
- trạng thái phiếu;
- ID, tên và trạng thái của từng work.

Một command/round-trip trả hai result set cho phiếu và danh sách work. Controller gọi snapshot trước `UpdateAsync()` hoặc `CompleteAsync()`. Không có HTTP call Zalo trong transaction database.

## Detect diff

`RequestProgressChangeDetector` normalize trạng thái bằng:

- `YeuCauTrangThaiCatalog`;
- `YeuCauCongViecTrangThaiCatalog`.

Sau khi DB commit thành công, controller tải snapshot mới và so sánh:

- trạng thái phiếu cũ/mới sau normalize;
- trạng thái work theo `RequestWorkItemId`.

Chỉ work ID tồn tại ở cả hai snapshot được so sánh. Work mới thêm và work bị xóa không tạo notification trong scope này. Thay đổi ghi chú, nhân viên hoặc field khác không xuất hiện trong snapshot nên không phát sinh tin.

## Update flow

```text
Validate/normalize form
→ snapshot before
→ UpdateAsync + commit
→ snapshot after
→ detect request/work status diff
→ không có diff: không gọi Zalo
→ có diff: gửi một RequestProgressUpdated
```

Nếu Zalo trả failure hoặc ném exception, action vẫn redirect thành công và `TempData` báo phiếu đã cập nhật nhưng chưa gửi được Zalo.

## Complete flow

`CompleteAsync()` hiện tự chuyển phiếu sang Hoàn thành và chuyển mọi work chưa hoàn thành/chưa hủy sang Hoàn thành trong cùng transaction.

Controller snapshot trước và sau Complete, sau đó gửi một change-set tổng hợp gồm:

- trạng thái phiếu cũ → Hoàn thành;
- từng existing work thực sự bị chuyển → Hoàn thành.

Nếu `AlreadyCompleted = true`, không tải diff sau và không gửi lại notification.

## Message aggregation

Thêm `IZaloMessageService.SendRequestProgressNotificationAsync()`:

1. tải thông tin phiếu/khách hàng;
2. gọi `ZaloRequestService.CreateLinkAsync(yeuCauId)` để reuse link `/zalo/request/{token}` còn hạn;
3. dựng một message chứa request change và toàn bộ work changes;
4. gọi lại `SendMessageAsync()` với `MessageType = RequestProgressUpdated` và `requireZaloUserId: true`.

Không có HTTP implementation thứ hai. Header `access_token`, parse `response.error`, retry `-216` một lần và message log tiếp tục dùng pipeline đã có.

Nếu message chi tiết dự kiến vượt 1.800 ký tự, danh sách work được rút gọn thành số lượng work vừa cập nhật và giữ link public. Tên work được giới hạn 100 ký tự mỗi dòng. Message không chứa DB ID, nhân viên, ghi chú nội bộ, audit user, GPS hoặc debug data.

## Zalo failure isolation

- Khách chưa kết nối Zalo: `SendMessageAsync()` ghi failure log theo convention hiện tại, không fallback số điện thoại.
- Zalo API/refresh lỗi: trả `ZaloSendResult.Fail` và vẫn giữ update/complete đã commit.
- Exception ngoài dự kiến: controller catch, structured log theo `RequestId`, không biến DB update thành failure.
- Một business update chỉ gửi tối đa một message; không dùng message log chung để chặn các lần cập nhật hợp lệ trong tương lai.

## Tests

Test bổ sung bao phủ:

1. request status đổi tạo request change;
2. status giống nhau sau normalize không tạo change;
3. một/hai work đổi được detect theo ID;
4. work mới/xóa không tạo status notification;
5. message tổng hợp chứa mã phiếu, old/new status, nhiều work và public link;
6. không dùng legacy rating link;
7. message dài được rút gọn;
8. Update đổi request + hai work chỉ gửi một message;
9. Update chỉ đổi field ngoài status không gửi;
10. Zalo failure không làm Update thất bại;
11. Complete đổi request + works chỉ gửi một message;
12. Complete đã hoàn thành không gửi duplicate;
13. service dùng `RequestProgressUpdated`, link service hiện tại, `SendMessageAsync()` và bắt buộc Zalo user ID.

## Build và test

- `dotnet restore apptech-dashboard.sln`: thành công, dependencies đã up-to-date.
- `dotnet build apptech-dashboard.sln -c Release`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release`: thành công, 140/140 test passed.
- `git diff --check`: không có whitespace error; chỉ có cảnh báo LF/CRLF theo quy ước worktree Windows.

## Database và migration

Không thay đổi schema, không tạo table/queue và không cần migration.

## Manual test checklist

1. Publish application và recycle IIS App Pool.
2. Đổi trạng thái phiếu Tạo mới → Đang thực hiện; xác nhận đúng một message.
3. Đổi một work Tạo mới → Đang thực hiện; xác nhận đúng một message.
4. Đổi hai work trong một lần lưu; xác nhận chỉ một message chứa cả hai.
5. Đồng thời đổi phiếu và work; xác nhận một message tổng hợp.
6. Chỉ đổi ghi chú hoặc phân công nhân viên; xác nhận không có message mới.
7. Bấm Hoàn thành; xác nhận một message gồm phiếu và các work tự chuyển Hoàn thành.
8. Bấm lại Complete cho phiếu đã hoàn thành; xác nhận không gửi lại.
9. Thử khách chưa kết nối hoặc Zalo API lỗi; xác nhận DB vẫn cập nhật và UI báo chưa gửi được Zalo.
10. Kiểm tra `TblZaloMessageLogs` với `MessageType = N'RequestProgressUpdated'`: success chỉ khi `ResponseJson.error = 0`.
