using System.Data.Common;

namespace ApptechDashboard.Configuration;

public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    public string Server { get; set; } = "";
    public int? Port { get; set; }
    public string Database { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = true;
    public bool IntegratedSecurity { get; set; }
    public bool MultipleActiveResultSets { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Server) &&
        !string.IsNullOrWhiteSpace(Database) &&
        (IntegratedSecurity || !string.IsNullOrWhiteSpace(UserId));

    public string BuildConnectionString()
    {
        var builder = new DbConnectionStringBuilder
        {
            ["Server"] = Port is > 0 ? $"{Server},{Port}" : Server,
            ["Database"] = Database,
            ["Encrypt"] = Encrypt,
            ["TrustServerCertificate"] = TrustServerCertificate,
            ["MultipleActiveResultSets"] = MultipleActiveResultSets
        };

        if (IntegratedSecurity)
        {
            builder["Integrated Security"] = true;
        }
        else
        {
            builder["User ID"] = UserId;
            builder["Password"] = Password;
        }

        return builder.ConnectionString;
    }
}
