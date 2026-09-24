# Báo cáo sửa Zalo message token và kết quả API

## Root cause

Hai luồng `SendMessageAsync()` và `SendDirectZaloMessageAsync()` có cùng hai lỗi:

1. Gửi access token bằng `Authorization: Bearer ...` thay vì header `access_token` theo contract của endpoint `/v3.0/oa/message/cs`.
2. Chỉ dùng HTTP status để xác định thành công. Zalo có thể trả HTTP 200 cùng payload nghiệp vụ `{"error":-216,"message":"Access token is invalid"}`, nên hệ thống đã ghi `IsSuccess = 1`, `ErrorMessage = NULL` dù tin không được gửi.

## Header trước và sau

Trước:

```text
Authorization: Bearer <ACCESS_TOKEN>
```

Sau:

```text
access_token: <ACCESS_TOKEN>
```

Thay đổi chỉ áp dụng cho transport gửi message. Endpoint lấy profile người dùng vẫn giữ implementation hiện tại vì là endpoint khác và không thuộc phạm vi sửa message lần này.

## Parse kết quả API

Thêm `ZaloTextApiClient` dùng chung cho cả hai đường gửi. Một attempt chỉ thành công khi đồng thời:

- HTTP status thuộc nhóm 2xx;
- response là JSON hợp lệ;
- trường `error` tồn tại và bằng `0`.

Client đọc thêm `data.message_id` để ghi application log phục vụ chẩn đoán, không thay đổi schema database. HTTP lỗi, JSON hỏng, thiếu `error` hoặc `error != 0` đều là thất bại.

Khi ghi `TblZaloMessageLogs`, cả hai luồng dùng kết quả đã parse:

- `IsSuccess = ApiSucceeded`;
- `ErrorMessage = ErrorMessage` khi thất bại;
- `ResponseJson` là response cuối cùng.

Mỗi business message chỉ ghi một record kết quả cuối; attempt đầu bị `-216` chỉ được ghi warning ở application log.

## Refresh và retry

Nếu attempt đầu trả `error = -216` hoặc thông báo tương đương cho biết access token invalid/expired:

1. Ghi warning có `RequestId`, `CustomerId`, `MessageType`, `ErrorCode`; không ghi token.
2. Gọi `ForceRefreshTokenAsync()`.
3. Gọi lại `GetValidAccessTokenAsync()` để đọc token mới đã lưu.
4. Retry gửi đúng một lần.

Nếu retry thành công thì kết quả cuối là success. Nếu retry vẫn lỗi thì kết quả cuối là failure. Nếu refresh ném exception, client trả lỗi yêu cầu admin kết nối lại Zalo OA; luồng tạo Phiếu yêu cầu vẫn giữ semantics lưu thành công và chỉ báo chưa gửi được Zalo.

Review vòng đời token xác nhận:

- refresh endpoint nhận refresh token và App ID, cùng `secret_key` theo implementation hiện tại;
- token mới và refresh token mới được cập nhật vào record hiện tại bằng `UpdateTokenAsync()`;
- `GetValidAccessTokenAsync()` đọc record mới nhất theo OA và nhận giá trị vừa cập nhật;
- không phát hiện lỗi trực tiếp khác liên quan tới tình huống `-216` trong phạm vi task.

## Files changed

- `Services/ZaloTextApiClient.cs`
- `Services/ZaloIntegrationService.cs`
- `Program.cs`
- `Tests/ApptechDashboard.Tests/ZaloTextApiClientTests.cs`
- `Tests/ApptechDashboard.Tests/ZaloMessageIntegrationWiringTests.cs`
- `reports/2026-09-24_fix-zalo-message-token-and-api-result.md`

## Tests bổ sung

Regression tests bao phủ:

1. HTTP 200 + `error = 0` thành công và đọc `message_id`.
2. HTTP 200 + lỗi nghiệp vụ không thành công.
3. `error = -216` gọi refresh và retry bằng token mới.
4. Retry thành công trả final success.
5. Retry vẫn `-216` trả final failure và không retry lần ba.
6. Refresh thất bại trả thông báo cần kết nối lại OA.
7. HTTP 500 luôn thất bại, kể cả body có `error = 0`.
8. HTTP 200 nhưng JSON không hợp lệ thất bại.
9. Thiếu trường `error` thất bại.
10. Header gửi message là `access_token`, không có Bearer Authorization.
11. Cả `SendMessageAsync()` và `SendDirectZaloMessageAsync()` dùng chung client.
12. Cả hai luồng lưu `ApiSucceeded` và `ErrorMessage` đã parse.
13. Test controller hiện có tiếp tục xác nhận lỗi Zalo không rollback Phiếu yêu cầu.

## Build và test

- `dotnet restore apptech-dashboard.sln`: thành công, dependencies đã up-to-date.
- `dotnet build apptech-dashboard.sln -c Release`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release`: thành công, 117/117 test passed.
- `git diff --check`: không có whitespace error; chỉ có cảnh báo LF/CRLF theo quy ước worktree Windows.

## Database và migration

Không thay đổi schema và không cần migration. Không sửa các record false-positive cũ.

## Checklist deploy thủ công

1. Publish ứng dụng và recycle IIS App Pool.
2. Tạo Phiếu yêu cầu cho khách đã kết nối Zalo.
3. Xác nhận khách nhận được tin và UI báo đã gửi.
4. Kiểm tra log `RequestCreatedNotification`: response có `error: 0`, `IsSuccess = 1`, `ErrorMessage = NULL`.
5. Thử bằng access token hết hạn/invalid; xác nhận application log có một warning refresh và request message được retry đúng một lần.
6. Kiểm tra response cuối lỗi có `IsSuccess = 0`, `ErrorMessage` khác null và UI vẫn báo Phiếu yêu cầu đã lưu nhưng chưa gửi Zalo.
7. Gửi token kết nối từ khách và kiểm tra `ZaloConnectAcknowledgement` cũng chỉ success khi response có `error: 0`.
8. Nếu refresh token invalid, kết nối lại OA từ trang quản trị; backend không tự thực hiện OAuth reconnect.

Truy vấn kiểm tra:

```sql
SELECT TOP (20)
       CustomerId,
       BookingId,
       ZaloUserId,
       MessageType,
       RequestJson,
       ResponseJson,
       IsSuccess,
       ErrorMessage,
       CreatedAtUtc
FROM dbo.TblZaloMessageLogs
WHERE MessageType IN (
    N'RequestCreatedNotification',
    N'ZaloConnectAcknowledgement'
)
ORDER BY CreatedAtUtc DESC;
```
