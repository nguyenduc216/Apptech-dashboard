# Fix checkout cho chấm công ngoài bình thường

## A. Root cause

Logic cũ từng dùng `IsPurchase || IsQuickPurchase` ở Razor và loại mọi `MuaHang` trong JavaScript, khiến chấm công ngoài bình thường không tạo event checkout.

## B. Server render fix

`ChamCongTimelineEventFactory` chỉ gộp event khi `IsQuickPurchase=true`. Chấm công ngoài bình thường có `ThoiDiemCheckOut` tạo hai event theo thứ tự check-in và checkout. Event quick có `IsCombined=true` để biểu diễn rõ nghiệp vụ.

## C. AJAX render fix

`renderHistory` tạo checkout khi có `thoiDiemCheckOut` và `isQuickPurchase` không phải `true`. Attendance type `MuaHang` không còn là điều kiện loại checkout.

## D. Event generation rule mới

- Ngoài nhanh: một combined event.
- Ngoài bình thường: check-in và checkout riêng.
- Công việc: check-in và checkout riêng.
- Văn phòng: check-in và checkout riêng.

Icon và `DisplayDescription` giữ nguyên.

## E. Test cases

Test factory bao phủ:

- ngoài bình thường: 2 event, event thứ hai là checkout;
- ngoài nhanh: 1 event và `IsCombined=true`;
- công việc: 2 event;
- văn phòng: 2 event.

Kết quả: **60/60 passed**.

## F. Build result

- Build Release: thành công, 0 warning, 0 error.
- Test Release: thành công, 60/60 passed.
- `git diff --check`: không có whitespace error.

## G. Database impact

Database schema changed: **NO**.

Migration required: **NO**. Không có migration SQL mới.

Feature commit: `5c17794e0739b6598c5cac734099dc4aea9dd0f2`.

Push: thành công lên `origin/main`.

Deploy: chưa thực hiện.
