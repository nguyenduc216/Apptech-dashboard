using System.Reflection;
using ApptechDashboard.Controllers;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class ZaloPublicRequestLandingTests
{
    [Fact]
    public void GroupWorkRows_GroupsEmployeesUnderCorrectWork()
    {
        var works = Map(
            Row(10, "Vệ sinh máy lạnh", "Đang thực hiện", 1, "Nguyễn Văn A"),
            Row(10, "Vệ sinh máy lạnh", "Đang thực hiện", 2, "Trần Văn B"),
            Row(20, "Kiểm tra điện", "Hoàn thành", 3, "Lê Văn C"));

        Assert.Equal(2, works.Count);
        Assert.Equal("Vệ sinh máy lạnh", works[0].WorkName);
        Assert.Equal("Đang thực hiện", works[0].Status);
        Assert.Equal(["Nguyễn Văn A", "Trần Văn B"], works[0].Employees.Select(item => item.FullName));
        Assert.Equal(["Lê Văn C"], works[1].Employees.Select(item => item.FullName));
    }

    [Fact]
    public void GroupWorkRows_DoesNotMoveEmployeeBetweenWorks()
    {
        var works = Map(
            Row(10, "Work A", "Tạo mới", 1, "Employee A"),
            Row(20, "Work B", "Tạo mới", 2, "Employee B"));

        Assert.DoesNotContain(works[0].Employees, item => item.FullName == "Employee B");
        Assert.DoesNotContain(works[1].Employees, item => item.FullName == "Employee A");
    }

    [Fact]
    public void GroupWorkRows_WorkWithoutAssignmentHasEmptyEmployees()
    {
        var work = Assert.Single(Map(Row(10, "Work A", null, null, null)));

        Assert.Empty(work.Employees);
        Assert.False(string.IsNullOrWhiteSpace(work.Status));
    }

    [Fact]
    public void GroupWorkRows_DeduplicatesRepeatedEmployeeAssignment()
    {
        var work = Assert.Single(Map(
            Row(10, "Work A", "Tạo mới", 1, "Employee A"),
            Row(10, "Work A", "Tạo mới", 1, "Employee A")));

        Assert.Single(work.Employees);
    }

    [Fact]
    public void RenderLanding_ContainsThreeAccessibleTabsAndExistingRatingRoute()
    {
        var html = Render(new ZaloRequestLandingView
        {
            Token = "safe-token",
            RequestCode = "YC-1",
            CustomerName = "Customer"
        });

        Assert.Contains("data-tab=\"info\">Thông tin", html);
        Assert.Contains("data-tab=\"works\">Công việc", html);
        Assert.Contains("data-tab=\"rating\">Đánh giá", html);
        Assert.Contains("role=\"tablist\"", html);
        Assert.Contains("aria-selected=\"true\"", html);
        Assert.Contains("href=\"/zalo/request/safe-token/rating\"", html);
    }

    [Fact]
    public void RenderLanding_RendersEmployeesAndBothEmptyStates()
    {
        var withEmployees = Render(new ZaloRequestLandingView
        {
            Token = "token",
            RequestCode = "YC-1",
            CustomerName = "Customer",
            Works =
            [
                new ZaloRequestWorkItem
                {
                    WorkName = "Work A",
                    Status = "Đang thực hiện",
                    Employees = [new ZaloRequestEmployeeItem { EmployeeId = 99, FullName = "Nguyễn Văn A" }]
                },
                new ZaloRequestWorkItem { WorkName = "Work B", Status = "Tạo mới" }
            ]
        });
        var withoutWorks = Render(new ZaloRequestLandingView
        {
            Token = "token",
            RequestCode = "YC-2",
            CustomerName = "Customer"
        });

        Assert.Contains("Nguyễn Văn A", withEmployees);
        Assert.Contains("Chưa phân công nhân viên.", withEmployees);
        Assert.DoesNotContain(">99<", withEmployees);
        Assert.Contains("Chưa có danh sách công việc.", withoutWorks);
    }

    [Fact]
    public void RenderLanding_EncodesAllPublicDatabaseText()
    {
        var html = Render(new ZaloRequestLandingView
        {
            Token = "token",
            RequestCode = "<code>",
            CustomerName = "<customer>",
            PhoneNumber = "<phone>",
            Works =
            [
                new ZaloRequestWorkItem
                {
                    WorkName = "<work>",
                    Status = "<status>",
                    Employees = [new ZaloRequestEmployeeItem { FullName = "<employee>" }]
                }
            ]
        });

        Assert.DoesNotContain("<customer>", html);
        Assert.DoesNotContain("<work>", html);
        Assert.DoesNotContain("<status>", html);
        Assert.DoesNotContain("<employee>", html);
        Assert.Contains("&lt;customer&gt;", html);
        Assert.Contains("&lt;employee&gt;", html);
    }

    [Fact]
    public void LandingEmployeeModelContainsNoSensitiveEmployeeFields()
    {
        var properties = typeof(ZaloRequestEmployeeItem).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal([nameof(ZaloRequestEmployeeItem.EmployeeId), nameof(ZaloRequestEmployeeItem.FullName)], properties);
    }

    [Fact]
    public void LandingQueryIsScopedByResolvedRequestAndUsesOneAssignmentJoin()
    {
        var source = File.ReadAllText(FindRepositoryFile("Services", "ZaloRequestService.cs"));

        Assert.Contains("WHERE ycvc.IDYeuCau = @RequestId", source);
        Assert.Contains("LEFT JOIN [TblYeuCauCongViecNhanVien]", source);
        Assert.Contains("LEFT JOIN [TblNhanVien]", source);
        Assert.DoesNotContain("nv.SoDienThoai", source);
        Assert.DoesNotContain("nv.Email", source);
    }

    [Fact]
    public void ExistingConnectionAndTokenRoutesRemainAvailable()
    {
        var html = Render(new ZaloRequestLandingView
        {
            Token = "verification-token",
            RequestCode = "YC-1",
            CustomerName = "Customer",
            OaId = "oa-1"
        });
        var source = File.ReadAllText(FindRepositoryFile("Controllers", "ZaloRequestController.cs"));

        Assert.Contains("Kết nối Zalo OA", html);
        Assert.Contains("Sao chép mã xác nhận", html);
        Assert.Contains("verification-token", html);
        Assert.Contains("[HttpGet(\"zalo/request/{token}\")]", source);
        Assert.Contains("[HttpGet(\"zalo/request/{token}/rating\")]", source);
    }

    private static IReadOnlyList<ZaloRequestWorkItem> Map(params ZaloRequestService.ZaloRequestWorkEmployeeRow[] rows) =>
        ZaloRequestService.GroupWorkRows(rows);

    private static ZaloRequestService.ZaloRequestWorkEmployeeRow Row(
        int workId,
        string workName,
        string? status,
        int? employeeId,
        string? employeeName) => new(workId, workName, status, employeeId, employeeName);

    private static string Render(ZaloRequestLandingView model)
    {
        var method = typeof(ZaloRequestController).GetMethod("RenderLanding", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(ZaloRequestController), "RenderLanding");
        return Assert.IsType<string>(method.Invoke(null, [model]));
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
