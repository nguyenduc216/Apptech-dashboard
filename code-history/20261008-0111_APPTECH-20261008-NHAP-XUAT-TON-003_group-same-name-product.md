# APPTECH-20261008-NHAP-XUAT-TON-003 — Group same-name product

- FEATURE_ID: `APPTECH-REPORT-NHAP-XUAT-TON`
- CHANGE_ID: `APPTECH-20261008-NHAP-XUAT-TON-003`
- Branch: `main`
- Baseline: `1ca910fbfbde3fe1a6b1e4a11e2eb43814351864`

## Root cause

Final SQL aggregation dùng `IDHangHoa + IDKho`, nên hai master product ID khác nhau có cùng tên nghiệp vụ và cùng kho xuất hiện thành hai dòng thay vì một dòng tổng hợp.

## Grain

- Old grain: `IDHangHoa + IDKho`.
- New grain: `Normalized TenHangHoa + IDKho`.
- `TenHangHoa` được `LTRIM/RTRIM` và dùng collation `Latin1_General_100_CI_AI`; không sửa, merge hoặc xóa master data.

## SQL changes

- Giữ nguyên nguồn movement nhập/xuất và status hoàn tất.
- Nhánh nhập vẫn cộng trực tiếp `PNCT.SoLuongNhap`, không join PNCT với nhiều vật tư.
- Thêm `FilteredMovements`: resolve tên/mã theo source `IDHangHoa`, áp dụng filter, rồi final aggregation mới group tên chuẩn hóa + `IDKho`.
- Tồn đầu, nhập, xuất được SUM qua mọi product ID cùng tên/cùng kho; tồn cuối tiếp tục là tồn đầu + nhập - xuất.

## Filter handling

Filter tổng hợp theo tên/mã chạy trước khi mất source ID. Vì vậy filter mã `A01` chỉ lấy movement `A01`; filter tên lấy mọi ID khớp tên rồi tổng hợp.

## Drill-down handling

Row tổng hợp tiếp tục truyền `FromDate`, `ToDate`, `TenHangHoa`, `KhoId` và mở tab mới. Contract báo cáo chi tiết được mở rộng bằng optional `ExactHangHoa=true` cho drill-down; tìm kiếm thông thường vẫn dùng LIKE tên/mã.

## Excel

Không aggregate lần hai. Excel dùng trực tiếp `NhapXuatTonReportViewModel.Items`, nên cùng tên/cùng kho chỉ xuất một row giống UI.

## Tests

Bổ sung regression coverage cho: hai ID cùng tên/cùng kho cộng 5 + 6 = 11; khác kho tách dòng; cộng tồn đầu/nhập/xuất/tồn cuối; filter mã cách ly source ID; Excel một row; drill-down đủ filter và exact-name. Kết quả build/test/publish thực tế được cập nhật trước commit.

## DB precheck

UNAVAILABLE. Đã thử truy vấn read-only ba tên `BÁO ĐỘNG`, `BỘ ĐIỀU KHIỂN`, `BỘ CHUYỂN ĐỔI ĐIỆN QUANG`; SQL Server từ chối login `Dev_User` vào database `AppTech_Portal`. Không thay đổi cấu hình hoặc dữ liệu.

## Build / test / publish

- Build: PASS — `dotnet build apptech-dashboard.sln --no-restore`, 0 warning, 0 error.
- Tests: PASS — `dotnet test Tests/ApptechDashboard.Tests/ApptechDashboard.Tests.csproj --no-build --no-restore`, 169/169.
- Publish: PASS — Release output tại `artifacts/publish/ApptechDashboard`; DLL và deps tồn tại, output được ignore khỏi Git.

## Commit / push

- Commit SHA và push status được ghi trong kết quả bàn giao vì commit không thể tự tham chiếu SHA của chính nó.
