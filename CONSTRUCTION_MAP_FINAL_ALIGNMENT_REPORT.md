# Construction Map Final Alignment Report

## 1. Implementation Khách hàng dùng làm chuẩn

Dashboard được căn theo viewer trong `Views/KhachHang/Detail.cshtml`: DOM `crud-form` + `khach-hang-location-viewer-body` + `khach-hang-location-viewer-map-shell`, map dùng hai class `khach-hang-location-map khach-hang-location-viewer-map`; Leaflet singleton được tạo một lần, HOT OSM tile layer, `setView()` và `invalidateSize()` sau 100–120 ms.

## 2. Code Dashboard đã bỏ

Đã bỏ destroy/recreate fullscreen map, size guard, ResizeObserver, rAF retry, tile layer reference, counters, tileerror fallback, provider switch và toàn bộ diagnostic riêng của fullscreen.

## 3. Code Dashboard đã reuse

Fullscreen reuse đúng class DOM, control class, close button style, singleton lifecycle, provider và tile options của map Khách hàng. Logic nghiệp vụ tọa độ, marker, audit, permission, Google Maps và OSRM được giữ nguyên. Cleanup/IntersectionObserver của thumbnail vẫn được giữ.

## 4. CSS đã bỏ

Đã bỏ `.construction-map-shell .construction-map-modal`, `.construction-map-canvas` và `.construction-map-canvas.leaflet-container`.

## 5. CSS dùng chung

Dashboard nhận trực tiếp các rule `.khach-hang-location-map`, `.khach-hang-location-viewer-map`, `.khach-hang-location-viewer-map-shell`, `.khach-hang-location-viewer-map-actions` và các rule chung cho Leaflet pane/tile/layer. Nút map dùng `menu-nav-button khach-hang-location-map-action`, chỉ còn icon và có `aria-label`/`title`.

## 6. Computed style tile Khách hàng

BLOCKED: không có browser/session đăng nhập để đọc computed style runtime. Source chung bảo vệ `.leaflet-tile` bằng `max-width: none !important` và `max-height: none !important`; kích thước chuẩn do Leaflet stylesheet quản lý.

## 7. Computed style tile Dashboard

BLOCKED cho đo runtime. Dashboard hiện dùng chính class map và selector Leaflet của Khách hàng, không còn construction-specific tile override.

## 8. Map-pane transform hai bên

BLOCKED: chưa inspect được `translate3d(...)` trong DevTools production. Hai map hiện dùng cùng `.khach-hang-location-viewer-map` và cùng pane CSS source.

## 9. DOM tile vùng trắng

BLOCKED: chưa có browser/session và dữ liệu YC-2600001 để xác nhận vùng trắng có `<img class="leaflet-tile">` hay không.

## 10. Network status tile

BLOCKED: chưa thể thu Network trong đúng browser/session production. Không dùng kiểm tra curl thay cho acceptance này.

## 11. Loaded CSS version sau deploy

Chưa deploy/IIS nên chưa xác minh response production. Artifact `site.css` trùng source với SHA-256 `4541BB1779387D4B22F9BB93836AC15C29DFF6CAA3F34FCD0F5D6F3952E33257`; layout dùng `asp-append-version="true"` để sinh query version khi chạy.

## 12. Loaded JS/HTML version

Chưa thể xác minh DevTools Sources sau deploy. Razor view mới đã được compile vào artifact Release; deployed commit dự kiến là source commit bên dưới.

## 13. F5 test

BLOCKED vì không có browser/session đăng nhập.

## 14. Ctrl+F5 test

BLOCKED vì không có browser/session đăng nhập.

## 15. Mở/đóng 10 lần

BLOCKED. Source hiện reuse đúng một fullscreen map instance như viewer Khách hàng; chưa ghi PASS cho runtime.

## 16. GPS test

BLOCKED cho browser/geolocation. Static review xác nhận candidate marker, confirm, POST backend, cập nhật marker chính và audit vẫn còn nguyên.

## 17. Route test

BLOCKED cho browser/geolocation/OSRM. Static review xác nhận route vẫn `fitBounds()` và gọi `invalidateSize()` sau 100 ms.

## 18. Build result

`dotnet build apptech-dashboard.sln -c Release`: thành công, 0 warning, 0 error.

## 19. Publish result

Thành công tại `artifacts/publish/ApptechDashboard`. Output cũ bị lồng đệ quy đã được chuyển sang `.codex-tmp` (ignored), sau đó publish lại sạch không warning.

## 20. Commit SHA

Source: `22e2b5f3be1a55092db39cbdaf39781c13e973c6` - `fix: align construction map with customer location viewer`.

## 21. Push status

Source commit đã push thành công lên `origin/main` (`a5dfd18..22e2b5f`). Report được commit và push tiếp theo trên cùng branch. Acceptance “không còn vùng trắng tile” chưa đạt trạng thái xác nhận cho tới khi hoàn tất test production cùng browser/session.
