using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Interfaces;

namespace PermitPro.Core.Services;

public class AppSettingsService : IAppSettingsService
{
	private readonly ApplicationDbContext _dbContext;
	private readonly IDataProtector _protector;
	private readonly IMemoryCache _cache;

	private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

	// IMemoryCache has no key-enumeration/wildcard removal, so when a global (CompanyId == null)
	// row changes we need to know every company whose per-company cache entry might now be stale
	// (since those entries merge in global rows). Tracks every companyId seen by the cache reads
	// below; this service is Scoped, so the set must be static to survive across requests.
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _knownCompanyIds = new();

	public AppSettingsService(ApplicationDbContext dbContext, IDataProtectionProvider dataProtectionProvider, IMemoryCache cache)
	{
		_dbContext = dbContext;
		_protector = dataProtectionProvider.CreateProtector("AppSettings");
		_cache = cache;
	}


	// -------------------------------------------------------------------------
	// Categories
	// -------------------------------------------------------------------------

	public async Task<IEnumerable<AppSettingCategory>> GetCategoriesAsync(Guid companyId)
	{
		_knownCompanyIds.TryAdd(companyId, 0);

		var cacheKey = CatsCacheKey(companyId);
		if (_cache.TryGetValue(cacheKey, out IEnumerable<AppSettingCategory>? cached))
			return cached!;

		var rows = await _dbContext.AppSettingCategories
			 .Where(c => c.CompanyId == companyId || c.CompanyId == null)
			 .OrderBy(c => c.SortOrder)
			 .ThenBy(c => c.DisplayName)
			 .ToListAsync();

		// Company-specific categories override globals with the same Name
		var result = rows
			 .GroupBy(c => c.Name)
			 .Select(g => g.OrderByDescending(c => c.CompanyId.HasValue).First())
			 .OrderBy(c => c.SortOrder)
			 .ThenBy(c => c.DisplayName)
			 .ToList();

		_cache.Set(cacheKey, (IEnumerable<AppSettingCategory>)result, CacheDuration);
		return result;
	}

	public async Task<IEnumerable<AppSettingCategory>> GetGlobalCategoriesAsync()
	{
		return await _dbContext.AppSettingCategories
			 .Where(c => c.CompanyId == null)
			 .OrderBy(c => c.SortOrder)
			 .ThenBy(c => c.DisplayName)
			 .ToListAsync();
	}

	public async Task UpsertCategoryAsync(AppSettingCategory category)
	{
		var existing = await _dbContext.AppSettingCategories
			 .FirstOrDefaultAsync(c => c.Id == category.Id);

		if (existing is null)
		{
			if (category.Id == Guid.Empty)
				category.Id = Guid.NewGuid();
			_dbContext.AppSettingCategories.Add(category);
		}
		else
		{
			existing.Name = category.Name;
			existing.DisplayName = category.DisplayName;
			existing.Description = category.Description;
			existing.Icon = category.Icon;
			existing.SortOrder = category.SortOrder;
			existing.CompanyId = category.CompanyId;
		}

		await _dbContext.SaveChangesAsync();
		InvalidateCategoryCache(category.CompanyId);
	}

	public async Task DeleteCategoryAsync(Guid id)
	{
		var category = await _dbContext.AppSettingCategories.FindAsync(id);
		if (category is null) return;

		_dbContext.AppSettingCategories.Remove(category);
		await _dbContext.SaveChangesAsync();
		InvalidateCategoryCache(category.CompanyId);
	}


	// -------------------------------------------------------------------------
	// Settings
	// -------------------------------------------------------------------------

	public async Task<string?> GetValueAsync(Guid companyId, string categoryName, string key)
	{
		var settings = await GetAllSettingsFlatAsync(companyId);
		var match = ResolveEffectiveMatch(settings, categoryName, key, companyId);

		if (match is null) return null;
		return Decrypt(match);
	}

	public async Task<int> GetIntAsync(Guid companyId, string categoryName, string key)
	{
		var settings = await GetAllSettingsFlatAsync(companyId);
		var match = ResolveEffectiveMatch(settings, categoryName, key, companyId);

		if (match is null) return 0;
		var decrypt = Decrypt(match);

		return int.TryParse(decrypt, out var result) ? result : 0;
	}

	public async Task<IEnumerable<AppSetting>> GetSettingsAsync(Guid companyId, Guid categoryId)
	{
		var category = await _dbContext.AppSettingCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId);
		if (category is null) return [];

		var globalCategoryId = await ResolveGlobalCategoryIdAsync(category);

		var all = await GetAllSettingsFlatAsync(companyId);

		var companyRows = all.Where(s => s.CategoryId == categoryId && s.CompanyId == companyId).ToList();
		var globalRows = globalCategoryId is null
			 ? []
			 : all.Where(s => s.CategoryId == globalCategoryId && s.CompanyId == null).ToList();

		// Merge: company-specific row wins over global for the same Key
		var merged = globalRows
			 .Where(g => !companyRows.Any(c => string.Equals(c.Key, g.Key, StringComparison.OrdinalIgnoreCase)))
			 .Concat(companyRows)
			 .OrderBy(s => s.SortOrder)
			 .ThenBy(s => s.DisplayName)
			 .Select(s => DecryptForDisplay(s))
			 .ToList();

		return merged;
	}

	public async Task<IEnumerable<AppSetting>> GetGlobalSettingsAsync(Guid categoryId)
	{
		// categoryId may be a company-owned category or the raw global category (when the
		// merge in GetCategoriesAsync has nothing to override it with) — either way, resolve
		// the actual global category by Name, since company and global categories with the
		// same Name are separate rows with different Ids.
		var category = await _dbContext.AppSettingCategories
			 .AsNoTracking()
			 .FirstOrDefaultAsync(c => c.Id == categoryId);
		if (category is null) return [];

		var globalCategoryId = await ResolveGlobalCategoryIdAsync(category);
		if (globalCategoryId is null) return [];

		var rows = await _dbContext.AppSettings
			 .Where(s => s.CompanyId == null && s.CategoryId == globalCategoryId)
			 .OrderBy(s => s.SortOrder)
			 .ThenBy(s => s.DisplayName)
			 .ToListAsync();

		return rows.Select(DecryptForDisplay).ToList();
	}

	/// <summary>
	/// Company and global categories that represent "the same" category share a Name but are
	/// separate rows with different Ids. Resolves the global sibling's Id for a given category.
	/// </summary>
	private async Task<Guid?> ResolveGlobalCategoryIdAsync(AppSettingCategory category)
	{
		if (category.CompanyId is null) return category.Id;

		var globalCategory = await _dbContext.AppSettingCategories
			 .AsNoTracking()
			 .FirstOrDefaultAsync(c => c.CompanyId == null && c.Name == category.Name);

		return globalCategory?.Id;
	}

	public async Task UpsertSettingAsync(AppSetting setting)
	{
		var existing = await _dbContext.AppSettings
			 .FirstOrDefaultAsync(s => s.Id == setting.Id);

		if (existing is null)
		{
			if (setting.Id == Guid.Empty)
				setting.Id = Guid.NewGuid();
			if (setting.IsEncrypted && !string.IsNullOrEmpty(setting.Value))
				setting.Value = _protector.Protect(setting.Value);
			_dbContext.AppSettings.Add(setting);
		}
		else
		{
			existing.Key = setting.Key;
			existing.DisplayName = setting.DisplayName;
			existing.DataType = setting.DataType;
			existing.IsEncrypted = setting.IsEncrypted;
			existing.SortOrder = setting.SortOrder;
			existing.CompanyId = setting.CompanyId;
			existing.CategoryId = setting.CategoryId;
			existing.IsUsingGlobal = setting.IsUsingGlobal;

			// While deferring to global, the (disabled) Value field submits blank/stale data —
			// leave the row's own last-known value untouched so unchecking restores it.
			if (!setting.IsUsingGlobal)
			{
				if (setting.IsEncrypted)
				{
					// Blank means "keep the existing encrypted value unchanged" (the UI hint).
					if (!string.IsNullOrEmpty(setting.Value))
						existing.Value = _protector.Protect(setting.Value);
				}
				else
				{
					existing.Value = setting.Value;
				}
			}
		}

		await _dbContext.SaveChangesAsync();
		InvalidateSettingsCache(setting.CompanyId, setting.CategoryId);
	}

	public async Task DeleteSettingAsync(Guid id)
	{
		var setting = await _dbContext.AppSettings.FindAsync(id);
		if (setting is null) return;

		_dbContext.AppSettings.Remove(setting);
		await _dbContext.SaveChangesAsync();
		InvalidateSettingsCache(setting.CompanyId, setting.CategoryId);
	}


	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	private async Task<List<AppSetting>> GetAllSettingsFlatAsync(Guid companyId)
	{
		_knownCompanyIds.TryAdd(companyId, 0);

		var cacheKey = ValsCacheKey(companyId);
		if (_cache.TryGetValue(cacheKey, out List<AppSetting>? cached))
			return cached!;

		var rows = await _dbContext.AppSettings
			 .Include(s => s.Category)
			 .Where(s => s.CompanyId == companyId || s.CompanyId == null)
			 .ToListAsync();

		_cache.Set(cacheKey, rows, CacheDuration);
		return rows;
	}

	/// <summary>
	/// Resolves the effective row for a (category, key) pair: the company row unless it's
	/// explicitly deferring to global (<see cref="AppSetting.IsUsingGlobal"/>), in which case
	/// the global row is used (falling back to the company row if no global row exists).
	/// </summary>
	private static AppSetting? ResolveEffectiveMatch(List<AppSetting> settings, string categoryName, string key, Guid companyId)
	{
		AppSetting? companyMatch = null;
		AppSetting? globalMatch = null;

		foreach (var s in settings)
		{
			if (!string.Equals(s.Category?.Name, categoryName, StringComparison.OrdinalIgnoreCase) ||
				 !string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))
				continue;

			if (s.CompanyId == companyId) companyMatch = s;
			else if (s.CompanyId == null) globalMatch = s;
		}

		if (companyMatch != null && !companyMatch.IsUsingGlobal)
			return companyMatch;

		return globalMatch ?? companyMatch;
	}

	private string? Decrypt(AppSetting setting)
	{
		if (!setting.IsEncrypted || string.IsNullOrEmpty(setting.Value))
			return setting.Value;
		try { return _protector.Unprotect(setting.Value); }
		catch { return null; }
	}

	private AppSetting DecryptForDisplay(AppSetting setting)
	{
		if (!setting.IsEncrypted || string.IsNullOrEmpty(setting.Value))
			return setting;

		// Return a shallow copy with the decrypted value so the cached entity is not mutated
		return new AppSetting
		{
			Id = setting.Id,
			CategoryId = setting.CategoryId,
			Category = setting.Category,
			CompanyId = setting.CompanyId,
			Key = setting.Key,
			DisplayName = setting.DisplayName,
			DataType = setting.DataType,
			IsEncrypted = setting.IsEncrypted,
			SortOrder = setting.SortOrder,
			IsUsingGlobal = setting.IsUsingGlobal,
			Value = Decrypt(setting)
		};
	}

	private void InvalidateCategoryCache(Guid? companyId)
	{
		_cache.Remove(CatsCacheKey(companyId ?? Guid.Empty));
		_cache.Remove(CatsCacheKey(Guid.Empty));

		// A global category change can affect every company's merged view.
		if (companyId is null)
		{
			foreach (var knownCompanyId in _knownCompanyIds.Keys)
				_cache.Remove(CatsCacheKey(knownCompanyId));
		}
	}

	private void InvalidateSettingsCache(Guid? companyId, Guid categoryId)
	{
		_cache.Remove(ValsCacheKey(companyId ?? Guid.Empty));
		_cache.Remove(ValsCacheKey(Guid.Empty));

		// A global setting change can affect every company's merged/effective value.
		if (companyId is null)
		{
			foreach (var knownCompanyId in _knownCompanyIds.Keys)
				_cache.Remove(ValsCacheKey(knownCompanyId));
		}
	}

	private static string CatsCacheKey(Guid companyId) => $"appsettings:cats:{companyId}";
	private static string ValsCacheKey(Guid companyId) => $"appsettings:vals:{companyId}";

}
