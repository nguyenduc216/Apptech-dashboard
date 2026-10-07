# APPTECH-20261008-NHAP-XUAT-TON-005 — Material detail link

- FEATURE_ID: `APPTECH-REPORT-NHAP-XUAT-TON`
- CHANGE_ID: `APPTECH-20261008-NHAP-XUAT-TON-005`
- Branch: `main`
- Baseline: `c997dbefa6d2d775492242341b4410b3106d3416`

## Scope

Bổ sung navigation tại cột Vật tư (chi tiết) của báo cáo `/bao-cao/nhap-xuat-kho`. Không đổi quantity, report grain, nghiệp vụ nhập/xuất/tồn, Excel hoặc database schema.

## Existing material route reused

Reuse `VatTuController.Index` qua route `/vat-tu` và query `editId=<TblChiTietHangHoa.ID>`, đúng contract mà view quản lý vật tư hiện hữu đang dùng để mở popup vật tư.

## Model / query / view

- `NhapXuatKhoReportItem.ChiTietHangHoaId` là nullable material key.
- Query nhập và xuất cùng trả `ct.ID AS ChiTietHangHoaId`; mapper đọc nullable ID.
- View dùng tag helpers `asp-controller="VatTu"`, `asp-action="Index"`, `asp-route-editId`; mở `target="_blank"` với `rel="noopener"`.
- Nếu ID không hợp lệ/null, view giữ text `TenChiTiet` và không sinh anchor.

## Permission behavior

Không tạo/bypass permission. Navigation đi vào `VatTuController` hiện hữu có `[Authorize]`, giữ nguyên authorization convention của trang vật tư.

## Tests

Coverage xác nhận cả hai query trả ID, model/mapper map nullable ID, view có link/tab mới/fallback text, và route/action/authorization vật tư hiện hữu được reuse.

## Build / publish

- Build: PASS — 0 warning, 0 error.
- Tests: PASS — 179/179.
- Publish: PASS — `artifacts/publish/ApptechDashboard`; DLL, deps, runtimeconfig và static content tồn tại; artifact bị ignore khỏi Git.
- Migration required: NO.
- Migration executed: NO.

## Commit / push

Commit SHA, origin/main SHA và push status được ghi trong kết quả bàn giao vì commit không thể tự tham chiếu SHA của chính nó.
