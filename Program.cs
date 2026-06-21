using ApptechDashboard.Configuration;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Rewrite;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Database.json", optional: true, reloadOnChange: true);

var sqlServerOptions = builder.Configuration
    .GetSection(SqlServerOptions.SectionName)
    .Get<SqlServerOptions>();

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")) &&
    sqlServerOptions is { IsConfigured: true })
{
    builder.Configuration["ConnectionStrings:DefaultConnection"] = sqlServerOptions.BuildConnectionString();
}

builder.Services.Configure<SqlServerOptions>(
    builder.Configuration.GetSection(SqlServerOptions.SectionName));
builder.Services.Configure<ZaloOptions>(
    builder.Configuration.GetSection(ZaloOptions.SectionName));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/dang-nhap";
        options.LogoutPath = "/dang-xuat-he-thong";
        options.AccessDeniedPath = "/dang-nhap";
        options.SlidingExpiration = true;
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(12);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient("ZaloOA");
builder.Services.AddScoped<ISidebarMenuService, SidebarMenuService>();
builder.Services.AddScoped<IUserAccountService, UserAccountService>();
builder.Services.AddScoped<IUserPermissionService, UserPermissionService>();
builder.Services.AddScoped<IPermissionCatalogService, PermissionCatalogService>();
builder.Services.AddScoped<IDonViTinhService, DonViTinhService>();
builder.Services.AddScoped<IPhongBanService, PhongBanService>();
builder.Services.AddScoped<INhanVienService, NhanVienService>();
builder.Services.AddScoped<IVaiTroService, VaiTroService>();
builder.Services.AddScoped<IKhachHangService, KhachHangService>();
builder.Services.AddScoped<IYeuCauService, YeuCauService>();
builder.Services.AddScoped<IChamCongService, ChamCongService>();
builder.Services.AddScoped<IChamCongReportService, ChamCongReportService>();
builder.Services.AddScoped<ICongViecReportService, CongViecReportService>();
builder.Services.AddScoped<IKhoService, KhoService>();
builder.Services.AddScoped<IHangHoaService, HangHoaService>();
builder.Services.AddScoped<ICongViecService, CongViecService>();
builder.Services.AddScoped<IVatTuService, VatTuService>();
builder.Services.AddScoped<IXuatKhoService, XuatKhoService>();
builder.Services.AddScoped<INhapKhoService, NhapKhoService>();
builder.Services.AddScoped<INhapXuatImageService, NhapXuatImageService>();
builder.Services.AddScoped<INhaCungCapService, NhaCungCapService>();
builder.Services.AddScoped<ICommonAuditService, CommonAuditService>();
builder.Services.AddScoped<ISimpleExcelService, SimpleExcelService>();
builder.Services.AddScoped<ZaloIntegrationService>();
builder.Services.AddScoped<IZaloAuthService>(provider => provider.GetRequiredService<ZaloIntegrationService>());
builder.Services.AddScoped<IZaloMessageService>(provider => provider.GetRequiredService<ZaloIntegrationService>());
builder.Services.AddScoped<ICustomerLinkService>(provider => provider.GetRequiredService<ZaloIntegrationService>());
builder.Services.AddScoped<IZaloWebhookService>(provider => provider.GetRequiredService<ZaloIntegrationService>());
builder.Services.AddSingleton<IQrCodeBatchService, QrCodeBatchService>();
builder.Services.AddHostedService<ZaloTokenRefreshWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var permissionCatalogService = scope.ServiceProvider.GetRequiredService<IPermissionCatalogService>();
    await permissionCatalogService.EnsureYeuCauWorkEmployeePermissionsAsync();
    await permissionCatalogService.EnsureYeuCauCheckinDistancePermissionsAsync();
    await permissionCatalogService.EnsureCongViecReportPermissionsAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRewriter(new RewriteOptions()
    .AddRedirect("^$", "trang-chu", 301)
    .AddRedirect("^home$", "trang-chu", 301)
    .AddRedirect("^home/index$", "trang-chu", 301));
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "dashboard",
    pattern: "trang-chu",
    defaults: new { controller = "Home", action = "Index" });

app.MapControllerRoute(
    name: "login",
    pattern: "dang-nhap",
    defaults: new { controller = "Home", action = "Login" });

app.MapControllerRoute(
    name: "account-update",
    pattern: "tai-khoan/cap-nhat",
    defaults: new { controller = "Home", action = "UpdateAccount" });

app.MapControllerRoute(
    name: "account-change-password",
    pattern: "tai-khoan/doi-mat-khau",
    defaults: new { controller = "Home", action = "ChangePassword" });

app.MapControllerRoute(
    name: "logout",
    pattern: "dang-xuat-he-thong",
    defaults: new { controller = "Home", action = "Logout" });

app.MapControllerRoute(
    name: "don-vi-tinh",
    pattern: "don-vi-tinh",
    defaults: new { controller = "DonViTinh", action = "Index" });

app.MapControllerRoute(
    name: "phong-ban",
    pattern: "phong-ban",
    defaults: new { controller = "PhongBan", action = "Index" });

app.MapControllerRoute(
    name: "nhan-vien",
    pattern: "nhan-vien",
    defaults: new { controller = "NhanVien", action = "Index" });

app.MapControllerRoute(
    name: "vai-tro",
    pattern: "vai-tro",
    defaults: new { controller = "VaiTro", action = "Index" });

app.MapControllerRoute(
    name: "khach-hang",
    pattern: "khach-hang",
    defaults: new { controller = "KhachHang", action = "Index" });

app.MapControllerRoute(
    name: "yeu-cau",
    pattern: "yeu-cau",
    defaults: new { controller = "YeuCau", action = "Index" });

app.MapControllerRoute(
    name: "kho",
    pattern: "kho",
    defaults: new { controller = "Kho", action = "Index" });

app.MapControllerRoute(
    name: "hang-hoa",
    pattern: "hang-hoa",
    defaults: new { controller = "HangHoa", action = "Index" });

app.MapControllerRoute(
    name: "cong-viec",
    pattern: "cong-viec",
    defaults: new { controller = "CongViec", action = "Index" });

app.MapControllerRoute(
    name: "vat-tu",
    pattern: "vat-tu",
    defaults: new { controller = "VatTu", action = "Index" });

app.MapControllerRoute(
    name: "xuat-kho",
    pattern: "xuat-kho",
    defaults: new { controller = "XuatKho", action = "Index" });

app.MapControllerRoute(
    name: "nhap-kho",
    pattern: "nhap-kho",
    defaults: new { controller = "NhapKho", action = "Index" });

app.MapControllerRoute(
    name: "ncc",
    pattern: "ncc",
    defaults: new { controller = "NhaCungCap", action = "Index" });

app.MapControllerRoute(
    name: "nha-cung-cap",
    pattern: "nha-cung-cap",
    defaults: new { controller = "NhaCungCap", action = "Index" });

app.MapControllerRoute(
    name: "qrcode",
    pattern: "qrcode",
    defaults: new { controller = "QrCode", action = "Index" });

app.MapControllerRoute(
    name: "setting",
    pattern: "cai-dat",
    defaults: new { controller = "Setting", action = "Index" });

app.MapControllerRoute(
    name: "settings",
    pattern: "Settings",
    defaults: new { controller = "Setting", action = "Index" });

app.MapControllerRoute(
    name: "bao-cao-cham-cong",
    pattern: "bao-cao/cham-cong",
    defaults: new { controller = "Report", action = "ChamCong" });

app.MapControllerRoute(
    name: "bao-cao-cong-viec",
    pattern: "bao-cao/cong-viec",
    defaults: new { controller = "Report", action = "CongViec" });

app.MapControllerRoute(
    name: "qr-gen",
    pattern: "qr-gen",
    defaults: new { controller = "QrCode", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
