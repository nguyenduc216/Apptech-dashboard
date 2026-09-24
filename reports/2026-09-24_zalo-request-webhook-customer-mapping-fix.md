# Báo cáo sửa mapping webhook Zalo từ Phiếu yêu cầu

## Root cause

Luồng QR Phiếu yêu cầu tạo dữ liệu trong `TblZaloRequestLinks`, nhưng webhook cũ ưu tiên resolve qua `TblCustomerInteractionLinks` và `UpsertMappingFromExternalIdAsync()` chỉ lấy `CustomerId` từ bảng cũ. Khi token thuộc Phiếu yêu cầu, hệ thống có thể tìm thấy `UserExternalId` nhưng không giữ nguyên `RequestId`/`CustomerId` đã xác định, dẫn đến không map được hoặc có nguy cơ dùng recent-link fallback sai khách.

Ngoài ra:

- follow event có thể đi qua recent-link fallback dù không đủ dữ kiện xác định khách;
- acknowledgement resolve lại customer context thay vì dùng request context đã match;
- popup admin chưa hiển thị profile và chưa dừng polling ngay khi kết nối;
- trang khách tự redirect sang rating nên không hiển thị rõ trạng thái kết nối;
- `appsettings.json` chứa Zalo AppSecret thật.

## Files changed

- `Services/ZaloIntegrationService.cs`
- `Services/ZaloRequestService.cs`
- `Models/ZaloRequestViewModels.cs`
- `Controllers/ZaloRequestController.cs`
- `Views/YeuCau/Detail.cshtml`
- `Tests/ApptechDashboard.Tests/ZaloRequestWebhookFlowTests.cs`
- `appsettings.json`
- `reports/2026-09-24_zalo-request-webhook-customer-mapping-fix.md`

## Flow before

```text
Webhook
→ extract message
→ tìm TblCustomerInteractionLinks trước
→ có thể tìm TblZaloRequestLinks nhưng chỉ trả UserExternalId
→ UpsertMappingFromExternalIdAsync lại chỉ đọc TblCustomerInteractionLinks
→ nếu không match thì đoán link mở gần đây
→ resolve customer context lần nữa để gửi acknowledgement
```

## Flow after

```text
Webhook
→ parse event_name / sender.id / message text
→ giữ nguyên token gồm chữ, số, "-", "_"
→ exact-match TblZaloRequestLinks trước
→ trả context RequestId + CustomerId + Token + UserExternalId + Status
→ khóa và xác nhận lại link trong transaction
→ chống gắn cùng Zalo user sang customer khác
→ upsert TblCustomerZaloProfiles
→ update TblKhachHang.ZaloID
→ update TblZaloRequestLinks.Status, giữ nguyên Rated
→ đồng bộ TblZaloUserMappings nếu bảng tồn tại
→ commit
→ gửi acknowledgement bằng context vừa match
→ status API/polling hiển thị Đã kết nối Zalo
```

Nếu không exact-match request link, flow cũ `TblCustomerInteractionLinks` vẫn hoạt động. Recent-link fallback chỉ còn áp dụng cho flow cũ, chỉ khi webhook không có message text, và từ chối map nếu có nhiều candidate. Follow/unfollow không dùng recent-link fallback; nó chỉ cập nhật `IsFollowingOa` khi Zalo user đã được biết trước.

## Atomicity and idempotency

- Mapping request chạy trong transaction.
- Link được kiểm tra lại bằng `UPDLOCK, HOLDLOCK`, gồm token, request, customer, hạn dùng và status hợp lệ.
- Profile dùng `MERGE` theo `(ZaloUserId, OaId)` nên gửi lại cùng token không tạo duplicate.
- Nếu Zalo user đã thuộc customer khác, mapping bị từ chối và ghi warning.
- Status `Rated` không bị downgrade; các status hợp lệ khác chuyển thành `ZaloConnected`.
- Acknowledgement dùng message log thành công để tránh gửi lặp. Lỗi profile API hoặc acknowledgement không rollback mapping.

## Logging

Structured logs mới không ghi token truy cập, refresh token hoặc AppSecret:

- webhook nhận được: `EventName`, `ZaloUserId`, `OaId`, `HasMessageText`;
- request match: `RequestId`, `CustomerId`, `UserExternalId`, `ZaloUserId`;
- không match: `CandidateCount`, không log toàn payload/token;
- map thành công/thất bại và lý do cụ thể;
- follow event chỉ báo có cập nhật profile đã biết hay không.

`TblZaloWebhookEvents` tiếp tục lưu webhook event theo cơ chế hiện có để phục vụ audit production.

## UI

- Popup Phiếu yêu cầu tự chuyển sang `Đã kết nối Zalo`, hiển thị tên/số điện thoại/Zalo User ID nếu có và dừng polling.
- `/zalo/request/{token}` hiển thị `Đã kết nối Zalo thành công`, ẩn hướng dẫn gửi lại mã nhưng vẫn cho phép tiếp tục đánh giá.
- Rating flow không thay đổi.

## DB changes and migration

Không có thay đổi schema và không cần migration mới. Các index cần thiết đã có trong schema hiện tại:

- unique token và `UserExternalId` của `TblZaloRequestLinks`;
- unique `(ZaloUserId, OaId)` và index `CustomerId` của `TblCustomerZaloProfiles`.

## Tests

Thêm 14 test cho:

1. exact request token;
2. token bắt đầu bằng `-`;
3. token có `_` và `-`;
4. update `TblKhachHang.ZaloID`;
5. upsert `TblCustomerZaloProfiles`;
6. Created/Opened → ZaloConnected;
7. giữ nguyên Rated;
8. webhook lặp không insert duplicate profile;
9. request lookup trước fallback bảng cũ;
10. nhiều recent link không tự đoán và follow không dùng recent fallback;
11. profile API lỗi vẫn tiếp tục mapping Zalo ID;
12. acknowledgement lỗi không rollback mapping;
13. status API trả trạng thái/profile sau mapping;
14. parser đọc `sender.id` và `message.content`.

Token bắt buộc đã được test nguyên vẹn:

```text
-KLhCpZGsUlIyoMF1fT_NdBk3vrUDwYG45-2AYdUF2g
```

Kết quả:

- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: 100/100 passed.
- `git diff --check`: không có whitespace error.

## Security note

`appsettings.json` đã bỏ giá trị AppSecret. Production phải dùng cấu hình database đã mã hóa hoặc environment variable:

```text
Zalo__AppSecret
```

**Zalo AppSecret cũ đã từng tồn tại trong git history và cần rotate thủ công trên Zalo Developer/OA sau deploy.** Không thực hiện rotate tự động trong task này.

## Deployment notes

1. Rotate AppSecret thủ công trên Zalo Developer/OA.
2. Cập nhật secret mới qua màn hình cấu hình được bảo vệ hoặc environment variable `Zalo__AppSecret`.
3. Publish/deploy ứng dụng và recycle IIS App Pool.
4. Xác nhận webhook URL là `/api/zalo/webhook` và OA đang gửi `user_send_text`.
5. Sau khi kiểm tra xong, tắt `stdoutLogEnabled` nếu đang bật tạm để tránh log tăng dung lượng không giới hạn.

## Manual verification sau deploy

1. Mở một Phiếu yêu cầu có khách hàng.
2. Bấm "Quét QR bằng Zalo".
3. Scan QR trên điện thoại.
4. Quan tâm OA nếu chưa quan tâm.
5. Copy mã xác nhận.
6. Gửi mã vào chat OA.
7. Kiểm tra OA gửi acknowledgement.
8. Kiểm tra popup admin chuyển "Đã kết nối Zalo" mà không reload.
9. Kiểm tra `TblZaloRequestLinks` đúng `RequestId`, `CustomerId`, status `ZaloConnected` hoặc `Rated`.
10. Kiểm tra `TblCustomerZaloProfiles` đúng customer/request/Zalo user/OA và timestamp.
11. Kiểm tra `TblKhachHang.ZaloID` bằng Zalo sender ID.
12. Tiếp tục đánh giá công việc.
13. Kiểm tra rating vẫn lưu bình thường.

Các truy vấn kiểm tra:

```sql
SELECT RequestId, CustomerId, Token, UserExternalId, Status, ExpiresAtUtc, UpdatedAtUtc
FROM dbo.TblZaloRequestLinks
WHERE Token = @Token;

SELECT CustomerId, RequestId, ZaloUserId, OaId, ZaloDisplayName,
       ZaloPhoneNumber, IsFollowingOa, ConnectedAtUtc, LastInteractionAtUtc
FROM dbo.TblCustomerZaloProfiles
WHERE RequestId = @RequestId;

SELECT ID, TenKhachHang, ZaloID, ZaloLastUpdate
FROM dbo.TblKhachHang
WHERE ID = @CustomerId;

SELECT TOP (20) EventName, OaId, AppId, IsSignatureValid,
       ProcessedAtUtc, CreatedAtUtc
FROM dbo.TblZaloWebhookEvents
ORDER BY CreatedAtUtc DESC;
```

Khi có lỗi, đối chiếu `TblZaloWebhookEvents` và structured logs theo thứ tự:

1. Zalo có gọi webhook hay không.
2. `EventName` có phải event message phù hợp không.
3. `ZaloUserId` từ `sender.id` có tồn tại không.
4. `HasMessageText` có bằng `true` không.
5. Có log `Zalo request verification matched` hay chỉ `token not found`.
6. Có log `Zalo customer linked successfully` hay log mapping rejected/failed.
7. Có message log `ZaloConnectAcknowledgement` thành công hay lỗi.
