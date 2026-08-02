#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using PermitPro.App.ViewModels;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Interfaces;

using System.Security.Claims;
using System.Text.Json;

namespace PermitPro.App.Controllers;

[Authorize]
public class AdminFormTemplatesController : Controller
{
	private readonly IFormTemplateService _templates;
	private readonly IFormControlService _controls;

	public AdminFormTemplatesController(
		IFormTemplateService templates,
		IFormControlService controls)
	{
		_templates = templates;
		_controls = controls;
	}


	// ── Index ─────────────────────────────────────────────────────────────────

	[HttpGet("/admin/formtemplates")]
	[HttpGet("/admin/certificatetemplates")]
	public async Task<IActionResult> Index(CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

		var companyIdStr = Request.Query["companyId"].ToString();
		if (!Guid.TryParse(companyIdStr, out var companyId) || companyId == Guid.Empty)
			return Redirect("/admin/companies");

		var typeStr = Request.Query["type"].ToString();
		FormTemplateTypeEnum? typeFilter = typeStr switch
		{
			"permit" => FormTemplateTypeEnum.Permit,
			"certificate" => FormTemplateTypeEnum.Certificate,
			_ when Request.Path.Value?.Contains("certificate", StringComparison.OrdinalIgnoreCase) == true
				=> FormTemplateTypeEnum.Certificate,
			_ => null,
		};

		var templates = await _templates.GetAllAsync(companyId, typeFilter, ct);

		var vm = new FormTemplatesIndexViewModel
		{
			CompanyId = companyId,
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

		return View(vm);
	}


	// ── Builder (create) ──────────────────────────────────────────────────────

	[HttpGet("/admin/formtemplates/new")]
	[HttpGet("/admin/certificatetemplates/new")]
	public async Task<IActionResult> Builder(CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

		var companyIdStr = Request.Query["companyId"].ToString();
		if (!Guid.TryParse(companyIdStr, out var companyId) || companyId == Guid.Empty)
			return Redirect("/admin/companies");

		var isCertRoute = Request.Path.Value?.Contains("certificate", StringComparison.OrdinalIgnoreCase) == true;

		var vm = await BuildBuilderVmAsync(companyId, null, isCertRoute ? FormTemplateTypeEnum.Certificate : FormTemplateTypeEnum.Permit, ct);
		return View(vm);
	}


	// ── Builder (edit) ────────────────────────────────────────────────────────

	[HttpGet("/admin/formtemplates/{id:guid}/edit")]
	[HttpGet("/admin/certificatetemplates/{id:guid}/edit")]
	public async Task<IActionResult> BuilderEdit(Guid id, CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

		var template = await _templates.GetByIdAsync(id, ct);
		if (template is null) return NotFound();

		var vm = await BuildBuilderVmAsync(template.CompanyId, template, template.TemplateType, ct);
		return View("Builder", vm);
	}


	// ── Save (create + update) ────────────────────────────────────────────────

	[HttpPost("/admin/formtemplates/save")]
	[IgnoreAntiforgeryToken]
	public async Task<IActionResult> Save([FromBody] FormBuilderSaveRequest req, CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

		if (!Guid.TryParse(req.CompanyId, out var companyId) || companyId == Guid.Empty)
			return BadRequest("Invalid company.");

		if (string.IsNullOrWhiteSpace(req.Name))
			return BadRequest("Template name is required.");

		if (!ValidateLayoutJson(req.LayoutJson, out var layoutError))
			return BadRequest(layoutError);

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
				CompanyId = companyId,
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


	// ── Delete ────────────────────────────────────────────────────────────────

	[HttpPost("/admin/formtemplates/{id:guid}/delete")]
	[HttpPost("/admin/certificatetemplates/{id:guid}/delete")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Delete(Guid id, Guid companyId, FormTemplateTypeEnum type, CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

		var deletedBy = GetCurrentUserId() ?? Guid.Empty;
		await _templates.DeleteAsync(id, deletedBy, ct);
		TempData["SuccessMessage"] = "Template deleted.";

		var returnUrl = type == FormTemplateTypeEnum.Certificate
			 ? $"/admin/certificatetemplates?companyId={companyId}"
			 : $"/admin/formtemplates?companyId={companyId}";

		return Redirect(returnUrl);
	}


	// ── Helpers ───────────────────────────────────────────────────────────────

	private async Task<FormBuilderViewModel> BuildBuilderVmAsync(
		 Guid companyId,
		 FormTemplate template,
		 FormTemplateTypeEnum type,
		 CancellationToken ct)
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

	private bool IsSuperUser()
		 => User.IsInRole("Super User") || User.IsInRole("SUPERUSER");

	private Guid? GetCurrentUserId()
	{
		var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
		return Guid.TryParse(id, out var guid) ? guid : null;
	}

	private static bool ValidateLayoutJson(string json, out string error)
	{
		error = null;
		if (string.IsNullOrWhiteSpace(json)) { error = "Layout JSON is required."; return false; }
		try { JsonDocument.Parse(json); return true; }
		catch { error = "Layout JSON is invalid."; return false; }
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

// ── Save request model ────────────────────────────────────────────────────────

public class FormBuilderSaveRequest
{
	public Guid? TemplateId { get; set; }
	public string CompanyId { get; set; }
	public string Name { get; set; }
	public string Description { get; set; }
	public FormTemplateTypeEnum TemplateType { get; set; }
	public string LayoutJson { get; set; }
	public string HeaderConfigJson { get; set; }
	public string FooterConfigJson { get; set; }
}
