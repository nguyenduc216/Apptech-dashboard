# APPTECH-20261008-NHAP-XUAT-TON-004 — Decouple report permission

- FEATURE_ID: `APPTECH-REPORT-NHAP-XUAT-TON`
- CHANGE_ID: `APPTECH-20261008-NHAP-XUAT-TON-004`
- Branch: `main`
- Baseline: `9437f6c86a043fa29a4849ceaa89cf3f769c71a5`

## Root cause

`NhapXuatTonReportService` gọi `INhapXuatKhoReportService` để lấy lookup kho; controller dùng chung `Report_NhapXuatKho_View`; database chưa có item menu và permission NXT độc lập. Vì vậy user không thể được cấp NXT độc lập với report chi tiết.

## Architecture before / after

- Before: NXT report → detail report service → lookup kho; hai web/Excel report dùng chung permission chi tiết.
- After: NXT report → `IKhoService`; detail report → `IKhoService`; không report service nào phụ thuộc report service kia.
- Dependency removed: `INhapXuatKhoReportService` khỏi constructor `NhapXuatTonReportService`.

## Permission, menu và authorization

- Detail giữ `Report_NhapXuatKho_View` và DB function `Report_NhapXuatKho`.
- NXT dùng `Report_NhapXuatTon_View` và DB function/menu `Report_NhapXuatTon`.
- `NhapXuatTon` và `ExportNhapXuatTon` check quyền mới; detail web/Excel giữ quyền cũ.
- Controller chỉ expose drill-down flag khi user có quyền detail; view ẩn link và render plain text nếu thiếu quyền.

## Excel

Excel NXT tiếp tục chỉ dùng `NhapXuatTonReportService` và `BuildNhapXuatTonReport`, với authorization riêng `Report_NhapXuatTon_View`.

## Migration

- Migration: `App_Data/Migrations/20261008_add_nhap_xuat_ton_report_permission.sql`.
- Verification SQL: `sql/20261008_verify_nhap_xuat_ton_report_permission.sql`.
- Migration executed: **NO**.
- Script idempotent, không grant role/user, không sửa mapping hoặc permission không liên quan.

## Tests

Coverage xác nhận constructor dependency độc lập, permission web/Excel riêng, navigation có điều kiện, menu/permission migration riêng, migration idempotency và verification SQL read-only.

## Build / test / publish

- Build: PASS — 0 warning, 0 error.
- Tests: PASS — 174/174.
- Publish: PASS — `artifacts/publish/ApptechDashboard`; DLL, deps, runtimeconfig và static content đã xác minh tồn tại; artifact bị ignore khỏi Git.

## Git

- Commit SHA và push status được ghi trong kết quả bàn giao vì commit không thể tự tham chiếu chính nó.

## Deployment steps

1. Deploy code theo convention IIS của repository.
2. Chạy thủ công migration `App_Data/Migrations/20261008_add_nhap_xuat_ton_report_permission.sql`.
3. Chạy read-only verification `sql/20261008_verify_nhap_xuat_ton_report_permission.sql`.
4. Gán `Report_NhapXuatTon_View` cho role/user cần sử dụng; migration không tự grant.
