#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

using PermitPro.App.Controllers.Base;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Interfaces;

using System.Security.Claims;
using System.Text.Json;

namespace PermitPro.App.Controllers;

[Authorize]
public class TenantFormTemplatesController : AppControllerBase
{
	private readonly IFormTemplateService _templates;
	private readonly IFormControlService _controls;

	public TenantFormTemplatesController(
		ApplicationDbContext dbContext,
		IHttpContextAccessor httpContextAccessor,
		SignInManager<UserInfo> signInManager,
		ISystemConfigurationService systemConfigurationService,
		IFormTemplateService templates,
		IFormControlService controls)
		: base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_templates = templates;
		_controls = controls;
	}


	[HttpGet("{company}/formtemplates")]
	[HttpGet("{company}/certificatetemplates")]
	public async Task<IActionResult> Index(CancellationToken ct)
	{
		if (!IsSuperUserOrPortalAdmin()) return Forbid();

		var typeStr = Request.Query["type"].ToString();
		FormTemplateTypeEnum? typeFilter = typeStr switch
		{
			"permit" => FormTemplateTypeEnum.Permit,
			"certificate" => FormTemplateTypeEnum.Certificate,
			_ when Request.Path.Value?.Contains("certificate", StringComparison.OrdinalIgnoreCase) == true
				=> FormTemplateTypeEnum.Certificate,
			_ => null,
		};
		var isCert = typeFilter == FormTemplateTypeEnum.Certificate;

		var templates = await _templates.GetAllAsync(CompanyID, typeFilter, ct);

		var vm = new FormTemplatesIndexViewModel
		{
			CompanyId = CompanyID,
			TypeFilter = typeFilter,
			Templates = templates.Select(t => new FormTemplateListViewModel
			{
				Id = t.Id,
				Name = t.Name,
				Description = t.Description,
				TemplateType = t.TemplateType,
				CreatedWhen = t.CreatedWhen,
				UpdatedWhen = t.UpdatedWhen,
			}).ToList(),
		};

		if (isCert)
		{
			TempData["CurrentPage"] = "Certificate Templates";
		}

		return View("~/Views/AdminFormTemplates/Index.cshtml", vm);
	}


	[HttpGet("{company}/formtemplates/new")]
	[HttpGet("{company}/certificatetemplates/new")]
	public async Task<IActionResult> Builder(CancellationToken ct)
	{
		if (!IsSuperUserOrPortalAdmin()) return Forbid();

		var isCert = Request.Path.Value?.Contains("certificate", StringComparison.OrdinalIgnoreCase) == true;
		var vm = await BuildBuilderVmAsync(CompanyID, null, isCert ? FormTemplateTypeEnum.Certificate : FormTemplateTypeEnum.Permit, ct);

		if (isCert)
		{
			TempData["CurrentPage"] = "Certificate Templates";
		}

		return View("~/Views/AdminFormTemplates/Builder.cshtml", vm);
	}


	[HttpGet("{company}/formtemplates/{id:guid}/edit")]
	[HttpGet("{company}/certificatetemplates/{id:guid}/edit")]
	public async Task<IActionResult> BuilderEdit(Guid id, CancellationToken ct)
	{
		if (!IsSuperUserOrPortalAdmin()) return Forbid();

		var template = await _templates.GetByIdAsync(id, ct);
		if (template is null) return NotFound();

		var vm = await BuildBuilderVmAsync(template.CompanyId, template, template.TemplateType, ct);
		return View("~/Views/AdminFormTemplates/Builder.cshtml", vm);
	}


	[HttpPost("{company}/formtemplates/save")]
	[IgnoreAntiforgeryToken]
	public async Task<IActionResult> Save([FromBody] FormBuilderSaveRequest req, CancellationToken ct)
	{
		if (!IsSuperUserOrPortalAdmin()) return Forbid();

		if (string.IsNullOrWhiteSpace(req?.Name))
			return BadRequest("Template name is required.");

		Guid templateId;

		if (req.TemplateId.HasValue)
		{
			var existing = await _templates.GetByIdAsync(req.TemplateId.Value, ct);
			if (existing is null) return NotFound();

			existing.Name = req.Name.Trim();
			existing.Description = req.Description?.Trim();
			existing.TemplateType = req.TemplateType;
			existing.LayoutJson = req.LayoutJson;
			existing.HeaderConfigJson = NormaliseJson(req.HeaderConfigJson);
			existing.FooterConfigJson = NormaliseJson(req.FooterConfigJson);
			existing.UpdatedBy = GetCurrentUserId();

			await _templates.UpdateAsync(existing, ct);
			templateId = existing.Id;
		}
		else
		{
			var template = new FormTemplate
			{
				CompanyId = CompanyID,
				Name = req.Name.Trim(),
				Description = req.Description?.Trim(),
				TemplateType = req.TemplateType,
				LayoutJson = req.LayoutJson,
				HeaderConfigJson = NormaliseJson(req.HeaderConfigJson),
				FooterConfigJson = NormaliseJson(req.FooterConfigJson),
				CreatedBy = GetCurrentUserId(),
			};

			await _templates.CreateAsync(template, ct);
			templateId = template.Id;
		}

		return Ok(new { templateId });
	}


	[HttpPost("{company}/formtemplates/{id:guid}/delete")]
	[HttpPost("{company}/certificatetemplates/{id:guid}/delete")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Delete(Guid id, FormTemplateTypeEnum type, CancellationToken ct)
	{
		if (!IsSuperUserOrPortalAdmin()) return Forbid();

		await _templates.DeleteAsync(id, GetCurrentUserId() ?? Guid.Empty, ct);
		TempData["SuccessMessage"] = "Template deleted.";

		return Redirect(type == FormTemplateTypeEnum.Certificate
			 ? $"/{CompanyID}/certificatetemplates"
			 : $"/{CompanyID}/formtemplates");
	}


	private async Task<FormBuilderViewModel> BuildBuilderVmAsync(
		 Guid companyId, FormTemplate template, FormTemplateTypeEnum type, CancellationToken ct)
	{
		var controls = await _controls.GetAllAsync(companyId, ct);

		return new FormBuilderViewModel
		{
			TemplateId = template?.Id,
			CompanyId = companyId,
			Name = template?.Name ?? string.Empty,
			Description = template?.Description,
			TemplateType = template?.TemplateType ?? type,
			LayoutJson = template?.LayoutJson ?? "{\"rows\":[]}",
			HeaderConfigJson = template?.HeaderConfigJson,
			FooterConfigJson = template?.FooterConfigJson,
			ControlLibrary = controls.Select(c => new FormControlLibraryItem
			{
				Id = c.Id,
				Name = c.Name,
				Label = c.Label,
				ControlType = c.ControlType,
				IsRequired = c.IsRequired,
				Placeholder = c.Placeholder,
				HelpText = c.HelpText,
				OptionsJson = c.OptionsJson,
			}).ToList(),
		};
	}

	private bool IsSuperUserOrPortalAdmin() => User.IsInRole("Super User") || User.IsInRole("SUPERUSER");

	private Guid? GetCurrentUserId()
	{
		var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
		return Guid.TryParse(id, out var guid) ? guid : null;
	}

	private static string NormaliseJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;
		try
		{
			using var doc = JsonDocument.Parse(json);
			return JsonSerializer.Serialize(doc.RootElement);
		}
		catch { return null; }
	}
}
