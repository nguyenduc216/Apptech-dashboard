using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class ConstructionCheckinQueryTests
{
    [Theory]
    [InlineData(null, ConstructionCheckinSortCatalog.RecentAttendance)]
    [InlineData("unknown", ConstructionCheckinSortCatalog.RecentAttendance)]
    [InlineData("NEWEST", ConstructionCheckinSortCatalog.Newest)]
    [InlineData("oldest", ConstructionCheckinSortCatalog.Oldest)]
    [InlineData("deadline", ConstructionCheckinSortCatalog.Deadline)]
    public void Sort_IsWhitelistedWithRecentAttendanceFallback(string? value, string expected)
    {
        Assert.Equal(expected, ConstructionCheckinSortCatalog.Normalize(value));
    }

    [Fact]
    public void RecentAttendance_UsesCheckoutWhenItIsLatest()
    {
        var checkin = new DateTime(2026, 9, 21, 8, 0, 0);
        var checkout = new DateTime(2026, 9, 21, 16, 0, 0);

        Assert.Equal(checkout, YeuCauService.GetLatestAttendanceActivity(checkin, checkout));
        Assert.Equal(checkin, YeuCauService.GetLatestAttendanceActivity(checkin, null));
    }

    [Fact]
    public void RecentAttendance_IsEmployeeSpecificAndFallsBackOldestFirst()
    {
        var attendanceSql = YeuCauService.GetConstructionAttendanceApplySql();
        var orderBy = YeuCauService.GetConstructionCheckinOrderBy(null);

        Assert.Contains("attendance.IDNhanVien = @EmployeeId", attendanceSql);
        Assert.Contains("attendance.ThoiDiemCheckOut", attendanceSql);
        Assert.Contains("LastAttendance DESC", orderBy);
        Assert.EndsWith("ISNULL(yc.NgayYeuCau, yc.Created_Date) ASC, yc.ID ASC", orderBy);
    }

    [Theory]
    [InlineData(ConstructionCheckinSortCatalog.Newest, "Created_Date) DESC")]
    [InlineData(ConstructionCheckinSortCatalog.Oldest, "Created_Date) ASC")]
    [InlineData(ConstructionCheckinSortCatalog.Deadline, "NgayHetHan ASC")]
    public void ExplicitSort_UsesFixedSql(string sort, string expectedFragment)
    {
        Assert.Contains(expectedFragment, YeuCauService.GetConstructionCheckinOrderBy(sort));
    }

    [Theory]
    [InlineData("0986590425", "0986590425")]
    [InlineData("0986 590 425", "0986590425")]
    [InlineData("+84.986-590-425", "84986590425")]
    [InlineData("AppTech", "")]
    public void PhoneSearch_RemovesFormatting(string value, string expected)
    {
        Assert.Equal(expected, YeuCauService.NormalizePhoneSearch(value));
    }
}
