using System.Net;
using System.Security.Claims;
using ApptechDashboard.Configuration;
using ApptechDashboard.Controllers;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class WorkProgressAuthorizationTests
{
    [Theory]
    [InlineData("admin","",false,true,true)]
    [InlineData("ADMIN","",false,true,true)]
    [InlineData("employee","Quản trị viên",false,true,true)]
    [InlineData("employee","",true,true,true)]
    [InlineData("employee","Kỹ thuật",false,true,false)]
    [InlineData("admin-helper","",false,true,false)]
    [InlineData("admin","Quản trị viên",true,false,false)]
    public async Task CurrentAccountAdminRulesAreConsistentForPermissionAndScope(string username,string group,bool flag,bool active,bool admin)
    {
        var id=Guid.NewGuid(); var accounts=new Mock<IUserAccountService>(); var permissions=new Mock<IUserPermissionService>();
        accounts.Setup(a=>a.GetAccountByIdAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(new UserAccount { Id=id,Username=username,GroupName=group,IsAdministrator=flag,IsActive=active,EmployeeId=37 });
        permissions.Setup(p=>p.GetPermissionsAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<UserPermission>());
        var service=new WorkProgressService(new ConfigurationBuilder().Build(),permissions.Object,accounts.Object);
        var user=WorkProgressTests.Principal(id);
        Assert.Equal(admin,await service.Allowed(user,"TienDoCongViec_View",default));
        Assert.Equal((admin,active?37:0),await service.Scope(user,default));
    }

    [Fact]
    public async Task EmployeeNeedsExplicitPermissionAndCannotPromoteThroughClaims()
    {
        var id=Guid.NewGuid(); var accounts=new Mock<IUserAccountService>(); var permissions=new Mock<IUserPermissionService>();
        accounts.Setup(a=>a.GetAccountByIdAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(new UserAccount { Id=id,Username="employee",EmployeeId=37 });
        permissions.Setup(p=>p.GetPermissionsAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{new UserPermission { PermissionCode="TienDoCongViec_View" }});
        var service=new WorkProgressService(new ConfigurationBuilder().Build(),permissions.Object,accounts.Object);
        var user=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,id.ToString()),new Claim(ClaimTypes.Name,"admin"),new Claim(ClaimTypes.Role,"Administrator"),new Claim("group_name","Quản trị viên")],"test"));
        Assert.True(await service.Allowed(user,"TienDoCongViec_View",default));
        Assert.False(await service.Allowed(user,"TienDoCongViec_Update",default));
        Assert.Equal((false,37),await service.Scope(user,default));
    }

    [Fact]
    public async Task MissingCurrentAccountCannotUseStalePermissions()
    {
        var id=Guid.NewGuid(); var permissions=new Mock<IUserPermissionService>();
        permissions.Setup(p=>p.GetPermissionsAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{new UserPermission { PermissionCode="TienDoCongViec_View" }});
        var service=new WorkProgressService(new ConfigurationBuilder().Build(),permissions.Object,new Mock<IUserAccountService>().Object);
        Assert.False(await service.Allowed(WorkProgressTests.Principal(id),"TienDoCongViec_View",default));
        Assert.Equal((false,0),await service.Scope(WorkProgressTests.Principal(id),default));
    }

    [Fact]
    public async Task RealCookieForbidTerminatesAt403WhileAnonymousStillGoesToLogin()
    {
        var repo=new DirectoryInfo(AppContext.BaseDirectory);
        while(repo is not null && !File.Exists(Path.Combine(repo.FullName,"ApptechDashboard.csproj"))) repo=repo.Parent;
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath=repo!.FullName,ApplicationName=typeof(AccessController).Assembly.GetName().Name });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AccessController).Assembly);
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options=>
        {
            options.LoginPath="/dang-nhap";
            CookieAuthorization.ConfigureAccessDenied(options);
        });
        builder.Services.AddAuthorization();
        var id=Guid.NewGuid(); var accounts=new Mock<IUserAccountService>(); var permissions=new Mock<IUserPermissionService>();
        accounts.Setup(a=>a.GetAccountByIdAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(new UserAccount { Id=id,Username="employee",EmployeeId=37 });
        permissions.Setup(p=>p.GetPermissionsAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<UserPermission>());
        builder.Services.AddSingleton(accounts.Object); builder.Services.AddSingleton(permissions.Object); builder.Services.AddScoped<WorkProgressService>();
        await using var app=builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); app.MapControllerRoute("default","{controller=Home}/{action=Index}/{id?}");
        // Test-only cookie issuer; the production entrypoint does not contain this route.
        app.MapGet("/test-sign-in",async context=>await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,WorkProgressTests.Principal(id)));
        await app.StartAsync();
        try
        {
            var url=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler=new HttpClientHandler { AllowAutoRedirect=false,CookieContainer=new CookieContainer() };
            using var client=new HttpClient(handler) { BaseAddress=new Uri(url) };
            var anonymous=await client.GetAsync("/TienDoCongViec");
            Assert.Equal(HttpStatusCode.Redirect,anonymous.StatusCode);
            Assert.Contains("/dang-nhap?ReturnUrl=",anonymous.Headers.Location!.ToString());
            await client.GetAsync("/test-sign-in");
            var denied=await client.GetAsync("/TienDoCongViec");
            Assert.Equal(HttpStatusCode.Redirect,denied.StatusCode);
            Assert.Contains("/khong-co-quyen",denied.Headers.Location!.ToString());
            Assert.DoesNotContain("/dang-nhap",denied.Headers.Location.ToString());
            var terminal=await client.GetAsync(denied.Headers.Location);
            Assert.Equal(HttpStatusCode.Forbidden,terminal.StatusCode);
            Assert.Null(terminal.Headers.Location);
            Assert.Contains("Không có quyền truy cập",await terminal.Content.ReadAsStringAsync());
            var unsafeReturn=await client.GetAsync("/khong-co-quyen?ReturnUrl=https://example.com");
            Assert.Equal(HttpStatusCode.Forbidden,unsafeReturn.StatusCode); Assert.Null(unsafeReturn.Headers.Location);
            foreach(var header in new[]{"X-Requested-With","Accept"})
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,"/TienDoCongViec");
                request.Headers.Add(header,header=="Accept"?"application/json":"XMLHttpRequest");
                var json=await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Forbidden,json.StatusCode); Assert.Null(json.Headers.Location);
                Assert.Equal("application/json",json.Content.Headers.ContentType!.MediaType);
                Assert.Contains("message",await json.Content.ReadAsStringAsync());
            }
        }
        finally { await app.StopAsync(); }
    }
}
