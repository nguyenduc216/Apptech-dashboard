# Construction Fullscreen Map Root Cause Report

## Trạng thái

- **SOURCE FIXED:** Có.
- **RUNTIME VERIFIED:** Không. Môi trường công cụ hiện tại không có browser/session (`browsers: []`) và repository không cung cấp credential đăng nhập.
- **ACCEPTANCE:** Chưa đạt trạng thái xác nhận. Không tuyên bố hết block trắng khi chưa test YC-2600001 sau deploy.

## 1. Root cause popup nhỏ

Commit `22e2b5f` bỏ `construction-map-shell`, `construction-map-modal` và không gắn `data-location-view-shell`, khiến Construction rơi về kích thước generic `crud-modal`. Fullscreen Chấm công thực tế dựa vào modal stack `data-location-view-shell` cùng các class fullscreen riêng. Markup Construction đã được khôi phục theo đúng pattern đó.

## 2. Root cause tile trắng

Chưa đủ dữ liệu runtime để kết luận nguyên nhân cuối cùng. Source trước sửa có DOM/CSS/lifecycle khác fullscreen Chấm công và từng chịu selector ảnh card va vào tile thumbnail. Source mới đồng bộ fullscreen sizing/pane/tile CSS, mở map sau khi shell visible, đồng thời thêm log để xác định `total = 0` (grid/viewport) hay `total > loaded` (network/image). Chỉ runtime mới chốt được root cause cuối cùng.

## 3. CSS fullscreen mới

`construction-map-fullscreen-shell/modal/body/canvas` dùng `100vw x 100dvh`, không max-size, không border-radius, header cố định và canvas chiếm toàn bộ phần còn lại. Action button là icon-only 42x42. Tile được giữ 256x256, `max-width/max-height: none`, `object-fit: initial`; pane/layer theo cùng positioning với fullscreen Chấm công. Audit/status/confirm là overlay; confirm tự scroll, không làm co canvas.

## 4. Map size runtime

**RUNTIME NOT VERIFIED.** Watchdog sau 1500 ms log `clientWidth`, `clientHeight`, `boundingWidth`, `boundingHeight`, zoom và center qua `[ConstructionMapDiagnostics]`.

## 5. Tile total

**RUNTIME NOT VERIFIED.** Watchdog đếm `.leaflet-tile` và log trường `total`.

## 6. Tile loaded

**RUNTIME NOT VERIFIED.** Watchdog đếm `.leaflet-tile-loaded` và log `loaded`/`missing`.

## 7. Tile error

**RUNTIME NOT VERIFIED.** Listener `tileerror` log từng lỗi. Mỗi URL được retry đúng một lần; fallback chỉ kích hoạt khi ít nhất ba URL vẫn lỗi sau retry.

## 8. Tile URL lỗi

Chưa có URL lỗi thực tế. Log `[ConstructionMapTile]` chứa `event`, `provider`, `src`, map size, zoom và center cho `tileloadstart`, `tileload`, `tileerror`, `load`.

## 9. Service worker cache result

Navigation đang network-first; `/css/site.css` và `/js/dashboard.js` cũng network-first. Static cache đã tăng từ `apptech-static-v3` lên `apptech-static-v4`; activate xoá cache version cũ. Layout dùng `asp-append-version="true"` cho `site.css` và `dashboard.js`.

## 10. Asset hash production

Chưa có quyền đọc IIS production thực tế. Trong output profile IIS local:

- `publish/iis/wwwroot/css/site.css`: `8EB90619E1CCFF87670839A4A2C3A697B071981386FA9CAF0A2E11EA5C116BED` (trùng source).
- `publish/iis/wwwroot/service-worker.js`: `948861C22DC13E4479FFF9684D226E29D8FD5FABBE979E5D9CE57E3E24DA729F` (trùng source).
- `publish/iis/ApptechDashboard.dll`: `9A5EAC25F7450CDA8CAED736AEF5B92A6FB5472D316E046B9AEAA17180422A5E`.

## 11. So sánh Chấm công và Construction

Source hai fullscreen hiện cùng modal stack, HOT OSM primary provider/options, Leaflet singleton, `attributionControl: false`, shell-visible trước init, rAF/invalidate, pane positioning và icon action size. Construction giữ thêm nghiệp vụ GPS candidate, cập nhật tọa độ/audit và diagnostic per-tile. Thumbnail vẫn được destroy khi fullscreen mở và sync lại khi đóng.

## 12. F5 result

**RUNTIME NOT VERIFIED.**

## 13. Ctrl+F5 result

**RUNTIME NOT VERIFIED.**

## 14. Open/close 10 lần

**RUNTIME NOT VERIFIED.** Fullscreen map được reuse; close dọn route, GPS candidate, diagnostic timer và khởi tạo lại thumbnail visible.

## 15. GPS result

**RUNTIME NOT VERIFIED.** Static review xác nhận GPS candidate, confirm, POST Lat/Long, marker chính và audit vẫn còn.

## 16. Route result

**RUNTIME NOT VERIFIED.** Static review xác nhận OSRM route, `fitBounds()` và invalidate sau route vẫn còn.

## 17. Build result

`dotnet build apptech-dashboard.sln -c Release`: thành công, 0 warning, 0 error.

## 18. Publish result

`dotnet publish ApptechDashboard.csproj /p:PublishProfile=IIS --no-restore`: thành công, không warning. `DefaultItemExcludes` đã bổ sung `artifacts/publish/**` để tránh output cũ bị lồng đệ quy.

## 19. Deployed path

Output local theo profile IIS: `publish/iis`. Repository không chứa cấu hình/path hoặc quyền truy cập IIS production khác, nên chưa copy/recycle application pool trên server thực tế.

## 20. Commit SHA

Source: `b3ba2b55ff12718c7ea82b5a0c5703f1978afcc5` - `fix: rebuild construction fullscreen map rendering`.

## 21. Push status

Source commit đã push thành công lên `origin/main` (`e38de21..b3ba2b5`). Report được commit và push tiếp theo trên cùng branch. Build/publish/DataProtection outputs không được commit.
