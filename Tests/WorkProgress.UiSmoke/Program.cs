using ApptechDashboard.Controllers;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.Data.SqlClient;
using Moq;
using System.Security.Claims;
var repo=new DirectoryInfo(AppContext.BaseDirectory);
while(repo is not null && !File.Exists(Path.Combine(repo.FullName,"ApptechDashboard.csproj"))) repo=repo.Parent;
var root=repo?.FullName??throw new InvalidOperationException("Repository not found");
var local=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("APPTECH_KANBAN_TEST_SQL")??throw new InvalidOperationException("Set APPTECH_KANBAN_TEST_SQL explicitly"));
if(!new[]{".","localhost","(local)",@".\SQLEXPRESS"}.Contains(local.DataSource)) throw new InvalidOperationException("Only local SQL test servers allowed");
local.InitialCatalog="master";
var dbName="ApptechKanban_Test_UI_"+Guid.NewGuid().ToString("N");
await using var master=new SqlConnection(local.ConnectionString); await master.OpenAsync();
async Task Sql(SqlConnection db,string sql) { await using var cmd=new SqlCommand(sql,db); await cmd.ExecuteNonQueryAsync(); }
await Sql(master,$"CREATE DATABASE [{dbName}]");
try {
local.InitialCatalog=dbName;var cs=local.ConnectionString;
await using(var db=new SqlConnection(cs)) { await db.OpenAsync(); await Sql(db,File.ReadAllText(Path.Combine(root,"Tests/ApptechDashboard.Tests/Fixtures/WorkProgress.sql"))); await Sql(db,File.ReadAllText(Path.Combine(root,"App_Data/Migrations/20261020_work_progress_kanban.sql")).Replace("@SchemaReviewed bit=0","@SchemaReviewed bit=1")); await Sql(db,"UPDATE dbo.TblYeuCauCongViec SET StartDate='20261018',ExpectedCompletionDate='20261019' WHERE ID=1; INSERT dbo.TblNhanVien(ID,Ho,Ten) VALUES(3,N'Lê',N'Chi'),(4,N'Phạm',N'Dũng'); INSERT dbo.TblYeuCauCongViecNhanVien(IDYeuCauCongViec,IDNhanVien) VALUES(1,2),(1,3),(1,4);"); }
var options=new WebApplicationOptions { ContentRootPath=root,WebRootPath=Path.Combine(root,"wwwroot"), ApplicationName=typeof(TienDoCongViecController).Assembly.GetName().Name };
var builder=WebApplication.CreateBuilder(options); builder.Configuration["ConnectionStrings:DefaultConnection"]=cs;
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(TienDoCongViecController).Assembly);
builder.Services.AddAuthentication("Cookie").AddCookie("Cookie",ApptechDashboard.Configuration.CookieAuthorization.ConfigureAccessDenied);builder.Services.AddAuthorization();builder.Services.AddDistributedMemoryCache();builder.Services.AddSession();
// Regression: legacy persisted admin username with QuanTriVien=false must retain both access and full scope.
var id=Guid.NewGuid(); var accounts=new Mock<IUserAccountService>(); accounts.Setup(x=>x.GetAccountByIdAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync(new UserAccount { Id=id,Username="admin",EmployeeId=1,IsAdministrator=false });
var permissions=new Mock<IUserPermissionService>(); permissions.Setup(x=>x.GetPermissionsAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<UserPermission>());
var menus=new Mock<ISidebarMenuService>(); menus.Setup(x=>x.GetMenuAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<NavigationMenuItem>());
builder.Services.AddSingleton(accounts.Object);builder.Services.AddSingleton(permissions.Object);builder.Services.AddSingleton(menus.Object);builder.Services.AddScoped<WorkProgressService>();
var app=builder.Build();app.UseStaticFiles();app.UseRouting();app.UseSession();app.UseAuthentication();
app.Use(async(ctx,next)=> {ctx.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,id.ToString()),new Claim(ClaimTypes.Name,"admin"),new Claim(ClaimTypes.Role,"Administrator"),new Claim("display_name","Kiểm thử Kanban")},"smoke"));WorkStatusContext.Begin();try {WorkStatusContext.Current!.Statuses=await ctx.RequestServices.GetRequiredService<WorkProgressService>().Statuses(ctx.RequestAborted);await next(ctx);}finally{WorkStatusContext.End();}});
app.UseAuthorization();app.MapControllerRoute("default","{controller=TienDoCongViec}/{action=Index}/{id?}");app.MapGet("/__shutdown",()=> {app.Lifetime.StopApplication();return "stopping";});
await app.RunAsync("http://127.0.0.1:5188");
} finally {SqlConnection.ClearAllPools();await Sql(master,$"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;DROP DATABASE [{dbName}]");}
