# Báo cáo cập nhật tọa độ bản đồ công trình

Ngày thực hiện: 17/09/2026

## 1. File đã kiểm tra

- `Services/KhachHangService.cs`
- `Services/YeuCauService.cs`
- `Controllers/KhachHangController.cs`
- `Controllers/YeuCauController.cs`
- `Controllers/HomeController.cs`
- `Models/YeuCauViewModels.cs`
- `Views/KhachHang/Detail.cshtml`
- `Views/YeuCau/Detail.cshtml`
- `Views/Home/Index.cshtml`
- `wwwroot/css/site.css`

## 2. File đã sửa

- `Controllers/HomeController.cs`
- `Models/YeuCauViewModels.cs`
- `Services/YeuCauService.cs`
- `Views/Home/Index.cshtml`
- `wwwroot/css/site.css`

## 3. ActiveTab thực tế

Tab "Công việc" dùng giá trị `cong-viec`. `YeuCauController.Edit` nhận query `activeTab`, gán vào `Form.ActiveTab`; view kích hoạt server-side nút/panel có `data-tab="cong-viec"`. POST giữ giá trị qua hidden input `Form.ActiveTab` và `NormalizeFormState`.

## 4. Cơ chế click cũ

Dashboard tạo URL chi tiết với `activeTab=checkin`, nên click tên khách hàng mở tab Checkin.

## 5. Cơ chế click mới

Cả URL do backend trả về và fallback trong `buildConstructionDetailUrl()` đều dùng `activeTab=cong-viec`. Không dùng JavaScript để click tab sau khi tải trang.

## 6. Lấy tọa độ hiện tại

Nút trong popup bản đồ gọi `navigator.geolocation.getCurrentPosition()` với `enableHighAccuracy=true`, `timeout=15000`, `maximumAge=0`. GPS thành công chỉ tạo marker tạm và khung xác nhận gồm tọa độ, độ chính xác, nút cập nhật và nút hủy. DB chưa thay đổi cho tới khi người dùng xác nhận.

Các lỗi permission denied, position unavailable và timeout có thông báo tiếng Việt riêng. Chức năng này độc lập với GPS dùng làm điểm xuất phát cho chỉ đường OSRM.

## 7. Endpoint cập nhật

`POST /Home/UpdateConstructionLocationCoordinates`, có anti-forgery và authentication kế thừa từ `[Authorize]` của controller.

Endpoint kiểm tra request/location ID, miền latitude/longitude, quan hệ `TblYeuCau.IDDiaDiem`, và quyền admin hoặc nhân viên được phân công. Frontend không gửi tên người thao tác.

## 8. Bảng được cập nhật

Chỉ cập nhật `TblKhachHangDiaDiem`, đúng `IDDiaDiem` đang gắn với `TblYeuCau`. Không ghi tọa độ trực tiếp vào `TblYeuCau`, không thay schema và không tạo bảng audit mới.

## 9. Audit Khách hàng

`KhachHangService` đã ghi audit trong cả create và edit địa điểm:

- `Updated_LongLat_Date = GETDATE()` khi có đủ tọa độ.
- `Updated_LongLat_By = currentUser` khi có đủ tọa độ.

Luồng đồng bộ danh sách địa điểm cũng ghi hai trường trên. Kiểm tra DB: 2/2 địa điểm hiện có đủ Lat/Long đều có đủ audit.

## 10. Audit Phiếu yêu cầu và Dashboard

`YeuCauController.UpdateLocationCoordinates` dùng `YeuCauService.UpdateLocationCoordinatesAsync` cho lần gắn tọa độ đầu tiên. Dashboard dùng `ReplaceLocationCoordinatesAsync`; cả hai đi qua cùng private service `SaveLocationCoordinatesAsync`, cập nhật cùng bảng và cùng hai trường audit.

## 11. Người thao tác

Backend ưu tiên claim `display_name`, sau đó `User.Identity.Name`, claim name, name identifier và cuối cùng `system`. Giá trị được giới hạn 50 ký tự trước khi ghi DB. Không tin dữ liệu `updatedBy` từ frontend.

## 12. Timestamp

Audit chính thức dùng `GETDATE()` của SQL Server. Frontend chỉ hiển thị `updatedLongLatDate` đọc lại từ DB trong response.

## 13. Quyền cập nhật

- Administrator được cập nhật.
- Nhân viên phải được phân công vào công việc của đúng phiếu yêu cầu.
- Cờ UI chỉ điều khiển hiển thị nút; endpoint vẫn kiểm tra quyền độc lập và trả HTTP 403 khi không đủ quyền.
- `requestId` và `locationId` được đối chiếu phía backend để chống thay ID bằng DevTools.

## 14. Kết quả test

| Test | Kết quả | Bằng chứng |
|---|---|---|
| 1. Bỏ phần trăm tiến độ | PASS (source/build) | Dòng gauge/% đã bị xóa khỏi `renderConstructionCard`. |
| 2. Mở tab Công việc | PASS (source/build) | URL dùng `activeTab=cong-viec`; GET/POST mapping đã xác minh. |
| 3. Có nút lấy tọa độ | PASS (source/build) | Nút `data-construction-coordinate-capture` trong popup. |
| 4. GPS tạo marker, chưa lưu | PASS (source review) | Chỉ tạo candidate marker; chưa POST trước xác nhận. |
| 5. Lưu bốn cột DB | PASS (runtime) | Phiếu 4020, địa điểm 4058 được cập nhật thử và hoàn nguyên. |
| 6. Audit user từ backend | PASS (runtime/source) | DB nhận `CodexRuntimeAudit` từ tham số backend service; endpoint tự resolve claims. |
| 7. Timestamp server | PASS (runtime) | DB trả timestamp `2026-09-17T11:24:33.320`; SQL dùng `GETDATE()`. |
| 8. Cập nhật map/card không F5 | PASS (source review) | Response cập nhật marker, dataset và thumbnail tại chỗ; route cũ bị xóa. |
| 9. Từ chối GPS | PASS (source review) | Mã lỗi 1 có thông báo riêng. |
| 10. GPS timeout | PASS (source review) | Mã lỗi 3 có thông báo riêng. |
| 11. User không quyền | PASS (source review) | Endpoint kiểm tra admin/assignment và trả 403. |
| 12. Audit Khách hàng | PASS (code/DB inspection) | Create/edit/sync đều ghi audit; DB hiện tại 2/2 bản ghi tọa độ có audit. |
| 13. Audit Phiếu yêu cầu | PASS (code/runtime) | Dùng chung `SaveLocationCoordinatesAsync`. |
| 14. Gắn tọa độ lần đầu | PASS (source review) | Card chưa có tọa độ vẫn mở popup nếu có location ID và quyền. |

Kiểm thử thao tác GPS qua browser thật: NOT RUN. Môi trường không có phiên đăng nhập hoặc credential hợp lệ; không ghi PASS giả cho kiểm thử tương tác thiết bị. Dữ liệu test tích hợp đã được khôi phục chính xác về `LongAddress=109.18060`, `LatAddress=12.21804`, `Updated_LongLat_By=Admin`, timestamp cũ.

## 15. Build

`dotnet build -c Release`: PASS, 0 warning, 0 error.

## 16. Test project

Repository không có test project. Kiểm thử tích hợp service/SQL chạy thành công; tọa độ ngoài miền bị chặn.

## 17. Publish

`dotnet publish -c Release -o publish/iis`: PASS.

Publish path: `publish/iis` (được Git ignore, không commit).

## 18. Commit

Source commit: `83168bb6a4f781ce4b2f7f2a4d581a820d271649`

## 19. Push

Branch: `main`

Remote: `origin`

Status: PASS.
