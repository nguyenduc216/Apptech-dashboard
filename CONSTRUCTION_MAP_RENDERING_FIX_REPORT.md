# Construction Map Rendering Fix Report

## 1. Root cause thực tế

Dashboard giữ một `constructionMap` singleton qua nhiều lần đóng/mở modal. Map được tái sử dụng sau khi container từng bị `hidden`, trong khi kích thước được sửa bằng chuỗi timeout 40/140/320/700 ms. Cách này có thể giữ tile grid/viewport cũ và tạo các vùng tile trắng dù marker và tọa độ vẫn đúng.

## 2. Vì sao Dashboard lỗi nhiều hơn màn hình chức năng

Dashboard vừa có các mini-map trên card vừa có map fullscreen, đồng thời fullscreen dùng lifecycle singleton đặc thù. `YeuCau/Detail` và `KhachHang/Detail` dùng cùng HOT OSM provider nhưng không có tổ hợp nhiều thumbnail và lifecycle fullscreen này.

## 3. Số Leaflet instance trên Dashboard

Fullscreen chỉ có tối đa 1 instance khi modal đang mở và được huỷ khi đóng. Thumbnail có 1 instance cho mỗi card đã thực sự đi vào vùng quan sát; số lượng cụ thể phụ thuộc số card visible, không còn khởi tạo trước toàn bộ danh sách.

## 4. Ảnh hưởng của thumbnail tới tile request

Có. Mỗi thumbnail tạo tile request riêng. Observer nay chỉ theo dõi thumbnail chưa khởi tạo với `rootMargin: 40px`, unobserve ngay sau init, disconnect khi fullscreen mở và đồng bộ lại khi fullscreen đóng. Thumbnail đã visible không bị destroy.

## 5. Kết quả Network tile request

Kiểm tra GET trực tiếp ngày 2026-09-17 cho cùng tile `/hot/6/51/29.png` trên ba host `a`, `b`, `c.tile.openstreetmap.fr`: cả ba trả HTTP 200, `image/png`, 18,689 bytes. Không có phiên browser đăng nhập sẵn, nên chưa thể thu DevTools Network của chính Dashboard để phân loại tuyệt đối A/B/C trong runtime ứng dụng.

## 6. Tileerror

Không ghi nhận `tileerror` từ browser runtime do không có phiên đăng nhập. Source đã bổ sung listener `tileerror`; mỗi lần mở map chỉ cho phép đúng một lần `invalidateSize` + `redraw`, không reload trang và không retry vô hạn.

## 7. Lifecycle trước sửa

Hiện modal, reuse singleton nếu tồn tại, rồi gọi `invalidateSize(true)` tại bốn timeout. Khi đóng chỉ ẩn modal; map, tile layer, observer/layer state không được huỷ đầy đủ.

## 8. Lifecycle sau sửa

Hiện modal trước, dừng observer thumbnail, huỷ instance cũ, chờ `requestAnimationFrame` đến khi container có width/height dương, tạo map/tile/marker mới, `setView`, rồi invalidate ở frame kế tiếp. Khi đóng: clear candidate, disconnect observer/timer, off event, remove map và null toàn bộ reference.

## 9. Recreate map khi mở popup

Có. Mỗi lần `openConstructionMap()` đều đi qua `destroyConstructionMap()` và tạo instance mới sau khi container visible, có kích thước thực tế.

## 10. ResizeObserver

Observer gắn với `constructionMapElement`, debounce 150 ms và gọi `invalidateSize({ animate: false, pan: false, debounceMoveend: true })`. Observer và timer được dọn khi destroy/close.

## 11. Tile retry/redraw

`constructionTileLayer` được quản lý bằng reference riêng. Lần `tileerror` đầu tiên của mỗi instance lên lịch một redraw ở animation frame; cờ `constructionTileRetryUsed` chặn mọi retry tiếp theo.

## 12. CSS đã chỉnh

Không chỉnh CSS và không đổi z-index. Kiểm tra xác nhận canvas đã có `width: 100%`, height cụ thể `min(68dvh, 620px)`, `min-height: 420px`; không có `scale/zoom/transform` trên wrapper map cần sửa.

## 13. File đã sửa

- `Views/Home/Index.cshtml`
- `CONSTRUCTION_MAP_RENDERING_FIX_REPORT.md`

## 14. Kết quả test desktop

Chưa chạy UI tại 1920/1440/1280/1024 vì môi trường không có browser/session đăng nhập. Static review xác nhận container-size guard và ResizeObserver không phụ thuộc breakpoint. Không ghi PASS cho test chưa chạy.

## 15. Kết quả test mobile

Chưa chạy UI tại 430/390/360 vì môi trường không có browser/session đăng nhập. CSS responsive hiện hữu được giữ nguyên. Không ghi PASS cho test chưa chạy.

## 16. Kết quả mở/đóng 10 lần

Chưa chạy UI do không có phiên đăng nhập. Lifecycle source bảo đảm mỗi lần đóng huỷ map và mỗi lần mở tạo map mới; cần kiểm chứng acceptance test bằng tài khoản có dữ liệu `YC-2600001`.

## 17. Build result

`dotnet restore apptech-dashboard.sln`: thành công.  
`dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.

## 18. Test result

Repository không có test project riêng trong solution. Runtime browser tests 1-11 chưa thể chạy vì không có browser/session đăng nhập trong môi trường; kiểm tra HTTP tile độc lập đạt 3/3 phản hồi 200. Các luồng marker, cập nhật tọa độ, OSRM route, Google Maps, audit và permission không bị thay đổi logic.

## 19. Publish path

Publish thành công tại `artifacts/publish/ApptechDashboard`. Đường dẫn này được `.gitignore` loại trừ và không được commit.

## 20. Commit SHA

Source fix: `0b122ee0754695a8423a33db95a22287102c9eec` (`fix: stabilize construction dashboard map rendering`).

## 21. Push status

Source fix đã push thành công lên `origin/main` (`3506fe0..0b122ee`). Report này được commit và push tiếp theo trên cùng branch `main`.
