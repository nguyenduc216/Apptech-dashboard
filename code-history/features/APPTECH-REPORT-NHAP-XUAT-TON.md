# APPTECH-REPORT-NHAP-XUAT-TON

## Mục đích

Báo cáo tổng hợp Nhập – Xuất – Tồn theo kỳ, mỗi dòng là một cặp Hàng hóa × Kho.

## Kiến trúc và owner hiện tại

- Model/filter/result: `Models/NhapXuatTonReportModels.cs`.
- SQL và orchestration: `Services/NhapXuatTonReportService.cs` (`INhapXuatTonReportService`).
- HTTP, permission và Excel endpoint: `Controllers/ReportController.cs`.
- UI: `Views/Report/NhapXuatTon.cshtml`.
- Excel: `ISimpleExcelService.BuildNhapXuatTonReport` / `SimpleExcelService`.
- DI và route: `Program.cs`; route `/bao-cao/nhap-xuat-ton`.
- Drill-down reuse báo cáo `/bao-cao/nhap-xuat-kho` qua `NhapXuatKhoReportService`.

## Nguồn dữ liệu và invariant

- Nhập: `TblPhieuNhapKho` + `TblPhieuNhapKhoChiTiet`, chỉ trạng thái `da-nhap`, số lượng `SoLuongNhap`, kho từ phiếu nhập.
- Xuất: `TblPhieuXuatKho` + `TblPhieuXuatKhoChiTiet` + `TblChiTietHangHoa`, chỉ trạng thái `xuat-kho`, số lượng `SoLuongXuat`, kho từ vật tư thực tế.
- Business grain cố định: `IDHangHoa + IDKho`.
- Tồn đầu = nhập hoàn tất trước `FromDate` - xuất hoàn tất trước `FromDate`.
- Tồn cuối = tồn đầu + nhập trong kỳ - xuất trong kỳ.
- `ToDate` bao trọn ngày bằng điều kiện `< ToDate + 1 ngày`.
- Không dùng `TblChiTietHangHoa.SoLuongTon` để suy ngược lịch sử.
- Không join PNCT với các dòng `TblChiTietHangHoa` khi cộng nhập, tránh double-count hàng loại 2.

## Tích hợp

- Web và Excel luôn có đúng 6 cột: Tên hàng hóa, Kho, Tồn đầu, Nhập, Xuất, Tồn cuối.
- Drill-down mở tab mới, truyền `FromDate`, `ToDate`, tên hàng hóa và `KhoId` vào báo cáo chi tiết hiện hữu.
- Permission reuse `WarehouseInOutReportViewPermissionCode`; không thêm permission hay nhóm menu mới.

## Registry CHANGE_ID

- `APPTECH-20261007-NHAP-XUAT-TON-001`: tạo pipeline báo cáo tổng hợp ban đầu.
- `APPTECH-20261007-NHAP-XUAT-TON-002`: cố định grain/layout 6 cột, thêm Excel, drill-down và verification tập trung.
