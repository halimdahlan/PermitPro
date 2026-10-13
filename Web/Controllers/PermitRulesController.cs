#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

using PermitPro.App.Controllers.Base;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

using System.Text.Json;

namespace PermitPro.App.Controllers;

// Admin → Rules: the company's preset permit rules (Super Users). The rules themselves run in
// PermitRulesBackgroundService; this page switches them on/off, edits settings and shows a dry run.
[Authorize]
public class PermitRulesController : AppControllerBase
{
	private readonly IPermitRuleService _rules;
	private readonly IConfiguration _configuration;

	private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = null };

	public PermitRulesController(
		ApplicationDbContext dbContext,
		IHttpContextAccessor httpContextAccessor,
		SignInManager<UserInfo> signInManager,
		ISystemConfigurationService systemConfigurationService,
		IPermitRuleService rules,
		IConfiguration configuration)
		: base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_rules = rules;
		_configuration = configuration;
	}

	private bool IsSuperUser() => User.IsInRole("Super User");


	[HttpGet("{company}/rules")]
	public async Task<IActionResult> Index(CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

		var rules = await _rules.GetOrCreatePresetsAsync(CompanyID, ct);

		return View(new PermitRulesViewModel
		{
			CompanyId = CompanyID,
			EmailsEnabled = _configuration.GetValue("PermitRules:SendEmails", false),
			Rules = rules.Select(r => new PermitRuleItemViewModel
			{
				RuleType = r.RuleType,
				IsEnabled = r.IsEnabled,
				SettingsJson = r.SettingsJson ?? "{}",
				LastRunWhen = r.LastRunWhen.HasValue ? GeneralHelper.GetDateInTimeZone(r.LastRunWhen.Value) : null,
				LastRunSummary = r.LastRunSummary,
			}).ToList(),
		});
	}


	public class SaveRuleRequest
	{
		public PermitRuleTypeEnum RuleType { get; set; }
		public bool IsEnabled { get; set; }
		public JsonElement Settings { get; set; }
	}

	[HttpPost("{company}/rules/save")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Save([FromBody] SaveRuleRequest request, CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();
		if (request == null || !Enum.IsDefined(request.RuleType)) return BadRequest("Unknown rule.");

		try
		{
			await _rules.SaveAsync(CompanyID, request.RuleType, request.IsEnabled, request.Settings.GetRawText(), ct);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}

		return Ok();
	}


	// Dry run: permits the rule would act on now, with the (unsaved) settings from the page.
	[HttpPost("{company}/rules/preview")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Preview([FromBody] SaveRuleRequest request, CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();
		if (request == null || !Enum.IsDefined(request.RuleType)) return BadRequest("Unknown rule.");

		var preview = await _rules.PreviewAsync(CompanyID, request.RuleType, request.Settings.GetRawText(), ct);

		return new JsonResult(new
		{
			preview.Count,
			Sample = preview.Sample.Select(p => new
			{
				p.Id,
				p.PermitNo,
				p.Description,
				Status = p.Status == nameof(PermitStatusEnum.ClosedNoAction) ? "Closed – no action" : p.Status,
				Date = p.Date.HasValue ? GeneralHelper.GetDateInTimeZone(p.Date.Value).ToString("dd MMM yyyy, hh:mm tt") : "",
			}),
		}, _json);
	}
}
