# APPTECH-20261007-NHAP-XUAT-TON-001

## Yêu cầu
Bổ sung báo cáo Nhập - Xuất - Tồn cho Apptech Dashboard.

## Phạm vi
- Không thay đổi logic báo cáo Nhập xuất kho chi tiết hiện có.
- Thêm báo cáo tổng hợp riêng tại `/bao-cao/nhap-xuat-ton`.
- Dùng chung quyền xem báo cáo kho hiện tại.
- Bộ lọc:
  - Từ ngày
  - Đến ngày
  - Tên / mã hàng hóa
  - Kho
  - Nhóm theo: Hàng hóa, Kho, Hàng hóa + Kho
- Cột báo cáo:
  - Tồn đầu
  - Nhập trong kỳ
  - Xuất trong kỳ
  - Tồn cuối

## Công thức
- Tồn đầu = tổng phiếu nhập đã nhập trước Từ ngày - tổng phiếu xuất đã xuất trước Từ ngày.
- Nhập trong kỳ = tổng phiếu nhập trạng thái `da-nhap` trong [Từ ngày, Đến ngày].
- Xuất trong kỳ = tổng phiếu xuất trạng thái `xuat-kho` trong [Từ ngày, Đến ngày].
- Tồn cuối = Tồn đầu + Nhập trong kỳ - Xuất trong kỳ.
- Không dùng `TblChiTietHangHoa.SoLuongTon` để suy ngược lịch sử.
- Không join chi tiết vật tư vào dòng nhập để tránh nhân đôi số lượng với hàng hóa loại 2.

## File thay đổi
- `Models/NhapXuatTonReportModels.cs`
- `Services/NhapXuatTonReportService.cs`
- `Controllers/ReportController.cs`
- `Program.cs`
- `Views/Report/NhapXuatTon.cshtml`
- `Views/Report/NhapXuatKho.cshtml`
- `code-history/APPTECH-20261007-NHAP-XUAT-TON-001.md`

## Build / Test
- Chưa chạy build/test runtime trong phiên này vì connector GitHub không cung cấp môi trường build.
- Đã rà source-level:
  - route mới không đè route cũ;
  - service được đăng ký DI;
  - controller dùng cùng permission warehouse report;
  - query chỉ tính phiếu hoàn tất;
  - ToDate dùng exclusive next-day;
  - 3 group mode là danh sách cố định, không đưa input người dùng trực tiếp vào SQL.

## Branch
`feat/nhap-xuat-ton-report`

## Base
`e35f14aaf0664a09aef6d68b12c52f51d72c3f15`

## CHANGE_ID
`APPTECH-20261007-NHAP-XUAT-TON-001`
