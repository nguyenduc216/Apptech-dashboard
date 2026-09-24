# Báo cáo sửa lỗi Zalo `Invalid appsecret_proof`

## Phạm vi và phiên bản

- Nhánh: `main`.
- HEAD trước sửa: `4d555f5b0325c44899e1ae5eda56604011b6eaa2`.
- Giữ nguyên business flow tạo Phiếu yêu cầu, cập nhật tiến độ, token refresh một lần và cơ chế ghi `TblZaloMessageLogs`.

## Root cause

`ZaloTextApiClient` chỉ gửi header `access_token`. Zalo OA hiện kiểm tra thêm `appsecret_proof` đối với API dùng access token nên API trả:

```text
-242 Invalid appsecret_proof provided in the API argument
```

Ngoài ra, `appsettings.Production.json` chứa placeholder `YOUR_APP_SECRET`. Bộ đọc JSON provider trước đây cho phép placeholder ở provider nạp sau ghi đè AppSecret hợp lệ từ `appsettings.json`, sau đó mới loại placeholder, khiến runtime có thể mất AppSecret dù cấu hình gốc hợp lệ.

## Đối chiếu tài liệu chính thức

- Zalo PHP SDK chính thức tạo proof bằng `hash_hmac('sha256', $accessToken, $appSecret)`.
- Kết quả là chuỗi hex thường theo mặc định của PHP `hash_hmac`.
- SDK gửi `appsecret_proof` trong HTTP header, cùng với cơ chế header access token.
- Zalo liệt kê `-242` là lỗi `Invalid appsecret_proof` và changelog chính thức mô tả kiểm tra App Secret Proof cho API sử dụng access token.

Nguồn:

- https://github.com/zaloplatform/zalo-php-sdk/blob/master/src/Zalo.php
- https://github.com/zaloplatform/zalo-php-sdk/blob/master/src/ZaloRequest.php
- https://github.com/zaloplatform/zalo-php-sdk/blob/master/README.md
- https://docs.zaloplatforms.com/docs/ZBS/bang-ma-loi
- https://developers.zalo.me/changelog/v240409-them-tinh-nang-kiem-tra-app-secret-proof-khi-goi-api-co-su-dung-access-token-7603

## Implementation

`ZaloTextApiClient` hiện tạo proof theo công thức:

```text
lowercase_hex(HMAC-SHA256(key = current AppSecret, data = current access token))
```

Mỗi send attempt lấy một snapshot `ZaloSettingsService.Current`, tạo proof từ AppSecret và access token của chính attempt đó rồi gửi hai header:

```text
access_token: <current access token>
appsecret_proof: <proof tương ứng>
```

Khi API trả `-216`, transport vẫn refresh đúng một lần. Retry lấy token mới và tạo proof mới; proof của token cũ không được tái sử dụng. Lỗi `-242` là failure cuối, không kích hoạt refresh và không tạo vòng lặp.

Cả `RequestCreatedNotification` và `RequestProgressUpdated` tiếp tục đi qua cùng `ZaloTextApiClient`, vì vậy không có hai implementation transport khác nhau.

## Configuration precedence

- AppSecret hợp lệ trong database đã mã hóa vẫn có ưu tiên cao nhất.
- Nếu database không có giá trị hợp lệ, AppSecret hợp lệ từ JSON được dùng.
- Placeholder/giá trị rỗng ở `appsettings.Production.json` không còn ghi đè giá trị hợp lệ của provider JSON trước đó.
- Environment vẫn là fallback theo thứ tự hiện có khi database và JSON không cung cấp giá trị hợp lệ.
- Không ghi secret thật vào `appsettings.Production.json`.

## Logging và bảo mật

Khi nhận `-242`, structured warning chỉ ghi:

- `RequestId`, `CustomerId`, `MessageType`;
- AppSecret có được cấu hình hay không;
- proof có được tạo hay không;
- mã lỗi API.

Log không chứa access token, AppSecret hoặc giá trị proof. Request body cũng không chứa các giá trị này.

## Tests

Các test bao phủ:

1. vector xác định cho HMAC-SHA256 hex thường;
2. cùng token/secret tạo cùng proof;
3. đổi token làm đổi proof;
4. đổi secret làm đổi proof;
5. proof được gửi đúng header chính thức;
6. access token tiếp tục ở header và không dùng Bearer;
7. refresh dùng token B và proof B;
8. proof B khác proof A, không tái sử dụng;
9. `-242` trả failure và không refresh;
10. `error = 0` vẫn là success;
11. structured log không lộ token/secret/proof;
12. hai business message path dùng chung transport;
13. placeholder Production không ghi đè AppSecret hợp lệ.

## Database và triển khai

- Không thay đổi schema và không cần migration.
- Cần publish/deploy application và recycle IIS App Pool.
- Xác nhận production có AppSecret đang hoạt động trong database đã mã hóa hoặc nguồn cấu hình hợp lệ.
- Sau deploy, gửi thử một `RequestCreatedNotification` và một `RequestProgressUpdated`; kiểm tra `TblZaloMessageLogs` có `ResponseJson.error = 0`.

## Build và test

- `dotnet restore apptech-dashboard.sln`: thành công, dependencies đã up-to-date.
- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 143/143 test passed.
- `git diff --check`: không có whitespace error; cảnh báo LF/CRLF (nếu có) là quy ước worktree Windows.
