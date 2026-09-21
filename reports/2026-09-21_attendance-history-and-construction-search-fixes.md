# Báo cáo hoàn thiện tìm kiếm công việc và lịch sử chấm công

## A. Refresh button sizing

Nút refresh dùng chung class `crud-modal-close` với nút đóng, cùng ô grid 38 × 38 px trên desktop/mobile. Nút vẫn icon-only, giữ `aria-label`, `title`, disabled state và animation quay mà không đổi kích thước.

## B. Customer phone search fix

Search server-side kiểm tra cả `TblKhachHang.SoDienThoai` và `TblKhachHangDiaDiem.DienThoai`. Hai nguồn được chuẩn hóa ký tự định dạng ngay trong biểu thức SQL; không cập nhật dữ liệu lưu trữ.

## C. Phone-like keyword detection

Nhánh tìm điện thoại chỉ bật khi keyword có ít nhất 6 chữ số và ngoài chữ số chỉ chứa khoảng trắng, `+`, `-`, `.`, `(`, `)`. Vì vậy `YC-2600001`, `ABC 2`, `Khách 2026` không bị hiểu sai là điện thoại. Dạng `+84` được chuẩn hóa tương thích với đầu `0` Việt Nam.

## D. Check-in/out icon mapping

- Check-in: `fa-right-to-bracket`.
- Check-out: `fa-right-from-bracket`.
- Chấm công ngoài nhanh được render một dòng: `fa-right-left` với nhãn `Check-in & Check-out`.
- Badge loại chấm công building/route/list-check vẫn giữ nguyên ở vị trí riêng.

## E. 5px icon spacing

Class semantic `.cham-cong-entry-time-icon` đặt `margin-right: 5px`; không áp dụng margin toàn cục cho icon.

## F. Attendance description mapping

`ChamCongHistoryItem.DisplayDescription` thống nhất quy tắc:

- `ChamCong` → `Chấm công văn phòng`;
- `MuaHang` → `PurchaseNote`, fallback `Chấm công ngoài`;
- `KhachHang` → `CustomerDisplayName`.

Mã yêu cầu vẫn nằm trong data attribute để mở popup chi tiết, nhưng text clickable chính là tên khách hàng.

## G. Server/AJAX consistency

Razor dùng trực tiếp `DisplayDescription`; `BuildChamCongHistoryJson` trả thêm `displayDescription` cho AJAX. Cả hai nhánh dùng cùng icon hành động, tooltip và mô tả nên reload/đổi ngày/đổi nhân viên không làm thay đổi wording.

## H. Regression

Không thay đổi endpoint, internal attendance type, format lịch sử, bốn sort mode, cách tính recent attendance, card, vùng scroll hoặc logic Leaflet/map/GPS.

## I. Tests

56/56 tests passed. Test bổ sung bao phủ phone-like hợp lệ/không hợp lệ, chuẩn hóa `+84`, hai cột điện thoại và mapping mô tả ba loại chấm công.

## J. Build

- `dotnet restore apptech-dashboard.sln`: thành công.
- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 56/56 passed.
- `git diff --check`: không có whitespace error; chỉ có cảnh báo LF/CRLF trên Windows.

## K. Database impact

Schema changed: **NO**.

## L. Migration

Migration required: **NO**. Không có migration SQL mới.

## M. Commit SHA

Feature commit: `c47280edc7829fd0fc4233b363db45b80209f9f2`.

Message: `fix(attendance): refine construction search and history presentation`.

## N. Push result

Push thành công lên `origin/main`.

## O. Deploy status

**Not deployed.** Chưa có IIS server/path triển khai được xác nhận trong repository.
