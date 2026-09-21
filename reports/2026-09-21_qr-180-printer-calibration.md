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
