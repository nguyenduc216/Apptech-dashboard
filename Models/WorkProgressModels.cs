using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ApptechDashboard.Models;

// FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN
// CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-001
public sealed class WorkStatus
{
    public int Id { get; set; }
    [Required, StringLength(50), RegularExpression(@"^[A-Z][A-Z0-9_]*$")]
    public string Code { get; set; } = "";
    [Required, StringLength(100)] public string Name { get; set; } = "";
    // Immutable storage key preserves legacy integrations when display names change.
    [ValidateNever] public string StorageValue { get; set; } = "";
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$")]
    public string Color { get; set; } = "#64748b";
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    [Required, RegularExpression("^(Normal|Completed|Cancelled)$")]
    public string Kind { get; set; } = "Normal";
    [ValidateNever] public string Version { get; set; } = "";
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string TextColor => WorkProgressRules.TextColor(Color);
}

public static class WorkProgressRules
{
    public static bool ValidDates(DateTime? start, DateTime? expected) => !start.HasValue || !expected.HasValue || expected >= start;
    public static string TextColor(string color)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(color ?? "", "^#[0-9a-fA-F]{6}$")) return "#ffffff";
        var rgb = Convert.ToInt32(color![1..], 16);
        return .2126 * Channel(rgb >> 16) + .7152 * Channel((rgb >> 8) & 255) + .0722 * Channel(rgb & 255) > .179 ? "#111827" : "#ffffff";
    }
    private static double Channel(int value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
}

public sealed class WorkBoardQuery
{
    [StringLength(200)] public string? Keyword { get; set; }
    public int? CustomerId { get; set; }
    public int? EmployeeId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public bool Overdue { get; set; }
    [Range(1,1000000)] public int Page { get; set; } = 1;
}
public sealed class WorkCard
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public int StatusId { get; set; }
    public string Name { get; set; } = "";
    public string RequestCode { get; set; } = "";
    public string Customer { get; set; } = "";
    public DateTime? Start { get; set; }
    public DateTime? Expected { get; set; }
    public DateTime? Actual { get; set; }
    public string Version { get; set; } = "";
    public List<string> Employees { get; set; } = [];
    public bool Overdue { get; set; }
    public bool Late { get; set; }
}
public sealed class WorkBoardModel
{
    public WorkBoardQuery Query { get; set; } = new();
    public IReadOnlyList<WorkStatus> Statuses { get; set; } = [];
    public IReadOnlyList<WorkCard> Cards { get; set; } = [];
    public Dictionary<int, int> Counts { get; set; } = [];
    public List<(int Id, string Name)> Customers { get; set; } = [];
    public List<(int Id, string Name)> Employees { get; set; } = [];
    public int Total { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanManage { get; set; }
}
public sealed class WorkMove
{
    [Range(1, int.MaxValue)] public int Id { get; set; }
    [Range(1, int.MaxValue)] public int StatusId { get; set; }
    [Required] public string Version { get; set; } = "";
}
