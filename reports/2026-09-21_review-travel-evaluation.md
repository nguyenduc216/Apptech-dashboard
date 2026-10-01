# Báo cáo review Travel Time Evaluation trước deploy

## 1. Kết luận

Implementation bám phần lớn yêu cầu nghiệp vụ: chỉ đánh giá check-in trong ca, bỏ lần đầu ca, ưu tiên mốc checkout của lượt trước, lấy route OSRM, lưu kết quả và chỉ đọc dữ liệu đã lưu khi mở báo cáo. Công thức và ngưỡng cảnh báo đúng.

**Kết luận deploy: BLOCK DEPLOY.** Cần một commit fix trước deploy vì foreign key mới có thể làm hỏng hai luồng xóa check-in đang có. Ngoài ra nên bổ sung test tích hợp cho truy vấn chọn lượt trước và database migration; 68 test hiện tại đều qua nhưng chưa bao phủ các rủi ro này.

Phạm vi review: diff `5c17794e0739b6598c5cac734099dc4aea9dd0f2..71dd0a42c11cfccc1a998d7d7a44982e9467ff75`. Không sửa source, migration, commit hoặc push trong vòng review này.

## 2. Bảng kiểm

| Hạng mục | Status | Ghi chú |
|---|---|---|
| Database | Block deploy | Đủ field và unique index phù hợp; foreign key không có chính sách xóa làm regression luồng xóa check-in. |
| Config | Đạt có lưu ý | Đọc/lưu trong `TblCauHinhHeThong`, có default/fallback 15 và 120; chưa seed vật lý hai key cho tới lần lưu cấu hình. |
| Evaluation Logic | Đạt | Chạy sau khi check-in đã commit; lỗi đánh giá bị bắt và không rollback check-in. |
| Shift Detection | Đạt | Current phải thuộc sáng/chiều và previous phải có check-in từ đầu cùng ca. Các case 07:45/09:30/13:35/14:30 cho kết quả đúng. |
| Previous Attendance | Đạt có lưu ý | Chọn lượt cùng nhân viên/cùng ngày gần nhất theo check-in; dùng checkout time/GPS nếu có, nếu không dùng check-in time/GPS. |
| Route | Đạt có rủi ro | Dùng OSRM driving, không dùng Haversine/vận tốc giả; lỗi có log và không làm fail check-in. Phụ thuộc public endpoint. |
| Formula | Đạt | `Actual = CurrentCheckin - PreviousTime`; `Deviation = Actual - Expected`; warning chỉ khi `Deviation > Allowed`. |
| Report | Đạt có điều kiện | `LEFT JOIN` bảng kết quả, không gọi route/tính lại. Migration bắt buộc phải chạy trước ứng dụng mới. |
| UI | Đạt | Warning có nền/màu/icon và tooltip đủ khoảng cách, dự kiến, thực tế, chênh lệch; false giữ UI cũ. |
| Performance | Nên sửa | Route chạy đồng bộ trong request, timeout 8 giây; thao tác check-in có thể chậm tương ứng. |

## 3. Các phần đã đúng

### Database

- `TblChamCongTravelEvaluation` có đầy đủ các trường được yêu cầu: hai attendance ID, employee ID, bốn tọa độ, khoảng cách, thời gian dự kiến/thực tế/chênh lệch, tolerance, warning và thời điểm tạo.
- `UX_TravelEvaluation_CurrentAttendance` thể hiện đúng quy tắc một kết quả cho một current attendance và phục vụ trực tiếp phép join báo cáo.
- `IX_TravelEvaluation_EmployeeWarning(EmployeeId, IsWarning, CreatedDate)` hợp lý cho truy vấn lịch sử/cảnh báo theo nhân viên, dù truy vấn báo cáo hiện tại chưa dùng index này.
- Kiểu decimal đủ cho GPS và các số liệu route. Không thấy thiếu field bắt buộc theo đặc tả.

### Configuration

- `AllowedTravelDeviationMinutes` có default 15, validation/clamp 0–240.
- `MaxTravelEvaluationGapMinutes` có default 120, validation/clamp 1–1440.
- Service đọc và upsert cả hai key trong `TblCauHinhHeThong`; transaction lưu cấu hình bao gồm các key mới.
- Khi key chưa tồn tại, model default là fallback. UI đã có đúng hai field và validation message.

### Quy tắc ca và lượt trước

- Current ngoài hai khung giờ được bỏ qua.
- Query chỉ lấy lượt trước của cùng `EmployeeId`, cùng ngày, có `ThoiDiem < CurrentTime`, gần nhất theo `ThoiDiem DESC, ID DESC`.
- Việc kiểm tra `previous.CheckinTime >= shiftStart` khiến lượt đầu buổi sáng và lượt đầu buổi chiều không được đánh giá. Vì previous luôn sớm hơn current và current không vượt shift end, previous thỏa điều kiện này cũng nằm trong cùng ca.
- Case previous check-in 08:00, checkout 09:00, current 09:30 cho actual 30 phút. Nếu không checkout, actual là 90 phút.
- Gap được chấp nhận khi `0 <= actual <= max`; chỉ skip khi lớn hơn max. Gap đúng 120 phút vẫn có thể đánh giá nhưng không tự động thành warning; điều này phù hợp quy tắc `> MaxGap` mới bỏ qua.

### Route, công thức và độ an toàn check-in

- Named client `AttendanceRoute` gọi OSRM `/route/v1/driving`, dùng distance và duration thật từ response.
- Không có fallback Haversine hay vận tốc giả.
- Timeout được đặt 8 giây. Mọi exception trong evaluation đều được log warning và nuốt sau khi bản ghi check-in đã được commit, nên route/DB evaluation lỗi không rollback check-in.
- Các case công thức đều đúng: chênh lệch bằng tolerance không warning; lớn hơn tolerance warning; đi nhanh hơn dự kiến không warning.

### Report và UI

- Báo cáo dùng `LEFT JOIN dbo.TblChamCongTravelEvaluation` theo `CurrentAttendanceId`; không gọi route API và không tính lại khi load.
- View model có distance, expected, actual, deviation và warning. UI đánh dấu cả ô ngày lẫn dòng check-in và tooltip hiển thị đủ nội dung yêu cầu.

## 4. Lỗi phát hiện

### [Block deploy] Foreign key làm hỏng chức năng xóa check-in hiện hữu

Hai foreign key `CurrentAttendanceId` và `PreviousAttendanceId` tham chiếu `TblCheckinHistory(ID)` nhưng không có `ON DELETE CASCADE` hoặc xử lý xóa evaluation trước.

Hệ thống hiện có ít nhất hai luồng xóa trực tiếp `TblCheckinHistory`:

- `ChamCongService.DeletePurchaseCheckinAsync` xóa lượt mua hàng.
- `YeuCauService.DeleteCheckinAsync` xóa lượt check-in yêu cầu/công việc.

Sau khi một lượt đã có evaluation, xóa chính lượt đó bị FK Current chặn. Nghiêm trọng hơn, một lượt đã được dùng làm `PreviousAttendanceId` cho lượt sau cũng bị FK Previous chặn. Service xóa sẽ bắt exception và trả lỗi chung, tạo regression rõ ràng cho purchase flow và customer/work check-in.

Đề xuất commit fix trước deploy:

- Chọn chính sách lifecycle rõ ràng. Phương án phù hợp nhất với dữ liệu dẫn xuất là xóa các evaluation liên quan trước khi xóa attendance, trong cùng transaction; hoặc thiết kế FK cascade có kiểm chứng trên cả hai quan hệ.
- Không nên chỉ cascade một FK: bản ghi có thể được tham chiếu ở vai trò current hoặc previous.
- Thêm integration test xóa attendance ở cả hai vai trò.

### [Nên sửa] Test chưa chứng minh các case nghiệp vụ ở tầng SQL/service

Các test mới chỉ gọi ba helper thuần (`ResolveShift`, `ShouldEvaluateTravel`, `Calculate`). Test “missing route” chỉ gán local variable bằng null, không kiểm tra `EvaluateAsync`. Chưa có test cho:

- Query chọn đúng previous của cùng nhân viên/cùng ngày/cùng ca.
- Ưu tiên checkout time/GPS và fallback check-in.
- Lưu/upsert kết quả, unique constraint và route lỗi.
- Xóa attendance khi đã có evaluation.
- Mapping `LEFT JOIN` và tooltip payload.

Do đó 68/68 pass không loại trừ regression database nêu trên.

## 5. Rủi ro

### [Nên sửa] Request check-in chờ route tối đa 8 giây

`EvaluateAsync` được await trực tiếp trong controller. Check-in đã commit nên an toàn dữ liệu chính, nhưng người dùng chỉ nhận response sau khi OSRM trả về hoặc timeout. Khi public OSRM chậm, thao tác có thể tăng gần 8 giây.

Ngoài ra controller truyền `HttpContext.RequestAborted`; nếu client mất kết nối sau khi check-in đã lưu, evaluation có thể bị hủy và không có cơ chế retry. Chuyển sang background queue bền vững sẽ cải thiện trải nghiệm và độ đầy đủ dữ liệu, nhưng đây là đề xuất kiến trúc, không bắt buộc trong commit sửa FK nếu chấp nhận best-effort.

### [Nên sửa] Phụ thuộc public OSRM và không lưu trạng thái “không đánh giá”

Khi route lỗi/timeout, code chỉ log rồi không ghi row. Kết quả đáp ứng nhánh “trạng thái không đánh giá” theo nghĩa không có evaluation, nhưng báo cáo không phân biệt được: lần đầu ca, thiếu GPS, quá max gap, route lỗi hay evaluation chưa chạy. Nếu cần audit/vận hành, nên thêm status/reason hoặc retry telemetry. Đây không phải field bắt buộc hiện tại.

### [Nên sửa] Migration và ứng dụng phải deploy theo đúng thứ tự

Report query tham chiếu bảng mới trực tiếp. Nếu ứng dụng mới chạy trước migration, toàn bộ load báo cáo chấm công sẽ lỗi. Cần chạy migration trước khi switch ứng dụng và kiểm tra quyền tạo table/index/FK.

### [Có thể cải thiện] Default chỉ là fallback, chưa được seed vật lý

Hai giá trị 15/120 hiển thị đúng khi database chưa có key và sẽ được upsert khi người dùng lưu trang cấu hình. Tuy nhiên migration không insert hai default key. Nếu yêu cầu vận hành cần nhìn thấy cấu hình mặc định trực tiếp trong DB ngay sau deploy, nên bổ sung seed idempotent ở commit fix/migration deploy.

### [Có thể cải thiện] Upsert có cửa sổ race nhỏ

Mẫu `UPDATE; IF @@ROWCOUNT = 0 INSERT` không khóa hàng/key. Hai lần đánh giá đồng thời cùng current ID có thể cùng insert và một lần vướng unique index. Exception bị nuốt nên check-in vẫn thành công nhưng có thể thiếu evaluation. Thực tế controller thường chỉ gọi một lần; có thể harden bằng transaction/locking hoặc xử lý duplicate retry.

## 6. Regression assessment

- Check-in văn phòng, công trình và mua hàng vẫn lưu trước evaluation; lỗi route/evaluation không rollback bản ghi chính.
- Check-out, GPS capture, map và popup công việc không bị sửa trực tiếp.
- Báo cáo cũ giữ cách tính công; chỉ bổ sung `LEFT JOIN` và metadata warning.
- Regression xác định được là xóa check-in do FK, vì vậy chưa thể xác nhận an toàn deploy.

## 7. Kết quả build/test

- `dotnet build apptech-dashboard.sln -c Release --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: thành công, 68/68 test passed.
- `git diff --check`: không phát hiện whitespace error trước khi tạo báo cáo.

Build/test xanh không thay thế kiểm thử SQL integration; lỗi foreign key có thể tái hiện ở runtime sau khi migration đã chạy và bảng evaluation có dữ liệu.

## 8. Danh sách hành động trước deploy

1. **Block deploy:** sửa lifecycle/FK để xóa attendance không bị chặn ở cả vai trò current và previous.
2. **Nên sửa:** thêm integration test cho migration, chọn previous, route failure, save/join và delete.
3. **Nên sửa trong checklist deploy:** bắt buộc chạy migration trước ứng dụng mới.
4. **Cân nhắc:** tách route evaluation khỏi request hoặc dùng background queue/retry để tránh timeout và mất evaluation khi client disconnect.
5. **Cân nhắc:** seed vật lý hai key default và lưu status/reason cho trường hợp không đánh giá nếu cần audit.
