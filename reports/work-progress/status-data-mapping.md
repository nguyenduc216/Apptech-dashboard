# Mapping dữ liệu trạng thái công việc bằng text

FEATURE_ID: `APPTECH-WORK-PROGRESS-KANBAN`

CHANGE_ID: `APPTECH-20261020-WORK-PROGRESS-002`

## Trang tiến độ bị vòng lặp đăng nhập — change 005

Nếu đã verify SQL PASS nhưng trình duyệt báo `ERR_TOO_MANY_REDIRECTS` tại `/dang-nhap?ReturnUrl=%2FTienDoCongViec`, dùng package publish mới của change 005. Bản này sửa cấu hình AccessDenied/Login và đồng bộ cách nhận admin từ bản ghi tài khoản đang active. Không cần chạy lại các migration SQL để áp dụng bản sửa này.

Triển khai các file ứng dụng mới lên môi trường IIS cần kiểm tra, giữ `appsettings*.json`, uploads và khóa Data Protection của server, rồi recycle app pool theo quy trình hiện có. Mở lại `/TienDoCongViec` bằng tài khoản admin. Tài khoản nhân viên thực sự thiếu quyền sẽ thấy trang 403 `/khong-co-quyen`; AJAX nhận 403 JSON trực tiếp. Cấp `TienDoCongViec_View`/`Update` qua vai trò hiện có chỉ cho người cần dùng, không tự cấp rộng để xử lý lỗi vòng lặp.

## Verify một báo cáo duy nhất — change 004

Mở `sql/20261020_work_progress_verify.sql` bản mới và chạy toàn bộ file. Không cần chọn/chạy riêng từng SELECT. Script chỉ đọc dữ liệu ứng dụng, trả **một bảng Results duy nhất** gồm `STT`, `KiemTra`, `CauLenhSQL`, `ThucTe`, `KyVong`, `KetQua`, `GhiChu`. Mỗi dòng có câu SQL SELECT có thể chạy riêng để đối chiếu. Tab **Messages** cũng in từng câu SQL và kết quả, kết thúc bằng `KẾT LUẬN CUỐI CÙNG: PASS` hoặc `FAIL`.

- `PASS`: kiểm tra đạt đúng kỳ vọng.
- `FAIL`: sai kỳ vọng, thiếu bảng/cột hoặc câu kiểm tra gặp lỗi; lỗi được ghi rõ trong `GhiChu` thay vì làm mất phần kết luận.
- `INFO`: dữ liệu tham khảo, không quyết định PASS/FAIL. Ví dụ các công việc lịch sử đã hoàn thành nhưng chưa rõ ngày hoàn thành được phép để trống.
- Dòng cuối `KẾT LUẬN CUỐI CÙNG` chỉ PASS khi tất cả kiểm tra bắt buộc đều đạt và không có lỗi truy vấn. Kết luận này là kết quả kiểm tra SQL, không thay thế nghiệm thu UI/phân quyền thực tế.

Mặc định `@CheckOriginalSnapshot=1` đối soát đúng tổng 53, mới 39, hoàn thành 14, orphan 13, có phiếu hợp lệ 40, sổ ghi nhận 13. Nếu dữ liệu đã thay đổi hợp lệ sau vận hành, đối chiếu thay đổi rồi đặt `@CheckOriginalSnapshot=0`: các dòng số lượng này thành INFO, còn kiểm tra schema/mapping/ngày/orphan chưa duyệt vẫn bắt buộc PASS.

## Tiếp tục bước 2 sau khi đã chọn giữ lịch sử orphan — change 003

Người dùng đã xác nhận chọn cách 2 cho 13 công việc thiếu 8 phiếu cha. Bản mới của `App_Data/Migrations/20261020_work_progress_kanban.sql` cho phép giữ nguyên **đúng danh sách đã duyệt**, không xóa hoặc chuyển các công việc này sang phiếu khác:

```text
5036, 5037, 5038, 5048, 5049, 5050, 5051,
5052, 5055, 5056, 6026, 6027, 6028
```

Để tiếp tục trên database đang dừng ở lỗi orphan:

1. Mở **bản mới nhất** của `20261020_work_progress_kanban.sql` từ checkout/PR, thay nội dung query cũ trong SSMS. Không chỉ bỏ dòng kiểm tra orphan.
2. Sau khi đã đối chiếu preflight như bước 2 trước đó, đặt `@SchemaReviewed=1`. Giữ `@PreserveReviewedOrphans=1` (đã mặc định bật theo quyết định của người dùng), rồi chạy toàn bộ riêng script schema.
3. Script kiểm tra cả ID công việc, ID phiếu thiếu, ID loại công việc và text trạng thái so với danh sách đã gửi. Nếu khớp, các công việc/nhân viên/checklist/ảnh ở nguyên bảng và liên kết hiện có; chỉ ghi thêm sổ đối chiếu `TblWorkProgressLegacyOrphan`. `ReviewedAt` là thời điểm ghi nhận quyết định lưu trữ, không phải ngày hoàn thành công việc. Nếu có orphan mới hoặc thông tin trong danh sách đã thay đổi, script dừng và rollback.
4. Khi bước 2 thành công, tiếp tục migration dữ liệu và verify như hướng dẫn phía dưới. `@RequireOriginalSnapshot=1` vẫn đối soát toàn bộ **53** công việc (39 mới / 14 hoàn thành), gồm cả 13 dòng lịch sử.

Với đúng bộ dữ liệu người dùng đã gửi:

| Nhóm | Tạo mới | Hoàn thành | Tổng |
|---|---:|---:|---:|
| Có phiếu cha hợp lệ — xuất hiện trên Kanban | 28 | 12 | 40 |
| Thiếu phiếu cha — giữ lịch sử, không hiện Kanban thường | 11 | 2 | 13 |
| Toàn bộ dữ liệu được bảo toàn | 39 | 14 | 53 |

Kanban đã dùng INNER JOIN với phiếu yêu cầu, nên 13 dòng thiếu phiếu không được đưa vào board hoặc API kéo thả. Không tạo phiếu cha giả và không tự điền ngày hoàn thành cho hai công việc lịch sử đã hoàn thành. Báo cáo verify mới có dòng đếm riêng số công việc thiếu phiếu và có phiếu hợp lệ; dòng “Orphan mới hoặc khác hồ sơ đã duyệt” phải có thực tế bằng 0 và PASS.

Ngoại lệ này chỉ dành cho danh sách đã duyệt, không cho phép chung mọi dữ liệu orphan. Nếu migration đã được cài và chạy lại, orphan mới/khác sổ ghi nhận vẫn bị chặn. Không thực thi thay đổi trên database thực tế từ phiên Codex này.

Giữ hợp đồng liên kết đã triển khai:

```text
TblYeuCauCongViec.TrangThaiCongViec
    → TblTrangThaiCongViec.StorageValue
```

`StorageValue` là khóa text cố định. `Name` là tên hiển thị có thể thay đổi. `ID` vẫn là khóa chính của danh mục và được API Kanban sử dụng để chọn trạng thái; không bổ sung cột ID trạng thái vào dữ liệu công việc.

Kết quả người dùng cung cấp qua ảnh:

| Text hiện có | Số byte | Số công việc | Code danh mục | Loại nghiệp vụ |
|---|---:|---:|---|---|
| Tạo mới | 14 | 39 | NEW | Normal |
| Hoàn thành | 20 | 14 | COMPLETED | Completed |

Đây là ảnh chụp dữ liệu tại một thời điểm, chưa phải kết quả truy vấn trực tiếp trong phiên làm việc này. Số lượng có thể thay đổi khi người dùng tiếp tục thao tác.

Danh mục chuẩn vẫn có đủ 5 trạng thái: `NEW`, `IN_PROGRESS`, `REVIEW`, `COMPLETED`, `CANCELLED`. Ba trạng thái còn lại được giữ để dùng cho các công việc sau này, dù chưa có dữ liệu lịch sử.

## Cách chạy

1. Sao lưu database và chạy `sql/20261020_work_progress_preflight.sql`. Phần bổ sung cuối script hiển thị mapping text dự kiến cùng đối soát 39/14. Các giá trị trống, lạ hoặc khác byte Unicode sẽ hiện yêu cầu kiểm tra; không tự đổi về “Tạo mới”. Đây là script chỉ đọc, chạy được trước khi có danh mục.
2. Nếu chưa triển khai schema Kanban: kiểm tra schema/ngày/rowversion/menu như hướng dẫn chính, rồi chạy bản đã được duyệt của `App_Data/Migrations/20261020_work_progress_kanban.sql`. Giữ chốt `@SchemaReviewed` cho tới khi đã kiểm tra. Nếu schema đã cài đầy đủ thì bỏ qua bước này.
3. Chạy `App_Data/Migrations/20261020_work_progress_status_data.sql` với cấu hình mặc định:

   ```sql
   DECLARE @ApplyChanges bit=0;
   DECLARE @RequireOriginalSnapshot bit=0;
   ```

   Đây là **xem trước**, không ghi dữ liệu. Kiểm tra các bảng kết quả: kế hoạch seed, text/số byte/số lượng/Code mapping và so sánh ảnh 39/14. Giá trị `KEEP_EXISTING` giữ nguyên tên, màu, kích hoạt, thứ tự và timestamp đang cấu hình. `INSERT_MISSING` là mục chuẩn chưa có. `CONFLICT_REVIEW_REQUIRED` hoặc `UNMAPPED_REVIEW_REQUIRED` cần xử lý rõ ràng trước khi áp dụng.
4. Khi mapping đã đúng, chạy lại chính script với:

   ```sql
   DECLARE @ApplyChanges bit=1;
   DECLARE @RequireOriginalSnapshot bit=1;
   ```

   Chế độ này yêu cầu đúng 39 “Tạo mới” và 14 “Hoàn thành”, không có trạng thái khác. Nếu dữ liệu đã thay đổi hợp lệ, đối soát lại dữ liệu hiện tại rồi dùng `@RequireOriginalSnapshot=0`; vẫn phải mapping đầy đủ mọi text, không có cơ chế bỏ qua giá trị chưa xác định.
5. Chạy `sql/20261020_work_progress_verify.sql`. Kiểm tra dòng “Công việc chưa mapping chính xác” bằng 0 và PASS. Với dữ liệu đã xác nhận, số lượng phải là 39 mới / 14 hoàn thành / tổng 53, trong đó 13 orphan được duyệt và 40 có phiếu hợp lệ. Đọc dòng cuối “KẾT LUẬN CUỐI CÙNG”.

Migration schema gốc đã seed 5 mục chuẩn. Vì vậy trên database đã cài đầy đủ, migration dữ liệu bổ sung có thể không INSERT dòng nào; nó vẫn kiểm tra tính đầy đủ và tính đúng đắn của mapping. Nếu thiếu mục chuẩn chưa được tham chiếu, script thêm đúng mục còn thiếu. Không hardcode ID hoặc tạo trùng danh mục.

## Dữ liệu được bảo toàn

- Không UPDATE 53 bản ghi công việc: text hiện có đã khớp `StorageValue`, nên chỉ cần danh mục tương ứng để liên kết tự có hiệu lực.
- Không ghi ngày bắt đầu/dự kiến/hoàn thành, check-in/check-out, audit hoặc rowversion công việc.
- Không bật/tắt trigger, không tạo lịch sử chuyển trạng thái giả.
- Không ghi đè tên/màu/thứ tự/kích hoạt của danh mục đã tùy chỉnh.
- Xung đột Code/StorageValue/Kind hoặc text chưa mapping làm rollback cả transaction; không giữ lại một phần seed.
- So sánh byte chính xác giúp phát hiện trường hợp collation SQL coi hai chuỗi khác nhau là tương đương nhưng ứng dụng nhận chuỗi khác nhau.

## Rollback

Nên giữ các mục danh mục bổ sung. Không cần rollback dữ liệu công việc vì migration này không thay đổi chúng. Nếu cần gỡ mục vừa thêm, dùng `sql/20261020_work_progress_status_data_rollback.sql` và điền **đúng ID/Code/StorageValue từ kết quả INSERT của lần chạy đó**, sau đó bật `@AllowCatalogRemoval=1`. Script từ chối xóa mục đã được công việc hoặc lịch sử tham chiếu. Nếu lần chạy không INSERT mục nào, không có dữ liệu thuộc migration này để gỡ.

## Kiểm thử và giới hạn

Kiểm thử tích hợp dùng database SQL cục bộ tách biệt với bộ dữ liệu đúng 39/14. Kiểm tra xem trước, seed thiếu, giữ tùy chỉnh danh mục, chạy lại không đổi rowversion, bảo toàn toàn bộ dòng công việc, ngày hoàn thành đã biết, không sinh history, rollback khi gặp giá trị lạ/xung đột, chặn rollback mục đang dùng và phát hiện số lượng thay đổi. Script preflight và verify cũng được thực thi trên database kiểm thử.

Không chạy migration trên database được cấu hình hoặc production trong phiên này. Ảnh xác nhận phân bố trạng thái; schema thực tế, liên kết bảng và menu vẫn cần preflight như hướng dẫn triển khai ban đầu. Các thay đổi lần này chỉ bổ sung script dữ liệu, kiểm thử và tài liệu; hợp đồng đọc/ghi của ứng dụng được giữ nguyên.
