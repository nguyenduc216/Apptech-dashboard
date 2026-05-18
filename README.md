# Apptech Dashboard - ASP.NET Core 8 MVC

Dự án mẫu dashboard theo phong cách admin hiện đại, lấy cảm hứng từ bố cục Ace Admin và phối màu theo nhận diện Apptech Nha Trang.

## Cấu trúc chính
- ASP.NET Core 8 MVC
- `Views/Shared/_Layout.cshtml`: khung sidebar + topbar
- `Views/Home/Index.cshtml`: dashboard chính
- `wwwroot/css/site.css`: toàn bộ giao diện
- `wwwroot/js/dashboard.js`: line chart và donut chart thuần JavaScript

## Chạy dự án
```bash
cd apptech-dashboard
dotnet restore
dotnet run
```

## Gợi ý mở rộng
- Tách dữ liệu sang API hoặc database
- Thêm đăng nhập / phân quyền
- Đổi dữ liệu mẫu thành số liệu thật từ thiết bị hoặc hệ thống của bạn
- Bổ sung trang quản lý camera, khóa cửa, chiếu sáng, motor cổng
