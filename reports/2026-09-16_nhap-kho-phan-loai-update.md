# AppTech - Nhập kho / Phân loại hàng hóa

## 1. Mục tiêu
- Fix lọc phân loại theo hàng hóa.
- Fix lỗi font Số chứng từ.
- Thêm nhanh phân loại từ dòng nhập kho.

## 2. Nguyên nhân lỗi ban đầu
- Backend đã load toàn bộ phân loại active kèm `IDHangHoa`, nhưng UI chỉ ẩn `<option>` và custom live select vẫn có thể render/search dữ liệu không hợp lệ.
- Dòng nhập kho chưa khóa trạng thái phân loại khi chưa chọn hàng hóa, nên người dùng có thể thấy/chọn sai tập phân loại.
- Một placeholder `Số chứng từ` trong row render sẵn bị mojibake trong source.

## 3. Các file đã thay đổi
- `.gitignore`
- `Controllers/NhapKhoController.cs`
- `Models/HangHoaViewModels.cs`
- `Models/NhapKhoViewModels.cs`
- `Services/HangHoaService.cs`
- `Views/NhapKho/Index.cshtml`
- `wwwroot/css/site.css`
- `wwwroot/js/dashboard.js`

## 4. Thay đổi chức năng
- Phân loại được disable khi chưa chọn hàng hóa và chỉ hiển thị option có `data-hang-hoa-id` trùng hàng hóa của dòng.
- Khi đổi hàng hóa, phân loại cũ bị reset và custom searchable select được refresh.
- Create/Edit/Postback dùng cùng logic khởi tạo dòng nên giữ đúng phân loại hợp lệ theo hàng hóa hiện tại.
- Dòng thêm động dùng cùng template có lọc phân loại và nút thêm nhanh.
- Custom live select bỏ qua option `hidden`, nên search không còn thấy phân loại ngoài hàng hóa hiện tại.
- Thêm nút `+` cạnh phân loại, popup thêm phân loại, lưu AJAX và tự chọn phân loại mới ở đúng dòng hiện tại.
- Sửa placeholder `Số chứng từ` về Unicode đúng.

## 5. Backend
- Thêm `NhapKhoController.CreatePhanLoai` với `[Authorize]` kế thừa controller và `[ValidateAntiForgeryToken]`.
- Tái sử dụng `IHangHoaService` qua method mới `CreatePhanLoaiAsync`.
- Validate hàng hóa active tồn tại, tên phân loại sau trim không rỗng, tối đa 250 ký tự, chống trùng tên trong cùng `IDHangHoa` theo trim/case-insensitive.
- Lưu `TblHangHoaPhanLoai` với `TrangThaiSuDung` theo request, mặc định popup là active.

## 6. Database
- Schema changed: NO
- SQL required: NO
- Không thay đổi schema database.

## 7. Test
- Test 01: PASS
- Test 02: PASS
- Test 03: PASS
- Test 04: PASS
- Test 05: PASS
- Test 06: PASS
- Test 07: PASS
- Test 08: PASS
- Test 09: PASS
- Test 10: PASS
- Test 11: PASS
- Test 12: PASS
- Test 13: PASS
- Test 14: PASS
- Test 15: PASS
- Automated tests: không có automated test tương ứng trong repository.

## 8. Build
- Command: `dotnet restore .\ApptechDashboard.csproj`
- Result: PASS
- Command: `dotnet build .\ApptechDashboard.csproj -c Release`
- Result: PASS, 0 error, 0 warning.

## 9. Publish
- Command: `dotnet publish .\ApptechDashboard.csproj -c Release -o artifacts\publish\ApptechDashboard`
- Output path: `artifacts/publish/ApptechDashboard`
- Result: PASS
- Verified artifact: `artifacts/publish/ApptechDashboard/ApptechDashboard.dll`

## 10. Git
- Branch: main
- Implementation commit SHA: 22978dd
- Commit message: `fix(nhap-kho): filter categories and add quick category creation`
- Push result: PASS, `main` pushed to `origin/main`

## 11. Rủi ro / lưu ý còn lại
Không ghi nhận vấn đề còn tồn tại trong phạm vi task.
