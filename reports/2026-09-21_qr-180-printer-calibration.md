# Báo cáo nâng cấp In180 – Printer Calibration

## A. Current-state analysis

Luồng cũ nhận số trang trong `QrCodeBatchRequestModel`, tạo request cố định `180 × số trang`, cấp phát sequence qua `GenerateBatchAsync`, rồi gọi `Generate180LabelSheetPdfDocument(values)`. PDF dùng A4 210 × 297 mm, 10 cột × 18 dòng, lề trái 6 mm, lề trên 14 mm, bước 20 × 15 mm và QR 14,5 mm. `Build180LabelRequest` từng đặt QR 17 × 17 mm nhưng các giá trị đó không được PDF In180 sử dụng.

## B. Root cause

Bốn trường khoảng cách và kích thước QR trên UI thuộc luồng tạo QR thông thường. Generator In180 là template riêng và hard-code toàn bộ geometry, nên thay đổi các trường đó không tác động tới In180. Sai số driver, printable area và kéo giấy của từng máy vì vậy không có điểm hiệu chỉnh riêng.

## C. Changes

- `Models/QrCodeViewModels.cs`: model riêng cho settings, request và profile In180; validation decimal, range, chồng lấn và biên A4.
- `Services/QrCodeBatchService.cs`: nhận calibration, tính geometry tập trung, tạo PDF test crosshair vector.
- `Services/Qr180PrinterProfileService.cs`: đọc/lưu profile SQL Server và tạo default profile idempotent.
- `Controllers/QrCodeController.cs`: tách Print180, PrintCalibration180 và Save180Profile; validate trước khi cấp sequence.
- `Views/QrCode/Index.cshtml`, `wwwroot/css/site.css`: panel In180 riêng, tooltip, profile, hướng dẫn Actual size và responsive.
- `App_Data/Migrations/20260921_add_qr180_printer_profiles.sql`: schema/profile mặc định idempotent.
- `Tests/ApptechDashboard.Tests/*`, `apptech-dashboard.sln`, `ApptechDashboard.csproj`: test project và cấu hình build.
- `Program.cs`: đăng ký profile service.

## D. Calibration model

| Thông số | Mặc định | Giới hạn |
|---|---:|---:|
| Offset X | 0,00 mm | -10 đến +10 mm |
| Offset Y | 0,00 mm | -10 đến +10 mm |
| Pitch X | 20,00 mm | 18 đến 22 mm |
| Pitch Y | 15,00 mm | 13 đến 17 mm |
| QR Size | 14,50 mm | 10 đến 18 mm |

Offset Y âm dịch lên, dương dịch xuống. Server từ chối cấu hình làm QR chồng nhau hoặc vượt khổ A4.

## E. Formula

Với `column = 0..9`, `row = 0..17`:

```text
Left = 6 + OffsetX + column × PitchX + (PitchX - QrSize) / 2
Top  = 14 + OffsetY + row × PitchY + (PitchY - QrSize) / 2
```

`Top` được đổi sang tọa độ bottom-left khi ghi PDF. Default cho vị trí đầu là Left 8,75 mm / Top 14,25 mm và vị trí cuối là Left 188,75 mm / Top 269,25 mm, tương đương layout cũ.

## F. Calibration test print

`PrintCalibration180` tạo một trang A4 10 × 18 với 180 crosshair vector tại tâm tem. Luồng này gọi trực tiếp PDF calibration, không gọi `GenerateBatchAsync`, không reserve sequence và không ghi `qr-sequence.txt`. Test tự động xác nhận file sequence không được tạo.

## G. Printer profiles

UI cho phép chọn profile, tạo profile mới, sửa/lưu profile không mặc định và dùng các giá trị đang hiển thị cho cả In thử lẫn In 180 QR. Profile `Mặc định` luôn là fallback; không bị xóa hoặc ghi đè làm mất baseline.

## H. Database

- Script: `App_Data/Migrations/20260921_add_qr180_printer_profiles.sql`.
- Bảng: `dbo.TblQr180PrinterProfile`.
- Trường: `Id`, `ProfileName`, `OffsetX`, `OffsetY`, `PitchX`, `PitchY`, `QrSize`, `IsDefault`, `CreatedAt`, `UpdatedAt`.
- Default profile: `Mặc định`, 0 / 0 / 20 / 15 / 14,5.
- Service tự bảo đảm schema/default bằng SQL idempotent và fallback in-memory về default nếu DB tạm thời không truy cập được.

## I. Regression

- Không thay đổi Prefix, Base36, CodeLength, ECC, uniqueness hoặc logic cấp sequence.
- Luồng tạo QR, preview và Export PDF thông thường giữ nguyên.
- Luồng đánh mã vật tư/quét QR không thay đổi.
- In180 vẫn A4 10 × 18 và QR vẫn được vẽ vector theo từng module.
- Không sửa các module check-in, yêu cầu, kho, chấm công hoặc mua hàng.

## J. Build/test

- `dotnet restore apptech-dashboard.sln`: thành công.
- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-restore`: thành công, 10/10 test passed.
- `git diff --check`: thành công; chỉ có cảnh báo quy ước LF/CRLF của worktree Windows, không có whitespace error.

## K. Commit

- Feature commit SHA: `4b585b1011acb8dc404fa07b6d00a7780ed87bf5`.
- Commit message: `feat(qr): add printer calibration for 180-label printing`.
- Branch: `main`.
- Push result: thành công lên `origin/main`.

## L. Deploy

- Trạng thái: **Not deployed**.
- Target: chưa xác nhận. Repo chỉ mô tả cách publish ra `publish/iis`; không cung cấp IIS server/path triển khai thực tế.
- Kết quả: build hoàn tất nhưng chưa deploy vì không có deployment target được xác nhận.

---

# Follow-up: hoàn thiện workflow calibration

## A. Issues fixed

- Sau khi lưu, controller dùng `ProfileId` service trả về để reload và chọn đúng profile vừa lưu.
- Profile `Mặc định` có trạng thái UI riêng, nút đổi thành **Lưu thành cấu hình mới** và server không cho ghi đè/tạo custom profile trùng tên dành riêng.
- PDF In thử hiển thị cả crosshair tâm và bounding box đúng kích thước QR.
- Test 2 trang được sửa để kiểm tra 360 mapping thực tế, 180 item mỗi trang và item đầu trang 2 reset về `indexWithinPage = 0`.

## B. Files changed

- `ApptechDashboard.csproj`
- `Controllers/QrCodeController.cs`
- `Models/QrCodeViewModels.cs`
- `Services/Qr180PrinterProfileService.cs`
- `Services/QrCodeBatchService.cs`
- `Views/QrCode/Index.cshtml`
- `wwwroot/css/site.css`
- `Tests/ApptechDashboard.Tests/ApptechDashboard.Tests.csproj`
- `Tests/ApptechDashboard.Tests/Qr180PrintingTests.cs`

## C. Active profile flow

```text
SaveAsync
→ trả ProfileId của record INSERT/UPDATE
→ redirect Index?printerProfileId=...
→ server resolve profile theo Id
→ set Print180.ProfileId và toàn bộ calibration values
→ Razor chọn đúng option và render đúng input values
```

Profile custom khi update vẫn giữ cùng Id nên không tạo duplicate. Profile mới được redirect về Id mới sinh.

## D. Default profile protection

`Mặc định` không bị overwrite. Service kiểm tra record đích: chỉ profile tồn tại và `IsDefault = false` mới được UPDATE; default luôn chuyển sang INSERT custom. Cả model validation và service đều từ chối tên custom `Mặc định` sau khi trim và so sánh không phân biệt hoa thường. UI không gửi Id default khi lưu và yêu cầu người dùng đặt tên mới.

## E. Calibration PDF

Mỗi vị trí dùng chính `Qr180LayoutPosition` của PDF thật để vẽ:

- bounding box vector 0,25 pt tại `Left`, `Top`, rộng/cao bằng `QrSize`;
- crosshair vector tại tâm QR;
- không tô nền và dùng nét xám nhẹ để thuận tiện đặt chồng với decal.

Thay đổi QR Size làm thay đổi trực tiếp bounding box. In thử vẫn không gọi batch generation và không ghi sequence.

## F. Tests

Tổng cộng 15 test pass. Các coverage mới/sửa gồm:

- 360 values tạo 2 trang, mỗi trang 180 placement;
- item thứ 181 thuộc page 2, `indexWithinPage = 0`, đúng value và geometry đầu trang;
- calibration PDF có 180 bounding box và kích thước box thay đổi từ QR 10 mm sang 18 mm;
- calibration test không tạo `qr-sequence.txt`;
- quyết định INSERT profile mới, UPDATE custom, không UPDATE default;
- cấm tên custom `Mặc định`;
- resolve active profile và calibration values theo requested Id;
- save controller redirect kèm đúng `printerProfileId` vừa lưu.

## G. Build

- `dotnet restore apptech-dashboard.sln`: thành công.
- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 15/15 test passed.
- `git diff --check`: không có whitespace error; chỉ có thông báo LF/CRLF của Git trên Windows.

## H. Database

Không thay schema và không tạo migration mới. Migration hiện tại phải được chạy trước deploy:

`App_Data/Migrations/20260921_add_qr180_printer_profiles.sql`

## I. Commit

- SHA: `08c922285031a603ee169ee1a52aafebc4d4b264`
- Message: `fix(qr): complete 180-label calibration workflow`
- Branch: `main`
- Push result: thành công lên `origin/main`.
