using System.Data;
using System.Security.Claims;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;

namespace ApptechDashboard.Services;

// FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN
// CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-001
// Grain: one card per TblYeuCauCongViec. Every read/write applies request assignment scope.
public sealed class WorkProgressService(IConfiguration configuration, IUserPermissionService permissions, IUserAccountService accounts)
{
    public const int PageSize = 30;
    private SqlConnection Connection() => new(configuration.GetConnectionString("DefaultConnection"));

    public async Task<bool> Allowed(ClaimsPrincipal user, string code, CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        var account = await accounts.GetAccountByIdAsync(id, ct);
        if (account?.IsAdministrator == true) return true;
        return (await permissions.GetPermissionsAsync(id, ct)).Any(p => p.PermissionCode == code);
    }
    public async Task<(bool Admin, int Employee)> Scope(ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return (false, 0);
        var account = await accounts.GetAccountByIdAsync(id, ct);
        return (account?.IsAdministrator == true, account?.EmployeeId ?? 0);
    }
    private const string ScopeSql = """
        (@Admin=1 OR EXISTS (SELECT 1 FROM dbo.TblYeuCauCongViec sw
         JOIN dbo.TblYeuCauCongViecNhanVien sa ON sa.IDYeuCauCongViec=sw.ID
         WHERE sw.IDYeuCau=r.ID AND sa.IDNhanVien=@ScopeEmployee AND @ScopeEmployee>0))
        """;
    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0) =>
        cmd.Parameters.Add(new SqlParameter(name, type, size) { Value = value ?? DBNull.Value });

    public async Task<IReadOnlyList<WorkStatus>> Statuses(CancellationToken ct)
    {
        await using var db = Connection(); await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT ID,Code,Name,StorageValue,Color,SortOrder,Active,Kind,Version,CreatedAt,UpdatedAt FROM dbo.TblTrangThaiCongViec ORDER BY SortOrder,ID";
        var result = new List<WorkStatus>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) result.Add(new WorkStatus { Id=rd.GetInt32(0), Code=rd.GetString(1), Name=rd.GetString(2), StorageValue=rd.GetString(3), Color=rd.GetString(4), SortOrder=rd.GetInt32(5), Active=rd.GetBoolean(6), Kind=rd.GetString(7), Version=Convert.ToBase64String((byte[])rd[8]), CreatedAt=rd.GetDateTime(9), UpdatedAt=rd.GetDateTime(10) });
        return result;
    }
    public async Task SaveStatus(WorkStatus s, CancellationToken ct)
    {
        await using var db = Connection(); await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        // Code, storage key and semantic kind remain immutable after creation.
        cmd.CommandText = s.Id == 0 ? """
            INSERT dbo.TblTrangThaiCongViec(Code,Name,StorageValue,Color,SortOrder,Active,Kind)
            VALUES(@Code,@Name,@Code,@Color,@Sort,@Active,@Kind)
            """ : """
            UPDATE dbo.TblTrangThaiCongViec SET Name=@Name,Color=@Color,SortOrder=@Sort,Active=@Active,UpdatedAt=SYSDATETIME()
            WHERE ID=@Id AND Version=@Version;
            IF @@ROWCOUNT=0 THROW 51001,N'Dữ liệu đã thay đổi. Hãy tải lại danh mục.',1;
            """;
        Add(cmd,"@Id",SqlDbType.Int,s.Id); Add(cmd,"@Code",SqlDbType.NVarChar,s.Code,50);
        Add(cmd,"@Name",SqlDbType.NVarChar,s.Name.Trim(),100); Add(cmd,"@Color",SqlDbType.VarChar,s.Color,7);
        Add(cmd,"@Sort",SqlDbType.Int,s.SortOrder); Add(cmd,"@Active",SqlDbType.Bit,s.Active); Add(cmd,"@Kind",SqlDbType.VarChar,s.Kind,12);
        Add(cmd,"@Version",SqlDbType.Binary,s.Id == 0 ? new byte[8] : Convert.FromBase64String(s.Version),8);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<WorkBoardModel> Board(WorkBoardQuery q, ClaimsPrincipal user, CancellationToken ct)
    {
        var scope = await Scope(user,ct);
        var model = new WorkBoardModel { Query=q, Statuses=await Statuses(ct), CanUpdate=await Allowed(user,"TienDoCongViec_Update",ct), CanManage=await Allowed(user,"TrangThaiCongViec_View",ct) };
        q.Page=Math.Max(1,q.Page);
        await using var db=Connection(); await db.OpenAsync(ct);
        await using var cmd=db.CreateCommand();
        // One materialized filtered set powers counts, per-column paging and batched staff lookup.
        cmd.CommandText = $$"""
            SELECT w.ID,w.IDYeuCau,w.TrangThaiCongViec,cv.TenCongViec,r.MaYeuCau,kh.TenKhachHang,
                   w.StartDate,w.ExpectedCompletionDate,w.ActualCompletionDate,w.ProgressVersion,s.ID AS StatusId,s.Kind,
                   ROW_NUMBER() OVER(PARTITION BY s.ID ORDER BY CASE WHEN w.ExpectedCompletionDate IS NULL THEN 1 ELSE 0 END,w.ExpectedCompletionDate,w.ID) AS rn
            INTO #filtered
            FROM dbo.TblYeuCauCongViec w JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau
            JOIN dbo.TblTrangThaiCongViec s ON s.StorageValue=w.TrangThaiCongViec
            LEFT JOIN dbo.TblCongViec cv ON cv.ID=w.IDCongViec
            LEFT JOIN dbo.TblKhachHang kh ON kh.ID=r.IDKhachHang
            WHERE {{ScopeSql}}
              AND (@Keyword IS NULL OR cv.TenCongViec LIKE @Keyword OR r.MaYeuCau LIKE @Keyword)
              AND (@Customer IS NULL OR r.IDKhachHang=@Customer)
              AND (@Employee IS NULL OR EXISTS(SELECT 1 FROM dbo.TblYeuCauCongViecNhanVien a WHERE a.IDYeuCauCongViec=w.ID AND a.IDNhanVien=@Employee))
              AND (@From IS NULL OR w.StartDate>=@From) AND (@To IS NULL OR w.StartDate<DATEADD(day,1,@To))
              AND (@Overdue=0 OR (s.Kind='Normal' AND w.ExpectedCompletionDate<CONVERT(date,SYSDATETIME())));
            SELECT StatusId,COUNT(*) FROM #filtered GROUP BY StatusId;
            SELECT ID,IDYeuCau,StatusId,TenCongViec,MaYeuCau,TenKhachHang,StartDate,ExpectedCompletionDate,ActualCompletionDate,ProgressVersion,Kind
            FROM #filtered WHERE rn BETWEEN @First AND @Last ORDER BY StatusId,rn;
            SELECT DISTINCT a.IDYeuCauCongViec,LTRIM(RTRIM(CONCAT(n.Ho,N' ',n.Ten)))
            FROM dbo.TblYeuCauCongViecNhanVien a JOIN dbo.TblNhanVien n ON n.ID=a.IDNhanVien
            JOIN #filtered f ON f.ID=a.IDYeuCauCongViec WHERE f.rn BETWEEN @First AND @Last;
            SELECT DISTINCT kh.ID,kh.TenKhachHang FROM dbo.TblYeuCau r JOIN dbo.TblKhachHang kh ON kh.ID=r.IDKhachHang WHERE {{ScopeSql}} ORDER BY kh.TenKhachHang;
            SELECT DISTINCT n.ID,LTRIM(RTRIM(CONCAT(n.Ho,N' ',n.Ten))) FROM dbo.TblYeuCau r
            JOIN dbo.TblYeuCauCongViec w ON w.IDYeuCau=r.ID JOIN dbo.TblYeuCauCongViecNhanVien a ON a.IDYeuCauCongViec=w.ID
            JOIN dbo.TblNhanVien n ON n.ID=a.IDNhanVien WHERE {{ScopeSql}} ORDER BY 2;
            """;
        Add(cmd,"@Admin",SqlDbType.Bit,scope.Admin); Add(cmd,"@ScopeEmployee",SqlDbType.Int,scope.Employee);
        Add(cmd,"@Keyword",SqlDbType.NVarChar,string.IsNullOrWhiteSpace(q.Keyword)?null:"%"+q.Keyword.Trim()+"%",250);
        Add(cmd,"@Customer",SqlDbType.Int,q.CustomerId); Add(cmd,"@Employee",SqlDbType.Int,q.EmployeeId);
        Add(cmd,"@From",SqlDbType.Date,q.From?.Date); Add(cmd,"@To",SqlDbType.Date,q.To?.Date); Add(cmd,"@Overdue",SqlDbType.Bit,q.Overdue);
        Add(cmd,"@First",SqlDbType.Int,(q.Page-1)*PageSize+1); Add(cmd,"@Last",SqlDbType.Int,q.Page*PageSize);
        var cards=new List<WorkCard>();
        await using var rd=await cmd.ExecuteReaderAsync(ct);
        while(await rd.ReadAsync(ct)) { model.Counts[rd.GetInt32(0)]=rd.GetInt32(1); model.Total+=rd.GetInt32(1); }
        await rd.NextResultAsync(ct);
        while(await rd.ReadAsync(ct))
        {
            var c=new WorkCard { Id=rd.GetInt32(0),RequestId=rd.GetInt32(1),StatusId=rd.GetInt32(2),Name=rd.IsDBNull(3)?"Công việc":rd.GetString(3),RequestCode=rd.IsDBNull(4)?"—":rd.GetString(4),Customer=rd.IsDBNull(5)?"—":rd.GetString(5),Start=rd.IsDBNull(6)?null:rd.GetDateTime(6),Expected=rd.IsDBNull(7)?null:rd.GetDateTime(7),Actual=rd.IsDBNull(8)?null:rd.GetDateTime(8),Version=Convert.ToBase64String((byte[])rd[9]) };
            c.Overdue=rd.GetString(10)=="Normal" && c.Expected?.Date<DateTime.Today;
            c.Late=rd.GetString(10)=="Completed" && c.Actual?.Date>c.Expected?.Date;
            cards.Add(c);
        }
        await rd.NextResultAsync(ct); var byId=cards.ToDictionary(c=>c.Id);
        while(await rd.ReadAsync(ct)) if(byId.TryGetValue(rd.GetInt32(0),out var c)) c.Employees.Add(rd.GetString(1));
        await rd.NextResultAsync(ct); while(await rd.ReadAsync(ct)) model.Customers.Add((rd.GetInt32(0),rd.GetString(1)));
        await rd.NextResultAsync(ct); while(await rd.ReadAsync(ct)) model.Employees.Add((rd.GetInt32(0),rd.GetString(1)));
        model.Cards=cards; return model;
    }
    public async Task Move(WorkMove move, ClaimsPrincipal user, CancellationToken ct)
    {
        var scope=await Scope(user,ct);
        await using var db=Connection(); await db.OpenAsync(ct);
        await using var tx=(SqlTransaction)await db.BeginTransactionAsync(ct);
        await using var cmd=db.CreateCommand(); cmd.Transaction=tx;
        // Compare rowversion atomically. Trigger validates active status and records history/dates.
        cmd.CommandText=$$"""
            UPDATE w SET TrangThaiCongViec=s.StorageValue,Updated_By=@Actor,Updated_Date=SYSDATETIME()
            FROM dbo.TblYeuCauCongViec w JOIN dbo.TblYeuCau r ON r.ID=w.IDYeuCau
            CROSS JOIN dbo.TblTrangThaiCongViec s
            WHERE w.ID=@Id AND w.ProgressVersion=@Version AND s.ID=@Status AND s.Active=1 AND {{ScopeSql}};
            IF @@ROWCOUNT<>1 THROW 51001,N'Công việc đã thay đổi, trạng thái bị tắt hoặc bạn không có quyền truy cập. Hãy làm mới.',1;
            """;
        Add(cmd,"@Id",SqlDbType.Int,move.Id); Add(cmd,"@Status",SqlDbType.Int,move.StatusId); Add(cmd,"@Version",SqlDbType.Binary,Convert.FromBase64String(move.Version),8);
        Add(cmd,"@Admin",SqlDbType.Bit,scope.Admin); Add(cmd,"@ScopeEmployee",SqlDbType.Int,scope.Employee);
        Add(cmd,"@Actor",SqlDbType.NVarChar,user.Identity?.Name??"system",50);
        await cmd.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
    }
}
