#nullable disable

using PermitPro.Core.Entities;

namespace PermitPro.App.ViewModels;

public class AdminCompanySettingsViewModel
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public string CompanyLogoFileName { get; set; }
    public IEnumerable<AppSettingCategory> Categories { get; set; } = [];

    /// <summary>All companies, for the Global/Company context-switcher dropdown.</summary>
    public IEnumerable<AdminSettingsCompanyOption> Companies { get; set; } = [];
}

public class AdminGlobalSettingsViewModel
{
    public IEnumerable<AppSettingCategory> Categories { get; set; } = [];

    /// <summary>All companies, for the Global/Company context-switcher dropdown.</summary>
    public IEnumerable<AdminSettingsCompanyOption> Companies { get; set; } = [];
}

public class AdminSettingsCompanyOption
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}
