# Báo cáo nâng cấp tìm kiếm/sắp xếp Chấm công công việc

## A. Current behavior

Endpoint `Home/CheckinCongTrinhRequests` trước đây chỉ nhận `employeeId`, lấy tối đa 100 phiếu rồi sắp xếp theo ngày tạo từ cũ đến mới. Popup chưa có tìm kiếm hoặc lựa chọn cách sắp xếp; nút tải lại chiếm một cột rộng trong header.

## B. UI changes

- Nút tải lại đổi thành icon-only, có `aria-label`, `title`, kích thước tương đương nút đóng và animation khi tải.
- Thêm ô tìm theo tên khách hàng/số điện thoại và combobox bốn tiêu chí sắp xếp.
- Desktop hiển thị search/sort cùng hàng; mobile xếp thành hai hàng.
- Giữ nguyên card, vùng danh sách scroll độc lập và toàn bộ markup/logic thumbnail Leaflet.

## C. Search implementation

Client debounce 300 ms, tiếp tục dùng `AbortController` và request sequence. Server trim/giới hạn keyword 200 ký tự rồi lọc có tham số trong SQL trước `TOP (@Limit)`. Tên khách hỗ trợ collation không phân biệt hoa/thường/dấu; số điện thoại được bỏ khoảng trắng, dấu chấm, gạch nối và dấu cộng để tìm từng phần. Mã yêu cầu cũng được hỗ trợ.

## D. Sort criteria

- `recent-checkin`: lần check-in/checkout mới nhất của nhân viên đang áp dụng.
- `newest`: ngày yêu cầu/ngày tạo giảm dần.
- `oldest`: ngày yêu cầu/ngày tạo tăng dần.
- `deadline`: hạn hoàn thành tăng dần, phiếu không có hạn nằm cuối.

Sort đầu vào được whitelist bằng `ConstructionCheckinSortCatalog`; giá trị không hợp lệ fallback về `recent-checkin` và không được nối trực tiếp vào SQL.

## E. Recent check-in/out calculation

Một `OUTER APPLY` trên `TblCheckinHistory` tính `MAX` của activity gần nhất. Với mỗi lịch sử, checkout được dùng khi mới hơn check-in; nếu chưa checkout thì dùng check-in. Điều kiện luôn gồm cả `IDYeuCau = yc.ID` và `IDNhanVien = @EmployeeId`.

## F. Fallback logic

Phiếu có lịch sử của nhân viên được đưa lên trước và sắp theo activity giảm dần. Phiếu chưa có lịch sử nằm sau, sắp theo `ISNULL(NgayYeuCau, Created_Date) ASC, ID ASC`.

## G. Deadline logic

`deadline` sắp `NgayHetHan ASC`; hạn quá hạn vẫn đứng trước hạn tương lai. Giá trị null nằm cuối, sau đó dùng ngày tạo và ID để ổn định thứ tự.

## H. Employee-specific behavior

Giữ nguyên toàn bộ validation/phân quyền chọn nhân viên trong controller. `employeeId` sau khi resolve được truyền vào truy vấn cho cả điều kiện phân công và lịch sử attendance, nên admin đổi nhân viên sẽ nhận đúng thứ tự của nhân viên mới.

## I. SQL/performance

Search và sort chạy trước `TOP (@Limit)`. Lịch sử được tổng hợp trong cùng một query bằng `OUTER APPLY`, không có N+1. Keyword và employee đều dùng SQL parameter; chỉ các ORDER BY cố định từ whitelist được nội suy.

## J. Files changed

- `Controllers/HomeController.cs`
- `Models/YeuCauViewModels.cs`
- `Services/YeuCauService.cs`
- `Views/Home/Index.cshtml`
- `wwwroot/css/site.css`
- `Tests/ApptechDashboard.Tests/ConstructionCheckinQueryTests.cs`

## K. Tests

39/39 tests passed. Test mới bao phủ whitelist/fallback sort, checkout là latest activity, điều kiện lịch sử theo nhân viên, fallback cũ đến mới, newest/oldest/deadline và chuẩn hóa tìm số điện thoại.

## L. Build

- `dotnet restore apptech-dashboard.sln`: thành công.
- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 39/39 passed.
- `git diff --check`: không có whitespace error; chỉ có cảnh báo quy ước LF/CRLF trên Windows.

## M. Database impact

Database schema changed: **NO**. Chỉ đọc các cột/bảng hiện hữu.

## N. Migration required

Migration required: **NO**. Không có migration SQL mới.

## O. Commit SHA

Feature commit: `1aef9983641e57b3eb0842a82498dbd6cdf2c759`.

Message: `feat(attendance): add construction request search and sorting`.

## P. Push result

Push thành công lên `origin/main`.

## Q. Deploy status

**Not deployed.** Chưa có IIS server/path triển khai được xác nhận trong repository.
