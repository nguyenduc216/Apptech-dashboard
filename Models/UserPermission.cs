namespace ApptechDashboard.Models;

public sealed class UserPermission
{
    public int FunctionId { get; set; }
    public string FunctionCode { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public string FunctionUrl { get; set; } = string.Empty;
    public int PermissionId { get; set; }
    public string PermissionName { get; set; } = string.Empty;
    public string PermissionCode { get; set; } = string.Empty;
}
