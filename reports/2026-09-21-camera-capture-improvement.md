# Báo cáo cải thiện camera check-in và phát hiện ảnh thiếu sáng

## 1. Files changed

- `wwwroot/js/camera-helper.js`: helper camera dùng chung.
- `Views/Shared/_Layout.cshtml`: nạp helper trước các script trang.
- `Views/Home/Index.cshtml`: camera Dashboard/chấm công ngoài dùng warm-up, constraints và brightness validation.
- `Views/YeuCau/Detail.cshtml`: camera check-in công trình/khách hàng và ảnh công việc dùng cùng pipeline; `takePhoto()` không còn bỏ qua validation.
- `reports/2026-09-21-camera-capture-improvement.md`: báo cáo triển khai.

Không thay đổi controller, API upload, storage, database hoặc migration.

## 2. Camera flow updated

```text
Chọn camera sau/device đã chọn
→ openCamera với constraints theo browser support
→ apply continuous focus/exposure/white balance nếu capability hỗ trợ
→ chờ video HAVE_ENOUGH_DATA
→ warm-up 1.200 ms
→ cho phép bấm chụp
→ capture Blob gốc
→ brightness analysis
→ đạt: giữ flow preview/overlay/upload cũ
→ thiếu sáng/ngược sáng: không gán payload, cảnh báo và cho chụp lại
```

Dashboard vẫn upload chính blob đang preview. Check-in công trình vẫn giữ `ImageCapture.takePhoto()` và fallback `grabFrame()`/video canvas. Timestamp/GPS overlay vẫn được thực hiện sau khi ảnh gốc vượt validation.

## 3. Constraint changes

Helper gọi `navigator.mediaDevices.getSupportedConstraints()` trước khi thêm constraint:

- `width: { ideal: 1280 }`.
- `height: { ideal: 1280 }`.
- `deviceId: { exact: selectedId }` khi người dùng đã chọn camera.
- Nếu chưa chọn: `facingMode: { ideal: "environment" }` để ưu tiên camera sau.

Sau khi có stream, helper đọc `track.getCapabilities()` và chỉ thử `continuous` cho focus/exposure/white balance khi capability thực sự công bố giá trị đó. `applyConstraints` được bọc fallback; thiết bị không hỗ trợ vẫn tiếp tục camera mặc định. Nếu device ID exact lỗi, helper thử lại camera environment.

## 4. Warm-up implementation

- Nút chụp bị disable trong lúc khởi động.
- Helper chờ `readyState >= HAVE_ENOUGH_DATA` và kích thước video hợp lệ, timeout 6 giây.
- Sau đó chờ thêm 1.200 ms, nằm trong yêu cầu 1.000–1.500 ms, để AE/AF/AWB hội tụ.
- UI hiển thị “Đang ổn định ánh sáng và lấy nét camera...” rồi “Camera đã sẵn sàng.”

## 5. Brightness detection logic

Blob được decode vào canvas phân tích tối đa 160 px chiều rộng. Detector dùng luminance chuẩn theo trọng số RGB và tính:

- Average luminance toàn ảnh.
- Average luminance vùng trung tâm 50% chiều rộng × 60% chiều cao.
- Average luminance vùng ngoài.
- Tỷ lệ pixel tối toàn ảnh và vùng trung tâm.

Ba tín hiệu cảnh báo được thiết kế theo hướng không quá gắt:

- Toàn ảnh rất tối: average `< 38` và dark ratio `> 72%`.
- Vùng trung tâm rất tối: center average `< 48` và center dark ratio `> 70%`.
- Nghi ngược sáng: center average `< 65`, vùng ngoài sáng hơn trung tâm `> 55`, center dark ratio `> 60%`.

Detector chạy trên blob cuối của Dashboard/ảnh công việc và trên blob gốc từ cả `ImageCapture.takePhoto()` lẫn fallback của check-in công trình. Không áp gamma, brightness boost, AI enhance hoặc filter màu.

## 6. UX changes

Khi ảnh bị đánh giá thiếu sáng/ngược sáng:

> Ảnh đang thiếu sáng hoặc ngược sáng. Vui lòng đổi hướng hoặc di chuyển tới nơi đủ sáng và chụp lại.

Blob lỗi bị xóa, không được đưa vào `FormData`/file input. Camera vẫn hoạt động để người dùng đổi hướng và chụp lại. Flow ảnh đạt yêu cầu giữ nguyên preview, timestamp/GPS và nút lưu hiện tại.

## 7. Regression result

- Chấm công văn phòng và chấm công ngoài giữ nguyên GPS, loại chấm công, ghi chú và API upload.
- Check-in/check-out công trình giữ nguyên `ImageCapture`, fallback, timestamp và GPS overlay.
- Ảnh công việc giữ nguyên giới hạn số ảnh và endpoint upload.
- Không sửa check-out service, map, popup report hoặc báo cáo chấm công.
- Backend tiếp tục lưu nguyên byte upload.

Không có thiết bị camera thật trong môi trường build, vì vậy chưa thực hiện hardware test Android/iOS. Cần smoke test trên thiết bị trước production.

## 8. Build/test result

- `node --check wwwroot/js/camera-helper.js`: thành công.
- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 73/73 test passed.
- `git diff --check`: không có whitespace error; cảnh báo LF/CRLF là quy ước worktree Windows.

## 9. Thiết bị/browser cần smoke test

- Android Chrome: camera sau và camera trước.
- iOS Safari: camera sau và camera trước.
- Ánh sáng đều ngoài trời.
- Ngược sáng với trời/cửa phía sau.
- Trong nhà thiếu sáng.
- Camera không công bố exposure/focus capabilities để xác nhận fallback.

## 10. Deploy recommendation

Có thể đưa lên môi trường staging sau khi build/test tự động pass. Trước production cần smoke test tối thiểu Android Chrome và iOS Safari, kiểm tra ngưỡng không cảnh báo nhầm trong ánh sáng tốt và xác nhận ảnh ngược sáng/thiếu sáng không được upload.
