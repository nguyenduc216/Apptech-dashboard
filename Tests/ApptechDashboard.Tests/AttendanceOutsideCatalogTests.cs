using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ApptechDashboard.Controllers;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class AttendanceOutsideCatalogTests
{
    [Fact]
    public void ActiveCatalog_IsFilteredAndOrderedBySortThenId()
    {
        var result = DanhMucChamCongNgoaiService.FilterAndOrderActive([
            new() { Id = 4, TenNoiDung = "B", ThuTuHienThi = 2, IsActive = true },
            new() { Id = 3, TenNoiDung = "Ẩn", ThuTuHienThi = 1, IsActive = false },
            new() { Id = 2, TenNoiDung = "A2", ThuTuHienThi = 1, IsActive = true },
            new() { Id = 1, TenNoiDung = "A1", ThuTuHienThi = 1, IsActive = true }
        ]);
        Assert.Equal(["A1", "A2", "B"], result.Select(item => item.Name));
    }

    [Fact]
    public void OutsideAttendanceContent_IsOptional()
    {
        var request = new MuaHangCheckinRequest { NoiDungCongViec = null, GhiChuNhanVien = null };
        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
        Assert.Null(ChamCongService.BuildPurchaseWorkNote(request.NoiDungCongViec, request.GhiChuNhanVien));
    }

    [Fact]
    public void OneAndMultipleSelections_AreStoredAsSnapshotText()
    {
        Assert.Equal("[Giao hàng]", ChamCongService.BuildPurchaseWorkNote("Giao hàng", null));
        Assert.Equal("[Mua vật tư; Đi ngân hàng] Gấp", ChamCongService.BuildPurchaseWorkNote("Mua vật tư; Đi ngân hàng", " Gấp "));
    }

    [Fact]
    public void HistoricalSnapshot_StillParsesAfterCatalogRenameOrDeactivate()
    {
        var history = new ChamCongHistoryItem { CheckInType = "MuaHang", GhiChuNhanVien = "[Mua vật tư; Đi ngân hàng] Gấp" };
        Assert.Equal(["Mua vật tư", "Đi ngân hàng"], history.PurchaseWorkContent);
        Assert.Equal("Gấp", history.PurchaseNote);
        Assert.Equal("Chấm công ngoài", history.Title);
    }

    [Fact]
    public void NoteOnlyPurchaseAttendance_KeepsNoteWithoutWorkContent()
    {
        var history = new ChamCongHistoryItem { CheckInType = "MuaHang", GhiChuNhanVien = "Ghi chú phát sinh" };

        Assert.Empty(history.PurchaseWorkContent);
        Assert.Equal("Ghi chú phát sinh", history.PurchaseNote);
    }

    [Theory]
    [InlineData("index")]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("set-active")]
    public async Task CatalogEndpoints_DenyUsersWithoutServerPermission(string action)
    {
        var catalogService = new Mock<IDanhMucChamCongNgoaiService>(MockBehavior.Strict);
        var permissionService = new Mock<IUserPermissionService>();
        permissionService
            .Setup(service => service.GetPermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var controller = CreateController(catalogService.Object, permissionService.Object);

        IActionResult result = action switch
        {
            "index" => await controller.Index(null, null, null),
            "create" => await controller.Save(new DanhMucChamCongNgoaiForm()),
            "update" => await controller.Save(new DanhMucChamCongNgoaiForm { Id = 1 }),
            _ => await controller.SetActive(1, false)
        };

        Assert.IsType<ForbidResult>(result);
        catalogService.VerifyNoOtherCalls();
    }

    private static DanhMucChamCongNgoaiController CreateController(
        IDanhMucChamCongNgoaiService catalogService,
        IUserPermissionService permissionService)
    {
        var accountId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, accountId.ToString())],
                "Test")),
            Session = new TestSession()
        };
        return new DanhMucChamCongNgoaiController(catalogService, permissionService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = [];
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString();
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }

    [Fact]
    public void DashboardModel_ReceivesOutsideOptions()
    {
        var model = new ChamCongDashboardModel { OutsideWorkOptions = [new() { Id = 1, Name = "Giao hàng", SortOrder = 1 }] };
        Assert.Single(model.OutsideWorkOptions);
        Assert.Equal("Giao hàng", model.OutsideWorkOptions[0].Name);
    }
}
