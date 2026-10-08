# APPTECH-20261008-VAT-TU-PHIEU-XUAT-002

- FEATURE_ID: `APPTECH-WAREHOUSE-MATERIAL`
- CHANGE_ID: `APPTECH-20261008-VAT-TU-PHIEU-XUAT-002`
- Branch: `main`

## Root cause and fix

`Views/VatTu/Index.cshtml` đã render cột Phiếu xuất ở vị trí thứ 10, nhưng CSS cục bộ ẩn toàn bộ `th` và `td` thứ 10. Rule ẩn này đã được xóa và `tableColumnCount` được đổi từ 10 thành 11 để các hàng empty/group dùng đúng colspan.

## Scope preserved

- Giữ nguyên batch lookup phiếu xuất, paging và popup history.
- Giữ nguyên link `XuatKhoController.Index?editId=<PhieuXuatId>`, mở tab mới với `target="_blank"` và `rel="noopener"`.
- Không sửa hoặc chạy SQL/migration/index.

## Regression coverage

Source test xác nhận bảng dùng 11 cột, thứ tự header không đổi, cột thứ 10 không còn bị CSS ẩn, colspan dùng `tableColumnCount`, và contract mở link phiếu xuất ở tab mới vẫn còn.

## Verification

Kết quả build, test, publish và commit/push được ghi nhận trong bàn giao của change này.
