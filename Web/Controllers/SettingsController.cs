#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using PermitPro.App.Controllers.Base;
using PermitPro.App.Models.Ajax;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Interfaces;

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace PermitPro.App.Controllers;

public class SettingsController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;
	private readonly IAppSettingsService _appSettings;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private readonly IEmailTemplateService _emailTemplates;

	public SettingsController(
		ApplicationDbContext dbContext
		, IHttpContextAccessor httpContextAccessor
		, SignInManager<UserInfo> signInManager
		, ISystemConfigurationService systemConfigurationService
		, IAppSettingsService appSettings
		, IConfiguration configuration
		, IWebHostEnvironment env
		, IEmailTemplateService emailTemplates
	) : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_emailTemplates = emailTemplates;
		_dbContext = dbContext;
		_appSettings = appSettings;
		_configuration = configuration;
		_env = env;
	}


	public async Task<IActionResult> Index()
	{
		var categories = await _appSettings.GetCategoriesAsync(CompanyID);
		ViewData["Title"] = "Settings";
		ViewData["SubTitle"] = "System and company settings by category";
		return View(categories);
	}


	// -------------------------------------------------------------------------
	// About
	// -------------------------------------------------------------------------

	[Authorize(Roles = "Portal Admin,Super User")]
	public async Task<IActionResult> About()
	{
		ViewData["Title"] = "About";
		ViewData["SubTitle"] = "Version, environment and system health";

		var totalUsers = await _dbContext.Users.CountAsync(u => u.UserCompany.Id == CompanyID);
		var activeUsers = await _dbContext.Users.CountAsync(u => u.UserCompany.Id == CompanyID && u.IsActive);

		var permitCounts = await _dbContext.Permits
			.Where(p => p.Company.Id == CompanyID)
			.GroupBy(p => p.PermitStatus)
			.Select(g => new { Status = g.Key, Count = g.Count() })
			.ToListAsync();

		var totalPermits = permitCounts.Sum(p => p.Count);
		var closedPermits = permitCounts
			.Where(p => p.Status == PermitStatusEnum.Closed || p.Status == PermitStatusEnum.ClosedNoAction)
			.Sum(p => p.Count);
		var rejectedPermits = permitCounts.Where(p => p.Status == PermitStatusEnum.Rejected).Sum(p => p.Count);
		var openPermits = totalPermits - closedPermits - rejectedPermits;

		var breakdown = Enum.GetValues<PermitStatusEnum>()
			.Select(s => new AboutPermitStatusCount
			{
				Status = s.ToString(),
				Count = permitCounts.FirstOrDefault(p => p.Status == s)?.Count ?? 0,
			})
			.Where(s => s.Count > 0)
			.OrderByDescending(s => s.Count)
			.ToList();

		var connectionString = _configuration.GetConnectionString("MSSQL") ?? string.Empty;
		var csBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);

		decimal? dbSizeMb = null;
		var dbState = "Unknown";

		try
		{
			dbSizeMb = await _dbContext.Database
				.SqlQueryRaw<decimal>("SELECT CAST(SUM(size) * 8.0 / 1024 AS DECIMAL(18,2)) AS Value FROM sys.database_files")
				.FirstOrDefaultAsync();

			dbState = await _dbContext.Database
				.SqlQueryRaw<string>("SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Status') AS NVARCHAR(60)) AS Value")
				.FirstOrDefaultAsync() ?? "Unknown";
		}
		catch
		{
			// Insufficient permissions or unsupported provider — leave size/state unknown rather than fail the page.
		}

		// Read the version straight off the compiled DLL's file version resource, rather than the
		// in-memory assembly metadata, so this reflects whatever binary is actually running.
		var appVersion = "1.0.0";
		var assemblyLocation = Assembly.GetExecutingAssembly().Location;
		
		if (!string.IsNullOrEmpty(assemblyLocation) && System.IO.File.Exists(assemblyLocation))
		{
			var fileVersionInfo = FileVersionInfo.GetVersionInfo(assemblyLocation);
			appVersion = fileVersionInfo.ProductVersion ?? fileVersionInfo.FileVersion ?? appVersion;
		}

		return View(new AboutViewModel
		{
			AppVersion = appVersion,
			TotalUsers = totalUsers,
			ActiveUsers = activeUsers,
			InactiveUsers = totalUsers - activeUsers,
			TotalPermits = totalPermits,
			OpenPermits = openPermits,
			ClosedPermits = closedPermits,
			RejectedPermits = rejectedPermits,
			PermitStatusBreakdown = breakdown,
			DbServer = csBuilder.DataSource,
			DbName = csBuilder.InitialCatalog,
			DbUser = csBuilder.UserID,
			DbSizeMb = dbSizeMb,
			DbState = dbState,
		});
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

	// Renders an email template from the editor with sample data (nothing is saved or sent). JSON, not HTML,
	// so the template text is never served as a page; the page shows it in a sandboxed iframe.
	[HttpPost]
	public IActionResult PreviewEmail([FromBody] AjaxEmailPreviewModel model)
	{
		try
		{
			var html = _emailTemplates.Preview(model?.Key, model?.Html);
			return html == null ? BadRequest(new { error = "This setting is not an email template." }) : Ok(new { html });
		}
		catch (FormatException ex)
		{
			return BadRequest(new { error = ex.Message });
		}
	}

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
