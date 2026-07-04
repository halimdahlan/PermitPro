namespace PermitPro.App.ViewModels;

public class AdminLogsViewModel
{
    public List<AdminLogRowViewModel> Logs { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public string? LogTypeFilter { get; set; }
    public string? SearchTerm { get; set; }
}

public class AdminLogRowViewModel
{
    public Guid Id { get; set; }
    public DateTime CreatedWhen { get; set; }
    public string LogType { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? EntityName { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? UserName { get; set; }
}

public class AdminLogDetailModel
{
    public Guid Id { get; set; }
    public DateTime CreatedWhen { get; set; }
    public DateTime? UpdatedWhen { get; set; }
    public string LogType { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? EntityName { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? SerializedData { get; set; }
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }
}
