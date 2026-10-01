# Phân tích hiện tượng ảnh check-in bị tối

## 1. Kết luận

Không có bằng chứng cho thấy backend, JPEG compression hoặc CSS đang làm giảm độ sáng. Root cause có khả năng cao nhất là **auto exposure/HDR của camera trong Web API**: stream chỉ yêu cầu camera sau hoặc device ID, không yêu cầu exposure/focus/resolution; ảnh có nền trời/vùng sáng mạnh khiến camera đo sáng theo nền và khuôn mặt bị thiếu sáng. Web camera cũng thường không có đầy đủ HDR/face metering như ứng dụng camera native.

Riêng flow check-in công trình còn ưu tiên `ImageCapture.takePhoto()`. Trên một số tổ hợp Android/browser, still-capture pipeline có thể cho exposure khác frame preview. Kiểm tra ảnh tối hiện tại không bao phủ nhánh `takePhoto()` và ngưỡng fallback chỉ phát hiện frame gần như đen hoàn toàn, không phát hiện khuôn mặt tối trên nền sáng.

Đây là kết luận xác suất cao từ code. Repository không có ảnh check-in mẫu trong `wwwroot/uploads`, nên chưa thể đo histogram/EXIF hoặc xác nhận theo model thiết bị cụ thể.

## 2. Bảng kiểm

| Component | Status | Issue |
|---|---|---|
| Camera stream | Có rủi ro cao | Constraints chỉ chọn device/facing mode; không có exposure, focus, white balance, resolution hoặc thời gian chờ AE ổn định. |
| Capture | Có rủi ro cao | Có flow chụp ngay từ video frame; flow công trình ưu tiên `ImageCapture.takePhoto()` có thể khác exposure so với preview. |
| Canvas processing | Không thấy làm tối | Chỉ `drawImage`, resize và đóng timestamp; không chỉnh brightness/contrast/gamma. |
| Compression | Không phải nguyên nhân chính | JPEG quality 0.88/0.9 giảm chi tiết, không chủ động giảm luminance. |
| Upload | Đạt | Server copy nguyên byte `IFormFile` xuống disk, không decode/re-encode/xử lý màu. |
| Display | Đạt | Ảnh dùng `object-fit: cover`; không có `filter: brightness(...)` hoặc opacity làm tối ảnh check-in. |

## 3. Camera flow hiện tại

### Dashboard chấm công văn phòng/chấm công ngoài

File chính: `Views/Home/Index.cshtml`.

```text
getUserMedia(deviceId exact hoặc facingMode environment)
→ video.srcObject
→ canvas.drawImage(video)
→ canvas.toBlob(image/jpeg, 0.88)
→ preview bằng URL.createObjectURL(cùng blob)
→ FormData imageFile
→ HomeController.SaveCheckinImageAsync
→ CopyToAsync xuống wwwroot/uploads/checkin
→ hiển thị bằng <img>
```

Evidence:

- Camera constraints tại khoảng dòng 1713–1725 chỉ có `deviceId` hoặc `facingMode`.
- Capture tại khoảng dòng 1844–1858 lấy đúng kích thước `videoWidth/videoHeight`, vẽ trực tiếp và xuất JPEG quality 0.88.
- Blob đang preview cũng chính là blob được đưa vào `FormData`; do đó nếu preview sau capture đã tối thì lỗi xảy ra trước upload.

### Check-in/check-out công trình, khách hàng

File chính: `Views/YeuCau/Detail.cshtml`.

```text
getUserMedia(deviceId exact hoặc facingMode environment)
→ video preview
→ ưu tiên ImageCapture.takePhoto()
→ nếu thất bại: ImageCapture.grabFrame()/canvas.drawImage(video)
→ load ảnh vào canvas, resize tối đa 1600 px
→ thêm dải timestamp/GPS ở đáy
→ canvas.toBlob(image/jpeg, 0.88)
→ gán File vào input + preview từ cùng canvas
→ YeuCauController.SaveCheckinImageAsync
→ CopyToAsync xuống wwwroot/uploads/checkin
```

Evidence:

- Constraints tại khoảng dòng 2958–2975 vẫn chỉ chọn camera.
- `takePhoto()` tại khoảng dòng 3421–3429 được ưu tiên trước nhánh canvas.
- Nhánh fallback kiểm tra tối tại dòng 3432–3438, nhưng blob từ `takePhoto()` không đi qua `isCanvasFrameTooDark` trước khi được xử lý tiếp.
- `drawCapturedFileToCanvas` resize, thêm overlay đen chỉ ở dải đáy và tái mã hóa JPEG. Không có phép biến đổi toàn ảnh làm tối.

### Ảnh công việc

File chính: `Views/YeuCau/Detail.cshtml`, khoảng dòng 4421–4537.

Flow dùng cùng constraints tối giản, `drawImage(video)` rồi JPEG 0.88. Không có kiểm tra độ sáng hoặc enhance.

### Backend lưu file

- `Controllers/HomeController.cs`, `SaveCheckinImageAsync`, khoảng dòng 1435–1453.
- `Controllers/YeuCauController.cs`, `SaveCheckinImageAsync`, khoảng dòng 1263–1281.

Hai controller chỉ kiểm tra extension/kích thước và `CopyToAsync`. Không có thư viện Sharp/ImageMagick, resize, gamma, color conversion hoặc re-compression trên server.

## 4. Root cause khả năng cao

### 4.1 Backlight và auto exposure của camera web

Các camera stream không áp exposure compensation, exposure mode, focus mode hoặc white balance. Khi phía sau nhân viên là bầu trời/cửa sổ/vùng nắng, thuật toán auto exposure của camera/browser có thể bảo vệ highlight và làm mặt tối. Đây khớp với mô tả “ngoài trời, đủ sáng nhưng khuôn mặt tối”.

Khả năng này phụ thuộc thiết bị. `MediaTrackCapabilities` không đảm bảo mọi Android/iOS hỗ trợ exposure controls; Safari thường hạn chế hơn Chrome Android.

### 4.2 Web capture thiếu xử lý computational photography của camera native

Native camera thường dùng HDR nhiều frame, face detection/metering, tone mapping và night/portrait processing. `getUserMedia`/canvas thường nhận frame video đã xử lý nhẹ hơn; “môi trường đủ sáng” không bảo đảm chủ thể ngược sáng được nâng shadow như app Camera.

### 4.3 `ImageCapture.takePhoto()` có thể khác preview

Flow công trình dùng still capture nếu API tồn tại. Driver có thể chuyển resolution/mode khi chụp và tạo ảnh khác exposure so với video preview. Đây là nghi vấn mạnh nếu người dùng thấy preview video sáng nhưng ảnh ngay sau khi bấm chụp tối.

### 4.4 Chụp trước khi AE ổn định

Code chỉ chờ video có current frame và hai animation frame; không có warm-up tối thiểu hoặc quan sát exposure ổn định. Người dùng có thể bấm ngay khi camera vừa mở, lúc AE/focus vẫn đang hội tụ.

### 4.5 Dark detection hiện tại quá yếu và không đúng vùng nghiệp vụ

`isCanvasFrameTooDark` chỉ lấy mẫu 80×60 ở giữa và coi tối khi average dưới 10/255 hoặc dưới 2% pixel vượt 24/255. Đây gần như là detector “ảnh đen/lỗi frame”, không phải detector “mặt thiếu sáng”. Nền sáng có thể làm phép đo pass dù khuôn mặt tối. Ngoài ra detector không chặn blob từ `takePhoto()`.

## 5. Những thành phần không phải nguyên nhân chính

- JPEG quality 0.88/0.9 không phải phép giảm brightness. Compression có thể làm mất chi tiết vùng tối nhưng không giải thích mức thiếu sáng rõ rệt.
- Canvas `drawImage` không thay đổi brightness khi không đặt filter/composite khác.
- Overlay timestamp chỉ phủ một dải đen ở đáy ảnh công trình, không phủ khuôn mặt/toàn ảnh.
- Server không xử lý lại ảnh; byte upload và byte lưu là một.
- CSS ảnh check-in chỉ quy định kích thước và `object-fit: cover`; không có opacity/filter. `object-fit` có thể crop nhưng không làm tối.
- EXIF bị loại khi canvas tái mã hóa; việc này có thể ảnh hưởng orientation/metadata nhưng không phải nguyên nhân giảm sáng pixel. Backend không tự xóa metadata vì chỉ copy file.

## 6. Cách xác minh root cause trên thiết bị

Trước khi sửa, nên ghi nhận một case có thể tái hiện với ba ảnh/trạng thái:

1. Screenshot video preview ngay trước khi chụp.
2. Preview ảnh ngay sau capture trong popup.
3. File tải lại từ `/uploads/checkin/...`.

Diễn giải:

- Video preview đã tối: auto exposure/backlight của stream là nguyên nhân.
- Video preview sáng, preview sau capture tối: ưu tiên điều tra `ImageCapture.takePhoto()`/still pipeline.
- Preview sau capture sáng nhưng file tải lại tối: không phù hợp code hiện tại vì cùng pixel/blob được upload; cần kiểm tra đúng URL/cache hoặc thu file thực tế.

Nên log tạm trên thiết bị thử nghiệm: browser/OS/model, camera label, `track.getSettings()`, `track.getCapabilities()`, thời gian từ lúc `playing` đến lúc capture, kích thước/type/size của blob và brightness histogram. Không log nội dung ảnh hoặc GPS ngoài nhu cầu chẩn đoán.

## 7. Đề xuất fix

| Phương án | Độ khó | Ảnh hưởng UX/code | Đánh giá |
|---|---:|---|---|
| 1. Constraints + warm-up | Thấp–trung bình | Camera mở chậm thêm khoảng 0.5–1.5 giây; cần fallback theo capability | Nên làm đầu tiên. Yêu cầu resolution phù hợp, thử continuous focus/exposure khi thiết bị hỗ trợ, khóa nút chụp ngắn để AE ổn định. |
| 2. Brightness detection sau capture | Trung bình | Có thể yêu cầu chụp lại; cần ngưỡng tránh false positive | Nên làm. Phân tích nhiều vùng/histogram, áp dụng cho cả `takePhoto()` và canvas, không chỉ ảnh gần đen. |
| 3. Auto enhance | Trung bình–cao | Ảnh sáng hơn nhưng có thể tăng noise/sai màu; cần lưu ý tính xác thực ảnh | Chỉ dùng mức nhẹ, có guard và so sánh trước/sau. Không nên tăng sáng cố định mọi ảnh. |
| 4. Hướng dẫn/chụp lại | Thấp | Thêm một bước khi ngược sáng | Nên kết hợp: cảnh báo “Mặt đang tối, xoay khỏi nguồn sáng hoặc chụp lại”, vẫn cho quản lý override nếu nghiệp vụ cần. |

Hướng ưu tiên đề xuất:

1. Thống nhất camera helper cho Dashboard và Yêu cầu để tránh ba implementation khác nhau.
2. Thêm warm-up và capability-based constraints có fallback an toàn.
3. Chạy detector trên ảnh cuối cùng sau `takePhoto()`/canvas và trước upload; hiển thị cảnh báo/chụp lại.
4. Chỉ sau khi có dữ liệu thực tế mới cân nhắc auto-enhance gamma/tone curve nhẹ.

## 8. Estimate ảnh hưởng

- Constraints/warm-up dùng chung: khoảng 1–2 ngày gồm kiểm thử Android Chrome và iOS Safari.
- Detector độ sáng + UX chụp lại: khoảng 1–2 ngày, cần hiệu chỉnh ngưỡng bằng ảnh thực tế.
- Auto-enhance có kiểm soát: khoảng 2–4 ngày cộng thời gian QA màu/noise và xác minh yêu cầu lưu ảnh chứng cứ.
- Test matrix tối thiểu: Android Chrome camera trước/sau, iOS Safari camera trước/sau, ánh sáng đều, trong nhà, ngược cửa sổ và ngoài trời ngược nắng.

## 9. Có cần sửa code không?

**Có**, nếu hiện tượng tái hiện trên preview sau capture. Không nên sửa backend upload/CSS vì không có bằng chứng hai lớp này làm tối ảnh. Bản sửa tiếp theo nên tập trung vào camera startup/exposure capability và validation ảnh cuối trước upload.

Vòng này chỉ điều tra và tạo báo cáo; không sửa source, không commit, không push và không tạo migration.
