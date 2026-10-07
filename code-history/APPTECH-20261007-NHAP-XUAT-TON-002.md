# APPTECH-20261007-NHAP-XUAT-TON-002

- FEATURE_ID: `APPTECH-REPORT-NHAP-XUAT-TON`
- CHANGE_ID: `APPTECH-20261007-NHAP-XUAT-TON-002`
- Branch: `feat/nhap-xuat-ton-report`

## Yêu cầu và scope

Hoàn thiện continuation của báo cáo Nhập – Xuất – Tồn: cố định 6 cột và grain Hàng hóa × Kho, giữ đúng công thức lịch sử, filter, Excel, drill-down và permission hiện hữu. Không sửa migration hay nghiệp vụ nhập/xuất kho.

## Architecture reused

Tiếp tục dùng `NhapXuatTonReportService`, `ReportController`, view hiện hữu, `SimpleExcelService`, route `/bao-cao/nhap-xuat-ton`, báo cáo chi tiết `/bao-cao/nhap-xuat-kho` và `WarehouseInOutReportViewPermissionCode`; không tạo pipeline song song.

## Functions/sections sửa

- `NhapXuatTonReportModels`: bỏ lựa chọn GroupBy khỏi contract.
- `NhapXuatTonReportService.GetReportAsync/LoadItemsAsync/BuildSql`: cố định grain và filter ngày/kho/hàng hóa.
- `ReportController.NhapXuatTon/ExportNhapXuatTon`: web và endpoint Excel dùng cùng service.
- `SimpleExcelService.BuildNhapXuatTonReport`: workbook đúng 6 cột.
- `Views/Report/NhapXuatTon.cshtml`: filter gọn, bảng 6 cột, Excel và drill-down tab mới.
- `site.css`: căn phải bốn cột số.

## SQL/query

Nhập được cộng trực tiếp từ PNCT và kho trên phiếu nhập, không join vật tư; xuất xác định kho từ `IDChiTietHangHoa`. Chỉ phiếu `da-nhap`/`xuat-kho`; dùng `[FromDate, ToDateExclusive)` và group `IDHangHoa + IDKho`.

## Verification

- Unit tests: công thức tồn cuối, SQL grain/status/date/type-2 safety, Excel đúng 6 header và giá trị.
- Source review: drill-down truyền ngày + hàng hóa + kho, `target=_blank`; report chi tiết không đổi.
- Database smoke: đã thử kết nối read-only bằng cấu hình hiện có nhưng môi trường không mở được kết nối (`MethodInvocationException`); không sửa cấu hình hay migration.

## Build / test / publish / Git

- Build: `dotnet build apptech-dashboard.sln --no-restore` PASS, 0 warning, 0 error.
- Test: `dotnet test Tests/ApptechDashboard.Tests/ApptechDashboard.Tests.csproj --no-restore` PASS 164/164.
- Publish: `dotnet publish ApptechDashboard.csproj -c Release --no-restore -o artifacts/publish/ApptechDashboard` PASS; DLL và deps đã xác minh tồn tại; output được `.gitignore` loại khỏi commit.
- Git: commit SHA và push được ghi trong kết quả bàn giao vì không thể tự tham chiếu SHA hiện tại bên trong chính commit.

## Deployment note

Deploy artifact ASP.NET Core theo convention IIS của repository. Không có migration database cho change này và không commit publish output.
