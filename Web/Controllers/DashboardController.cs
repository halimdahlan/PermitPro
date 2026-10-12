#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using System.Text.Json;

using PermitPro.App.Controllers.Base;
using PermitPro.App.Models.Charts;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

namespace PermitPro.App.Controllers;

[Authorize]
public class DashboardController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;
	private readonly JsonSerializerOptions _jsonOptions;
	private readonly ICurrentUserService _currentUserService;
	private readonly ILogger<DashboardController> _logger;

	public DashboardController(
		ApplicationDbContext dbContext
		, IHttpContextAccessor httpContextAccessor
		, SignInManager<UserInfo> signInManager
		, ICurrentUserService currentUserService
		, ISystemConfigurationService systemConfigurationService
		, ILogger<DashboardController> logger) : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_dbContext = dbContext;
		_currentUserService = currentUserService;
		_logger = logger;

		_jsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		};
	}


	#region "Views"

	public async Task<IActionResult> Index(Guid company, CancellationToken cancellationToken)
	{
		var permits = await _dbContext.Permits
			.AsNoTracking()
			.Include(e => e.Company)
			.Include(e => e.Site)
			.Where(e => e.Company.Id == company && e.PermitWorkflowStep != null && e.Site != null)
			.ToListAsync(cancellationToken);

		var allCount = permits.Count;

		var activePermits = permits.Count(e =>
			e.PermitStatus != PermitStatusEnum.KIV &&
			e.PermitStatus != PermitStatusEnum.Closed &&
			e.PermitStatus != PermitStatusEnum.Suspended &&
			e.PermitStatus != PermitStatusEnum.Draft &&
			e.PermitStatus != PermitStatusEnum.ClosedNoAction);

		var pendingPermits = permits.Count(e => e.PermitStatus == PermitStatusEnum.Pending);
		var approvedPermits = permits.Count(e => e.PermitStatus == PermitStatusEnum.Approved);
		var rejectedPermits = permits.Count(e => e.PermitStatus == PermitStatusEnum.Rejected);
		var closedPermits = permits.Count(e => e.PermitStatus == PermitStatusEnum.Closed);

		// Status breakdown for chart
		var statusBreakdown = permits
			.GroupBy(e => e.PermitStatus)
			.Select(g => new DashboardChartData
			{
				Status = g.Key.ToString().ToUpper(),
				Count = g.Count(),
				Percentage = (decimal)(allCount > 0 ? Math.Round((decimal)g.Count() / allCount * 100, 1) : 0),
				Color = GeneralHelper.GetCategoryColor(g.Key)
			})
			.ToList();

		// Location breakdown for chart
		var locationBreakdown = permits
			.GroupBy(e => e.Site.Name)
			.OrderByDescending(g => g.Count())
			.Select(g => new DashboardLocationData
			{
				SiteName = g.Key,
				Count = g.Count(),
				Percentage = (decimal)(allCount > 0 ? Math.Round((decimal)g.Count() / allCount * 100, 1) : 0)
			})
			.Take(7)
			.ToList();

		// Recent permits (last 5) — pre-fetch creator names to avoid N+1 queries
		var recentPermitsRaw = permits
			.OrderByDescending(e => e.CreatedWhen)
			.Take(5)
			.ToList();

		var creatorIds = recentPermitsRaw.Select(e => e.CreatedBy.ToString()).ToHashSet();
		var userNames = await _dbContext.Users
			.AsNoTracking()
			.IgnoreQueryFilters()
			.Where(u => creatorIds.Contains(u.Id))
			.ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}", cancellationToken);

		var recentPermits = recentPermitsRaw
			.Select(e => new DashboardRecentPermit
			{
				Id = e.Id,
				PermitNumber = $"PTW{e.PermitNo}",
				PermitDescription = GetDescriptionFromJson(e.PermitForm),
				SiteName = e.Site?.Name ?? "—",
				RequestedByName = userNames.TryGetValue(e.CreatedBy.ToString(), out var name) ? name : "—",
				Status = e.PermitStatus,
				CreatedWhen = GeneralHelper.GetDateInTimeZone(e.CreatedWhen),
				CompanyId = company
			})
			.ToList();

		// Recent activity from AuditLog (last 3 for this company)
		var recentActivity = (await _dbContext.AuditLogs
			.AsNoTracking()
			.Include(e => e.AuditLogUser)
			.Where(e => e.CompanyId == company && e.LogType == LogTypeEnum.Information)
			.OrderByDescending(e => e.CreatedWhen)
			.Take(3)
			.ToListAsync(cancellationToken))
			.Select(e => new DashboardActivityItem
			{
				IconClass = GetActivityIcon(e.Category, e.LogType),
				IconBackground = GetActivityBackground(e.LogType),
				IconColor = GetActivityIconColor(e.LogType),
				Title = GetActivityTitle(e.Category, e.LogType),
				Description = e.Description ?? string.Empty,
				TimeAgo = GetTimeAgo(e.CreatedWhen),
			})
			.ToList();

		return View(new DashboardViewModel
		{
			TotalActive = activePermits,
			TotalApproved = approvedPermits,
			TotalClosed = closedPermits,
			TotalPending = pendingPermits,
			TotalRejected = rejectedPermits,
			CompanyId = company,
			StatusBreakdown = statusBreakdown,
			LocationBreakdown = locationBreakdown,
			RecentPermits = recentPermits,
			RecentActivity = recentActivity
		});
	}


	public IActionResult About()
	{
		return View();
	}


	public IActionResult GetDonutChartModels(Guid company)
	{
		// Single query: group by status and count in one round-trip
		var statusCounts = _dbContext.Permits
			.AsNoTracking()
			.Where(e => e.Company.Id == company)
			.GroupBy(e => e.PermitStatus)
			.Select(g => new { Status = g.Key, Count = g.Count() })
			.ToList();

		var allPermits = statusCounts.Sum(x => x.Count);
		var pendingPermits = statusCounts.FirstOrDefault(x => x.Status == PermitStatusEnum.Pending)?.Count ?? 0;
		var approvedPermits = statusCounts.FirstOrDefault(x => x.Status == PermitStatusEnum.Approved)?.Count ?? 0;
		var closedPermits = statusCounts.FirstOrDefault(x => x.Status == PermitStatusEnum.Closed)?.Count ?? 0;

		float pendingPercentage = allPermits > 0 ? (float)pendingPermits / allPermits * 100f : 0f;
		float approvedPercentage = allPermits > 0 ? (float)approvedPermits / allPermits * 100f : 0f;
		float closedPercentage = allPermits > 0 ? (float)closedPermits / allPermits * 100f : 0f;

		var data = new DonutChartModel[]
		{
			new DonutChartModel(null, null, null) { Category = "Pending",  Value = pendingPercentage,  Color = "#f6c343" },
			new DonutChartModel(null, null, null) { Category = "Approved", Value = approvedPercentage, Color = "#00d97e" },
			new DonutChartModel(null, null, null) { Category = "Closed",   Value = closedPercentage,   Color = "#39afd1" },
		};

		return new JsonResult(data);
	}


	#endregion


	#region "API"

	[HttpGet("{company}/dashboard/charts/donut/permit/status")]
	[ResponseCache(Duration = 30, VaryByQueryKeys = ["startDate", "endDate"])]
	public async Task<IActionResult> GetDashboardDonutChartData(Guid company, string startDate, string endDate, CancellationToken cancellationToken)
	{
		try
		{
			var query = _dbContext.Permits
				.AsNoTracking()
				.Include(e => e.Site)
				.Where(e => e.Company.Id == company && e.Site != null);

			if (startDate != null && endDate != null)
			{
				var rangeStart = DateTime.Parse(startDate);
				var rangeEnd = DateTime.Parse(endDate);
				query = query.Where(e => e.CreatedWhen >= rangeStart && e.CreatedWhen <= rangeEnd);
			}

			var allPermits = await query.CountAsync(cancellationToken);

			var groups = await query
				.GroupBy(e => e.PermitStatus)
				.Select(e => new
				{
					Category = e.Key.ToString().ToUpper(),
					Value = string.Format("{0:N1}", ((float)e.Count() / (float)allPermits) * 100),
					Count = e.Count(),
					Color = GeneralHelper.GetCategoryColor(e.Key),
				})
				.ToListAsync(cancellationToken);

			return new JsonResult(groups, _jsonOptions);
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}


	[HttpGet("{company}/dashboard/charts/bar/permit/location")]
	[ResponseCache(Duration = 30, VaryByQueryKeys = ["startDate", "endDate"])]
	public async Task<IActionResult> GetDashboardBarChartDataByLocation(Guid company, string startDate, string endDate, CancellationToken cancellationToken)
	{
		try
		{
			var query = _dbContext.Permits
				.AsNoTracking()
				.Include(e => e.Site)
				.Where(e => e.Company.Id == company && e.Site != null);

			if (startDate != null && endDate != null)
			{
				var rangeStart = DateTime.Parse(startDate);
				var rangeEnd = DateTime.Parse(endDate);
				query = query.Where(e => e.CreatedWhen >= rangeStart && e.CreatedWhen <= rangeEnd);
			}

			var groups = await query
				.GroupBy(e => e.Site.Name)
				.Select(e => new BarChartModel
				{
					Category = e.Key.ToString(),
					TotalClosed = e.Count(f => f.PermitStatus == PermitStatusEnum.Closed),
					TotalPending = e.Count(f => f.PermitStatus == PermitStatusEnum.Pending),
					TotalActive = e.Count(f => f.PermitStatus != PermitStatusEnum.Draft && f.PermitStatus != PermitStatusEnum.KIV && f.PermitStatus != PermitStatusEnum.Closed && f.PermitStatus != PermitStatusEnum.Suspended),
				})
				.ToListAsync(cancellationToken);

			return new JsonResult(groups, _jsonOptions);
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}

	#endregion


	#region "Private static functions/methods"

	private static string GetDescriptionFromJson(string json)
	{
		if (string.IsNullOrEmpty(json))
			return string.Empty;

		using var doc = JsonDocument.Parse(json);
		string value = doc.RootElement
			.GetProperty("general")
			.GetProperty("description")
			.GetString() ?? string.Empty;

		return value;
	}

	private static string GetActivityIcon(string category, LogTypeEnum logType)
	{
		if (logType == LogTypeEnum.Error)
			return "fa-solid fa-triangle-exclamation";

		return category?.ToUpperInvariant() switch
		{
			"SIGN_IN" => "fa-solid fa-arrow-right-to-bracket",
			"CREATE_PERMIT" => "fa-solid fa-circle-plus",
			"UPDATE_PERMIT" => "fa-solid fa-pen",
			"DELETE_PERMIT" => "fa-solid fa-trash",
			"PERMIT_APPROVAL" => "fa-solid fa-check",
			"PERMIT_CLOSED" => "fa-solid fa-lock",
			"PERMIT_SUSPEND" => "fa-solid fa-pause",
			"FORGOT_PASSWORD" => "fa-solid fa-key",
			"RESET_PASSWORD" => "fa-solid fa-key",
			"SENDEMAIL" => "fa-solid fa-envelope",
			"TOKEN" => "fa-solid fa-shield",
			_ => "fa-solid fa-circle-info",
		};
	}

	private static string GetActivityBackground(LogTypeEnum logType) => logType switch
	{
		LogTypeEnum.Error => "var(--tint-red)",
		LogTypeEnum.Warning => "var(--tint-yellow)",
		_ => "var(--tint-blue)",
	};

	private static string GetActivityIconColor(LogTypeEnum logType) => logType switch
	{
		LogTypeEnum.Error => "#e74c3c",
		LogTypeEnum.Warning => "#e67e22",
		_ => "#2c7be5",
	};

	private static string GetActivityTitle(string category, LogTypeEnum logType)
	{
		if (logType == LogTypeEnum.Error)
		{
			return category?.ToUpperInvariant() switch
			{
				"SIGN_IN" => "Sign In Failed",
				"FORGOT_PASSWORD" => "Password Reset Error",
				"RESET_PASSWORD" => "Password Reset Error",
				"PERMIT_SUSPEND" => "Permit Suspend Failed",
				_ => "Error",
			};
		}

		return category?.ToUpperInvariant() switch
		{
			"SIGN_IN" => "User Signed In",
			"CREATE_PERMIT" => "Permit Created",
			"UPDATE_PERMIT" => "Permit Updated",
			"DELETE_PERMIT" => "Permit Deleted",
			"PERMIT_APPROVAL" => "Permit Approved",
			"PERMIT_CLOSED" => "Permit Closed",
			"PERMIT_SUSPEND" => "Permit Suspended",
			"FORGOT_PASSWORD" => "Password Reset Requested",
			"RESET_PASSWORD" => "Password Reset",
			"SENDEMAIL" => "Email Sent",
			"TOKEN" => "API Token Issued",
			_ => category ?? "Activity",
		};
	}

	private static string GetTimeAgo(DateTime createdWhen)
	{
		var diff = GeneralHelper.GetDateInTimeZone(DateTime.UtcNow) - GeneralHelper.GetDateInTimeZone(createdWhen);

		if (diff.TotalMinutes < 1) return "Just now";
		if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min{((int)diff.TotalMinutes > 1 ? "s" : "")} ago";
		if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} hour{((int)diff.TotalHours > 1 ? "s" : "")} ago";
		if (diff.TotalDays < 2) return "Yesterday";
		if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} days ago";
		return GeneralHelper.GetDateInTimeZone(createdWhen).ToLocalTime().ToString("dd MMM yyyy");
	}

	#endregion

}
