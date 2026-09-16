# Checkin Cong Trinh Implementation Report

## 1. Phan tich kien truc hien tai
- Dashboard cham cong nam trong `Views/Home/Index.cshtml`, dung `HomeController`, `ChamCongService`, Leaflet, OSRM route va Google Maps external link.
- Danh sach va chi tiet Phieu yeu cau nam trong `Views/YeuCau/Index.cshtml`, `Views/YeuCau/Detail.cshtml`, `YeuCauController`, `YeuCauService`.
- Du lieu yeu cau da co `MaYeuCau`, khach hang, dia diem, dien thoai, trang thai, tien do cong viec, ngay yeu cau, ngay thuc hien, nhan vien phu trach va `LatAddress`/`LongAddress`.

## 2. Cac file da sua
- `Controllers/HomeController.cs`
- `Controllers/YeuCauController.cs`
- `Program.cs`
- `Services/PermissionCatalogService.cs`
- `Views/Home/Index.cshtml`
- `Views/YeuCau/Index.cshtml`
- `wwwroot/css/site.css`

## 3. Cac file moi
- `CHECKIN_CONG_TRINH_IMPLEMENTATION_REPORT.md`

## 4. API/service da reuse
- Reuse `IYeuCauService.GetPagedAsync(...)` voi `assignedEmployeeId`.
- Reuse `INhanVienService.GetChamCongEmployeeOptionsAsync(...)`.
- Reuse co che map hien co: Leaflet, OpenStreetMap tile, OSRM route, Google Maps directions link.

## 5. Cach xac dinh employee
- User khong co quyen: popup khong render selector, endpoint force `targetEmployeeId = CurrentEmployeeId`; neu client gui employeeId khac se bi `403`.
- User co quyen: popup co selector rieng va chi lam viec voi 1 nhan vien tai mot thoi diem.
- Initial employee cua popup: uu tien current employee neu hop le; neu Dashboard chi chon dung 1 employee thi dung employee do; truong hop admin khong lien ket employee thi dung option dau tien hop le.

## 6. Cach lay request list
- Endpoint moi `Home/CheckinCongTrinhRequests?employeeId=...` tra JSON.
- Query loc request theo nhan vien duoc gan cong viec bang business logic san co trong `YeuCauService.GetPagedAsync`.

## 7. Cach render map thumbnail tung request
- Moi card co thumbnail rieng dua tren `LatAddress` va `LongAddress` cua request.
- Thumbnail dung Leaflet lightweight, center dung toa do request, marker dung toa do request.
- Thumbnail lazy-init bang `IntersectionObserver`, tat dragging/zoom/keyboard de tranh tao interactive map nang tren tat ca card.
- Neu thieu toa do: hien `Chua co vi tri` va disable thao tac map.

## 8. Cach mo map fullscreen
- Click thumbnail mo modal `construction-map-shell`.
- Fullscreen map dung Leaflet va marker rieng cua request vua click.

## 9. Cach goi chi duong
- Button `Chi duong` lay GPS hien tai, goi OSRM route va ve polyline tren Leaflet.
- Link `Mo Google Maps` dung Google Maps directions voi destination cua request.

## 10. Cach xu ly tel:
- So dien thoai render thanh `href="tel:..."`, giu text hien thi goc.

## 11. Cach mo request detail + checkin
- Ten khach hang link den `YeuCau/Edit/{id}?activeTab=checkin`.
- `YeuCauController.Edit` da nhan `activeTab` va view detail dung tab mechanism san co.

## 12. Thay doi thu tu cot Phieu yeu cau
- Thu tu dau bang: Tien do, Khach hang / Dia diem, Ngay yeu cau, Trang thai, Ma yeu cau, cac cot con lai.
- Khong thay doi filter, pagination, action, link edit/xoa.

## 13. Responsive da xu ly
- Popup fullscreen `100dvh`.
- Card mobile-first voi map 128px, 112px o mobile nho, va chuyen 1 cot o 360px.
- Tablet/desktop hien 2 cot.

## 14. Test da chay
- `dotnet build ApptechDashboard.csproj`

## 15. Build result
- Build succeeded, 0 warnings, 0 errors.

## Employee permission/security
- Permission code moi: `ChamCong_SelectEmployee`.
- Permission nay duoc seed trong `PermissionCatalogService.EnsureChamCongSelectEmployeePermissionsAsync()` va duoc goi tu `Program.cs`.
- Dashboard hien employee selector bang `ChamCongDashboardModel.CanSelectEmployees`, lay tu `CanSelectChamCongEmployeesAsync`.
- Popup Checkin cong trinh dung cung `CanSelectEmployees`: co quyen thi render dropdown rieng; khong co quyen thi chi hien text ten nhan vien hien tai.
- `CanSelectChamCongEmployeesAsync` chap nhan Admin, `ChamCong_SelectEmployee`, va giu tuong thich permission cu `Dashboard_View_CheckIn`/`Dasboard_View_CheckIn`.
- API `Home/CheckinCongTrinhRequests` khong tin `employeeId` tu client. User khong co quyen bi force ve `CurrentEmployeeId`; neu co tinh query employeeId khac thi tra `Forbid/403`.
- API chi tra `employeeOptions` khi `canSelectEmployees == true`, tranh leak employee id/ten nhan vien cho user thuong.
- User chua lien ket employee va khong co quyen se nhan loi: `Tai khoan chua lien ket nhan vien nen khong the xem phieu yeu cau.`
- Test unauthorized employee ID: backend path da co guard `employeeId != currentEmployeeId => Forbid()` khi khong co quyen.
- Test admin/delegated permission: cung di qua `CanSelectChamCongEmployeesAsync`; khong hard-code chi Admin.

## 16. Commit hash
- Bao cao trong phan tra loi cuoi sau khi commit.

## 17. Deploy/publish result
- Khong deploy/publish vi repository khong co yeu cau workflow publish trong turn nay.

## 18. Cac van de con ton tai
- Browser/manual QA bi chan trong moi truong nay vi Computer Use bao `Browser is not available` cho ca Chrome va in-app browser.
- Chua kiem tra duoc bang login user/DB thuc te tren cac viewport 360/390/414/430/768/desktop.
- Can verify lai tren moi truong co browser that: permission selector, request switching, map thumbnail Leaflet, fullscreen map, direction, phone, detail -> checkin, empty state, missing GPS.
