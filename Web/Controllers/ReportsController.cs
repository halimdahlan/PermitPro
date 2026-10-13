#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

using System.Text.Json;

using PermitPro.App.Controllers.Base;
using PermitPro.App.Models.Reports;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Helpers;
using PermitPro.Core.Enums;
using PermitPro.Core.Interfaces;

namespace PermitPro.App.Controllers;

[Authorize]
public class ReportsController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;
	private readonly ILogger<ReportsController> _logger;


	public ReportsController(
		ApplicationDbContext dbContext
		, IHttpContextAccessor httpContextAccessor
		, SignInManager<UserInfo> signInManager
		, ISystemConfigurationService systemConfigurationService
		, ILogger<ReportsController> logger
	) : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_dbContext = dbContext;
		_logger = logger;
	}


	#region "GET"

	public async Task<IActionResult> Index(Guid company, CancellationToken cancellationToken)
	{
		var model = new ReportsViewModel
		{
			CompanyId = company,
			Months = GetMonthOptions(),
			Years = GetYearOptions(),
			PermitHolders = await GetPermitHolderOptions(company, cancellationToken),
			Certificates = GetCertificateOptions(),
			PermitStatuses = GetPermitStatusOptions(),
		};

		return View(model);
	}


	// Values match ReportGridModel.CreatedMonth (e.g. "January").
	private static List<SelectListItem> GetMonthOptions()
	{
		var months = new List<SelectListItem> { new("(select)", "") };

		for (var n = 1; n <= 12; n++)
		{
			var name = new DateTime(2024, n, 1).ToString("MMMM");
			months.Add(new SelectListItem(name, name));
		}

		return months;
	}


	private static List<SelectListItem> GetYearOptions()
	{
		var years = new List<SelectListItem> { new("(select)", "") };

		for (var year = 2023; year <= DateTime.Now.Year; year++)
		{
			years.Add(new SelectListItem(year.ToString(), year.ToString()));
		}

		return years;
	}


	// Values match the certificate letters in ReportGridModel.Certificates.
	private static List<SelectListItem> GetCertificateOptions()
	{
		return new List<SelectListItem>
		{
			new("(select)", ""),
			new("Hot Work", "A"),
			new("Confined Space", "B"),
			new("Radiation", "C"),
			new("Excavation", "D"),
			new("Isolation", "E"),
			new("Method Statement", "F"),
			new("Lifting & Hoisting", "G"),
			new("Override", "H"),
		};
	}


	// Values match ReportGridModel.PermitStatusEnum.
	private static List<SelectListItem> GetPermitStatusOptions()
	{
		return new List<SelectListItem>
		{
			new("(select)", ""),
			new("Draft", "0"),
			new("Pending", "1"),
			new("Approved", "2"),
			new("Rejected", "3"),
			new("Suspended", "4"),
			new("Closed", "6"),
		};
	}


	// Users who have created at least one permit that entered a workflow.
	// Values match ReportGridModel.PermitHolderId (lowercase user id).
	private async Task<List<SelectListItem>> GetPermitHolderOptions(Guid company, CancellationToken cancellationToken)
	{
		var holderGuids = await _dbContext.Permits
			.AsNoTracking()
			.Where(p => p.Company.Id == company && p.PermitWorkflowStep != null)
			.Select(p => p.CreatedBy)
			.Distinct()
			.ToListAsync(cancellationToken);

		var holderIds = holderGuids.Select(g => g.ToString().ToLower()).ToList();

		var holders = await _dbContext.Users
			.AsNoTracking()
			.Where(u => holderIds.Contains(u.Id))
			.Select(u => new { Text = (u.FirstName + " " + u.LastName).Trim(), Value = u.Id })
			.OrderBy(u => u.Text)
			.ToListAsync(cancellationToken);

		var options = new List<SelectListItem> { new("(select)", "") };
		options.AddRange(holders.Select(h => new SelectListItem(h.Text, h.Value.ToLower())));

		return options;
	}


	public async Task<JsonResult> GetReportGrid(Guid company, CancellationToken cancellationToken)
	{
		// The All Permits list hides archived permits from everyone but Super Users (KPIs and charts still count them).
		var canSeeArchived = User.IsInRole("Super User");

		var rawPermits = await _dbContext.Permits
			.AsNoTracking()
			.Include(e => e.Site)
			.Where(e => e.Company.Id == company && e.PermitWorkflowStep != null && e.Site != null)
			.Where(e => canSeeArchived || !e.IsArchived)
			.OrderByDescending(e => e.CreatedWhen)
			.Select(e => new
			{
				e.Id,
				PermitHolderId = e.CreatedBy.ToString().ToLower(),
				PermitNo = string.Format("PTW{0:000000}", e.RunningNumber),
				Location = e.Site.Name,
				PermitStatus = e.PermitStatus.ToString(),
				e.PermitForm,
				e.CreatedWhen,
				LocationId = e.Site.Id,
				PermitStatusEnum = e.PermitStatus,
			})
			.ToListAsync(cancellationToken);

		var holderIds = rawPermits.Select(p => p.PermitHolderId).Distinct().ToList();
		var holderNames = await _dbContext.Users
			.AsNoTracking()
			.Where(u => holderIds.Contains(u.Id))
			.Select(u => new { u.Id, FullName = (u.FirstName + " " + u.LastName).Trim() })
			.ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

		var certMap = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["hotwork"] = "A", ["confinedspace"] = "B", ["radiation"] = "C",
			["excavation"] = "D", ["isolation"] = "E", ["methodStatement"] = "F",
			["liftingHoisting"] = "G", ["override"] = "H",
		};

		var list = new List<ReportGridModel>(rawPermits.Count);

		foreach (var permit in rawPermits)
		{
			using var doc = JsonDocument.Parse(permit.PermitForm);
			var general = doc.RootElement.GetProperty("general");

			var dtmStart = general.TryGetProperty("startDateTime", out var s) && s.ValueKind != JsonValueKind.Null ? s.GetString() : null;
			var dtmEnd   = general.TryGetProperty("endDateTime",   out var e) && e.ValueKind != JsonValueKind.Null ? e.GetString() : null;

			var certs = new List<string>();
			if (general.TryGetProperty("certificates", out var certsEl) && certsEl.ValueKind == JsonValueKind.Array)
			{
				foreach (var c in certsEl.EnumerateArray())
				{
					if (c.TryGetProperty("name", out var n) &&
						certMap.TryGetValue(n.GetString() ?? "", out var letter))
						certs.Add(letter);
				}
			}

			var dtm = GeneralHelper.GetDateInTimeZone(permit.CreatedWhen);

			var data = new ReportGridModel
			{
				Id = permit.Id,
				PermitNo = permit.PermitNo,
				PermitHolderName = holderNames.TryGetValue(permit.PermitHolderId, out var name) ? name : "(unknown)",
				Location = permit.Location,
				PermitStatus = permit.PermitStatus,
				Certificates = string.Join(", ", certs),
				CreatedWhen = new DateTime(dtm.Year, dtm.Month, dtm.Day),
				CreatedWhenString = dtm.ToString("dd MMMM yyyy"),
				CreatedMonth = dtm.ToString("MMMM"),
				CreatedYear = Convert.ToInt16(dtm.ToString("yyyy")),
				LocationId = permit.LocationId.ToString(),
				PermitHolderId = permit.PermitHolderId,
				PermitStatusEnum = (int)permit.PermitStatusEnum,
			};

			if (!string.IsNullOrEmpty(dtmStart)) data.StartDate = DateTime.Parse(dtmStart).ToLocalTime();
			if (!string.IsNullOrEmpty(dtmEnd))   data.EndDate   = DateTime.Parse(dtmEnd).ToLocalTime();

			list.Add(data);
		}

		return new JsonResult(list, new JsonSerializerOptions { PropertyNamingPolicy = null });
	}

	public async Task<JsonResult> GetChartData(
		Guid company,
		bool useDateRange = false,
		string month = "",
		int year = 0,
		DateTime? startDate = null,
		DateTime? endDate = null,
		string locationId = "",
		string certificateType = "",
		int permitStatus = -1,
		string holderId = "",
		CancellationToken cancellationToken = default)
	{
		var permitsRaw = await _dbContext.Permits
			.AsNoTracking()
			.Include(e => e.Site)
			.Where(e => e.Company.Id == company && e.PermitWorkflowStep != null && e.Site != null)
			.ToListAsync(cancellationToken);

		var holderGuids = permitsRaw.Select(p => p.CreatedBy.ToString().ToLower()).Distinct().ToList();
		var holderNames = await _dbContext.Users
			.AsNoTracking()
			.Where(u => holderGuids.Contains(u.Id))
			.Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim() })
			.ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

		var all = new List<ChartPermitData>();
		foreach (var p in permitsRaw)
		{
			if (string.IsNullOrWhiteSpace(p.PermitForm)) continue;
			using var doc = JsonDocument.Parse(p.PermitForm);
			var general = doc.RootElement.GetProperty("general");

			var dtmStart = general.TryGetProperty("startDateTime", out var s2) && s2.ValueKind != JsonValueKind.Null ? s2.GetString() : null;
			var dtmEnd   = general.TryGetProperty("endDateTime",   out var e2) && e2.ValueKind != JsonValueKind.Null ? e2.GetString() : null;

			var certs = new List<string>();
			if (general.TryGetProperty("certificates", out var certsEl2) && certsEl2.ValueKind == JsonValueKind.Array)
			{
				foreach (var cert in certsEl2.EnumerateArray())
				{
					if (!cert.TryGetProperty("name", out var certName)) continue;
					var letter = certName.GetString() switch
					{
						"hotwork" => "A", "confinedspace" => "B", "radiation" => "C",
						"excavation" => "D", "isolation" => "E", "methodStatement" => "F",
						"liftingHoisting" => "G", "override" => "H", _ => "?"
					};
					certs.Add(letter);
				}
			}

			var holderKey = p.CreatedBy.ToString().ToLower();
			var dtm = GeneralHelper.GetDateInTimeZone(p.CreatedWhen);
			all.Add(new ChartPermitData
			{
				Id = p.Id,
				PermitNo = string.Format("PTW{0:000000}", p.RunningNumber),
				HolderUserId = holderKey,
				HolderName = holderNames.TryGetValue(holderKey, out var hname) ? hname : "(unknown)",
				Status = p.PermitStatus,
				CreatedWhen = new DateTime(dtm.Year, dtm.Month, dtm.Day),
				LocationId = p.Site?.Id.ToString() ?? string.Empty,
				LocationName = p.Site?.Name ?? string.Empty,
				Certificates = string.Join(",", certs),
				StartDate = !string.IsNullOrEmpty(dtmStart) ? DateTime.Parse(dtmStart).ToLocalTime() : null,
				EndDate   = !string.IsNullOrEmpty(dtmEnd)   ? DateTime.Parse(dtmEnd).ToLocalTime()   : null,
			});
		}

		// Filtered set — used for KPI cards, donut chart, and location chart
		IEnumerable<ChartPermitData> filtered = all;

		if (useDateRange && startDate.HasValue && endDate.HasValue)
		{
			filtered = filtered.Where(p => p.CreatedWhen >= startDate.Value.Date && p.CreatedWhen <= endDate.Value.Date);
		}
		else if (!useDateRange)
		{
			if (!string.IsNullOrEmpty(month)) filtered = filtered.Where(p => p.CreatedWhen.ToString("MMMM") == month);
			if (year > 0) filtered = filtered.Where(p => p.CreatedWhen.Year == year);
		}

		if (!string.IsNullOrEmpty(locationId)) filtered = filtered.Where(p => p.LocationId == locationId);
		if (!string.IsNullOrEmpty(certificateType)) filtered = filtered.Where(p => p.Certificates.Contains(certificateType));
		if (permitStatus >= 0) filtered = filtered.Where(p => (int)p.Status == permitStatus);
		if (!string.IsNullOrEmpty(holderId)) filtered = filtered.Where(p => p.HolderUserId.Equals(holderId, StringComparison.OrdinalIgnoreCase));

		var f = filtered.ToList();

		var terminalStatuses = new[] { PermitStatusEnum.Closed, PermitStatusEnum.Rejected, PermitStatusEnum.ClosedNoAction };
		var now = DateTime.Now;

		var summary = new
		{
			total = f.Count,
			draft = f.Count(p => p.Status == PermitStatusEnum.Draft),
			pending = f.Count(p => p.Status == PermitStatusEnum.Pending),
			approved = f.Count(p => p.Status == PermitStatusEnum.Approved),
			rejected = f.Count(p => p.Status == PermitStatusEnum.Rejected),
			suspended = f.Count(p => p.Status == PermitStatusEnum.Suspended),
			kiv = f.Count(p => p.Status == PermitStatusEnum.KIV),
			closed = f.Count(p => p.Status == PermitStatusEnum.Closed || p.Status == PermitStatusEnum.ClosedNoAction),
			overdue = f.Count(p => p.EndDate.HasValue && p.EndDate.Value < now && !terminalStatuses.Contains(p.Status)),
		};

		var byLocation = f
			.GroupBy(p => p.LocationName)
			.Select(g => new
			{
				label    = g.Key,
				count    = g.Count(),
				approved = g.Count(p => p.Status == PermitStatusEnum.Approved || p.Status == PermitStatusEnum.Closed || p.Status == PermitStatusEnum.ClosedNoAction),
				pending  = g.Count(p => p.Status == PermitStatusEnum.Pending),
				other    = g.Count(p => p.Status != PermitStatusEnum.Approved && p.Status != PermitStatusEnum.Closed && p.Status != PermitStatusEnum.ClosedNoAction && p.Status != PermitStatusEnum.Pending),
			})
			.OrderByDescending(x => x.count)
			.Take(8)
			.ToList<object>();

		var byHolder = f
			.GroupBy(p => new { p.HolderUserId, p.HolderName })
			.Select(g => new
			{
				name     = g.Key.HolderName,
				total    = g.Count(),
				approved = g.Count(p => p.Status == PermitStatusEnum.Approved || p.Status == PermitStatusEnum.Closed || p.Status == PermitStatusEnum.ClosedNoAction),
				pending  = g.Count(p => p.Status == PermitStatusEnum.Pending),
				other    = g.Count(p => p.Status != PermitStatusEnum.Approved && p.Status != PermitStatusEnum.Closed && p.Status != PermitStatusEnum.ClosedNoAction && p.Status != PermitStatusEnum.Pending),
			})
			.OrderByDescending(x => x.total)
			.ToList<object>();

		var overduePermits = f
			.Where(p => p.EndDate.HasValue && p.EndDate.Value < now && !terminalStatuses.Contains(p.Status))
			.OrderBy(p => p.EndDate)
			.Select(p => new
			{
				id          = p.Id,
				permitNo    = p.PermitNo,
				holderName  = p.HolderName,
				location    = p.LocationName,
				endDate     = p.EndDate.Value.ToString("dd MMM yyyy"),
				daysOverdue = (int)Math.Round((now - p.EndDate.Value).TotalDays),
				status      = p.Status.ToString(),
			})
			.ToList<object>();

		// Monthly trend — always last 12 months; date filter is intentionally excluded
		// so the trend chart retains context when a specific month/year is selected.
		IEnumerable<ChartPermitData> trendBase = all;

		if (!string.IsNullOrEmpty(locationId)) trendBase = trendBase.Where(p => p.LocationId == locationId);
		if (!string.IsNullOrEmpty(certificateType)) trendBase = trendBase.Where(p => p.Certificates.Contains(certificateType));
		if (permitStatus >= 0) trendBase = trendBase.Where(p => (int)p.Status == permitStatus);
		if (!string.IsNullOrEmpty(holderId)) trendBase = trendBase.Where(p => p.HolderUserId.Equals(holderId, StringComparison.OrdinalIgnoreCase));

		var monthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-11);
		var trend = trendBase.Where(p => p.CreatedWhen >= monthStart).ToList();
		var byMonth = new List<object>();

		for (int i = 0; i < 12; i++)
		{
			var m = monthStart.AddMonths(i);
			var mp = trend.Where(p => p.CreatedWhen.Year == m.Year && p.CreatedWhen.Month == m.Month).ToList();

			byMonth.Add(new
			{
				label = m.ToString("MMM yy"),
				approved = mp.Count(p => p.Status == PermitStatusEnum.Approved || p.Status == PermitStatusEnum.Closed || p.Status == PermitStatusEnum.ClosedNoAction),
				pending = mp.Count(p => p.Status == PermitStatusEnum.Pending),
				other = mp.Count(p => p.Status != PermitStatusEnum.Approved && p.Status != PermitStatusEnum.Closed && p.Status != PermitStatusEnum.ClosedNoAction && p.Status != PermitStatusEnum.Pending),
			});
		}

		return new JsonResult(new 
		{ 
			summary, 
			byMonth, 
			byLocation, 
			byHolder, 
			overduePermits 
		}, 
		new JsonSerializerOptions
		{
			PropertyNamingPolicy = null
		});
	}

	#endregion


	#region "POST"
	#endregion


	#region "PUT"
	#endregion


	#region "DELETE"
	#endregion


	#region "Private functions/methods"
	#endregion

	private class ChartPermitData
	{
		public Guid Id { get; set; }
		public string PermitNo { get; set; } = string.Empty;
		public string HolderUserId { get; set; } = string.Empty;
		public string HolderName { get; set; } = string.Empty;
		public PermitStatusEnum Status { get; set; }
		public DateTime CreatedWhen { get; set; }
		public string LocationId { get; set; } = string.Empty;
		public string LocationName { get; set; } = string.Empty;
		public string Certificates { get; set; } = string.Empty;
		public DateTime? StartDate { get; set; }
		public DateTime? EndDate { get; set; }
	}
}
