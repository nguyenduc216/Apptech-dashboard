# Construction Map Thumbnail Lifecycle Report

## Root cause

`loadConstructionRequests()` thay toàn bộ `constructionList.innerHTML` nhưng các thumbnail cũ đã gắn Leaflet map không được gọi `off()` và `remove()`. DOM cũ biến mất trong khi map, event và tile request có thể còn sống. Ngoài ra, cập nhật tọa độ trong fullscreen có thể khởi tạo thumbnail nền ngay lập tức.

## Thay đổi

- Thêm `destroyConstructionMapThumb(element)` và `destroyAllConstructionMapThumbs()`.
- Disconnect `constructionThumbnailObserver` và destroy mọi thumbnail trước tất cả nhánh thay `constructionList.innerHTML`.
- Disconnect thumbnail observer khi đóng modal Công trình.
- Không init thumbnail trong `updateConstructionCardCoordinates()` khi fullscreen đang mở; `syncConstructionMapThumbs()` xử lý sau khi đóng.
- Debounce redraw sau `tileerror` 250 ms, vẫn giới hạn đúng một lần cho mỗi fullscreen map instance; timer được clear khi destroy.

## File sửa

- `Views/Home/Index.cshtml`
- `CONSTRUCTION_MAP_THUMBNAIL_LIFECYCLE_REPORT.md`

## Thumbnail instance trước và sau refresh

- Trước sửa: DOM hiện tại có thể chứa `N` thumbnail, nhưng số Leaflet instance tích luỹ có thể lớn hơn `N` sau nhiều lần refresh/đổi nhân viên vì instance cũ không được remove.
- Sau sửa: trước mỗi replacement, toàn bộ instance thuộc DOM cũ được `off()` và `remove()`. Sau render, số instance active chỉ tương ứng các thumbnail của DOM mới đã thực sự đi vào vùng IntersectionObserver; không tích luỹ qua refresh.
- Chưa đo heap/runtime qua 10 vòng do môi trường không có browser và session đăng nhập. Không ghi PASS cho bài test chưa chạy.

## Network tile result

GET trực tiếp cùng tile HOT OSM trên `a`, `b`, `c.tile.openstreetmap.fr` đều trả HTTP 200, `image/png`, 18,689 bytes. Chưa thu được DevTools Network của Dashboard có đăng nhập, nên chưa xác nhận định lượng request qua các kịch bản A-H. Source bảo đảm không init thumbnail nền khi cập nhật tọa độ trong fullscreen và observer được disconnect trong thời gian fullscreen mở.

## Build result

`dotnet build apptech-dashboard.sln -c Release`: thành công, 0 warning, 0 error.

## Commit SHA

`b8f5acc2586b4da1229f98f9f2b40cba9c5afe8e` - `fix: clean up construction map thumbnail lifecycle`

## Push status

Source commit đã push thành công lên `origin/main`. Report được commit và push tiếp theo trên cùng branch. Publish artifacts, `bin/` và `obj/` không được commit.
