namespace ApptechDashboard.Models;

public sealed class UserAccount
{
    public Guid Id { get; init; }

    public int? EmployeeId { get; init; }

    public string Username { get; init; } = "";

    public string LastName { get; init; } = "";

    public string FirstName { get; init; } = "";

    public string Email { get; init; } = "";

    public DateTime? DateOfBirth { get; init; }

    public string Address { get; init; } = "";

    public string PhoneNumber { get; init; } = "";

    public string Gender { get; init; } = "";

    public string ZaloId { get; init; } = "";

    public string AvatarUrl { get; init; } = "";

    public string PasswordHash { get; set; } = "";

    public bool IsActive { get; init; } = true;

    public bool IsAdministrator { get; init; }

    public string GroupName { get; init; } = "";

    public string FullName
    {
        get
        {
            var parts = new[] { LastName, FirstName }
                .Where(static part => !string.IsNullOrWhiteSpace(part))
                .ToArray();

            return parts.Length == 0 ? Username : string.Join(" ", parts);
        }
    }

    public string RoleDisplay =>
        IsAdministrator
            ? "Quản trị viên"
            : GetRoleLabel(GroupName);

    public string Initials
    {
        get
        {
            var tokens = FullName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Length == 0)
            {
                return "U";
            }

            if (tokens.Length == 1)
            {
                return tokens[0][0].ToString().ToUpperInvariant();
            }

            return string.Concat(tokens[0][0], tokens[^1][0]).ToUpperInvariant();
        }
    }

    private static string GetRoleLabel(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return "Người dùng hệ thống";
        }

        var normalized = groupName.Trim().ToLowerInvariant();

        if (normalized.Contains("quan tri") || normalized.Contains("quản trị") || normalized.Contains("admin"))
        {
            return "Quản trị viên";
        }

        if (normalized.Contains("ky thuat") || normalized.Contains("kỹ thuật"))
        {
            return "Kỹ thuật";
        }

        if (normalized.Contains("ke toan") || normalized.Contains("kế toán"))
        {
            return "Kế toán";
        }

        if (normalized.Contains("nhan vien") || normalized.Contains("nhân viên"))
        {
            return "Nhân viên";
        }

        return groupName;
    }
}
