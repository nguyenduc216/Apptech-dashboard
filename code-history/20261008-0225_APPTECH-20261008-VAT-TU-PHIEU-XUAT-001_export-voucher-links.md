# APPTECH-20261008-VAT-TU-PHIEU-XUAT-001

- FEATURE_ID: `APPTECH-WAREHOUSE-MATERIAL`
- CHANGE_ID: `APPTECH-20261008-VAT-TU-PHIEU-XUAT-001`
- Branch: `main`

## Requirement

Cột Phiếu xuất của danh sách vật tư hiển thị toàn bộ phiếu xuất hoàn tất chứa vật tư, mỗi mã là một link riêng mở tab mới bằng route hiện hữu.

## Architecture reused

Tiếp tục dùng `VatTuController`, `IVatTuService`/`VatTuService`, model và view vật tư hiện hữu; reuse `XuatKhoController.Index?editId=...` cùng authorization hiện hành. `GetExportHistoryAsync` cho popup được giữ nguyên.

## Batch strategy and SQL

Query paging vật tư và `TotalCount` không đổi. Sau khi lấy page, một batch query parameterized nhận toàn bộ ID vật tư, join chi tiết với header phiếu xuất, lọc `TrangThaiPhieu = N'xuat-kho'`, dùng `SELECT DISTINCT`, và sắp xếp theo ngày/ID phiếu giảm dần. Không có DB query trong vòng lặp map kết quả.

## Index review and migration

Source migration/index hiện hữu không có index phù hợp trên `TblPhieuXuatKhoChiTiet(IDChiTietHangHoa, IDPhieuXuatKho)`. Đã thêm migration index-only idempotent `App_Data/Migrations/20261008_add_vat_tu_export_lookup_index.sql`.

Migration executed: **NO**.

## Files and verification

- Models/service/controller/view/CSS vật tư.
- Focused tests bảo vệ status, distinct, parameterization, batch integration, link, paging/popup contracts và migration safety.
- Feature history và change history.
- Build: `dotnet build apptech-dashboard.sln --no-restore` PASS, 0 warning, 0 error.
- Tests: `dotnet test Tests/ApptechDashboard.Tests/ApptechDashboard.Tests.csproj --no-restore` PASS, 185/185.
- Publish: Release PASS tại `artifacts/publish/ApptechDashboard`; DLL, deps, runtimeconfig và `wwwroot` đã được xác minh.
- Commit/push/SHA: SHA thực tế được ghi trong kết quả bàn giao vì commit không thể tự tham chiếu SHA của chính nó.
