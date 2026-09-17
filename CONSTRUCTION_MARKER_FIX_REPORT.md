# Construction Marker Fix Report

## 1. Marker src cũ

Marker chính dùng `L.marker(point)` mặc định. Với Leaflet CSS tải từ `https://unpkg.com/leaflet@1.9.4/dist/leaflet.css`, các asset mặc định được resolve tới:

- `https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png`
- `https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png`
- `https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png`

Không có browser/session để đọc chính xác `src/currentSrc` của marker lỗi trên production.

## 2. Asset có phải 404 không

Chưa xác nhận được request trong browser production. Kiểm tra HTTP độc lập ngày 2026-09-17 cho cả ba URL đều trả 200 (lần lượt 1,466 / 2,464 / 618 bytes), vì vậy không có bằng chứng để kết luận asset CDN đang 404. Construction marker mới không còn phụ thuộc các PNG này.

## 3. CSS marker conflict

Có. Selector fullscreen Construction gom `.leaflet-marker-icon` và `.leaflet-marker-shadow` vào nhóm bị áp `left: 0 !important; top: 0 !important`. Hai selector marker đã được tách khỏi nhóm này để Leaflet tự quản lý tọa độ/transform.

## 4. Custom divIcon implementation

Marker chính dùng `L.divIcon()` với Font Awesome `fa-location-dot`, kích thước `36x44`, anchor `[18, 42]`, popup anchor `[0, -38]`. CSS tạo pin xanh AppTech, viền trắng và shadow; `bindPopup()` được giữ nguyên.

## 5. Candidate marker implementation

GPS candidate trước đây là `L.circleMarker()` và không phụ thuộc ảnh. Theo yêu cầu, candidate nay dùng `L.divIcon()` riêng với `fa-location-crosshairs`, cùng anchor và pin màu xanh lục. `setLatLng()` và luồng xác nhận/POST tọa độ không đổi.

## 6. Build result

`dotnet build apptech-dashboard.sln -c Release`: thành công, 0 warning, 0 error.

Runtime YC-2600001, zoom/pan, GPS, route và 10 vòng mở/đóng chưa được chạy trong phiên này vì không có browser/session đăng nhập; không ghi PASS cho các bài test đó.

## 7. Commit SHA

`6410f821848c93481d67cd269335e744cba34d6e` - `fix: stabilize construction map markers`.

## 8. Push status

Source commit đã push thành công lên `origin/main` (`ee64b01..6410f82`). Report được commit và push tiếp theo trên cùng branch. Không có build/publish artifact nào được commit.
