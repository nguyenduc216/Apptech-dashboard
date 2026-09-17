# Construction Map vs Customer Map Runtime Comparison

## Trạng thái

- **SOURCE INSTRUMENTED:** Có.
- **RUNTIME VERIFIED:** Không.
- **ROOT CAUSE ESTABLISHED:** Không.
- **ACCEPTANCE:** Không PASS.

Môi trường hiện tại không có browser/session đăng nhập để mở đồng thời Customer map và Dashboard map. Vì yêu cầu bắt buộc bằng chứng runtime, báo cáo không thay số đo bằng suy luận source.

## 1. Customer map DOM size

Chưa có số runtime. Khi mở viewer Khách hàng, console mới ghi `[CustomerMapComparison].rect` gồm `width`, `height`, `top`, `left`.

## 2. Dashboard map DOM size

Chưa có số runtime. Console ghi `[ConstructionMapLifecycle]` ngay sau khi shell visible và `[DashboardConstructionMapComparison].rect` sau 1500 ms. Source fullscreen vẫn là `100vw x 100dvh` với canvas `100% x 100%`.

## 3. Customer map.getSize()

Chưa có số runtime. Đọc tại `[CustomerMapComparison].mapSize` và so với `clientSize`.

## 4. Dashboard map.getSize()

Chưa có số runtime. `[ConstructionMapLifecycle]` ghi `before`, `after`, DOM rect và cờ `invalidated`. `invalidateSize()` chỉ chạy khi internal size lệch DOM hơn 1 px.

## 5. Customer tile DOM count

Chưa có số runtime. `[CustomerMapComparison].tiles` ghi `total`, `loaded`, `missing`.

## 6. Dashboard tile DOM count

Chưa có số runtime. `[DashboardConstructionMapComparison].tiles` và `[ConstructionMapDiagnostics]` ghi cùng các bộ đếm sau 1500 ms.

## 7. Customer tile computed style

Chưa có số runtime. Helper chung ghi width, height, max-size, position, left/top, transform, object-fit, display, opacity, visibility và z-index của tile đầu tiên.

## 8. Dashboard tile computed style

Chưa có số runtime. Helper chung ghi cùng cấu trúc với Customer để diff 1-1. Static CSS giữ tile Dashboard ở 256x256, `max-width/max-height: none` và `object-fit: initial`.

## 9. Map-pane transform hai bên

Chưa có số runtime. Hai log comparison chứa transform của `.leaflet-map-pane`, `.leaflet-tile-pane` và `.leaflet-tile-container`.

## 10. Network status hai bên

Chưa có DevTools Network trong cùng browser/session. Dashboard log URL HOT OSM chính xác tại `tileloadstart`, `tileload`, `tileerror`, `load`. Fallback provider đã bị xoá; source chỉ dùng `https://{s}.tile.openstreetmap.fr/hot/{z}/{x}/{y}.png`.

## 11. Parent CSS khác biệt

Chưa có computed runtime. Helper ghi toàn bộ parent chain từ `.leaflet-container` tới `body`: display, position, size, min/max-height, overflow, transform, contain, zoom, visibility, opacity, flex, grid rows và align-items.

## 12. Modal nesting khác biệt

Customer viewer và Construction fullscreen có modal shell riêng. Construction được mở khi modal Checkin Công trình vẫn visible, nhưng hai shell là sibling trong DOM chứ không phải map shell nằm bên trong modal cha. `body.modal-open` được giữ. Ảnh hưởng computed thực tế của hai shell chưa được xác nhận; parent-chain log sẽ chỉ ra node khác biệt.

## 13. Root cause chính xác

**CHƯA XÁC LẬP.** Không có dữ liệu runtime để phân loại CASE A (không có tile DOM), CASE B (request lỗi) hoặc CASE C (tile load nhưng CSS/layer che/lệch). Ghi một nguyên nhân cụ thể lúc này sẽ là bịa bằng chứng và trái yêu cầu.

## 14. Code đã sửa

- Xoá hoàn toàn fallback `tile.openstreetmap.org`; giữ duy nhất HOT OSM.
- Xoá các timeout `invalidateSize()` mù của Construction.
- Chỉ invalidate khi `map.getSize()` thực sự lệch DOM size.
- Thêm lifecycle log trước ensure, trước setView và sau setView.
- Thêm helper diagnostics dùng chung cho Customer/Dashboard.
- Thêm tile count/style, pane transform, parent-chain và map-instance diagnostics.
- Giữ fullscreen, icon-only controls, thumbnail cleanup, GPS, audit, route và Google Maps.

## 15. Popup fullscreen result

**SOURCE VERIFIED:** DOM/CSS là `100vw x 100dvh`, không max-size, canvas cao/rộng 100%. **RUNTIME NOT VERIFIED:** chưa đo viewport thực tế.

## 16. Runtime result

**RUNTIME NOT VERIFIED.** Cần mở cùng browser/session, thu hai log `CustomerMapComparison` và `DashboardConstructionMapComparison`, Network tile status, sau đó test fullscreen/zoom/GPS/route/Google Maps và 10 vòng mở đóng.

## 17. Build

`dotnet build apptech-dashboard.sln -c Release`: thành công, 0 warning, 0 error. Publish profile IIS cũng thành công tại `publish/iis`.

## 18. Commit SHA

Source instrumentation: `2a286cfde6562b7395e8edbeb875ffda8b339cf4` - `fix: instrument customer and construction map comparison`.

## 19. Push status

Source commit đã push thành công lên `origin/main` (`ab7bfc6..2a286cf`). Report được commit và push tiếp theo trên cùng branch. Output build/publish và DataProtection keys không được commit.
