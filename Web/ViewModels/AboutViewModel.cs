namespace PermitPro.App.ViewModels;

public class AboutViewModel
{
    public string AppVersion { get; set; } = string.Empty;

    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int InactiveUsers { get; set; }

    public int TotalPermits { get; set; }
    public int OpenPermits { get; set; }
    public int ClosedPermits { get; set; }
    public int RejectedPermits { get; set; }
    public List<AboutPermitStatusCount> PermitStatusBreakdown { get; set; } = [];

    public string DbServer { get; set; } = string.Empty;
    public string DbName { get; set; } = string.Empty;
    public string DbUser { get; set; } = string.Empty;
    public decimal? DbSizeMb { get; set; }
    public string DbState { get; set; } = string.Empty;
}

public class AboutPermitStatusCount
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}
