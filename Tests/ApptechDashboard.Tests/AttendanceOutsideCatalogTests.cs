using System.ComponentModel.DataAnnotations;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
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
    public void DashboardModel_ReceivesOutsideOptions()
    {
        var model = new ChamCongDashboardModel { OutsideWorkOptions = [new() { Id = 1, Name = "Giao hàng", SortOrder = 1 }] };
        Assert.Single(model.OutsideWorkOptions);
        Assert.Equal("Giao hàng", model.OutsideWorkOptions[0].Name);
    }
}
