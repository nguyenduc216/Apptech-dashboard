# Mapping dữ liệu trạng thái công việc bằng text

FEATURE_ID: `APPTECH-WORK-PROGRESS-KANBAN`

CHANGE_ID: `APPTECH-20261020-WORK-PROGRESS-002`

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
5. Chạy `sql/20261020_work_progress_verify.sql`. Kiểm tra `EXACT_MAPPING_OK` cho toàn bộ công việc và tổng số theo Code. Với dữ liệu trong ảnh, kết quả phải là `NEW = 39`, `COMPLETED = 14`, tổng 53.

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
