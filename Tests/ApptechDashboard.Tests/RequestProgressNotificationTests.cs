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

public sealed class RequestProgressNotificationTests
{
    [Fact]
    public void Detect_RequestStatusChanged_ReturnsOneRequestChange()
    {
        var changes = RequestProgressChangeDetector.Detect(
            State("Tạo mới"),
            State("Đang thực hiện"));

        Assert.NotNull(changes.RequestChange);
        Assert.Equal("Tạo mới", changes.RequestChange!.OldStatus);
        Assert.Equal("Đang thực hiện", changes.RequestChange.NewStatus);
        Assert.Empty(changes.WorkChanges);
    }

    [Fact]
    public void Detect_SameNormalizedStatuses_ReturnsNoChanges()
    {
        var changes = RequestProgressChangeDetector.Detect(
            State(" TẠO MỚI ", Work(10, "Work", " tạo mới ")),
            State("Tạo mới", Work(10, "Work", "Tạo mới")));

        Assert.False(changes.HasChanges);
    }

    [Fact]
    public void Detect_TwoExistingWorksChanged_ReturnsTwoChanges()
    {
        var changes = RequestProgressChangeDetector.Detect(
            State("Tạo mới", Work(10, "Work A", "Tạo mới"), Work(20, "Work B", "Đang thực hiện")),
            State("Tạo mới", Work(10, "Work A", "Đang thực hiện"), Work(20, "Work B", "Hoàn thành")));

        Assert.Equal(2, changes.WorkChanges.Count);
        Assert.Equal([10, 20], changes.WorkChanges.Select(change => change.RequestWorkItemId));
    }

    [Fact]
    public void Detect_OneExistingWorkChanged_ReturnsOneChange()
    {
        var changes = RequestProgressChangeDetector.Detect(
            State("Tạo mới", Work(10, "Work A", "Tạo mới")),
            State("Tạo mới", Work(10, "Work A", "Đang thực hiện")));

        var change = Assert.Single(changes.WorkChanges);
        Assert.Equal("Work A", change.WorkName);
        Assert.Equal("Tạo mới", change.OldStatus);
        Assert.Equal("Đang thực hiện", change.NewStatus);
    }

    [Fact]
    public void Detect_NewAndDeletedWorks_DoNotBecomeStatusChanges()
    {
        var changes = RequestProgressChangeDetector.Detect(
            State("Tạo mới", Work(10, "Deleted", "Tạo mới"), Work(20, "Existing", "Tạo mới")),
            State("Tạo mới", Work(20, "Existing", "Tạo mới"), Work(30, "New", "Tạo mới")));

        Assert.False(changes.HasChanges);
    }

    [Fact]
    public void MessageBuilder_AggregatesRequestAndWorksWithCurrentPublicLink()
    {
        var changes = new RequestProgressChangeSet(
            "YC-24-001",
            new RequestStatusChange("Đang thực hiện", "Hoàn thành"),
            [
                new ZaloWorkStatusChange(10, "Work A", "Tạo mới", "Đang thực hiện"),
                new ZaloWorkStatusChange(20, "Work B", "Đang thực hiện", "Hoàn thành")
            ]);

        var message = ZaloIntegrationService.BuildRequestProgressMessage(
            "YC-24-001",
            changes,
            "https://apptech.test/zalo/request/current-token");

        Assert.Contains("YC-24-001", message);
        Assert.Contains("Đang thực hiện → Hoàn thành", message);
        Assert.Contains("Work A", message);
        Assert.Contains("Work B", message);
        Assert.Contains("/zalo/request/current-token", message);
        Assert.DoesNotContain("/rating/", message);
    }

    [Fact]
    public void MessageBuilder_ManyLongWorks_UsesBoundedSummary()
    {
        var changes = new RequestProgressChangeSet(
            "YC-1",
            null,
            Enumerable.Range(1, 100)
                .Select(id => new ZaloWorkStatusChange(id, new string('A', 200), "Tạo mới", "Hoàn thành"))
                .ToList());

        var message = ZaloIntegrationService.BuildRequestProgressMessage("YC-1", changes, "https://apptech.test/zalo/request/token");

        Assert.True(message.Length < 1800);
        Assert.Contains("Có 100 công việc vừa được cập nhật", message);
    }

    [Fact]
    public async Task Update_RequestAndTwoWorksChanged_SendsOneAggregatedNotification()
    {
        var fixture = new ControllerFixture();
        fixture.SetProgressSequence(
            State("Tạo mới", Work(10, "Work A", "Tạo mới"), Work(20, "Work B", "Tạo mới")),
            State("Đang thực hiện", Work(10, "Work A", "Đang thực hiện"), Work(20, "Work B", "Hoàn thành")));
        RequestProgressChangeSet? sentChanges = null;
        fixture.ZaloMessageService
            .Setup(service => service.SendRequestProgressNotificationAsync(1, It.IsAny<RequestProgressChangeSet>(), It.IsAny<CancellationToken>()))
            .Callback<int, RequestProgressChangeSet, CancellationToken>((_, changes, _) => sentChanges = changes)
            .ReturnsAsync(ZaloSendResult.Ok("sent"));

        var result = await fixture.Controller.Update(ControllerFixture.ValidForm());

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(sentChanges?.RequestChange);
        Assert.Equal(2, sentChanges!.WorkChanges.Count);
        fixture.ZaloMessageService.Verify(
            service => service.SendRequestProgressNotificationAsync(1, It.IsAny<RequestProgressChangeSet>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Update_StatusChangedWithNotificationsOff_UpdatesWithoutSendingOrWarning()
    {
        var fixture = new ControllerFixture(notificationsEnabled: false);
        fixture.SetProgressSequence(State("Tạo mới"), State("Đang thực hiện"));

        var result = await fixture.Controller.Update(ControllerFixture.ValidForm());

        Assert.IsType<RedirectToActionResult>(result);
        fixture.VerifyNoProgressNotification();
        Assert.Equal("Cập nhật yêu cầu thành công.", fixture.Controller.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task Update_OnlyNoteOrEmployeeAssignmentChanged_DoesNotSendNotification()
    {
        var fixture = new ControllerFixture();
        fixture.SetProgressSequence(State("Tạo mới", Work(10, "Work", "Tạo mới")), State("Tạo mới", Work(10, "Work", "Tạo mới")));

        await fixture.Controller.Update(ControllerFixture.ValidForm());

        fixture.VerifyNoProgressNotification();
    }

    [Fact]
    public async Task Update_ZaloFailure_StillReturnsSuccessfulRedirect()
    {
        var fixture = new ControllerFixture();
        fixture.SetProgressSequence(State("Tạo mới"), State("Đang thực hiện"));
        fixture.ZaloMessageService
            .Setup(service => service.SendRequestProgressNotificationAsync(1, It.IsAny<RequestProgressChangeSet>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ZaloSendResult.Fail("not connected"));

        var result = await fixture.Controller.Update(ControllerFixture.ValidForm());

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("Cập nhật yêu cầu thành công", fixture.Controller.TempData["StatusMessage"]?.ToString());
        Assert.Contains("not connected", fixture.Controller.TempData["StatusMessage"]?.ToString());
        Assert.Equal("success", fixture.Controller.TempData["StatusType"]);
    }

    [Fact]
    public async Task Complete_RequestAndWorksChanged_SendsOneNotification()
    {
        var fixture = new ControllerFixture();
        fixture.SetProgressSequence(
            State("Đang thực hiện", Work(10, "Work A", "Tạo mới"), Work(20, "Work B", "Đang thực hiện")),
            State("Hoàn thành", Work(10, "Work A", "Hoàn thành"), Work(20, "Work B", "Hoàn thành")));
        fixture.RequestService
            .Setup(service => service.CompleteAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YeuCauCompleteResult(true, false, null, DateTime.Now, 2, 0));
        fixture.ZaloMessageService
            .Setup(service => service.SendRequestProgressNotificationAsync(1, It.IsAny<RequestProgressChangeSet>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ZaloSendResult.Ok("sent"));

        var result = await fixture.Controller.Complete(new YeuCauCompleteModel { Id = 1 });

        Assert.IsType<RedirectToActionResult>(result);
        fixture.ZaloMessageService.Verify(
            service => service.SendRequestProgressNotificationAsync(1, It.Is<RequestProgressChangeSet>(changes => changes.RequestChange != null && changes.WorkChanges.Count == 2), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Complete_WithNotificationsOff_CompletesWithoutSendingOrWarning()
    {
        var fixture = new ControllerFixture(notificationsEnabled: false);
        fixture.RequestService
            .Setup(service => service.GetProgressStateAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(State("Đang thực hiện"));
        fixture.RequestService
            .Setup(service => service.CompleteAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YeuCauCompleteResult(true, false, null, DateTime.Now, 0, 0));

        var result = await fixture.Controller.Complete(new YeuCauCompleteModel { Id = 1 });

        Assert.IsType<RedirectToActionResult>(result);
        fixture.VerifyNoProgressNotification();
        Assert.Equal("Đã hoàn thành phiếu yêu cầu.", fixture.Controller.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task Complete_AlreadyCompleted_DoesNotSendDuplicate()
    {
        var fixture = new ControllerFixture();
        fixture.RequestService
            .Setup(service => service.GetProgressStateAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(State("Hoàn thành"));
        fixture.RequestService
            .Setup(service => service.CompleteAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YeuCauCompleteResult(true, true, null, DateTime.Now, 0, 0));

        await fixture.Controller.Complete(new YeuCauCompleteModel { Id = 1 });

        fixture.VerifyNoProgressNotification();
    }

    [Fact]
    public void ProgressNotificationUsesDedicatedMessageTypeAndExistingSendPipeline()
    {
        var source = File.ReadAllText(FindRepositoryFile("Services", "ZaloIntegrationService.cs"));

        Assert.Contains("\"RequestProgressUpdated\"", source);
        Assert.Contains("return await SendMessageAsync(", source);
        Assert.Contains("zaloRequestService.CreateLinkAsync(yeuCauId", source);
        Assert.Contains("requireZaloUserId: true", source);
    }

    private static RequestProgressState State(string status, params RequestWorkProgressState[] works) =>
        new(1, "YC-1", status, works);

    private static RequestWorkProgressState Work(int id, string name, string status) => new(id, name, status);

    private sealed class ControllerFixture
    {
        public Mock<IYeuCauService> RequestService { get; } = new();
        public Mock<IZaloMessageService> ZaloMessageService { get; } = new();
        public YeuCauController Controller { get; }

        public ControllerFixture(bool notificationsEnabled = true)
        {
            RequestService.Setup(service => service.GetCheckinDistanceLimitMetersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(100m);
            RequestService.Setup(service => service.UpdateAsync(It.IsAny<YeuCauFormModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((true, null));

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

        public void SetProgressSequence(RequestProgressState before, RequestProgressState after)
        {
            RequestService
                .SetupSequence(service => service.GetProgressStateAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(before)
                .ReturnsAsync(after);
        }

        public void VerifyNoProgressNotification() => ZaloMessageService.Verify(
            service => service.SendRequestProgressNotificationAsync(It.IsAny<int>(), It.IsAny<RequestProgressChangeSet>(), It.IsAny<CancellationToken>()),
            Times.Never);

        public static YeuCauFormModel ValidForm() => new()
        {
            Id = 1,
            NgayYeuCau = DateTime.Today,
            IDDiaDiem = 1,
            IDKhachHang = 2,
            TrangThaiYeuCau = YeuCauTrangThaiCatalog.TaoMoi
        };
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(segments)}");
    }
}
