using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public sealed class SystemAuditQuery
{
    [StringLength(500)] public string? Keyword { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    [StringLength(128)] public string? Field { get; set; }
    [StringLength(500)] public string? Value { get; set; }
    public Dictionary<string, string> Filters { get; set; } = new();
    [Range(1, 1000000)] public int Page { get; set; } = 1;
    [Range(10, 100)] public int PageSize { get; set; } = 30;
}

public sealed class SystemAuditModel
{
    public SystemAuditQuery Query { get; set; } = new();
    public List<string> Columns { get; set; } = [];
    public List<Dictionary<string, string?>> Rows { get; set; } = [];
    public long Total { get; set; }
    public string? Error { get; set; }
    public static readonly Dictionary<string, string> Labels = new()
    {
        ["ID"] = "ID bản ghi", ["FunctionCode"] = "Chức năng", ["ActionCode"] = "Thao tác",
        ["ObjectType"] = "Loại đối tượng", ["ObjectId"] = "ID đối tượng", ["ObjectCode"] = "Mã đối tượng",
        ["UserName"] = "Người thao tác", ["Description"] = "Nội dung", ["SearchKey"] = "Khóa tìm kiếm",
        ["Data"] = "Dữ liệu", ["OldData"] = "Dữ liệu cũ", ["NewData"] = "Dữ liệu mới"
    };
}