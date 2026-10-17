#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using PermitPro.App.Controllers.Base;
using PermitPro.App.Models.Ajax;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

using System.Reflection;
using System.Security.Claims;

namespace PermitPro.App.Controllers;

public class AdminController : Controller
{
    private readonly SignInManager<UserInfo> _signInManager;
    private readonly UserManager<UserInfo> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ILogService _logService;
    private readonly IWebHostEnvironment _env;
    private readonly IAppSettingsService _appSettings;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly HealthCheckService _healthCheckService;

    private static readonly HashSet<string> _allowedLogoExts =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".svg" };

    public AdminController(
        SignInManager<UserInfo> signInManager,
        UserManager<UserInfo> userManager,
        ApplicationDbContext db,
        ILogService logService,
        IWebHostEnvironment env,
        IAppSettingsService appSettings,
        IMemoryCache cache,
        IConfiguration configuration,
        HealthCheckService healthCheckService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
        _logService = logService;
        _env = env;
        _appSettings = appSettings;
        _cache = cache;
        _configuration = configuration;
        _healthCheckService = healthCheckService;
    }


    // ── Auth ──────────────────────────────────────────────────────────────────

    [HttpGet("/admin/login")]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true && IsSuperUser())
            return Redirect("/admin/companies");

        return View(new AdminLoginViewModel { Email = string.Empty, Password = string.Empty });
    }

    [HttpPost("/admin/login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(AdminLoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == model.Email);

        if (user == null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Invalid credentials.");
            return View(model);
        }

        var isSuperUser = user.UserRoles.Any(ur => ur.Role.NormalizedName == "SUPERUSER");
        if (!isSuperUser)
        {
            ModelState.AddModelError(string.Empty, "Access denied. Super Admin credentials required.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Information, "ADMIN_SIGN_IN", $"Super Admin ({user.UserName}) logged in.", user);
            return Redirect("/admin/companies");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Account locked. Try again in 10 minutes.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Invalid credentials.");
        return View(model);
    }

    [HttpGet("/admin/logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Redirect("/admin/login");
    }


    // ── Company management ────────────────────────────────────────────────────

    [Authorize]
    [HttpGet("/admin")]
    public IActionResult Index() => Redirect("/admin/companies");

    [Authorize]
    [HttpGet("/admin/companies")]
    public async Task<IActionResult> Companies()
    {
        if (!IsSuperUser()) return Forbid();

        var companies = await _db.Companies
            .IgnoreQueryFilters()
            .Select(c => new AdminCompanyListViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                IsActive = c.IsActive,
                LogoFileName = c.LogoFileName,
                UserCount = _db.Users.Count(u => u.UserCompany.Id == c.Id),
                SiteCount = _db.Sites.IgnoreQueryFilters().Count(s => s.SiteCompany.Id == c.Id),
                CreatedWhen = c.CreatedWhen,
            })
            .OrderBy(c => c.Name)
            .ToListAsync();

        return View(companies);
    }

    [Authorize]
    [HttpGet("/admin/companies/new")]
    public IActionResult CompanyCreate()
    {
        if (!IsSuperUser()) return Forbid();
        return View("CompanyForm", new AdminCompanyFormViewModel { Name = string.Empty });
    }

    [Authorize]
    [HttpPost("/admin/companies/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompanyCreate(AdminCompanyFormViewModel model)
    {
        if (!IsSuperUser()) return Forbid();

        if (!ModelState.IsValid)
            return View("CompanyForm", model);

        var exists = await _db.Companies.IgnoreQueryFilters().AnyAsync(c => c.Name == model.Name);
        if (exists)
        {
            ModelState.AddModelError(nameof(model.Name), "A company with this name already exists.");
            return View("CompanyForm", model);
        }

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = model.Name,
            Description = model.Description,
            IsActive = model.IsActive,
        };

        if (model.LogoFile != null)
        {
            var (ok, fileName, error) = await SaveLogoAsync(model.LogoFile);
            if (!ok)
            {
                ModelState.AddModelError(nameof(model.LogoFile), error);
                return View("CompanyForm", model);
            }
            company.LogoFileName = fileName;
        }

        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Information, "ADMIN_COMPANY_CREATE", $"Company '{company.Name}' created.", CurrentUser());

        TempData["SuccessMessage"] = $"Company \"{company.Name}\" created successfully.";
        return Redirect("/admin/companies");
    }

    [Authorize]
    [HttpGet("/admin/companies/{id:guid}/edit")]
    public async Task<IActionResult> CompanyEdit(Guid id)
    {
        if (!IsSuperUser()) return Forbid();

        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (company == null) return NotFound();

        return View("CompanyForm", new AdminCompanyFormViewModel
        {
            Id = company.Id,
            Name = company.Name,
            Description = company.Description,
            IsActive = company.IsActive,
            ExistingLogoFileName = company.LogoFileName,
        });
    }

    [Authorize]
    [HttpPost("/admin/companies/{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompanyEdit(Guid id, AdminCompanyFormViewModel model)
    {
        if (!IsSuperUser()) return Forbid();

        if (!ModelState.IsValid)
            return View("CompanyForm", model);

        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (company == null) return NotFound();

        var nameTaken = await _db.Companies.IgnoreQueryFilters()
            .AnyAsync(c => c.Name == model.Name && c.Id != id);
        if (nameTaken)
        {
            ModelState.AddModelError(nameof(model.Name), "A company with this name already exists.");
            return View("CompanyForm", model);
        }

        company.Name = model.Name;
        company.Description = model.Description;
        company.IsActive = model.IsActive;

        if (model.RemoveLogo && !string.IsNullOrEmpty(company.LogoFileName))
        {
            DeleteLogoFile(company.LogoFileName);
            company.LogoFileName = null;
        }
        else if (model.LogoFile != null)
        {
            var (ok, fileName, error) = await SaveLogoAsync(model.LogoFile);
            if (!ok)
            {
                ModelState.AddModelError(nameof(model.LogoFile), error);
                model.ExistingLogoFileName = company.LogoFileName;
                return View("CompanyForm", model);
            }

            if (!string.IsNullOrEmpty(company.LogoFileName))
                DeleteLogoFile(company.LogoFileName);

            company.LogoFileName = fileName;
        }

        _db.Companies.Update(company);
        await _db.SaveChangesAsync();

        AppControllerBase.InvalidateCompanyMetaCache(_cache, id);
        await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Information, "ADMIN_COMPANY_EDIT", $"Company '{company.Name}' updated.", CurrentUser());

        TempData["SuccessMessage"] = $"Company \"{company.Name}\" updated successfully.";
        return Redirect("/admin/companies");
    }

    [Authorize]
    [HttpPost("/admin/companies/{id:guid}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompanyToggle(Guid id)
    {
        if (!IsSuperUser()) return Forbid();

        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (company == null) return NotFound();

        company.IsActive = !company.IsActive;
        _db.Companies.Update(company);
        await _db.SaveChangesAsync();

        AppControllerBase.InvalidateCompanyMetaCache(_cache, id);
        var state = company.IsActive ? "activated" : "deactivated";
        await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Information, "ADMIN_COMPANY_TOGGLE", $"Company '{company.Name}' {state}.", CurrentUser());

        TempData["SuccessMessage"] = $"Company \"{company.Name}\" {state}.";
        return Redirect("/admin/companies");
    }

    [Authorize]
    [HttpPost("/admin/companies/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompanyDelete(Guid id)
    {
        if (!IsSuperUser()) return Forbid();

        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (company == null) return NotFound();

        if (!string.IsNullOrEmpty(company.LogoFileName))
            DeleteLogoFile(company.LogoFileName);

        var currentUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : Guid.Empty;
        await _db.SoftDeleteAsync(company, currentUserId);

        await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Information, "ADMIN_COMPANY_DELETE", $"Company '{company.Name}' soft-deleted.", CurrentUser());

        TempData["SuccessMessage"] = $"Company \"{company.Name}\" has been deleted.";
        return Redirect("/admin/companies");
    }


    // ── Company settings ──────────────────────────────────────────────────────

    [Authorize]
    [HttpGet("/admin/companies/{id:guid}/settings")]
    public async Task<IActionResult> CompanySettings(Guid id)
    {
        if (!IsSuperUser()) return Forbid();

        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (company == null) return NotFound();

        var categories = await _appSettings.GetCategoriesAsync(id);
        return View(new AdminCompanySettingsViewModel
        {
            CompanyId = id,
            CompanyName = company.Name,
            CompanyLogoFileName = company.LogoFileName,
            Categories = categories,
            Companies = await GetSettingsCompanyOptionsAsync(),
        });
    }

    [Authorize]
    [HttpPost("/admin/companies/{id:guid}/settings/categories")]
    public async Task<IActionResult> SaveCompanyCategory(Guid id, [FromBody] AjaxAppSettingCategoryModel model)
    {
        if (!IsSuperUser()) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var normalizedName = model.Name.Trim().ToLowerInvariant();
        var duplicate = await _db.AppSettingCategories
            .AnyAsync(c => c.CompanyId == id && c.Name == normalizedName && c.Id != model.Id);
        if (duplicate)
            return Conflict($"A category with slug \"{normalizedName}\" already exists for this company.");

        // The merged list can surface a global category as a stand-in when this company has no
        // override yet. Unlike settings, a category can't be safely "forked" to a new company-
        // scoped Id — its child settings still reference the original CategoryId and would be
        // orphaned from the copy. Global categories can only be edited from /admin/settings.
        if (model.Id != Guid.Empty && await _db.AppSettingCategories.AsNoTracking().AnyAsync(c => c.Id == model.Id && c.CompanyId == null))
            return Conflict("This is a global category and can only be edited from Global Settings.");

        var category = new AppSettingCategory
        {
            Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
            CompanyId = id,
            Name = normalizedName,
            DisplayName = model.DisplayName.Trim(),
            Description = model.Description,
            Icon = model.Icon,
            SortOrder = model.SortOrder
        };

        await _appSettings.UpsertCategoryAsync(category);
        return Ok(new { id = category.Id });
    }

    [Authorize]
    [HttpDelete("/admin/companies/{id:guid}/settings/categories/{catId:guid}")]
    public async Task<IActionResult> DeleteCompanyCategory(Guid id, Guid catId)
    {
        if (!IsSuperUser()) return Forbid();

        var category = await _db.AppSettingCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == catId);
        if (category == null) return NotFound();
        if (category.CompanyId != id)
            return BadRequest("This category does not belong to the selected company — edit it from Global Settings or the owning company instead.");

        await _appSettings.DeleteCategoryAsync(catId);
        return Ok();
    }

    [Authorize]
    [HttpGet("/admin/companies/{id:guid}/settings/items")]
    public async Task<JsonResult> GetCompanySettings(Guid id, Guid categoryId)
    {
        if (!IsSuperUser()) return new JsonResult(Forbid());

        var settings = await _appSettings.GetSettingsAsync(id, categoryId);
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

        return new JsonResult(result, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        });
    }

    [Authorize]
    [HttpPost("/admin/companies/{id:guid}/settings/items")]
    public async Task<IActionResult> SaveCompanySetting(Guid id, [FromBody] AjaxAppSettingModel model)
    {
        if (!IsSuperUser()) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var normalizedKey = model.Key.Trim().ToLowerInvariant();
        var duplicate = await _db.AppSettings
            .AnyAsync(s => s.CompanyId == id && s.CategoryId == model.CategoryId && s.Key == normalizedKey && s.Id != model.Id);
        if (duplicate)
            return Conflict($"A setting with key \"{normalizedKey}\" already exists in this category.");

        // The merged list can surface a global row (CompanyId == null) as a stand-in when this
        // company has no override yet. Saving it must create a new company-scoped row rather
        // than reuse its Id, or it would silently convert the shared global row into this
        // company's private one.
        var targetsGlobalRow = model.Id != Guid.Empty &&
            await _db.AppSettings.AsNoTracking().AnyAsync(s => s.Id == model.Id && s.CompanyId == null);

        var setting = new AppSetting
        {
            Id = (model.Id == Guid.Empty || targetsGlobalRow) ? Guid.NewGuid() : model.Id,
            CategoryId = model.CategoryId,
            CompanyId = id,
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

    [Authorize]
    [HttpDelete("/admin/companies/{id:guid}/settings/items/{settingId:guid}")]
    public async Task<IActionResult> DeleteCompanySetting(Guid id, Guid settingId)
    {
        if (!IsSuperUser()) return Forbid();

        var setting = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == settingId);
        if (setting == null) return NotFound();
        if (setting.CompanyId != id)
            return BadRequest("This setting does not belong to the selected company — edit it from Global Settings or the owning company instead.");

        await _appSettings.DeleteSettingAsync(settingId);
        return Ok();
    }


    // ── Global settings ───────────────────────────────────────────────────────

    [Authorize]
    [HttpGet("/admin/settings")]
    public async Task<IActionResult> GlobalSettings()
    {
        if (!IsSuperUser()) return Forbid();

        var categories = await _appSettings.GetGlobalCategoriesAsync();
        return View(new AdminGlobalSettingsViewModel
        {
            Categories = categories,
            Companies = await GetSettingsCompanyOptionsAsync(),
        });
    }

    [Authorize]
    [HttpPost("/admin/settings/categories")]
    public async Task<IActionResult> SaveGlobalCategory([FromBody] AjaxAppSettingCategoryModel model)
    {
        if (!IsSuperUser()) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var normalizedName = model.Name.Trim().ToLowerInvariant();
        var duplicate = await _db.AppSettingCategories
            .AnyAsync(c => c.CompanyId == null && c.Name == normalizedName && c.Id != model.Id);
        if (duplicate)
            return Conflict($"A global category with slug \"{normalizedName}\" already exists.");

        var category = new AppSettingCategory
        {
            Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
            CompanyId = null,
            Name = normalizedName,
            DisplayName = model.DisplayName.Trim(),
            Description = model.Description,
            Icon = model.Icon,
            SortOrder = model.SortOrder
        };

        await _appSettings.UpsertCategoryAsync(category);
        return Ok(new { id = category.Id });
    }

    [Authorize]
    [HttpDelete("/admin/settings/categories/{catId:guid}")]
    public async Task<IActionResult> DeleteGlobalCategory(Guid catId)
    {
        if (!IsSuperUser()) return Forbid();
        await _appSettings.DeleteCategoryAsync(catId);
        return Ok();
    }

    [Authorize]
    [HttpGet("/admin/settings/items")]
    public async Task<JsonResult> GetGlobalSettings(Guid categoryId)
    {
        if (!IsSuperUser()) return new JsonResult(Forbid());

        var settings = await _appSettings.GetGlobalSettingsAsync(categoryId);
        var result = settings.Select(s => new AjaxAppSettingModel
        {
            Id = s.Id,
            CategoryId = s.CategoryId,
            CompanyId = s.CompanyId,
            Key = s.Key,
            DisplayName = s.DisplayName,
            Value = s.IsEncrypted ? null : s.Value,
            DataType = s.DataType,
            IsEncrypted = s.IsEncrypted,
            SortOrder = s.SortOrder
        });

        return new JsonResult(result, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        });
    }

    [Authorize]
    [HttpPost("/admin/settings/items")]
    public async Task<IActionResult> SaveGlobalSetting([FromBody] AjaxAppSettingModel model)
    {
        if (!IsSuperUser()) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var normalizedKey = model.Key.Trim().ToLowerInvariant();
        var duplicate = await _db.AppSettings
            .AnyAsync(s => s.CompanyId == null && s.CategoryId == model.CategoryId && s.Key == normalizedKey && s.Id != model.Id);
        if (duplicate)
            return Conflict($"A global setting with key \"{normalizedKey}\" already exists in this category.");

        var setting = new AppSetting
        {
            Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
            CategoryId = model.CategoryId,
            CompanyId = null,
            Key = normalizedKey,
            DisplayName = model.DisplayName.Trim(),
            Value = model.Value,
            DataType = model.DataType,
            IsEncrypted = model.IsEncrypted,
            SortOrder = model.SortOrder,
        };

        await _appSettings.UpsertSettingAsync(setting);
        return Ok(new { id = setting.Id });
    }

    [Authorize]
    [HttpDelete("/admin/settings/items/{settingId:guid}")]
    public async Task<IActionResult> DeleteGlobalSetting(Guid settingId)
    {
        if (!IsSuperUser()) return Forbid();
        await _appSettings.DeleteSettingAsync(settingId);
        return Ok();
    }

    private async Task<List<AdminSettingsCompanyOption>> GetSettingsCompanyOptionsAsync()
    {
        return await _db.Companies
            .IgnoreQueryFilters()
            .OrderBy(c => c.Name)
            .Select(c => new AdminSettingsCompanyOption { Id = c.Id, Name = c.Name })
            .ToListAsync();
    }


    // ── About ─────────────────────────────────────────────────────────────────

    [Authorize]
    [HttpGet("/admin/about")]
    public async Task<IActionResult> About()
    {
        if (!IsSuperUser()) return Forbid();

        var totalUsers = await _db.Users.CountAsync();
        var activeUsers = await _db.Users.CountAsync(u => u.IsActive);

        var permitCounts = await _db.Permits
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
            .Select(s => new AdminPermitStatusCount
            {
                Status = s.ToString(),
                Count = permitCounts.FirstOrDefault(p => p.Status == s)?.Count ?? 0,
            })
            .Where(s => s.Count > 0)
            .OrderByDescending(s => s.Count)
            .ToList();

        // Same key Program.cs uses; the builder also catches the "Pwd=" spelling of the password.
        var csBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_configuration.GetConnectionString("MSSQL") ?? string.Empty);
        if (!string.IsNullOrEmpty(csBuilder.Password))
            csBuilder.Password = "****";
        var maskedConnectionString = csBuilder.ConnectionString;

        decimal? dbSizeMb = null;
        try
        {
            dbSizeMb = await _db.Database
                .SqlQueryRaw<decimal>("SELECT CAST(SUM(size) * 8.0 / 1024 AS DECIMAL(18,2)) AS Value FROM sys.database_files")
                .FirstOrDefaultAsync();
        }
        catch
        {
            // Insufficient permissions or unsupported provider — leave size unknown rather than fail the page.
        }

        var dbHealthStatus = "Unknown";
        double? dbHealthResponseMs = null;
        try
        {
            var report = await _healthCheckService.CheckHealthAsync();
            if (report.Entries.TryGetValue("sqlserver", out var entry))
            {
                dbHealthStatus = entry.Status.ToString();
                dbHealthResponseMs = entry.Duration.TotalMilliseconds;
            }
        }
        catch
        {
            dbHealthStatus = "Unhealthy";
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;

        return View(new AdminAboutViewModel
        {
            AppVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0",
            Environment = _env.EnvironmentName,
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            InactiveUsers = totalUsers - activeUsers,
            TotalPermits = totalPermits,
            OpenPermits = openPermits,
            ClosedPermits = closedPermits,
            RejectedPermits = rejectedPermits,
            PermitStatusBreakdown = breakdown,
            MaskedConnectionString = maskedConnectionString,
            DatabaseSizeMb = dbSizeMb,
            DbHealthStatus = dbHealthStatus,
            DbHealthResponseMs = dbHealthResponseMs,
        });
    }


    // ── Logs ─────────────────────────────────────────────────────────────────

    [Authorize]
    [HttpGet("/admin/logs")]
    public async Task<IActionResult> Logs(int page = 1, int pageSize = 50, string logType = null, string search = null)
    {
        if (!IsSuperUser()) return Forbid();

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var query = _db.AuditLogs.Include(l => l.AuditLogUser).AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(logType) && Enum.TryParse<LogTypeEnum>(logType, true, out var parsedType))
            query = query.Where(l => l.LogType == parsedType);

        if (!string.IsNullOrEmpty(search))
        {
            var term = search.Trim();
            query = query.Where(l =>
                (l.Category != null && l.Category.Contains(term)) ||
                (l.Description != null && l.Description.Contains(term)) ||
                (l.EntityName != null && l.EntityName.Contains(term)));
        }

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max((int)Math.Ceiling(totalCount / (double)pageSize), 1);
        page = Math.Min(page, totalPages);

        var logs = await query
            .OrderByDescending(l => l.CreatedWhen)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AdminLogRowViewModel
            {
                Id = l.Id,
                CreatedWhen = l.CreatedWhen,
                LogType = l.LogType.ToString(),
                Category = l.Category,
                EntityName = l.EntityName,
                Description = l.Description,
                Url = l.Url,
                UserName = l.AuditLogUser != null ? (l.AuditLogUser.FirstName + " " + l.AuditLogUser.LastName).Trim() : null,
            })
            .ToListAsync();

        return View(new AdminLogsViewModel
        {
            Logs = logs,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            LogTypeFilter = logType,
            SearchTerm = search,
        });
    }

    [Authorize]
    [HttpGet("/admin/logs/{id:guid}")]
    public async Task<IActionResult> GetLogDetail(Guid id)
    {
        if (!IsSuperUser()) return Forbid();

        var log = await _db.AuditLogs.Include(l => l.AuditLogUser).AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (log == null) return NotFound();

        return new JsonResult(new AdminLogDetailModel
        {
            Id = log.Id,
            CreatedWhen = GeneralHelper.GetDateInTimeZone(log.CreatedWhen),
            UpdatedWhen = log.UpdatedWhen.HasValue ? GeneralHelper.GetDateInTimeZone(log.UpdatedWhen.Value) : null,
            LogType = log.LogType.ToString(),
            Category = log.Category,
            EntityName = log.EntityName,
            Description = log.Description,
            Url = log.Url,
            SerializedData = log.SerializedData,
            UserName = log.AuditLogUser != null ? $"{log.AuditLogUser.FirstName} {log.AuditLogUser.LastName}".Trim() : null,
            UserEmail = log.AuditLogUser?.Email,
        }, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        });
    }


    // ── Logo helpers ──────────────────────────────────────────────────────────

    private async Task<(bool ok, string fileName, string error)> SaveLogoAsync(Microsoft.AspNetCore.Http.IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName);
        if (!_allowedLogoExts.Contains(ext))
            return (false, null, "Only PNG, JPG, JPEG, WebP, and SVG files are allowed.");

        if (file.Length > 2 * 1024 * 1024)
            return (false, null, "Logo must be smaller than 2 MB.");

        var dir = Path.Combine(_env.WebRootPath, "img", "logos");
        Directory.CreateDirectory(dir);

        var storedName = $"{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(dir, storedName);

        await using var fs = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(fs);

        return (true, storedName, null);
    }

    private void DeleteLogoFile(string fileName)
    {
        var path = Path.Combine(_env.WebRootPath, "img", "logos", fileName);
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }

    private bool IsSuperUser() =>
        User.IsInRole("Super User") || User.IsInRole("SUPERUSER");

    private UserInfo CurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return _db.Users.FirstOrDefault(u => u.Id == userId);
    }
}
