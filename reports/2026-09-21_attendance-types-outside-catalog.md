# Báo cáo chuẩn hóa loại chấm công và danh mục nội dung chấm công ngoài

## A. Current state

Dashboard trước đây hiển thị ba nhóm bằng wording `Apptech`, `Mua hàng`, `Công trình`. Internal type tương ứng là `ChamCong`, `MuaHang`, `KhachHang`; các endpoint mua hàng/công trình đang phục vụ dữ liệu lịch sử. Mười nội dung đi ra ngoài được hard-code trực tiếp trong Razor. Controller còn bắt buộc cả nội dung và ghi chú, còn service lưu snapshot text trong `GhiChuNhanVien` theo dạng `[mục 1; mục 2] ghi chú`.

## B. UI label changes

- `ChamCong` hiển thị **Chấm công văn phòng**.
- `MuaHang` hiển thị **Chấm công ngoài**.
- `KhachHang` hiển thị **Chấm công công việc**.
- Chuẩn hóa title popup, timeline, trạng thái, checkout, xóa lượt và bản đồ theo wording mới.
- Không đổi internal type, endpoint hoặc tên method hiện hữu.

## C. Icon mapping

- Chấm công văn phòng: `fa-solid fa-building`.
- Chấm công ngoài: `fa-solid fa-route`.
- Chấm công công việc: `fa-solid fa-list-check`.
- Giữ nguyên các class màu `is-company-event`, `is-purchase-event`, `is-customer-event`.

## D. Outside attendance catalog

Thêm module **Danh mục nội dung chấm công ngoài** gồm service, model, controller, view CRUD, route và permission/menu. Trang hỗ trợ tìm kiếm, thêm, sửa, thứ tự hiển thị và bật/tắt sử dụng. Dashboard chỉ nhận danh mục active qua `ChamCongDashboardModel.OutsideWorkOptions`, sắp xếp `ThuTuHienThi`, sau đó `ID`.

Popup mặc định collapse bằng button có `aria-expanded=false`; khi mở vẫn cho chọn nhiều và summary hiển thị số mục đã chọn. Razor không còn danh sách 10 mục hard-code.

## E. Database migration

Migration idempotent:

`App_Data/Migrations/20260921_add_danh_muc_cham_cong_ngoai.sql`

Migration tạo `dbo.TblDanhMucChamCongNgoai` với `ID`, `TenNoiDung`, `ThuTuHienThi`, `IsActive`, `CreatedAt`, `UpdatedAt`; tạo unique index tên nội dung và seed 10 giá trị cũ mà không tạo duplicate.

## F. Optional-selection behavior

Controller và JavaScript không còn bắt buộc chọn nội dung hoặc nhập ghi chú. Các trường hợp sau đều hợp lệ nếu ảnh/GPS/quyền và điều kiện chấm công khác hợp lệ:

- không nội dung, không ghi chú → snapshot `null`;
- không nội dung, có ghi chú → lưu ghi chú sạch;
- có một/nhiều nội dung, không ghi chú → lưu phần `[... ]`;
- có nội dung và ghi chú → giữ format snapshot cũ.

Không lưu `[]`, `[;]` hoặc chuỗi `null`.

## G. Historical compatibility

Không thêm foreign key vào lịch sử và không migrate record cũ. Lượt chấm công ngoài tiếp tục lưu snapshot tên nội dung. Đổi tên hoặc deactivate danh mục không làm thay đổi lịch sử; `PurchaseWorkContent` và `PurchaseNote` vẫn parse format cũ. Internal type `MuaHang` và toàn bộ endpoint cũ được giữ nguyên.

## H. Files changed

- `App_Data/Migrations/20260921_add_danh_muc_cham_cong_ngoai.sql`
- `Controllers/DanhMucChamCongNgoaiController.cs`
- `Controllers/HomeController.cs`
- `Models/DanhMucChamCongNgoaiViewModels.cs`
- `Models/DashboardViewModel.cs`
- `Services/DanhMucChamCongNgoaiService.cs`
- `Services/ChamCongService.cs`
- `Services/PermissionCatalogService.cs`
- `Views/DanhMucChamCongNgoai/Index.cshtml`
- `Views/Home/Index.cshtml`
- `wwwroot/css/site.css`
- `Program.cs`
- `Tests/ApptechDashboard.Tests/AttendanceOutsideCatalogTests.cs`

## I. Tests

20/20 tests pass. Test mới xác nhận:

- chỉ lấy active và sắp xếp theo thứ tự/ID;
- nội dung và ghi chú optional;
- một/nhiều lựa chọn được lưu đúng snapshot;
- lịch sử cũ vẫn parse đúng sau rename/deactivate danh mục;
- Dashboard model nhận options từ service/ViewModel thay vì hard-code.

Regression build giữ nguyên các test QR/In180. Luồng chấm công giữ nguyên endpoint, GPS, camera, quick check-in, request/customer association và xóa lượt.

## J. Build

- `dotnet restore apptech-dashboard.sln`: thành công.
- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 20/20 passed.
- `git diff --check`: không có whitespace error; chỉ có thông báo LF/CRLF của Git trên Windows.

## K. Commit SHA

`a9f708df8bb9e66ceca6f633d83bcd79b15ae18a`

Message: `feat(attendance): standardize attendance types and outside work catalog`

Branch: `main`.

## L. Push result

Push thành công lên `origin/main`.

## M. Deploy status

**Not deployed.** Repo không xác nhận IIS server/path đích thực tế. Migration mới phải chạy trước khi deploy ứng dụng.

## N. Hoàn thiện sau code review

- Reset và collapse danh sách nội dung chỉ khi mở `purchase-checkin`; tải/refresh danh sách phiếu yêu cầu không còn làm mất lựa chọn đang nhập.
- Fallback lịch sử AJAX đổi từ `AppTech` thành `Chấm công văn phòng`; trạng thái chấm công ngoài đang mở thống nhất là `Đang thực hiện`.
- Lượt chỉ có ghi chú hiển thị `Không chọn nội dung` và vẫn hiển thị ghi chú độc lập, không còn bị gắn nhãn dữ liệu cũ.
- Bổ sung kiểm tra permission phía server cho Index/Create/Update/SetActive; Administrator được phép và người thiếu quyền nhận `Forbid` trước khi service dữ liệu chạy.
- Danh sách phiếu yêu cầu cuộn độc lập, các card giữ chiều cao nội dung và thumbnail bản đồ giữ kích thước.
- Không đổi schema, migration, endpoint, internal attendance type hoặc Leaflet.

## O. Kết quả xác minh sau review

- `dotnet build apptech-dashboard.sln -c Release`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build`: thành công, 25/25 passed.
- `git diff --check`: không có whitespace error; chỉ có thông báo LF/CRLF của Git trên Windows.
- Feature commit: `85c8a4f35c2d678e6838da5b58fe21dc3a96e1bb`.
- Message: `fix(attendance): complete attendance UX permissions and request scrolling`.
- Push: thành công lên `origin/main`.
- Deploy: chưa thực hiện.
