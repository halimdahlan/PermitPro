namespace PermitPro.App.ViewModels;

public class AdminAboutViewModel
{
    public string AppVersion { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;

    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int InactiveUsers { get; set; }

    public int TotalPermits { get; set; }
    public int OpenPermits { get; set; }
    public int ClosedPermits { get; set; }
    public int RejectedPermits { get; set; }
    public List<AdminPermitStatusCount> PermitStatusBreakdown { get; set; } = [];

    public string MaskedConnectionString { get; set; } = string.Empty;
    public decimal? DatabaseSizeMb { get; set; }

    public string DbHealthStatus { get; set; } = string.Empty;
    public double? DbHealthResponseMs { get; set; }
}

public class AdminPermitStatusCount
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}
