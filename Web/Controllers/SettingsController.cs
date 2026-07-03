#nullable disable

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PermitPro.App.Controllers.Base;
using PermitPro.App.Models.Ajax;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Interfaces;

using System.Text.Json;

namespace PermitPro.App.Controllers;

public class SettingsController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;
	private readonly IAppSettingsService _appSettings;

	public SettingsController(
		ApplicationDbContext dbContext
		, IHttpContextAccessor httpContextAccessor
		, SignInManager<UserInfo> signInManager
		, ISystemConfigurationService systemConfigurationService
		, IAppSettingsService appSettings
	) : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_dbContext = dbContext;
		_appSettings = appSettings;
	}


	public async Task<IActionResult> Index()
	{
		var categories = await _appSettings.GetCategoriesAsync(CompanyID);
		ViewData["Title"] = "Settings";
		ViewData["SubTitle"] = "Summary";
		return View(categories);
	}


	// -------------------------------------------------------------------------
	// Categories
	// -------------------------------------------------------------------------

	[HttpGet]
	public async Task<JsonResult> GetCategories()
	{
		var categories = await _appSettings.GetCategoriesAsync(CompanyID);
		var result = categories.Select(c => new AjaxAppSettingCategoryModel
		{
			Id = c.Id,
			CompanyId = c.CompanyId,
			Name = c.Name,
			DisplayName = c.DisplayName,
			Description = c.Description,
			Icon = c.Icon,
			SortOrder = c.SortOrder
		});

		return new JsonResult(result, new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		});
	}

	[HttpPost]
	public async Task<IActionResult> SaveCategory([FromBody] AjaxAppSettingCategoryModel model)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		var normalizedName = model.Name.Trim().ToLowerInvariant();
		var duplicate = await _dbContext.AppSettingCategories
			.AnyAsync(c => c.CompanyId == CompanyID && c.Name == normalizedName && c.Id != model.Id);
		if (duplicate)
			return Conflict($"A category with slug \"{normalizedName}\" already exists for this company.");

		// The merged list can surface a global category as a stand-in when this company has no
		// override yet. Unlike settings, a category can't be safely "forked" to a new company-
		// scoped Id — its child settings still reference the original CategoryId and would be
		// orphaned from the copy. Global categories can only be edited from /admin/settings.
		if (model.Id != Guid.Empty && await _dbContext.AppSettingCategories.AsNoTracking().AnyAsync(c => c.Id == model.Id && c.CompanyId == null))
			return Conflict("This is a global category and can only be edited from Global Settings.");

		var category = new AppSettingCategory
		{
			Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
			CompanyId = CompanyID,
			Name = normalizedName,
			DisplayName = model.DisplayName.Trim(),
			Description = model.Description,
			Icon = model.Icon,
			SortOrder = model.SortOrder
		};

		await _appSettings.UpsertCategoryAsync(category);
		return Ok(new { id = category.Id });
	}

	[HttpDelete]
	public async Task<IActionResult> DeleteCategory(Guid id)
	{
		var category = await _dbContext.AppSettingCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
		if (category == null) return NotFound();
		if (category.CompanyId != CompanyID)
			return BadRequest("This is a global (or another company's) category and cannot be deleted from here.");

		await _appSettings.DeleteCategoryAsync(id);
		return Ok();
	}


	// -------------------------------------------------------------------------
	// Settings
	// -------------------------------------------------------------------------

	[HttpGet]
	public async Task<JsonResult> GetSettings(Guid categoryId)
	{
		var settings = await _appSettings.GetSettingsAsync(CompanyID, categoryId);
		var globalSettings = await _appSettings.GetGlobalSettingsAsync(categoryId);
		var globalByKey = globalSettings.ToDictionary(g => g.Key, g => g, StringComparer.OrdinalIgnoreCase);

		var result = settings.Select(s =>
		{
			globalByKey.TryGetValue(s.Key, out var globalMatch);
			return new AjaxAppSettingModel
			{
				Id = s.Id,
				// Pin to the category being viewed, not the entity's own CategoryId — a row
				// inherited from global carries the global category's Id, but must be saved
				// back under this company's category so it's recognized as an override later.
				CategoryId = categoryId,
				CompanyId = s.CompanyId,
				Key = s.Key,
				DisplayName = s.DisplayName,
				Value = s.IsEncrypted ? null : s.Value,
				DataType = s.DataType,
				IsEncrypted = s.IsEncrypted,
				SortOrder = s.SortOrder,
				IsUsingGlobal = s.CompanyId != null && s.IsUsingGlobal,
				GlobalValue = globalMatch is null ? null : (globalMatch.IsEncrypted ? "(encrypted)" : globalMatch.Value),
				HasGlobalOverrideAvailable = globalMatch != null,
			};
		});

		return new JsonResult(result, new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		});
	}

	[HttpPost]
	public async Task<IActionResult> SaveSetting([FromBody] AjaxAppSettingModel model)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		var normalizedKey = model.Key.Trim().ToLowerInvariant();
		var duplicate = await _dbContext.AppSettings
			.AnyAsync(s => s.CompanyId == CompanyID && s.CategoryId == model.CategoryId && s.Key == normalizedKey && s.Id != model.Id);
		if (duplicate)
			return Conflict($"A setting with key \"{normalizedKey}\" already exists in this category.");

		// The merged list can surface a global row (CompanyId == null) as a stand-in when this
		// company has no override yet. Saving it must create a new company-scoped row rather
		// than reuse its Id, or it would silently convert the shared global row into this
		// company's private one.
		var targetsGlobalRow = model.Id != Guid.Empty &&
			await _dbContext.AppSettings.AsNoTracking().AnyAsync(s => s.Id == model.Id && s.CompanyId == null);

		var setting = new AppSetting
		{
			Id = (model.Id == Guid.Empty || targetsGlobalRow) ? Guid.NewGuid() : model.Id,
			CategoryId = model.CategoryId,
			CompanyId = CompanyID,
			Key = normalizedKey,
			DisplayName = model.DisplayName.Trim(),
			Value = model.Value,
			DataType = model.DataType,
			IsEncrypted = model.IsEncrypted,
			SortOrder = model.SortOrder,
			IsUsingGlobal = model.IsUsingGlobal,
		};

		await _appSettings.UpsertSettingAsync(setting);
		return Ok(new { id = setting.Id });
	}

	[HttpDelete]
	public async Task<IActionResult> DeleteSetting(Guid id)
	{
		var setting = await _dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
		if (setting == null) return NotFound();
		if (setting.CompanyId != CompanyID)
			return BadRequest("This is a global (or another company's) setting and cannot be deleted from here.");

		await _appSettings.DeleteSettingAsync(id);
		return Ok();
	}


	// -------------------------------------------------------------------------
	// Existing
	// -------------------------------------------------------------------------

	public JsonResult GetLocations(Guid? id)
	{
		var locations = _dbContext.Sites
			.Where(e => id.HasValue ? e.ParentId == id : e.ParentId == Guid.Empty)
			.OrderBy(e => e.Name)
			.Select(e => new
			{
				id = e.Id,
				name = e.Name,
				image = $"/img/site-type/16x16/{e.SiteType.ToString().ToLower()}.png",
				siteType = e.SiteType,
				parentId = e.ParentId,
				expanded = e.Name.ToLower() == "iot",
				hasChildren = _dbContext.Sites.Where(f => f.ParentId == e.Id).Count() > 0
			})
			.ToList();

		return Json(locations);
	}
}
