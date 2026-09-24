using System.Security.Claims;
using ApptechDashboard.Controllers;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class YeuCauCreatedZaloNotificationTests
{
    [Fact]
    public async Task Create_WhenAutomaticNotificationsAreOff_SavesWithoutSendingOrWarning()
    {
        var fixture = new ControllerFixture(notificationsEnabled: false);
        fixture.RequestService
            .Setup(service => service.CreateAsync(It.IsAny<YeuCauFormModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, 41));

        var result = await fixture.Controller.Create(ControllerFixture.ValidForm());

        Assert.IsType<RedirectToActionResult>(result);
        fixture.ZaloMessageService.Verify(
            service => service.SendRequestCreatedNotificationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal("Lưu yêu cầu thành công.", fixture.Controller.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task ManualSendSchedule_RemainsAvailableWhenAutomaticNotificationsAreOff()
    {
        var fixture = new ControllerFixture(notificationsEnabled: false);
        fixture.ZaloMessageService
            .Setup(service => service.SendBookingConfirmationAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ZaloSendResult.Ok("sent"));

        var result = await fixture.Controller.SendZaloSchedule(41, null, null, null);

        Assert.IsType<RedirectToActionResult>(result);
        fixture.ZaloMessageService.Verify(
            service => service.SendBookingConfirmationAsync(41, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_SendsNotificationOnlyAfterRequestWasCreated()
    {
        var fixture = new ControllerFixture();
        var requestWasCreated = false;
        fixture.RequestService
            .Setup(service => service.CreateAsync(It.IsAny<YeuCauFormModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => requestWasCreated = true)
            .ReturnsAsync((true, null, 42));
        fixture.ZaloMessageService
            .Setup(service => service.SendRequestCreatedNotificationAsync(42, It.IsAny<CancellationToken>()))
            .Callback(() => Assert.True(requestWasCreated))
            .ReturnsAsync(ZaloSendResult.Ok("sent"));

        var result = await fixture.Controller.Create(ControllerFixture.ValidForm());

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(YeuCauController.Edit), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues!["id"]);
        fixture.ZaloMessageService.Verify(
            service => service.SendRequestCreatedNotificationAsync(42, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_WhenNotificationReturnsFailure_StillRedirectsToSavedRequest()
    {
        var fixture = new ControllerFixture();
        fixture.RequestService
            .Setup(service => service.CreateAsync(It.IsAny<YeuCauFormModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, 43));
        fixture.ZaloMessageService
            .Setup(service => service.SendRequestCreatedNotificationAsync(43, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ZaloSendResult.Fail("not connected"));

        var result = await fixture.Controller.Create(ControllerFixture.ValidForm());

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(43, redirect.RouteValues!["id"]);
        Assert.Contains("not connected", fixture.Controller.TempData["StatusMessage"]?.ToString());
        Assert.Equal("success", fixture.Controller.TempData["StatusType"]);
    }

    [Fact]
    public async Task Create_WhenNotificationThrows_StillRedirectsToSavedRequest()
    {
        var fixture = new ControllerFixture();
        fixture.RequestService
            .Setup(service => service.CreateAsync(It.IsAny<YeuCauFormModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, 44));
        fixture.ZaloMessageService
            .Setup(service => service.SendRequestCreatedNotificationAsync(44, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Zalo unavailable"));

        var result = await fixture.Controller.Create(ControllerFixture.ValidForm());

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(44, redirect.RouteValues!["id"]);
        Assert.Contains("Có lỗi khi gửi thông báo Zalo", fixture.Controller.TempData["StatusMessage"]?.ToString());
        Assert.Equal("success", fixture.Controller.TempData["StatusType"]);
    }

    [Fact]
    public async Task Create_WhenSaveHasNoId_DoesNotAttemptNotification()
    {
        var fixture = new ControllerFixture();
        fixture.RequestService
            .Setup(service => service.CreateAsync(It.IsAny<YeuCauFormModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, (int?)null));

        var result = await fixture.Controller.Create(ControllerFixture.ValidForm());

        Assert.IsType<RedirectToActionResult>(result);
        fixture.ZaloMessageService.Verify(
            service => service.SendRequestCreatedNotificationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class ControllerFixture
    {
        public Mock<IYeuCauService> RequestService { get; } = new();
        public Mock<IZaloMessageService> ZaloMessageService { get; } = new();
        public YeuCauController Controller { get; }

        public ControllerFixture(bool notificationsEnabled = true)
        {
            RequestService
                .Setup(service => service.GetCheckinDistanceLimitMetersAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(100m);

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, "Administrator")],
                    "test"))
            };
            httpContext.Request.Form = new FormCollection([]);

            var tempDataProvider = new Mock<ITempDataProvider>();
            tempDataProvider.Setup(provider => provider.LoadTempData(httpContext)).Returns(new Dictionary<string, object>());

            Controller = new YeuCauController(
                RequestService.Object,
                Mock.Of<IKhachHangService>(),
                ZaloMessageService.Object,
                Mock.Of<IZaloSettingsService>(service => service.Current == new ApptechDashboard.Configuration.ZaloOptions
                {
                    EnableAutomaticCustomerNotifications = notificationsEnabled
                }),
                Mock.Of<IZaloRequestService>(),
                Mock.Of<IUserAccountService>(),
                Mock.Of<IUserPermissionService>(),
                Mock.Of<IDanhMucDichVuService>(),
                Mock.Of<ITravelEvaluationService>(),
                Mock.Of<IWebHostEnvironment>(),
                Mock.Of<ILogger<YeuCauController>>())
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, tempDataProvider.Object)
            };
        }

        public static YeuCauFormModel ValidForm() => new()
        {
            NgayYeuCau = DateTime.Today,
            IDDiaDiem = 1,
            IDKhachHang = 2,
            TrangThaiYeuCau = YeuCauTrangThaiCatalog.TaoMoi
        };
    }
}
