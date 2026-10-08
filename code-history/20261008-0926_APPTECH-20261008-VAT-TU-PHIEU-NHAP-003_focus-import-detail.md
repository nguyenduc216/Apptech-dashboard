# APPTECH-20261008-VAT-TU-PHIEU-NHAP-003

- FEATURE_ID: `APPTECH-WAREHOUSE-MATERIAL`
- CHANGE_ID: `APPTECH-20261008-VAT-TU-PHIEU-NHAP-003`
- Branch: `main`

## Requirement

Từ mã Phiếu nhập trên Danh sách vật tư, mở popup Nhập kho hiện hữu trong tab trình duyệt mới, tự chọn tab Hàng hóa nhập, highlight và scroll tới đúng dòng chi tiết đã sinh ra vật tư.

## Implementation

- Reuse `NhapKhoController.Index` với `editId`, `highlightDetailId` và `activeTab=hang-hoa-nhap`.
- Khóa focus là quan hệ chính xác `TblChiTietHangHoa.IDPhieuNhapChiTiet = TblPhieuNhapKhoChiTiet.ID`; không dò theo tên, mã hàng hóa hoặc index dòng.
- View đánh dấu từng dòng bằng `data-nhap-kho-detail-id`, thêm class highlight đúng ID và chỉ gọi `scrollIntoView` khi dòng đích tồn tại.
- Khi thiếu `PhieuNhapChiTietId`, link vẫn mở đúng phiếu ở tab mới và không highlight/scroll.

## Scope

Chỉ thay đổi navigation/UX. Không sửa business logic nhập kho, số lượng, loại hình nhập, report, phiếu xuất, permission, SQL hay database.

Migration required: **NO**.

Migration executed: **NO**.

## Verification

Kết quả build, tests, publish và commit/push được ghi nhận trong bàn giao của change này.
