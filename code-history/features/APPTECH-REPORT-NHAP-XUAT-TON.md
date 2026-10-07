# APPTECH-REPORT-NHAP-XUAT-TON

## Mục đích

Báo cáo tổng hợp Nhập – Xuất – Tồn theo kỳ, mỗi dòng là một cặp Hàng hóa × Kho.

## Kiến trúc và owner hiện tại

- Model/filter/result: `Models/NhapXuatTonReportModels.cs`.
- SQL và orchestration: `Services/NhapXuatTonReportService.cs` (`INhapXuatTonReportService`), độc lập với service báo cáo chi tiết.
- HTTP, permission và Excel endpoint: `Controllers/ReportController.cs`.
- UI: `Views/Report/NhapXuatTon.cshtml`.
- Excel: `ISimpleExcelService.BuildNhapXuatTonReport` / `SimpleExcelService`.
- DI và route: `Program.cs`; route `/bao-cao/nhap-xuat-ton`.
- Lookup kho dùng owner trung lập `IKhoService/KhoService`; cả hai report không phụ thuộc service nghiệp vụ của nhau.
- Drill-down chỉ là navigation sang `/bao-cao/nhap-xuat-kho`, không phải dependency service.

## Nguồn dữ liệu và invariant

- Nhập: `TblPhieuNhapKho` + `TblPhieuNhapKhoChiTiet`, chỉ trạng thái `da-nhap`, số lượng `SoLuongNhap`, kho từ phiếu nhập.
- Xuất: `TblPhieuXuatKho` + `TblPhieuXuatKhoChiTiet` + `TblChiTietHangHoa`, chỉ trạng thái `xuat-kho`, số lượng `SoLuongXuat`, kho từ vật tư thực tế.
- Business grain hiện tại: `Normalized TenHangHoa + IDKho`, trong đó tên được trim và so sánh theo collation không phân biệt hoa/thường.
- Nhiều `IDHangHoa` có cùng tên chuẩn hóa trong cùng kho được cộng thành một dòng; khác `IDKho` vẫn tách dòng.
- Filter tên/mã được áp dụng khi movement vẫn còn `IDHangHoa`, trước aggregation, để tìm theo một mã không kéo movement của ID khác chỉ vì trùng tên.
- Tồn đầu = nhập hoàn tất trước `FromDate` - xuất hoàn tất trước `FromDate`.
- Tồn cuối = tồn đầu + nhập trong kỳ - xuất trong kỳ.
- `ToDate` bao trọn ngày bằng điều kiện `< ToDate + 1 ngày`.
- Không dùng `TblChiTietHangHoa.SoLuongTon` để suy ngược lịch sử.
- Không join PNCT với các dòng `TblChiTietHangHoa` khi cộng nhập, tránh double-count hàng loại 2.

## Tích hợp

- Web và Excel luôn có đúng 6 cột: Tên hàng hóa, Kho, Tồn đầu, Nhập, Xuất, Tồn cuối.
- Drill-down mở tab mới, truyền `FromDate`, `ToDate`, tên hàng hóa và `KhoId` khi user có permission báo cáo chi tiết; nếu không có quyền, navigation được ẩn nhưng NXT vẫn hoạt động.
- Drill-down dùng optional exact-name trên báo cáo chi tiết; filter tìm kiếm thông thường vẫn giữ LIKE tên/mã.
- Permission NXT riêng: `Report_NhapXuatTon_View`; web và Excel NXT dùng quyền này.
- Menu NXT là item DB riêng `Report_NhapXuatTon`; report chi tiết giữ item/quyền `Report_NhapXuatKho_View`.
- Migration permission/menu: `App_Data/Migrations/20261008_add_nhap_xuat_ton_report_permission.sql` (manual, không auto-run); verification: `sql/20261008_verify_nhap_xuat_ton_report_permission.sql`.

## Registry CHANGE_ID

- `APPTECH-20261007-NHAP-XUAT-TON-001`: tạo pipeline báo cáo tổng hợp ban đầu.
- `APPTECH-20261007-NHAP-XUAT-TON-002`: cố định grain/layout 6 cột, thêm Excel, drill-down và verification tập trung.
- `APPTECH-20261008-NHAP-XUAT-TON-003`: đổi reporting grain sang tên hàng hóa chuẩn hóa + kho vì nhiều master product ID cùng tên phải hiển thị thành một dòng tổng hợp trong cùng kho.
- `APPTECH-20261008-NHAP-XUAT-TON-004`: tách dependency report-to-report, permission, menu và authorization web/Excel của NXT khỏi báo cáo chi tiết.
