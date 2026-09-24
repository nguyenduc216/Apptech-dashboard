# Báo cáo công tắc thông báo Zalo tự động

## Phạm vi và phiên bản

- Nhánh: `main`.
- HEAD trước sửa: `0e5ee53fe7c5ddea2ff31d5342eef817e6af9332`.
- Công tắc chỉ điều khiển notification tự động phát sinh từ Phiếu yêu cầu.

## Cấu hình

- Tên: `EnableAutomaticCustomerNotifications`.
- Section: `Zalo`.
- Mặc định: `false`.
- Đã thêm giá trị `false` vào `appsettings.json`, `appsettings.Database.json` và `appsettings.Production.json`.
- Không thay đổi AppSecret.

Tên cấu hình dùng chữ `Automatic` để phân biệt rõ với thao tác gửi Zalo thủ công của admin.

## Persistence và migration

`TblZaloSettings` dùng schema cột cố định nên bổ sung cột:

```sql
EnableAutomaticCustomerNotifications BIT NOT NULL DEFAULT (0)
```

`ZaloSettingsService` đã hỗ trợ đầy đủ:

- load từ database;
- save/update database;
- merge database và AppSettings;
- clone qua thuộc tính `Current`;
- tự thêm cột thiếu trong `EnsureTableAsync`.

Có migration idempotent:

`App_Data/Migrations/20260924_add_zalo_auto_notification_toggle.sql`

Migration không tạo bảng mới và giữ toàn bộ dữ liệu hiện tại ở trạng thái OFF.

## UI cấu hình

Trang `/admin/zalo-settings` có switch:

```text
Gửi thông báo Zalo cho khách hàng
```

Mô tả nêu rõ công tắc điều khiển việc tự động gửi khi tạo phiếu và khi trạng thái phiếu/công việc thay đổi. Card trạng thái hiển thị `Tạm tắt` hoặc `Đang bật` cùng mô tả tương ứng.

## Hành vi Create

Khi OFF:

- phiếu vẫn được lưu bình thường;
- không gọi `SendRequestCreatedNotificationAsync`;
- toast chỉ báo `Lưu yêu cầu thành công.`;
- không ghi warning/failure do Zalo đang tắt.

Khi ON, giữ nguyên flow gửi hiện tại và xử lý failure không rollback phiếu đã lưu.

## Hành vi Update và Complete

Khi OFF:

- update/complete database vẫn thành công;
- không gọi `SendRequestProgressNotificationAsync`;
- không hiển thị cảnh báo chưa gửi được Zalo;
- không tạo message failure log do intentional skip.

Khi ON, diff trạng thái và notification tổng hợp hoạt động như trước.

## Defensive guard

Hai service method tự động có guard lần hai trước khi đọc phiếu, tạo public link hoặc ghi message log:

- `SendRequestCreatedNotificationAsync`;
- `SendRequestProgressNotificationAsync`.

Khi OFF, service trả success/no-op và không ghi `TblZaloMessageLogs` failure.

## Gửi thủ công và các flow không bị ảnh hưởng

- Nút `Gửi lịch Zalo` gọi `SendBookingConfirmationAsync` và vẫn hoạt động khi auto notification OFF.
- OAuth và token refresh không đổi.
- Webhook không bị tắt.
- Mapping Zalo user/customer không đổi.
- Connect acknowledgement không đổi.
- QR/request link và `/zalo/request/{token}` không bị chặn.
- Rating flow không bị đưa vào công tắc trong scope này.

## Tests

Test bổ sung/cập nhật bao phủ:

1. config mặc định false;
2. merge/load trạng thái true;
3. merge/load trạng thái false;
4. SQL load/save có field mới;
5. Create OFF vẫn lưu, không gửi, không warning;
6. Create ON gửi đúng một lần;
7. Update OFF không gửi;
8. Update ON gửi đúng một lần khi có diff;
9. Complete OFF không gửi;
10. Complete ON gửi đúng một notification tổng hợp;
11. manual Send Zalo vẫn hoạt động khi OFF;
12. webhook/mapping không dùng công tắc;
13. public request controller không dùng công tắc.

## Triển khai

1. Chạy `20260924_add_zalo_auto_notification_toggle.sql`.
2. Publish/deploy application.
3. Recycle IIS App Pool.
4. Mở `/admin/zalo-settings`, xác nhận trạng thái mặc định `Tạm tắt`.
5. Tạo/cập nhật/hoàn thành một phiếu và xác nhận không có Zalo failure log.
6. Kiểm tra nút `Gửi lịch Zalo` vẫn gửi thủ công.
7. Chỉ bật công tắc sau khi OA đã có tier hỗ trợ API gửi tin phù hợp.

## Build và test

- `dotnet restore apptech-dashboard.sln`: thành công, dependencies đã up-to-date.
- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 153/153 test passed.
- `git diff --check`: không có whitespace error; cảnh báo LF/CRLF (nếu có) là quy ước worktree Windows.
