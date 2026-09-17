# Construction Map Tile Loading Report

## 1. Active thumbnail count trước fullscreen

Chưa đo được giá trị runtime trên Dashboard có đăng nhập. Source mới ghi `activeThumbnailMapsBeforeFullscreen` vào log `[ConstructionMap] fullscreen isolation` ngay trước khi destroy để phiên DevTools thực tế có thể ghi nhận chính xác.

## 2. Active thumbnail count khi fullscreen mở

Theo invariant của source mới phải bằng `0`: observer được disconnect và `destroyAllConstructionMapThumbs()` chạy trước khi fullscreen map được tạo. Chưa xác nhận bằng browser/session thật; log runtime có trường `activeThumbnailMaps` để phát hiện vi phạm.

## 3. Số primary tile load

Chưa có số runtime vì không có browser/session đăng nhập. `tileload` hiện tăng `constructionTileLoadCount`; sự kiện `load` ghi tổng qua `[ConstructionMap] tile` với provider `primary`.

## 4. Số primary tile error

Chưa có số runtime. `tileerror` tăng `constructionTileErrorCount`; từ lỗi thứ ba trong cùng map instance sẽ chuyển fallback đúng một lần.

## 5. HTTP status/error thực tế

BLOCKED: công cụ browser không khả dụng trong phiên và repository không có credential đăng nhập hợp lệ. Vì yêu cầu bắt buộc đo đúng DevTools Dashboard, báo cáo không dùng kết quả curl thay thế và không ghi PASS cho Network. Cần ghi status 200/404/429/5xx/canceled/pending hoặc lỗi trình duyệt từ phiên triển khai.

## 6. Fallback có kích hoạt không

Chưa quan sát runtime. Source chỉ kích hoạt fallback khi primary có ít nhất 3 `tileerror`; một lỗi đơn lẻ không chuyển provider.

## 7. Fallback load count

Chưa có số runtime. `constructionFallbackTileLoadCount` đếm `tileload` sau khi chuyển sang `https://tile.openstreetmap.org/{z}/{x}/{y}.png`.

## 8. DOM tile tại vùng trắng

BLOCKED: chưa inspect được vùng trắng trong browser có đăng nhập, nên chưa thể kết luận vùng đó có hay không có `<img class="leaflet-tile">`.

## 9. CSS computed width/height của tile

Chưa đọc computed style runtime. Static inspection phát hiện `.construction-request-card__map-thumb img` từng áp `width: 100%`, `height: 100%`, `object-fit: cover` lên cả Leaflet tile của thumbnail. Rule đã đổi thành child selector `> img`; `.leaflet-tile` trong thumbnail được giữ `256px x 256px`, bỏ giới hạn max-size và reset `object-fit`.

## 10. Nguyên nhân cuối cùng

Chưa đủ bằng chứng browser để kết luận nguyên nhân cuối cùng của block trắng fullscreen. Hai lỗi source đã xác nhận và sửa là: thumbnail map đã init vẫn tồn tại/cạnh tranh request khi fullscreen mở, và CSS ảnh card va chạm với tile thumbnail. Fullscreen nay là Leaflet map Công trình duy nhất trong lúc popup mở; primary tự chuyển fallback sau ngưỡng lỗi. Acceptance vẫn BLOCKED cho đến khi YC-2600001 được test thực tế không còn block trắng.

## 11. File sửa

- `Views/Home/Index.cshtml`
- `wwwroot/css/site.css`
- `CONSTRUCTION_MAP_TILE_LOADING_REPORT.md`

## 12. Build result

`dotnet build apptech-dashboard.sln -c Release`: thành công, 0 warning, 0 error.

## 13. Commit SHA

Source: `c7ec8d97089ea076022989e9166302b157d8e6c4` - `fix: isolate construction fullscreen tile loading`.

## 14. Push status

Source commit đã push thành công lên `origin/main` (`6001827..c7ec8d9`). Report được commit và push tiếp theo trên cùng branch. `bin/`, `obj/`, `publish/`, `artifacts/publish/` và DataProtection keys không được commit.
