# AppTech - Hoàn thành phiếu yêu cầu & Tab Đánh giá

## 1. Mục tiêu

- Chuyển Rating/Zalo hiện có vào tab Đánh giá.
- Bổ sung workflow Complete Phiếu yêu cầu.

## 2. Phân tích code trước sửa

- Rating/Zalo nằm trong `yeu-cau-zalo-rating-panel` phía trên `.crud-tabs` của `Views/YeuCau/Detail.cshtml`.
- Phiếu lưu tại `TblYeuCau`; trạng thái là `TrangThaiYeuCau`; thời điểm hoàn thành là `NgayHoanThanh`; metadata là `Updated_Date`, `Updated_By`.
- Công việc thực tế lưu tại `TblYeuCauCongViec`; trạng thái là `TrangThaiCongViec`; bảng có `Updated_Date`, `Updated_By` nhưng không có completion timestamp riêng. `FinishDate` hiện có thuộc checklist, không thuộc công việc.
- Catalog dùng `YeuCauTrangThaiCatalog` và `YeuCauCongViecTrangThaiCatalog`.
- Quyền dùng quyền Update của chức năng Yêu cầu hiện hữu; admin được phép theo convention hiện tại.
- Audit dùng `ICommonAuditService`/`TblCommonLogging` trong SQL transaction hiện hữu.
- Active tab dùng `Form.ActiveTab`, `data-tab`, local view state và hỗ trợ `thong-tin`, `cong-viec`, `checkin` trước thay đổi.
- Update cũ dùng `NgayHoanThanh?.Date`; đã sửa để không làm mất giờ và bảo toàn timestamp đã lưu khi cập nhật form sau này.

## 3. Files changed

- `Controllers/YeuCauController.cs`
- `Models/YeuCauViewModels.cs`
- `Services/YeuCauService.cs`
- `Views/YeuCau/Detail.cshtml`
- `reports/2026-09-17_yeu-cau-completion-rating-tab.md`

## 4. Tab Đánh giá

- Thêm tab `danh-gia` sau Checkin với icon `fa-star`.
- Tái sử dụng nguyên panel Rating/Zalo, di chuyển panel vào tab container khi khởi tạo; CSS ngăn panel hiển thị tại vị trí cũ.
- Giữ nguyên dữ liệu rating, QR/link Zalo, trạng thái kết nối và responsive hai cột/stack mobile.
- `Form.ActiveTab` tiếp tục lưu và khôi phục tab hiện tại.

## 5. Complete workflow

- Endpoint `POST /YeuCau/Complete` có `[ValidateAntiForgeryToken]`.
- Button chỉ render ở Edit, khi có quyền Update và trạng thái không phải Hoàn thành/Hủy.
- Modal xác nhận có Hủy, Đồng ý hoàn thành, loading state và chống submit lặp.
- `CompleteAsync` khóa phiếu bằng `UPDLOCK, HOLDLOCK`, chạy toàn bộ cập nhật và audit trong một transaction.
- Dùng một thời điểm SQL Server `GETDATE()` cho `NgayHoanThanh`, `Updated_Date` và work metadata; không truncate giờ.
- Công việc chưa Hoàn thành/Hủy chuyển sang `YeuCauCongViecTrangThaiCatalog.HoanThanh`; công việc Hủy giữ nguyên; công việc đã Hoàn thành không bị update lại.
- Phiếu đã Hoàn thành trả kết quả idempotent và giữ timestamp cũ; phiếu Hủy bị từ chối.

## 6. Database

- Schema changed: NO
- SQL required: NO
- Không tạo bảng/cột/status mới và không tự thay đổi DB production.

## 7. Permission

- UI dùng `asp-permission-action="update"` và `CanUpdateRequest`.
- Backend `Complete` kiểm tra lại quyền Update từ permission session; không dựa riêng vào việc ẩn button.

## 8. Audit

- Ghi một event `YEU_CAU / COMPLETE / YEU_CAU` trong transaction.
- Data gồm RequestId, trạng thái cũ/mới, completion timestamp, số work cập nhật, số work Hủy giữ nguyên và current user.

## 9. Test

| Test | Kết quả | Ghi chú |
| --- | --- | --- |
| TEST 01 | VERIFIED SOURCE | Điều kiện render button kiểm tra Edit, quyền Update và trạng thái. |
| TEST 02 | VERIFIED SOURCE | Button mở modal; chưa submit thì không gọi endpoint. |
| TEST 03 | VERIFIED SOURCE | Hủy/backdrop đóng modal, không submit. |
| TEST 04 | NOT RUN | Cần DB runtime để xác nhận dữ liệu thực tế. |
| TEST 05 | VERIFIED SOURCE | SQL bulk update loại trừ Hoàn thành và Hủy. |
| TEST 06 | VERIFIED SOURCE | Work đã Hoàn thành không bị UPDATE; schema work không có completion timestamp. |
| TEST 07 | VERIFIED SOURCE | Row lock và nhánh already-completed giữ `NgayHoanThanh`. |
| TEST 08 | VERIFIED SOURCE | UI ẩn và service từ chối trạng thái Hủy. |
| TEST 09 | VERIFIED SOURCE | Service trả lỗi rõ khi không tìm thấy, controller redirect với toast. |
| TEST 10 | VERIFIED SOURCE | Request, works và audit dùng chung transaction; exception không commit. |
| TEST 11 | VERIFIED SOURCE | UI và backend cùng kiểm tra quyền Update. |
| TEST 12 | VERIFIED SOURCE | CSS ẩn panel ở vị trí cũ; JS chuyển đúng panel vào tab container. |
| TEST 13 | VERIFIED SOURCE | Tab dùng nguyên panel Rating/Zalo hiện có. |
| TEST 14 | NOT RUN | Cần Zalo/browser runtime. |
| TEST 15 | VERIFIED SOURCE | Markup rating hiện có được giữ nguyên, không đổi API/model. |
| TEST 16 | NOT RUN | Chưa có browser automation để kiểm tra trực quan mobile. |
| TEST 17 | NOT RUN | Cần DB/browser runtime để complete rồi reload. |
| TEST 18 | VERIFIED SOURCE | UI chống submit lặp; transaction khóa hàng và xử lý idempotent. |

Không có test project phù hợp trong repository.

## 10. Build

- `dotnet restore .\ApptechDashboard.csproj`: PASS
- `dotnet build .\ApptechDashboard.csproj -c Release`: PASS
- Warnings: 0
- Errors: 0

## 11. Publish

- `dotnet publish .\ApptechDashboard.csproj -c Release -o artifacts\publish\ApptechDashboard`: PASS
- Đã xác nhận `artifacts/publish/ApptechDashboard/ApptechDashboard.dll` tồn tại.
- Publish artifacts, `bin/`, `obj/` không được commit.

## 12. Git

- Branch: `main`
- Code commit: `2a4568db4ff81527c60d46bbd875d8e08470d497`
- Code push: PASS, `origin/main` chứa code commit.

## 13. Remaining issues

- Chưa chạy test tích hợp với DB thật và chưa kiểm thử trực quan desktop/mobile/Zalo do phiên làm việc không có browser automation và môi trường test DB chuyên biệt.
