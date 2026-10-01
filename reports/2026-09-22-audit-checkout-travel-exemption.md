# Audit Summary

Phạm vi audit: implementation tại commit `b7ab36183584da460991efaaf2e68cf8a211d4d7`. Vòng audit không sửa source, không tạo migration, không commit và không push.

Kết luận tổng thể: **NEED FIX trước deploy**, không phải vì phát hiện công thức miễn về sớm sai trong luồng dữ liệu hợp lệ, mà vì test hiện tại chưa chứng minh các nhánh SQL/service bắt buộc và truy vấn exemption chưa có index theo `PreviousAttendanceId`. Logic nghiệp vụ chính đang đúng: exemption được tính động từ Travel Evaluation đã lưu, chỉ áp dụng khi `IsWarning = 0`, và tự mất khi evaluation liên quan bị xóa.

| Item | Status | Finding |
|---|---|---|
| IsCheckoutTravelExempt storage | PASS | Chỉ là property runtime trên view model; không thêm cột vào `TblCheckinHistory` hoặc `TblChamCongTravelEvaluation`. |
| PreviousAttendance mapping | PASS | Evaluation lưu `PreviousAttendanceId` của attendance gần nhất trong ca; Dashboard/report tra ngược đúng ID này. |
| Shift validation | PASS | Current phải nằm trong ca; query previous bắt đầu từ `ShiftStart` và nhỏ hơn current; kiểm tra phòng thủ còn xác nhận cùng ngày/cùng ca. |
| TravelEvaluation dependency | PASS | Chỉ `IsWarning = 0` mới miễn; thiếu evaluation, thiếu GPS, route lỗi hoặc warning đều không miễn. |
| Dashboard | PASS | `IsCheckoutViolation` được tắt cho checkout sớm có evaluation hợp lệ; timeline dùng giá trị này để bỏ đánh dấu vi phạm. |
| Report | PASS | Phần early minutes bằng 0 khi checkout được miễn; late minutes của check-in vẫn được giữ độc lập. |
| Delete handling | PASS | Xóa current attendance sẽ xóa evaluation liên quan trong cùng transaction; lần tải sau checkout cũ không còn exemption. |
| Automated tests | FAIL | Chỉ test helper và contract cleanup rời rạc; chưa test SQL mapping và sáu kịch bản bắt buộc theo luồng tích hợp. |
| Query performance | NEED FIX | Hai correlated `EXISTS` lọc theo `PreviousAttendanceId, IsWarning`, nhưng migration chưa có index bắt đầu bằng `PreviousAttendanceId`. |

## 1. Business rule compliance

**PASS, với điều kiện dữ liệu Travel Evaluation hợp lệ.**

Dashboard và report đều xác định checkout được miễn bằng quan hệ:

```text
travelExemption.PreviousAttendanceId = checkout attendance ID
AND travelExemption.IsWarning = 0
```

Sau đó helper chỉ tính về sớm khi checkout thực sự trước cuối ca và không có exemption:

```text
isBeforeShiftEnd && !hasValidNextTravel
```

Do vậy:

- Checkout đúng hoặc sau cuối ca không bị tính về sớm.
- Checkout sớm + evaluation hợp lệ không bị tính về sớm.
- Checkout sớm + warning vẫn bị tính về sớm.
- Checkout sớm + không có evaluation vẫn bị tính về sớm.
- Thiếu GPS hoặc route lỗi làm `EvaluateAsync` kết thúc mà không tạo evaluation, nên không được miễn.

Không thấy việc miễn làm mất phần đi trễ của chính lượt check-in: report tính `lateMinutes` riêng và chỉ đặt `earlyMinutes` về 0.

## 2. Data model

**PASS.**

`IsCheckoutTravelExempt` không được lưu vật lý. Nó chỉ tồn tại trên:

- `ChamCongHistoryItem` phục vụ Dashboard.
- `ChamCongReportCheckinDetail` và record nội bộ phục vụ report.

Nguồn sự thật vẫn là `TblChamCongTravelEvaluation`, gồm `CurrentAttendanceId`, `PreviousAttendanceId` và `IsWarning`. Đây là thiết kế phù hợp vì exemption là kết quả của quan hệ giữa hai attendance, không phải thuộc tính độc lập của checkout.

Thiết kế này tránh stale flag riêng trong `TblCheckinHistory`: sửa/xóa evaluation sẽ phản ánh ở lần đọc kế tiếp mà không cần đồng bộ thêm một cột boolean.

Lưu ý: bản thân Travel Evaluation là snapshot đã lưu. Nếu GPS, thời gian, lịch ca hoặc tolerance bị sửa nhưng evaluation không được chạy lại/xóa, exemption vẫn phản ánh snapshot cũ. Không tìm thấy cơ chế invalidation/re-evaluation tổng quát cho các thay đổi trực tiếp này. Đây là rủi ro dữ liệu nguồn, không phải stale state do `IsCheckoutTravelExempt`.

## 3. Exemption logic

**PASS.**

Hai truy vấn Dashboard và report dùng `EXISTS`, nên chỉ cần một evaluation không warning tham chiếu attendance ở vai trò previous. `IsWarning = 1` không thỏa điều kiện; không có row cũng trả false.

Matching của evaluator đúng theo thứ tự thời gian:

```sql
WHERE IDNhanVien = @EmployeeId
  AND ID <> @Id
  AND ThoiDiem >= @ShiftStart
  AND ThoiDiem < @CurrentTime
ORDER BY ThoiDiem DESC, ID DESC
```

Với X lúc 16:00, A lúc 16:30 và B lúc 17:00, B chọn A làm previous; X không được miễn bởi evaluation của B. Unique index trên `CurrentAttendanceId` bảo đảm mỗi current attendance chỉ có một kết quả.

Caveat: bảng không có constraint database chứng minh previous/current cùng nhân viên, đúng thứ tự hoặc cùng ca. Runtime tạo row đúng, nhưng row legacy/chèn tay sai vẫn có thể tạo exemption vì truy vấn đọc tin tưởng dữ liệu evaluation.

## 4. Shift handling

**PASS.**

`EvaluateAsync` chỉ chạy tiếp khi current check-in nằm trong ca cấu hình. `LoadPreviousInShiftAsync` chỉ tìm attendance có `ThoiDiem >= ShiftStart` và `< CurrentTime`; sau đó `IsPreviousAttendanceInShift` xác nhận cùng ngày, không trước đầu ca, không sau cuối ca và sớm hơn current.

Vì current đã nằm trong ca và previous phải sớm hơn current, lượt 11:20 ca sáng không thể trở thành previous của check-in 13:30 ca chiều: query ca chiều bắt đầu từ `AfternoonStart`.

Shift được kiểm tra lúc tạo Travel Evaluation, không được kiểm tra lại trong truy vấn exemption. Điều này hợp lý khi evaluation là dữ liệu tin cậy, nhưng nếu cấu hình ca thay đổi sau đó thì row cũ không tự được đánh giá lại.

## 5. Dynamic vs stored state

**PASS.**

Khi mở Dashboard/report, hệ thống chỉ đọc `TblChamCongTravelEvaluation` và tính alias `IsCheckoutTravelExempt` bằng `EXISTS`. Không có `UPDATE TblCheckinHistory` hoặc ghi database khi load trang. Không có migration/cột mới cho exemption trong commit được audit.

Hệ quả đúng mong muốn:

- Đổi `IsWarning` trên evaluation: lần đọc sau phản ánh ngay.
- Xóa evaluation: lần đọc sau exemption trở thành false.
- Xóa check-in sau: cleanup xóa evaluation và checkout trước trở lại trạng thái về sớm.

## 6. Delete consistency

**PASS.**

Hai flow xóa attendance production đều gọi `TravelEvaluationCleanup.DeleteRelatedAsync` trước khi xóa attendance, trong cùng transaction. SQL cleanup xóa row khi attendance là `CurrentAttendanceId` hoặc `PreviousAttendanceId`.

Với A là previous và B là current:

```text
Delete B
→ xóa evaluation CurrentAttendanceId = B
→ Dashboard/report tải lại
→ không còn row PreviousAttendanceId = A, IsWarning = 0
→ A trở lại “Về sớm” nếu checkout trước cuối ca
```

Nếu delete attendance thất bại, transaction rollback nên evaluation không bị mất riêng lẻ.

## 7. Report calculation

**PASS.**

Report đọc evaluation đã lưu, không gọi OSRM và không tính route lại. `CalculateLateEarlyMinutes` tính:

- `lateMinutes` từ giờ check-in và grace period.
- `earlyMinutes` từ checkout tới cuối ca, nhưng chỉ khi `ShouldCountEarlyCheckout(...)` trả true.

Ví dụ ca kết thúc 17:30, checkout 16:30, check-in kế tiếp 17:00 và evaluation không warning: early minutes của attendance trước bằng 0. Phần late minutes vẫn được cộng nếu check-in đầu attendance đó đi trễ.

Dashboard cũng dùng cùng helper để tạo `IsCheckoutViolation`, nên cách hiển thị và cách tổng hợp report thống nhất.

## 8. Test coverage

**FAIL — NEED FIX.**

78/78 test hiện tại pass, nhưng test exemption mới chỉ gọi helper thuần:

- `EarlyCheckout_WithValidNextTravel_IsNotCountedAsEarlyLeave`.
- `EarlyCheckout_WithoutValidNextTravel_IsStillCountedAsEarlyLeave`.
- `OnTimeCheckout_IsNeverCountedAsEarlyLeave`.

Các test shift/ngày và cleanup có tồn tại nhưng độc lập với exemption. Chúng không chạy truy vấn SQL, không map `IsCheckoutTravelExempt`, không gọi calculation report và không chứng minh chuỗi hành vi hoàn chỉnh.

Đối chiếu sáu test bắt buộc:

| Required case | Coverage | Finding |
|---|---|---|
| Checkout sớm + next check-in hợp lệ | Partial | Helper pass; chưa test evaluation row → SQL mapping → Dashboard/report. |
| Checkout sớm + next check-in warning | Partial | Giá trị `hasValidNextTravel=false` được test, nhưng chưa test row `IsWarning=1`. |
| Checkout sớm + không có next check-in | Partial | Cùng nhánh boolean false; chưa test không có row trong DB. |
| Checkout sáng + check-in đầu ca chiều | Partial | Shift helper được test riêng; chưa chứng minh exemption false trên dữ liệu/report. |
| Checkout và check-in khác ngày | Partial | Previous-day helper được test riêng; chưa chứng minh exemption false trên dữ liệu/report. |
| Xóa next check-in reset exemption | Partial | Cleanup SQL/order được test; chưa reload và xác nhận checkout trở lại violation. |

Cần integration test hoặc database-backed contract test cho toàn bộ sáu case trước khi coi regression đã được khóa.

## 9. Database / migration

**PASS cho data model; NEED FIX cho index hiệu năng.**

Commit audit không tạo migration và không thay đổi schema, đúng với thiết kế dynamic. Không có ảnh hưởng chuyển đổi dữ liệu cũ.

Migration Travel Evaluation hiện có:

- Unique index theo `CurrentAttendanceId`.
- Index `(EmployeeId, IsWarning, CreatedDate)`.

Hai query exemption lại tìm theo:

```sql
PreviousAttendanceId = ch.ID AND IsWarning = 0
```

Chưa có index bắt đầu bằng `PreviousAttendanceId`. Với lịch sử lớn, SQL Server có thể phải scan bảng evaluation hoặc thực hiện work lớn khi load Dashboard/report. Nên bổ sung index idempotent phù hợp, ví dụ `(PreviousAttendanceId, IsWarning)`, trong một commit/migration riêng sau audit.

## 10. Performance

**NEED FIX trước khi dữ liệu tăng lớn.**

- PASS: Dashboard/report không gọi OSRM và không tính route lại.
- PASS: Dùng kết quả Travel Evaluation đã lưu.
- PASS: Không phát sinh N+1 từ application; exemption nằm trong cùng SQL query.
- NEED FIX: correlated `EXISTS` xuất hiện ở cả Dashboard và monthly report nhưng không có supporting index theo `PreviousAttendanceId`.

Optimizer có thể chuyển `EXISTS` thành semi-join, nên đây chưa chắc là lỗi hiệu năng tức thời ở dữ liệu nhỏ. Tuy nhiên cấu trúc index hiện tại không phục vụ lookup chính, vì vậy cần đo execution plan và bổ sung index trước khi bảng tăng lớn.

## 11. Issues and severity

### Need fix

1. Thiếu integration test cho sáu kịch bản bắt buộc; test hiện tại không chứng minh SQL mapping, report calculation và delete/reload end-to-end.
2. Thiếu index phục vụ lookup `PreviousAttendanceId + IsWarning` ở Dashboard/report.

### Improvement only

1. Thêm cơ chế invalidation/re-evaluation nếu hệ thống cho phép sửa GPS, timestamp, lịch ca hoặc tolerance sau khi evaluation đã lưu.
2. Có thể harden query bằng cách join current attendance và kiểm tra employee/time/shift nếu cần tự bảo vệ trước dữ liệu legacy hoặc chỉnh tay sai; runtime hiện tại đã tạo quan hệ đúng.
3. Thêm test cạnh tranh/upsert nếu nhiều check-in được ghi gần như đồng thời.

### Block deploy

Không phát hiện lỗi logic trực tiếp đủ mức **Block deploy** trong đường chạy chuẩn. Tuy nhiên khuyến nghị **Need fix trước deploy** vì coverage bắt buộc chưa đạt; index có thể xử lý cùng đợt hoặc xác nhận bằng execution plan nếu dataset nhỏ.

## 12. Verification

- `git show --check b7ab36183584da460991efaaf2e68cf8a211d4d7`: không có whitespace error.
- `dotnet test apptech-dashboard.sln -c Release --no-build --no-restore`: 78/78 passed.
- Không sửa source, không tạo migration, không commit và không push trong vòng audit.
