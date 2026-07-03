namespace PermitPro.App.Models.Ajax;

public class AjaxAppSettingModel
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? CompanyId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string DataType { get; set; } = "text";
    public bool IsEncrypted { get; set; }
    public int SortOrder { get; set; }
    public bool IsUsingGlobal { get; set; }
    public string? GlobalValue { get; set; }
    public bool HasGlobalOverrideAvailable { get; set; }
}
