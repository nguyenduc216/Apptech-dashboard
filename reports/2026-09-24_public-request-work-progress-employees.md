# Báo cáo trang public tiến độ công việc và nhân viên thực hiện

## Phiên bản

- Nhánh: `main`.
- HEAD trước sửa: `af5b54efc17560d6db35574e811943f8af33d796`.
- Giữ nguyên route `/zalo/request/{token}` và `/zalo/request/{token}/rating`.
- Không thay token flow, QR flow, webhook mapping hoặc Zalo message sending.

## Files changed

- `Controllers/ZaloRequestController.cs`
- `Services/ZaloRequestService.cs`
- `Models/ZaloRequestViewModels.cs`
- `Properties/AssemblyInfo.cs`
- `Tests/ApptechDashboard.Tests/ZaloPublicRequestLandingTests.cs`
- `reports/2026-09-24_public-request-work-progress-employees.md`

## Data flow

```text
/zalo/request/{token}
→ LoadLinkAsync(token)
→ kiểm tra token tồn tại và còn hạn
→ lấy RequestId từ link đã xác thực
→ LoadLandingAsync(RequestId)
→ LoadWorksAsync(RequestId)
→ render thông tin, công việc/nhân viên và CTA đánh giá
```

Public route không nhận `RequestId` từ query string và không tạo endpoint public mới. Work/employee chỉ được tải bằng `RequestId` lấy từ token hợp lệ.

## Query strategy

`LoadWorksAsync()` dùng một query cho toàn bộ work và assignment, không query nhân viên theo từng work:

```sql
SELECT
    ycvc.ID AS WorkId,
    cv.TenCongViec,
    ycvc.TrangThaiCongViec,
    nv.ID AS EmployeeId,
    LTRIM(RTRIM(CONCAT(ISNULL(nv.Ho, N''), N' ', ISNULL(nv.Ten, N'')))) AS EmployeeFullName
FROM TblYeuCauCongViec ycvc
LEFT JOIN TblCongViec cv ON cv.ID = ycvc.IDCongViec
LEFT JOIN TblYeuCauCongViecNhanVien assignment
    ON assignment.IDYeuCauCongViec = ycvc.ID
LEFT JOIN TblNhanVien nv ON nv.ID = assignment.IDNhanVien
WHERE ycvc.IDYeuCau = @RequestId
ORDER BY ycvc.ID, nv.Ho, nv.Ten, nv.ID;
```

Các row được group theo `WorkId` trong C#. Assignment nhân viên trùng được loại theo `EmployeeId`. `LEFT JOIN` giữ lại work chưa có nhân viên.

## Model changes

`ZaloRequestWorkItem` được bổ sung:

```text
Employees: IReadOnlyList<ZaloRequestEmployeeItem>
```

`ZaloRequestEmployeeItem` chỉ chứa:

- `EmployeeId`: dùng để group/deduplicate, không render ra HTML;
- `FullName`: thông tin nhân viên duy nhất hiển thị public.

Không đưa phone, email, account, address, CCCD, mã nhân viên, phòng ban, HR, attendance, GPS hoặc avatar vào model/query public.

## UI changes

Landing page có ba tab mobile-first, không reload và không thêm dependency:

1. **Thông tin** — mặc định active, hiển thị mã phiếu, khách hàng, số điện thoại, ngày thực hiện, trạng thái đánh giá/kết nối và giữ nguyên block kết nối Zalo.
2. **Công việc** — card theo từng work, badge trạng thái và danh sách họ tên nhân viên.
3. **Đánh giá** — CTA tới route rating hiện tại.

Tab dùng `role=tablist`, `role=tab`, `role=tabpanel`, `aria-selected`, `aria-controls`; hỗ trợ click và phím mũi tên trái/phải. Không dùng bảng rộng hoặc SPA framework.

Empty states:

- `Chưa có danh sách công việc.`
- `Chưa phân công nhân viên.`

Trạng thái lấy trực tiếp từ `TblYeuCauCongViec.TrangThaiCongViec` và normalize bằng `YeuCauCongViecTrangThaiCatalog`. Badge có style riêng cho đang thực hiện/hoàn thành; trạng thái hiện tại hoặc mới khác dùng neutral fallback.

## Security review

- Route public tiếp tục được bảo vệ bằng token hiện tại.
- Token hết hạn/không hợp lệ không tải landing data.
- Query work bắt buộc có `WHERE ycvc.IDYeuCau = @RequestId`.
- Không có route public theo request ID.
- Query nhân viên chỉ select ID và full name.
- Employee ID chỉ dùng nội bộ trong transformation, không render lên trang.
- Link/QR cũ còn hạn tự động nhận UI mới, không regenerate hoặc invalidate token.

## XSS handling

Các giá trị từ database được đưa qua `Encode()` trước khi render:

- mã phiếu;
- tên khách hàng;
- số điện thoại;
- tên công việc;
- trạng thái;
- họ tên nhân viên;
- thông tin profile Zalo hiện có.

Token dùng trong URL được `Uri.EscapeDataString()`; token đưa vào JavaScript tiếp tục dùng encoder hiện có.

## Tests

Test bổ sung bao phủ:

1. group work name/status đúng;
2. hai nhân viên cùng nằm trong đúng work;
3. nhân viên không bị lẫn giữa hai work;
4. work chưa phân công có `Employees` rỗng;
5. assignment trùng không tạo nhân viên trùng;
6. query được scope bằng `RequestId` từ token và dùng assignment join;
7. model employee không có trường nhạy cảm;
8. landing render đủ ba tab và ARIA cơ bản;
9. render danh sách nhân viên và hai empty state;
10. dữ liệu public được HTML encode;
11. block kết nối Zalo vẫn còn;
12. route landing/rating và CTA rating cũ vẫn hoạt động.

## Build và test

- `dotnet restore apptech-dashboard.sln`: thành công, dependencies đã up-to-date.
- `dotnet build apptech-dashboard.sln -c Release`: thành công, 0 warning, 0 error.
- `dotnet test apptech-dashboard.sln -c Release`: thành công, 127/127 test passed.
- `git diff --check`: không có whitespace error; chỉ có cảnh báo LF/CRLF theo quy ước worktree Windows.

## Database và migration

Không thay đổi schema và không cần migration.

## Manual test checklist

1. Publish application và recycle IIS App Pool.
2. Chọn một phiếu có ít nhất hai work, hai nhân viên và trạng thái khác nhau.
3. Mở link cũ còn hạn `/zalo/request/{token}` trên trình duyệt điện thoại/Zalo.
4. Tab Thông tin: kiểm tra mã phiếu, khách, số điện thoại, ngày và trạng thái kết nối/đánh giá.
5. Tab Công việc: kiểm tra số work, tên, trạng thái và đúng nhân viên theo từng work.
6. Kiểm tra một work có nhiều nhân viên và một work chưa phân công.
7. Kiểm tra phiếu chưa có work.
8. Tab Đánh giá: CTA mở đúng `/zalo/request/{token}/rating` và form cũ vẫn lưu được đánh giá.
9. Kiểm tra flow quan tâm OA, copy mã xác nhận và trạng thái đã kết nối.
10. Kiểm tra tab bằng touch và bàn phím; không có scroll ngang trên mobile.
