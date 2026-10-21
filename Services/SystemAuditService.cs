using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public sealed class SystemAuditService(IConfiguration configuration, IOptions<SqlServerOptions> options)
{
    internal static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["ID"] = ["ID"], ["FunctionCode"] = ["FunctionCode"], ["ActionCode"] = ["ActionCode"],
        ["ObjectType"] = ["ObjectType"], ["ObjectId"] = ["ObjectId"],
        ["ObjectCode"] = ["ObjectCode", "ReferenceCode"],
        ["UserName"] = ["UserName", "Created_By", "CreatedBy", "CreatedUser"],
        ["Description"] = ["Description", "Content", "LogContent"], ["SearchKey"] = ["SearchKey"],
        ["Data"] = ["Data", "DataJson"], ["OldData"] = ["OldData", "OldValue"],
        ["NewData"] = ["NewData", "NewValue"],
        ["CreatedDate"] = ["Created_Date", "CreatedDate"]
    };

    internal static string Quote(string column) => "[" + column.Replace("]", "]]") + "]";
    internal static string Pattern(string value) => "%" + value.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    public async Task<SystemAuditModel> Search(SystemAuditQuery query, CancellationToken ct)
    {
        var model = new SystemAuditModel { Query = query };
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        await using var db = new SqlConnection(string.IsNullOrWhiteSpace(connectionString) ? options.Value.BuildConnectionString() : connectionString);
        await db.OpenAsync(ct);
        await using (var schema = db.CreateCommand())
        {
            schema.CommandText = "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TblCommonLogging') ORDER BY column_id";
            await using var reader = await schema.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) model.Columns.Add(reader.GetString(0));
        }
        if (model.Columns.Count == 0)
        {
            model.Error = "Chưa có bảng dbo.TblCommonLogging. Cần chạy migration nhật ký hệ thống.";
            return model;
        }
        await using var cmd = db.CreateCommand();
        var conditions = new List<string>();
        string Text(string column) => $"CONVERT(nvarchar(max),{Quote(column)})";
        void Contains(IEnumerable<string> columns, string value)
        {
            var name = "@p" + cmd.Parameters.Count;
            var expressions = columns.Select(c => $"{Text(c)} LIKE {name} ESCAPE N'~'").ToList();
            conditions.Add(expressions.Count == 0 ? "1=0" : "(" + string.Join(" OR ", expressions) + ")");
            cmd.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, -1) { Value = Pattern(value) });
        }
        var existing = model.Columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(query.Keyword)) Contains(model.Columns, query.Keyword);
        foreach (var filter in query.Filters.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
        {
            if (!Aliases.TryGetValue(filter.Key, out var aliases)) throw new ArgumentException("Trường lọc không hợp lệ.");
            Contains(aliases.Where(existing.Contains), filter.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Value))
        {
            var field = model.Columns.FirstOrDefault(c => string.Equals(c, query.Field, StringComparison.OrdinalIgnoreCase));
            if (field is null) throw new ArgumentException("Cột tìm kiếm không hợp lệ.");
            Contains([field], query.Value);
        }
        var dateColumn = Aliases["CreatedDate"].FirstOrDefault(existing.Contains);
        if (query.From.HasValue || query.To.HasValue)
        {
            if (dateColumn is null) conditions.Add("1=0");
            else
            {
                if (query.From.HasValue)
                {
                    conditions.Add($"TRY_CONVERT(datetime2,{Quote(dateColumn)})>=@From");
                    cmd.Parameters.Add(new SqlParameter("@From", SqlDbType.DateTime2) { Value = query.From.Value });
                }
                if (query.To.HasValue)
                {
                    conditions.Add($"TRY_CONVERT(datetime2,{Quote(dateColumn)})<=@To");
                    cmd.Parameters.Add(new SqlParameter("@To", SqlDbType.DateTime2) { Value = query.To.Value });
                }
            }
        }
        var where = conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions);
        var idColumn = model.Columns.FirstOrDefault(c => c.Equals("ID", StringComparison.OrdinalIgnoreCase));
        var order = new[] { dateColumn, idColumn }.Where(c => c is not null).Select(c => Quote(c!) + " DESC").ToList();
        cmd.CommandText = $"SELECT COUNT_BIG(*) FROM dbo.TblCommonLogging{where}; SELECT * FROM dbo.TblCommonLogging{where} ORDER BY {(order.Count == 0 ? "(SELECT NULL)" : string.Join(",", order))} OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";
        cmd.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = (query.Page - 1) * query.PageSize });
        cmd.Parameters.Add(new SqlParameter("@Limit", SqlDbType.Int) { Value = query.PageSize });
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (await rd.ReadAsync(ct)) model.Total = rd.GetInt64(0);
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < rd.FieldCount; i++)
                row[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i) switch
                {
                    DateTime date => date.ToString("dd/MM/yyyy HH:mm:ss.fff"),
                    byte[] bytes => Convert.ToBase64String(bytes),
                    var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                };
            model.Rows.Add(row);
        }
        return model;
    }

    public static string Display(Dictionary<string, string?> row, string field) =>
        Aliases.GetValueOrDefault(field, [field]).Select(c => row.GetValueOrDefault(c)).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "—";
}