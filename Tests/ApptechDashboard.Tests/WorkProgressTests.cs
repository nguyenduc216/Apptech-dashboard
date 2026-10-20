using System.Data;
using System.Security.Claims;
using ApptechDashboard.Controllers;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class WorkProgressTests
{
    [Theory]
    [InlineData("Chờ vật tư")]
    [InlineData("CUSTOM_PAUSED")]
    public void UnknownLegacyStatusIsPreserved(string status) => Assert.Equal(status,YeuCauCongViecTrangThaiCatalog.Normalize(status));

    [Fact]
    public void DynamicSemanticKindDoesNotDependOnDisplayName()
    {
        WorkStatusContext.Begin();
        try
        {
            WorkStatusContext.Current!.Statuses=[new WorkStatus { StorageValue="DONE_CUSTOM",Name="Đã nghiệm thu",Kind="Completed",Active=false,Color="#ffffff" }];
            Assert.True(YeuCauCongViecTrangThaiCatalog.IsCompleted("DONE_CUSTOM"));
            Assert.Equal("Đã nghiệm thu",YeuCauCongViecTrangThaiCatalog.Label("DONE_CUSTOM"));
            Assert.Empty(YeuCauCongViecTrangThaiCatalog.Options);
            Assert.Equal("#ffffff",YeuCauCongViecTrangThaiCatalog.Color("DONE_CUSTOM"));
        }
        finally { WorkStatusContext.End(); }
    }

    [Theory]
    [InlineData("#ffffff","#111827")]
    [InlineData("#000000","#ffffff")]
    [InlineData("#f59e0b","#111827")]
    public void BadgeUsesContrastingText(string color,string expected) => Assert.Equal(expected,WorkProgressRules.TextColor(color));

    [Fact]
    public void DatesAreValidatedIndependentlyFromAttendance()
    {
        Assert.True(WorkProgressRules.ValidDates(null,null));
        Assert.True(WorkProgressRules.ValidDates(DateTime.Today,null));
        Assert.False(WorkProgressRules.ValidDates(DateTime.Today,DateTime.Today.AddDays(-1)));
    }

    [Fact]
    public async Task ControllerRejectsMissingPermissionBeforeReadingOrMoving()
    {
        var account=Guid.NewGuid(); var users=new Mock<IUserAccountService>(); var permissions=new Mock<IUserPermissionService>();
        users.Setup(s=>s.GetAccountByIdAsync(account,It.IsAny<CancellationToken>())).ReturnsAsync(new UserAccount { Id=account, EmployeeId=1 });
        permissions.Setup(s=>s.GetPermissionsAsync(account,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<UserPermission>());
        var service=new WorkProgressService(new ConfigurationBuilder().Build(),permissions.Object,users.Object);
        var controller=new TienDoCongViecController(service,NullLogger<TienDoCongViecController>.Instance) { ControllerContext=new ControllerContext { HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext { User=Principal(account) } } };
        Assert.IsType<ForbidResult>(await controller.Index(new WorkBoardQuery(),default));
        Assert.IsType<ForbidResult>(await controller.Move(new WorkMove { Id=1,StatusId=1,Version="invalid" },default));
    }

    public static ClaimsPrincipal Principal(Guid id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,id.ToString()),new Claim(ClaimTypes.Name,"kanban-test")],"test"));
}

public sealed class LocalSqlFactAttribute : FactAttribute
{
    public LocalSqlFactAttribute() { if(Environment.GetEnvironmentVariable("APPTECH_KANBAN_TEST_SQL") is null) Skip="Set APPTECH_KANBAN_TEST_SQL to an explicitly authorized local SQL test server connection."; }
}

public sealed class WorkProgressSqlTests
{
    [LocalSqlFact]
    public async Task ReviewedOrphansAreRetainedWithoutParentFabricationAndExcludedFromKanban()
    {
        var root=FindRoot();
        var cs=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("APPTECH_KANBAN_TEST_SQL"));
        Assert.Contains(cs.DataSource,new[]{".","localhost","(local)",@".\SQLEXPRESS"});
        var name="ApptechKanban_Test_Orphans_"+Guid.NewGuid().ToString("N");
        cs.InitialCatalog="master";
        await using var master=new SqlConnection(cs.ConnectionString); await master.OpenAsync();
        await Execute(master,$"CREATE DATABASE [{name}]");
        try
        {
            cs.InitialCatalog=name;
            await using var db=new SqlConnection(cs.ConnectionString); await db.OpenAsync();
            await Execute(db,File.ReadAllText(Path.Combine(root,"Tests/ApptechDashboard.Tests/Fixtures/WorkProgress.sql")));
            await Execute(db,"""
                DELETE dbo.TblYeuCauCongViecNhanVien; DELETE dbo.TblYeuCauCongViec;
                SET IDENTITY_INSERT dbo.TblYeuCauCongViec ON;
                ;WITH tally AS(SELECT 1 n UNION ALL SELECT n+1 FROM tally WHERE n<40)
                INSERT dbo.TblYeuCauCongViec(ID,IDYeuCau,IDCongViec,TrangThaiCongViec)
                SELECT 100+n,1,1,CASE WHEN n<=28 THEN N'Tạo mới' ELSE N'Hoàn thành' END FROM tally;
                INSERT dbo.TblYeuCauCongViec(ID,IDYeuCau,IDCongViec,TrangThaiCongViec) VALUES
                 (5036,4030,2030,N'Tạo mới'),(5037,4031,2030,N'Tạo mới'),(5038,4032,2030,N'Tạo mới'),
                 (5048,4042,2026,N'Hoàn thành'),(5049,4042,2021,N'Hoàn thành'),
                 (5050,4042,2019,N'Tạo mới'),(5051,4042,2025,N'Tạo mới'),(5052,4042,2027,N'Tạo mới'),
                 (5055,4044,2028,N'Tạo mới'),(5056,4045,2028,N'Tạo mới'),
                 (6026,5024,2022,N'Tạo mới'),(6027,5024,2024,N'Tạo mới'),(6028,5025,2034,N'Tạo mới');
                SET IDENTITY_INSERT dbo.TblYeuCauCongViec OFF;
                INSERT dbo.TblYeuCauCongViecNhanVien(IDYeuCauCongViec,IDNhanVien) VALUES(5036,1);
                INSERT dbo.TblYeuCauCongViecCheckList(IDCongViec,IDCheckList,FinishDate,FinishBy) VALUES(5048,88,'20260101',N'legacy-user');
                SELECT ID,IDYeuCau,IDCongViec,TrangThaiCongViec,GhiChu,CheckInTime,CheckoutTime,Updated_By,Updated_Date,Created_By,Created_Date INTO #BeforeOrphanMigration FROM dbo.TblYeuCauCongViec;
                """);
            var script=File.ReadAllText(Path.Combine(root,"App_Data/Migrations/20261020_work_progress_kanban.sql"))
                .Replace("@SchemaReviewed bit=0","@SchemaReviewed bit=1");
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,script.Replace("@PreserveReviewedOrphans bit=1","@PreserveReviewedOrphans bit=0")));
            await Execute(db,"UPDATE dbo.TblYeuCauCongViec SET IDCongViec=9999 WHERE ID=5036");
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,script)); // changed approved tuple
            Assert.Equal(0,await Scalar(db,"SELECT COUNT(*) FROM sys.tables WHERE name='TblWorkProgressLegacyOrphan'"));
            await Execute(db,"UPDATE dbo.TblYeuCauCongViec SET IDCongViec=2030 WHERE ID=5036; INSERT dbo.TblYeuCauCongViec(IDYeuCau,IDCongViec,TrangThaiCongViec) VALUES(9999,1,N'Tạo mới');");
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,script)); // additional unknown orphan
            await Execute(db,"DELETE dbo.TblYeuCauCongViec WHERE IDYeuCau=9999");
            await Execute(db,script); await Execute(db,script);
            Assert.Equal(53,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec"));
            Assert.Equal(13,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblWorkProgressLegacyOrphan"));
            Assert.Equal(2,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCau")); // no fabricated request parents
            Assert.Equal(0,await Scalar(db,"SELECT COUNT(*) FROM (SELECT * FROM #BeforeOrphanMigration EXCEPT SELECT ID,IDYeuCau,IDCongViec,TrangThaiCongViec,GhiChu,CheckInTime,CheckoutTime,Updated_By,Updated_Date,Created_By,Created_Date FROM dbo.TblYeuCauCongViec) changed"));
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViecNhanVien WHERE IDYeuCauCongViec=5036 AND IDNhanVien=1"));
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViecCheckList WHERE IDCongViec=5048 AND FinishDate='20260101' AND FinishBy=N'legacy-user'"));
            Assert.Equal(0,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViecTrangThaiHistory"));
            Assert.Equal(0,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec WHERE ActualCompletionDate IS NOT NULL"));
            var id=Guid.NewGuid(); var accounts=new Mock<IUserAccountService>();
            accounts.Setup(a=>a.GetAccountByIdAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(new UserAccount { Id=id,IsAdministrator=true });
            var progress=new WorkProgressService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"]=cs.ConnectionString }).Build(),new Mock<IUserPermissionService>().Object,accounts.Object);
            var user=WorkProgressTests.Principal(id); var board=await progress.Board(new WorkBoardQuery(),user,default);
            Assert.Equal(40,board.Total); Assert.Equal(40,board.Cards.Count);
            Assert.DoesNotContain(board.Cards,c=>c.Id>=5036);
            Assert.Equal(28,board.Counts[board.Statuses.Single(s=>s.Code=="NEW").Id]);
            Assert.Equal(12,board.Counts[board.Statuses.Single(s=>s.Code=="COMPLETED").Id]);
            await using(var cmd=new SqlCommand("SELECT ProgressVersion FROM dbo.TblYeuCauCongViec WHERE ID=5036",db))
            {
                var version=Convert.ToBase64String((byte[])(await cmd.ExecuteScalarAsync())!);
                await Assert.ThrowsAsync<SqlException>(()=>progress.Move(new WorkMove { Id=5036,StatusId=board.Statuses.Single(s=>s.Code=="COMPLETED").Id,Version=version },user,default));
            }
            var data=File.ReadAllText(Path.Combine(root,"App_Data/Migrations/20261020_work_progress_status_data.sql"))
                .Replace("@ApplyChanges bit=0","@ApplyChanges bit=1").Replace("@RequireOriginalSnapshot bit=0","@RequireOriginalSnapshot bit=1");
            await Execute(db,data); // snapshot still counts all 53, including retained history
            await Execute(db,File.ReadAllText(Path.Combine(root,"sql/20261020_work_progress_verify.sql")));
            await Execute(db,"INSERT dbo.TblYeuCauCongViec(IDYeuCau,IDCongViec,TrangThaiCongViec) VALUES(9999,1,N'Tạo mới')");
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,script)); // even installed/rerun path must reject new orphan
        }
        finally { SqlConnection.ClearAllPools(); await Execute(master,$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]"); }
    }

    // CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-002 — reproduce the user's exact 39/14 text snapshot.
    [LocalSqlFact]
    public async Task StatusDataMigrationPreservesSnapshotDatesVersionsAndCatalogCustomization()
    {
        var root=FindRoot();
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("APPTECH_KANBAN_TEST_SQL"));
        Assert.Contains(connection.DataSource,new[]{".","localhost","(local)",@".\SQLEXPRESS"});
        var name="ApptechKanban_Test_Mapping_"+Guid.NewGuid().ToString("N");
        connection.InitialCatalog="master";
        await using var master=new SqlConnection(connection.ConnectionString); await master.OpenAsync();
        await Execute(master,$"CREATE DATABASE [{name}]");
        try
        {
            connection.InitialCatalog=name;
            await using var db=new SqlConnection(connection.ConnectionString); await db.OpenAsync();
            await Execute(db,File.ReadAllText(Path.Combine(root,"Tests/ApptechDashboard.Tests/Fixtures/WorkProgress.sql")));
            await Execute(db,"""
                DELETE dbo.TblYeuCauCongViecNhanVien;
                DELETE dbo.TblYeuCauCongViec;
                ;WITH tally AS (SELECT 1 AS n UNION ALL SELECT n+1 FROM tally WHERE n<53)
                INSERT dbo.TblYeuCauCongViec(IDYeuCau,IDCongViec,TrangThaiCongViec)
                SELECT 1,1,CASE WHEN n<=39 THEN N'Tạo mới' ELSE N'Hoàn thành' END FROM tally;
                """);
            await Execute(db,File.ReadAllText(Path.Combine(root,"sql/20261020_work_progress_preflight.sql")));
            var data=File.ReadAllText(Path.Combine(root,"App_Data/Migrations/20261020_work_progress_status_data.sql"));
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,data)); // schema prerequisite
            var schema=File.ReadAllText(Path.Combine(root,"App_Data/Migrations/20261020_work_progress_kanban.sql"))
                .Replace("@SchemaReviewed bit=0","@SchemaReviewed bit=1");
            await Execute(db,schema);
            await Execute(db,"""
                DELETE dbo.TblTrangThaiCongViec WHERE Code IN(N'IN_PROGRESS',N'REVIEW',N'CANCELLED');
                UPDATE dbo.TblTrangThaiCongViec SET Name=N'Đã nghiệm thu',Color='#aabbcc',SortOrder=77,Active=0 WHERE Code=N'COMPLETED';
                UPDATE dbo.TblYeuCauCongViec SET ActualCompletionDate='20250101' WHERE ID=(SELECT MIN(ID) FROM dbo.TblYeuCauCongViec WHERE TrangThaiCongViec=N'Hoàn thành');
                SELECT * INTO #WorkBeforeMapping FROM dbo.TblYeuCauCongViec;
                SELECT * INTO #CatalogBeforeMapping FROM dbo.TblTrangThaiCongViec;
                """);
            await Execute(db,data); // preview only, must not insert missing seeds
            Assert.Equal(2,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblTrangThaiCongViec"));
            var apply=data.Replace("@ApplyChanges bit=0","@ApplyChanges bit=1");
            var strict=apply.Replace("@RequireOriginalSnapshot bit=0","@RequireOriginalSnapshot bit=1");
            await Execute(db,strict);
            await Execute(db,File.ReadAllText(Path.Combine(root,"sql/20261020_work_progress_verify.sql")));
            Assert.Equal(5,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblTrangThaiCongViec"));
            Assert.Equal(39,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec w JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=w.TrangThaiCongViec WHERE s.Code=N'NEW'"));
            Assert.Equal(14,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec w JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=w.TrangThaiCongViec WHERE s.Code=N'COMPLETED'"));
            Assert.Equal(0,await Scalar(db,"SELECT COUNT(*) FROM (SELECT * FROM #WorkBeforeMapping EXCEPT SELECT * FROM dbo.TblYeuCauCongViec) differences"));
            Assert.Equal(0,await Scalar(db,"SELECT COUNT(*) FROM (SELECT * FROM #CatalogBeforeMapping EXCEPT SELECT * FROM dbo.TblTrangThaiCongViec) differences"));
            Assert.Equal(0,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViecTrangThaiHistory"));
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec WHERE ActualCompletionDate='20250101'"));
            await Execute(db,"SELECT * INTO #CatalogAfterMapping FROM dbo.TblTrangThaiCongViec");
            await Execute(db,strict); // rerun leaves existing catalog rowversions unchanged
            Assert.Equal(0,await Scalar(db,"SELECT COUNT(*) FROM (SELECT * FROM #CatalogAfterMapping EXCEPT SELECT * FROM dbo.TblTrangThaiCongViec) differences"));
            var rollback=File.ReadAllText(Path.Combine(root,"sql/20261020_work_progress_status_data_rollback.sql"));
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,rollback));
            var allowRemoval=rollback.Replace("@AllowCatalogRemoval bit=0","@AllowCatalogRemoval bit=1");
            const string declaration="DECLARE @InsertedRows TABLE (ID int PRIMARY KEY,Code nvarchar(50),StorageValue nvarchar(50));";
            var referencedRemoval=allowRemoval.Replace(declaration,declaration+" INSERT @InsertedRows SELECT ID,Code,StorageValue FROM dbo.TblTrangThaiCongViec WHERE Code=N'NEW';");
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,referencedRemoval));
            var removeInserted=allowRemoval.Replace(declaration,declaration+" INSERT @InsertedRows SELECT ID,Code,StorageValue FROM dbo.TblTrangThaiCongViec WHERE Code IN(N'IN_PROGRESS',N'REVIEW',N'CANCELLED');");
            await Execute(db,removeInserted);
            Assert.Equal(2,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblTrangThaiCongViec"));
            // Simulate legacy data outside a trusted FK, only inside this disposable test database.
            await Execute(db,"""
                ALTER TABLE dbo.TblYeuCauCongViec NOCHECK CONSTRAINT FK_WorkProgress_Status;
                INSERT dbo.TblYeuCauCongViec(IDYeuCau,IDCongViec,TrangThaiCongViec) VALUES(1,1,N'Chưa xác định');
                ALTER TABLE dbo.TblYeuCauCongViec CHECK CONSTRAINT FK_WorkProgress_Status;
                """);
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,apply));
            Assert.Equal(2,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblTrangThaiCongViec")); // seed INSERTs rolled back too
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec WHERE TrangThaiCongViec=N'Chưa xác định'"));
            await Execute(db,"DELETE dbo.TblYeuCauCongViec WHERE TrangThaiCongViec=N'Chưa xác định'; ALTER TABLE dbo.TblYeuCauCongViec WITH CHECK CHECK CONSTRAINT FK_WorkProgress_Status;");
            await Execute(db,"UPDATE dbo.TblTrangThaiCongViec SET Kind='Normal' WHERE Code=N'COMPLETED'");
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,apply));
            Assert.Equal(2,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblTrangThaiCongViec"));
            await Execute(db,"UPDATE dbo.TblTrangThaiCongViec SET Kind='Completed' WHERE Code=N'COMPLETED'");
            await Execute(db,"DELETE dbo.TblYeuCauCongViec WHERE ID=(SELECT MIN(ID) FROM dbo.TblYeuCauCongViec WHERE TrangThaiCongViec=N'Tạo mới')");
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,strict)); // snapshot drift is explicit, not silently remapped
            await Execute(db,apply); // reviewed current counts are supported without permanently enforcing 39/14
            Assert.Equal(52,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec"));
            Assert.Equal(5,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblTrangThaiCongViec"));
        }
        finally { SqlConnection.ClearAllPools(); await Execute(master,$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]"); }
    }

    // Creates only a synthetic, isolated test DB; never connects using application configuration.
    [LocalSqlFact]
    public async Task MigrationCatalogMovesScopeHistoryDatesConcurrencyAndRollback()
    {
        var root=FindRoot();
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("APPTECH_KANBAN_TEST_SQL"));
        Assert.Contains(connection.DataSource,new[]{".","localhost","(local)",@".\SQLEXPRESS"});
        var name="ApptechKanban_Test_"+Guid.NewGuid().ToString("N");
        connection.InitialCatalog="master";
        await using var master=new SqlConnection(connection.ConnectionString); await master.OpenAsync();
        await Execute(master,$"CREATE DATABASE [{name}]");
        try
        {
            connection.InitialCatalog=name;
            await using var db=new SqlConnection(connection.ConnectionString); await db.OpenAsync();
            await Execute(db,File.ReadAllText(Path.Combine(root,"Tests/ApptechDashboard.Tests/Fixtures/WorkProgress.sql")));
            await Execute(db,"INSERT dbo.TblYeuCauCongViec(IDYeuCau,IDCongViec,TrangThaiCongViec) VALUES(2,1,N'Hoan thanh')");
            var migration=File.ReadAllText(Path.Combine(root,"App_Data/Migrations/20261020_work_progress_kanban.sql"));
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,migration)); // explicit review gate
            migration=migration.Replace("@SchemaReviewed bit=0","@SchemaReviewed bit=1");
            await Execute(db,migration); await Execute(db,migration); // idempotent rerun
            Assert.Equal(4,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec"));
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblTrangThaiCongViec WHERE StorageValue=N'Hoan thanh' AND Kind='Completed' AND Active=0"));
            Assert.Equal(0,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec WHERE StartDate IS NOT NULL OR ActualCompletionDate IS NOT NULL"));
            var id=Guid.NewGuid(); var users=new Mock<IUserAccountService>(); var permissions=new Mock<IUserPermissionService>();
            users.Setup(s=>s.GetAccountByIdAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(new UserAccount { Id=id,EmployeeId=1 });
            permissions.Setup(s=>s.GetPermissionsAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{new UserPermission { PermissionCode="TienDoCongViec_View" },new UserPermission { PermissionCode="TienDoCongViec_Update" }});
            var service=new WorkProgressService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"]=connection.ConnectionString }).Build(),permissions.Object,users.Object);
            var principal=WorkProgressTests.Principal(id);
            var statuses=await service.Statuses(default);
            var legacy=Assert.Single(statuses,s=>s.StorageValue=="Chờ vật tư cũ"); Assert.False(legacy.Active);
            var board=await service.Board(new WorkBoardQuery(),principal,default);
            Assert.Equal(2,board.Total); Assert.DoesNotContain(board.Cards,c=>c.RequestId==2); Assert.Single(board.Customers);
            Assert.Contains(board.Cards,c=>c.StatusId==legacy.Id); Assert.All(board.Cards,c=>Assert.Single(c.Employees));
            await Execute(db,"UPDATE dbo.TblNhanVien SET Ten=N'An mới' WHERE ID=1");
            Assert.All((await service.Board(new WorkBoardQuery(),principal,default)).Cards,c=>Assert.Contains("Nguyễn An mới",c.Employees));
            var custom=new WorkStatus { Code="WAIT_PARTS",Name="Chờ vật tư",Color="#00aabb",SortOrder=25 };
            await service.SaveStatus(custom,default);
            await Assert.ThrowsAsync<SqlException>(()=>service.SaveStatus(custom,default)); // duplicate code
            custom=(await service.Statuses(default)).Single(s=>s.Code==custom.Code);
            custom.Name="Chờ linh kiện"; custom.Color="#aabbcc"; await service.SaveStatus(custom,default);
            await Assert.ThrowsAsync<SqlException>(()=>service.SaveStatus(custom,default)); // stale catalog update
            custom=(await service.Statuses(default)).Single(s=>s.Code==custom.Code);
            var card=board.Cards.Single(c=>c.Id==1);
            await service.Move(new WorkMove { Id=1,StatusId=custom.Id,Version=card.Version },principal,default);
            await Assert.ThrowsAsync<SqlException>(()=>service.Move(new WorkMove { Id=1,StatusId=custom.Id,Version=card.Version },principal,default));
            custom.Active=false; await service.SaveStatus(custom,default);
            board=await service.Board(new WorkBoardQuery(),principal,default); card=board.Cards.Single(c=>c.Id==1);
            Assert.Equal("Chờ linh kiện",board.Statuses.Single(s=>s.Id==card.StatusId).Name);
            var normal=statuses.Single(s=>s.Code=="IN_PROGRESS"); var done=statuses.Single(s=>s.Code=="COMPLETED");
            await service.Move(new WorkMove { Id=1,StatusId=normal.Id,Version=card.Version },principal,default);
            card=(await service.Board(new WorkBoardQuery(),principal,default)).Cards.Single(c=>c.Id==1);
            var count=await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViecTrangThaiHistory");
            await Assert.ThrowsAsync<SqlException>(()=>service.Move(new WorkMove { Id=1,StatusId=custom.Id,Version=card.Version },principal,default));
            Assert.Equal(count,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViecTrangThaiHistory")); // rollback
            await Assert.ThrowsAsync<SqlException>(()=>service.Move(new WorkMove { Id=3,StatusId=normal.Id,Version=card.Version },principal,default)); // out of scope
            await Execute(db,"UPDATE dbo.TblYeuCauCongViec SET StartDate='20260101',ExpectedCompletionDate='20260102',CheckInTime='20260101 08:00',CheckoutTime='20260101 09:00' WHERE ID=1");
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,"UPDATE dbo.TblYeuCauCongViec SET ExpectedCompletionDate='20251231' WHERE ID=1"));
            board=await service.Board(new WorkBoardQuery { Overdue=true },principal,default); Assert.True(Assert.Single(board.Cards).Overdue);
            card=board.Cards.Single();
            await service.Move(new WorkMove { Id=1,StatusId=done.Id,Version=card.Version },principal,default);
            card=(await service.Board(new WorkBoardQuery(),principal,default)).Cards.Single(c=>c.Id==1); Assert.NotNull(card.Actual); Assert.True(card.Late);
            var first=card.Actual;
            await Execute(db,"UPDATE dbo.TblYeuCauCongViec SET GhiChu=N'Unrelated save' WHERE ID=1");
            card=(await service.Board(new WorkBoardQuery(),principal,default)).Cards.Single(c=>c.Id==1); Assert.Equal(first,card.Actual);
            await service.Move(new WorkMove { Id=1,StatusId=normal.Id,Version=card.Version },principal,default);
            card=(await service.Board(new WorkBoardQuery(),principal,default)).Cards.Single(c=>c.Id==1); Assert.Null(card.Actual);
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViecTrangThaiHistory WHERE YeuCauCongViecId=1 AND PreviousCompletionDate IS NOT NULL"));
            await service.Move(new WorkMove { Id=1,StatusId=done.Id,Version=card.Version },principal,default);
            card=(await service.Board(new WorkBoardQuery(),principal,default)).Cards.Single(c=>c.Id==1); Assert.True(card.Actual>=first);
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec WHERE ID=1 AND CheckInTime='20260101 08:00' AND CheckoutTime='20260101 09:00'"));
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec WHERE ID=2 AND TrangThaiCongViec=N'Chờ vật tư cũ'"));
            // Two simultaneous clients with the same snapshot: exactly one wins.
            var moves=Enumerable.Range(0,2).Select(async _=> { try { await service.Move(new WorkMove { Id=1,StatusId=normal.Id,Version=card.Version },principal,default); return true; } catch(SqlException) { return false; } });
            Assert.Single(await Task.WhenAll(moves),ok=>ok);
            // Exact record status change from request editor SQL follows the same trigger semantics.
            await Execute(db,"UPDATE dbo.TblYeuCauCongViec SET TrangThaiCongViec=N'Hoàn thành',Updated_By=N'request-editor' WHERE ID=1 AND IDYeuCau=1");
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec WHERE ID=1 AND ActualCompletionDate IS NOT NULL"));
            await VerifyRequestEditor(service,db,principal,connection.ConnectionString);
            Assert.Equal(0,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblVaiTroVaQuyen"));
            var rollback=File.ReadAllText(Path.Combine(root,"sql/20261020_work_progress_rollback.sql"));
            await Assert.ThrowsAsync<SqlException>(()=>Execute(db,rollback));
            await Execute(db,rollback.Replace("@AllowDataLoss bit=0","@AllowDataLoss bit=1"));
            Assert.Equal(5,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec"));
        }
        finally { SqlConnection.ClearAllPools(); await Execute(master,$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]"); }
    }
    private static async Task VerifyRequestEditor(WorkProgressService progress,SqlConnection db,ClaimsPrincipal user,string cs)
    {
        var board=await progress.Board(new WorkBoardQuery(),user,default);
        var first=board.Cards.Single(c=>c.Id==1); var second=board.Cards.Single(c=>c.Id==2);
        await Execute(db,"INSERT dbo.TblYeuCauCongViecCheckList(IDCongViec,IDCheckList,FinishDate,FinishBy) VALUES(1,99,GETDATE(),N'kanban-test')");
        var forms=new List<YeuCauCongViecFormItem>
        {
            new() { YeuCauCongViecId=1,CongViecId=1,TrangThaiCongViec="Đang thực hiện",ProgressVersion=first.Version,StartDate=first.Start,ExpectedCompletionDate=first.Expected,CheckInTime=new DateTime(2026,1,1,8,0,0),CheckOutTime=new DateTime(2026,1,1,9,0,0),NhanViens=[new(){NhanVienId=1}],Checklists=[new(){ChecklistId=99,IsCompleted=true}] },
            new() { YeuCauCongViecId=2,CongViecId=1,TrangThaiCongViec="Chờ vật tư cũ",ProgressVersion=second.Version,NhanViens=[new(){NhanVienId=1}] }
        };
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"]=cs }).Build();
        var service=new YeuCauService(Microsoft.Extensions.Options.Options.Create(new ApptechDashboard.Configuration.SqlServerOptions()),config,NullLogger<YeuCauService>.Instance,new Mock<ICommonAuditService>().Object);
        var method=typeof(YeuCauService).GetMethod("SyncAssignedWorksAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        WorkStatusContext.Begin();
        try
        {
            WorkStatusContext.Current!.Statuses=await progress.Statuses(default);
            await using(var tx=(SqlTransaction)await db.BeginTransactionAsync())
            { await (Task)method.Invoke(service,[db,tx,1,forms,"kanban-test",CancellationToken.None])!; await tx.CommitAsync(); }
            board=await progress.Board(new WorkBoardQuery(),user,default);
            Assert.Null(board.Cards.Single(c=>c.Id==1).Actual);
            Assert.Equal(second.StatusId,board.Cards.Single(c=>c.Id==2).StatusId);
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViecCheckList WHERE IDCongViec=1 AND IDCheckList=99 AND FinishDate IS NOT NULL"));
            Assert.Equal(1,await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec WHERE ID=1 AND CheckInTime='20260101 08:00' AND CheckoutTime='20260101 09:00'"));
            // Reusing the old editor snapshot after that save must fail before deleting or writing anything.
            await using(var tx=(SqlTransaction)await db.BeginTransactionAsync())
            { await Assert.ThrowsAsync<YeuCauBusinessRuleException>(async()=>await (Task)method.Invoke(service,[db,tx,1,forms,"kanban-test",CancellationToken.None])!); await tx.RollbackAsync(); }
            var counts=await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec w JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=w.TrangThaiCongViec WHERE w.IDYeuCau=1 AND s.Kind='Completed'");
            Assert.Equal(0,new YeuCauListItem { SoCongViec=2,SoCongViecHoanThanh=counts }.TiLeHoanThanh);
            await progress.SaveStatus(new WorkStatus { Code="ACCEPTED",Name="Đã nghiệm thu",Kind="Completed" },default);
            WorkStatusContext.Current!.Statuses=await progress.Statuses(default);
            foreach(var form in forms) form.ProgressVersion=board.Cards.Single(c=>c.Id==form.YeuCauCongViecId).Version;
            forms[0].TrangThaiCongViec="ACCEPTED";
            await using(var tx=(SqlTransaction)await db.BeginTransactionAsync())
            { await (Task)method.Invoke(service,[db,tx,1,forms,"kanban-test",CancellationToken.None])!; await tx.CommitAsync(); }
            counts=await Scalar(db,"SELECT COUNT(*) FROM dbo.TblYeuCauCongViec w JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=w.TrangThaiCongViec WHERE w.IDYeuCau=1 AND s.Kind='Completed'");
            Assert.Equal(50,new YeuCauListItem { SoCongViec=2,SoCongViecHoanThanh=counts }.TiLeHoanThanh);
            board=await progress.Board(new WorkBoardQuery(),user,default);
            Assert.NotNull(board.Cards.Single(c=>c.Id==1).Actual);
            Assert.True(YeuCauCongViecTrangThaiCatalog.IsCompleted("ACCEPTED"));
            await progress.Move(new WorkMove { Id=1,StatusId=board.Statuses.Single(s=>s.Code=="IN_PROGRESS").Id,Version=board.Cards.Single(c=>c.Id==1).Version },user,default);
            board=await progress.Board(new WorkBoardQuery(),user,default);
            foreach(var form in forms) form.ProgressVersion=board.Cards.Single(c=>c.Id==form.YeuCauCongViecId).Version;
            forms[0].TrangThaiCongViec="Đang thực hiện";
            var added=new YeuCauCongViecFormItem { CongViecId=1,TrangThaiCongViec="Tạo mới",StartDate=new DateTime(2026,10,1),ExpectedCompletionDate=new DateTime(2026,10,5) };
            forms.Add(added);
            await using(var tx=(SqlTransaction)await db.BeginTransactionAsync())
            { await (Task)method.Invoke(service,[db,tx,1,forms,"kanban-test",CancellationToken.None])!; await tx.CommitAsync(); }
            Assert.True(added.YeuCauCongViecId>0);
            var addedCard=(await progress.Board(new WorkBoardQuery(),user,default)).Cards.Single(c=>c.Id==added.YeuCauCongViecId);
            Assert.Equal(added.StartDate,addedCard.Start); Assert.Equal(added.ExpectedCompletionDate,addedCard.Expected); Assert.Null(addedCard.Actual);
        }
        finally { WorkStatusContext.End(); }
    }
    private static async Task Execute(SqlConnection db,string sql) { await using var cmd=new SqlCommand(sql,db); cmd.CommandTimeout=60; await cmd.ExecuteNonQueryAsync(); }
    private static async Task<int> Scalar(SqlConnection db,string sql) { await using var cmd=new SqlCommand(sql,db); return Convert.ToInt32(await cmd.ExecuteScalarAsync()); }
    private static string FindRoot() { var dir=new DirectoryInfo(AppContext.BaseDirectory); while(dir is not null && !File.Exists(Path.Combine(dir.FullName,"ApptechDashboard.csproj"))) dir=dir.Parent; return dir?.FullName??throw new InvalidOperationException("Repository not found"); }
}
