# Báo cáo sửa checkout lịch sử chấm công ngoài

## Root cause

Razor coi mọi record `MuaHang` là chấm công nhanh nên chỉ tạo event check-in. Nhánh AJAX cũng loại checkout khi attendance type là `MuaHang`. Vì vậy chấm công ngoài bình thường có `ThoiDiemCheckOut` vẫn không hiển thị checkout.

## Server fix

Thêm `ChamCongTimelineEventFactory` làm nguồn tạo event cho Razor. Factory chỉ gộp một event khi `IsQuickPurchase=true`; các record còn lại tạo check-in và checkout theo timestamp thực tế.

## AJAX fix

Điều kiện tạo checkout trong `renderHistory` chỉ loại `item.isQuickPurchase`. Không còn loại toàn bộ `MuaHang`.

## Event generation rule

- Chấm công ngoài nhanh: một combined event, icon `fa-right-left`.
- Chấm công ngoài thường: check-in và checkout riêng nếu có timestamp.
- Chấm công công việc: giữ check-in và checkout riêng.
- Chấm công văn phòng: giữ check-in và checkout riêng.
- Mapping icon, `DisplayDescription`, ảnh, badge loại chấm công và khoảng cách icon 5px giữ nguyên.

## Test result

60/60 tests passed. Regression test xác nhận:

- chấm công ngoài thường có hai event;
- chấm công ngoài nhanh có một event;
- chấm công công việc có hai event;
- chấm công văn phòng có hai event.

## Build result

- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 60/60 passed.
- `git diff --check`: không có whitespace error; chỉ có cảnh báo LF/CRLF trên Windows.

## Database impact

Schema changed: **NO**.

## Migration required

Migration required: **NO**. Không có migration SQL mới.

## Commit SHA

Feature commit: `ccdaf1d526106ddc252b2ce3f86dacd17f9166dd`.

Message: `fix(attendance): render outside work checkout events correctly`.

## Push result

Push thành công lên `origin/main`.

## Deploy status

**Not deployed.** Chưa có IIS server/path triển khai được xác nhận trong repository.
