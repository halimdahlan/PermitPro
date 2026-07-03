using PermitPro.Core.Entities;

namespace PermitPro.Core.Interfaces;

public interface IAppSettingsService
{
    // Categories
    Task<IEnumerable<AppSettingCategory>> GetCategoriesAsync(Guid companyId);
    Task<IEnumerable<AppSettingCategory>> GetGlobalCategoriesAsync();
    Task UpsertCategoryAsync(AppSettingCategory category);
    Task DeleteCategoryAsync(Guid id);

    // Settings
    Task<string?> GetValueAsync(Guid companyId, string categoryName, string key);
    Task<int> GetIntAsync(Guid companyId, string categoryName, string key);
    Task<IEnumerable<AppSetting>> GetSettingsAsync(Guid companyId, Guid categoryId);
    Task<IEnumerable<AppSetting>> GetGlobalSettingsAsync(Guid categoryId);
    Task UpsertSettingAsync(AppSetting setting);
    Task DeleteSettingAsync(Guid id);
}
