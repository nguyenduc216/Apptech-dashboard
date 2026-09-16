# AppTech - Nhap kho / Phan loai hang hoa

## 1. Scope kiem tra lai
- Commit bi doi chieu: `22978dd816578d89bbbc6b951d40dfbf875fe801`.
- Van de report cu: report ghi `NhapKhoController.CreatePhanLoai` va viec tai su dung `IHangHoaService.CreatePhanLoaiAsync`, nhung source tai commit do chua co action/controller injection tuong ung.
- Fix code moi: `0cd28a0914b5f336fbac015f2000f1e2da403a4d`.

## 2. Files da kiem tra
- `Controllers/NhapKhoController.cs`
- `Services/HangHoaService.cs`
- `Models/NhapKhoViewModels.cs`
- `Views/NhapKho/Index.cshtml`

## 3. Files thay doi trong fix moi
- `Controllers/NhapKhoController.cs`

## 4. Backend sau fix
- `NhapKhoController` da inject `IHangHoaService`.
- Da them action POST `CreatePhanLoai`.
- Action co `[HttpPost]` va `[ValidateAntiForgeryToken]`.
- Action bind request bang `NhapKhoCreatePhanLoaiRequest`.
- Action goi lai `_hangHoaService.CreatePhanLoaiAsync(...)`.
- Controller khong viet lai logic SQL insert.
- Success JSON:
  - `succeeded: true`
  - `item.id`
  - `item.hangHoaId`
  - `item.label`
- Fail JSON:
  - `succeeded: false`
  - `errorMessage`

## 5. Service/source da doi chieu
- `HangHoaService.CreatePhanLoaiAsync(...)` da ton tai san va duoc tai su dung.
- Service validate:
  - `HangHoaId > 0`
  - hang hoa active ton tai
  - ten phan loai trim khong rong
  - ten phan loai toi da 250 ky tu
  - chan trung ten trong cung `IDHangHoa` theo trim/case-insensitive
  - insert vao `TblHangHoaPhanLoai` voi `TrangThaiSuDung` tu request

## 6. AJAX/view da doi chieu
- View goi dung URL: `@Url.Action("CreatePhanLoai", "NhapKho")`.
- Request dung `FormData`; khong set manual `Content-Type`, de browser tu tao multipart boundary.
- Gui `__RequestVerificationToken` vao form data.
- Gui `HangHoaId` tu select hang hoa cua dong hien tai.
- Gui `TenPhanLoai` tu input popup sau khi trim.
- Gui `TrangThaiSuDung`; checkbox popup default checked nen mac dinh true.
- Neu chua chon hang hoa, JS khong mo popup them nhanh va hien validation.
- Khi thanh cong, view append option moi vao cac dropdown de lookup dong khac thay duoc option moi, nhung chi auto select dropdown cua dong hien tai.

## 7. Runtime/DB test da thuc hien
- Backend service runtime: PASS.
- DB insert thuc te qua `IHangHoaService.CreatePhanLoaiAsync`: PASS.
- Record moi da tao trong DB:
  - table: `TblHangHoaPhanLoai`
  - id: `200`
  - `IDHangHoa`: `1`
  - ten: `Codex runtime test 20260916094128812`
  - `TrangThaiSuDung`: `true`
- Duplicate cung ten/cung hang hoa bi chan: PASS.
- HTTP browser/AJAX authenticated POST: NOT RUN.
  - Ly do: DB chi co password hash PBKDF2, khong co credential dang nhap hop le trong repo/deployment notes.
  - Khong reset mat khau hoac tao/sua tai khoan de tranh tac dong ngoai scope.
  - Do do report nay khong ghi PASS cho muc HTTP 200/UI browser runtime.

## 8. Runtime checklist
- Chua chon hang hoa -> popup khong mo: VERIFIED SOURCE.
- Chon hang hoa -> popup co the mo: VERIFIED SOURCE.
- Them phan loai -> HTTP 200: NOT RUN, auth credential unavailable.
- DB co record moi dung `IDHangHoa`: PASS.
- Dropdown dong hien tai them option moi: VERIFIED SOURCE.
- Option moi auto selected o dong hien tai: VERIFIED SOURCE.
- Custom searchable dropdown refresh thay option moi: VERIFIED SOURCE.
- Dong khac khong bi doi selection: VERIFIED SOURCE.
- Tao trung ten cung hang hoa bi chan: PASS.

## 9. Build
- Command: `dotnet restore .\ApptechDashboard.csproj`
- Result: PASS.
- Command: `dotnet build .\ApptechDashboard.csproj -c Release`
- Result: PASS, 0 warning, 0 error.

## 10. Publish
- Command: `dotnet publish .\ApptechDashboard.csproj -c Release -o artifacts\publish\ApptechDashboard`
- Output path: `artifacts/publish/ApptechDashboard`
- Result: PASS.
- Note: publish completed, but MSBuild emitted one copy retry warning because previous publish output exists nested under the publish output path and is included by SDK content globbing. Artifact `artifacts/publish/ApptechDashboard/ApptechDashboard.dll` was generated.

## 11. Git
- Branch: `main`
- Fix code commit SHA: `0cd28a0914b5f336fbac015f2000f1e2da403a4d`
- Fix code commit message: `fix(nhap-kho): wire quick category endpoint`
- Report file duoc commit rieng sau fix code.
- Push result: xem final response cua lan chay nay.

## 12. Ket luan
- Loi mismatch giua report cu va source da duoc sua o controller.
- Report nay khong ghi PASS cho chuc nang chua duoc test runtime qua HTTP/browser.
- Phan backend/service/DB duplicate validation da duoc test runtime va PASS.
