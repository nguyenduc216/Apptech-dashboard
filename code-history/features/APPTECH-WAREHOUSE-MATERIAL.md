# APPTECH-WAREHOUSE-MATERIAL

## Purpose

Quản lý danh sách và chi tiết vật tư, đồng thời thể hiện các quan hệ chứng từ nhập/xuất mà không thay đổi nghiệp vụ kho.

## Owners

- `VatTuController`
- `VatTuService` và `IVatTuService`
- Các model `VatTu*`
- `Views/VatTu/Index.cshtml`

## Invariants and integration

- Quan hệ phiếu nhập/xuất được đọc từ các bảng chi tiết chứng từ hiện hữu.
- Không lưu danh sách mã phiếu xuất dưới dạng chuỗi denormalized trong dữ liệu vật tư.
- Danh sách vật tư giữ nguyên grain, paging và `TotalCount`; quan hệ phiếu xuất được nạp bằng một batch query cho các ID của page hiện tại.
- Chỉ phiếu có trạng thái `xuat-kho` được hiển thị và mỗi phiếu được loại trùng bằng `DISTINCT`.
- Link phiếu xuất reuse `XuatKhoController.Index?editId=<PhieuXuatId>` và authorization hiện hữu.
- Index hỗ trợ tùy chọn được cung cấp tại `App_Data/Migrations/20261008_add_vat_tu_export_lookup_index.sql`; migration không tự động thực thi.

## Change registry

- `APPTECH-20261008-VAT-TU-PHIEU-XUAT-001`: hiển thị toàn bộ phiếu xuất hoàn tất theo từng vật tư bằng batch lookup, link độc lập mở tab mới.
