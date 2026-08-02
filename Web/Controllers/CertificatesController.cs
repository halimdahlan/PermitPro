#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PermitPro.App.Controllers.Base;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Enums;
using PermitPro.Core.Interfaces;

using System.Security.Claims;
using System.Text.Json;

namespace PermitPro.App.Controllers;

[Authorize]
public class CertificatesController : AppControllerBase
{
	private readonly ICertificateService _certs;
	private readonly IFormTemplateService _templates;
	private readonly ApplicationDbContext _db;

	public CertificatesController(
		ApplicationDbContext dbContext,
		IHttpContextAccessor httpContextAccessor,
		SignInManager<PermitPro.Core.Entities.UserInfo> signInManager,
		ISystemConfigurationService systemConfigurationService,
		ICertificateService certs,
		IFormTemplateService templates)
		: base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_certs = certs;
		_templates = templates;
		_db = dbContext;
	}


	// ── List certificates for a permit ────────────────────────────────────────

	[HttpGet("{company}/permits/{permitId}/certificates")]
	public async Task<IActionResult> Index(Guid company, Guid permitId, CancellationToken ct)
	{
		var certs = await _certs.GetByPermitAsync(permitId, ct);

		var vm = certs.Select(c => new CertificateListViewModel
		{
			Id = c.Id,
			Name = c.Name,
			Description = c.Description,
			HasTemplate = c.FormTemplateId.HasValue,
			HasData = !string.IsNullOrWhiteSpace(c.CertificateDataJson),
		}).ToList();

		ViewData["PermitId"] = permitId;
		ViewData["CompanyId"] = company;
		return View(vm);
	}


	// ── View / fill a certificate form ────────────────────────────────────────

	[HttpGet("{company}/permits/{permitId}/certificates/{id}")]
	public async Task<IActionResult> Form(Guid company, Guid permitId, Guid id, CancellationToken ct)
	{
		var cert = await _certs.GetByIdAsync(id, ct);
		if (cert is null) return NotFound();

		var availableTemplates = await _templates.GetAllAsync(company, FormTemplateTypeEnum.Certificate, ct);

		var vm = new CertificateFormViewModel
		{
			CertificateId = cert.Id,
			PermitId = permitId,
			Name = cert.Name,
			Description = cert.Description,
			FormTemplateId = cert.FormTemplateId,
			CertificateDataJson = cert.CertificateDataJson,
			LayoutJson = cert.FormTemplate != null
											? EnrichLayout(cert.FormTemplate.LayoutJson, company)
											: "{\"rows\":[]}",
			AvailableTemplates = availableTemplates.Select(t => new FormTemplateListViewModel
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


	// ── Attach a template to a certificate ───────────────────────────────────

	[HttpPost("{company}/permits/{permitId}/certificates/{id}/template")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AssignTemplate(Guid company, Guid permitId, Guid id, Guid templateId, CancellationToken ct)
	{
		var cert = await _db.Certificates.FindAsync([id], ct);
		if (cert is null) return NotFound();

		var template = await _templates.GetByIdAsync(templateId, ct);
		if (template is null) return NotFound();

		cert.FormTemplateId = templateId;
		cert.UpdatedWhen = DateTime.UtcNow;
		cert.UpdatedBy = GetCurrentUserId();
		await _db.SaveChangesAsync(ct);

		return RedirectToAction(nameof(Form), new { company, permitId, id });
	}


	// ── Save certificate field data ───────────────────────────────────────────

	[HttpPost("{company}/permits/{permitId}/certificates/{id}/save")]
	[IgnoreAntiforgeryToken]
	public async Task<IActionResult> SaveData(Guid company, Guid permitId, Guid id,
		 [FromBody] CertificateSaveRequest req, CancellationToken ct)
	{
		try
		{
			await _certs.SaveDataAsync(id, req?.DataJson ?? "{}", ct);
			return Ok(new { success = true });
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}


	// ── Create a new certificate on a permit ─────────────────────────────────

	[HttpPost("{company}/permits/{permitId}/certificates")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Create(Guid company, Guid permitId, string name, string description, Guid? templateId, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			TempData["ErrorMessage"] = "Certificate name is required.";
			return RedirectToAction(nameof(Index), new { company, permitId });
		}

		await _certs.CreateAsync(permitId, templateId, name, description, ct);
		TempData["SuccessMessage"] = $"Certificate \"{name}\" added.";
		return RedirectToAction(nameof(Index), new { company, permitId });
	}


	// ── Delete a certificate ─────────────────────────────────────────────────

	[HttpPost("{company}/permits/{permitId}/certificates/{id}/delete")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Delete(Guid company, Guid permitId, Guid id, CancellationToken ct)
	{
		await _certs.DeleteAsync(id, GetCurrentUserId() ?? Guid.Empty, ct);
		TempData["SuccessMessage"] = "Certificate deleted.";
		return RedirectToAction(nameof(Index), new { company, permitId });
	}


	// ── Get certificate data (for AJAX) ──────────────────────────────────────

	[HttpGet("{company}/permits/{permitId}/certificates/{id}/data")]
	public async Task<IActionResult> GetData(Guid company, Guid permitId, Guid id, CancellationToken ct)
	{
		var cert = await _certs.GetByIdAsync(id, ct);
		if (cert is null) return NotFound();

		return Ok(new
		{
			cert.Id,
			cert.Name,
			cert.Description,
			cert.FormTemplateId,
			cert.CertificateDataJson,
		});
	}


	// ── Helpers ───────────────────────────────────────────────────────────────

	private Guid? GetCurrentUserId()
	{
		var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
		return Guid.TryParse(id, out var guid) ? guid : null;
	}

	private string EnrichLayout(string layoutJson, Guid companyId)
	{
		if (string.IsNullOrWhiteSpace(layoutJson)) return "{\"rows\":[]}";
		try
		{
			using var doc = JsonDocument.Parse(layoutJson);
			var controlIds = doc.RootElement.GetProperty("rows").EnumerateArray()
				 .SelectMany(r => r.GetProperty("columns").EnumerateArray())
				 .Where(c => c.TryGetProperty("controlId", out var cid) && cid.ValueKind != JsonValueKind.Null)
				 .Select(c => { c.GetProperty("controlId").TryGetGuid(out var id); return id; })
				 .Where(id => id != Guid.Empty).Distinct().ToList();

			var controls = _db.FormControls.AsNoTracking()
				 .Where(c => c.CompanyId == companyId && controlIds.Contains(c.Id))
				 .Select(c => new { c.Id, c.Name, c.Label, ControlType = (int)c.ControlType, c.IsRequired, c.Placeholder, c.HelpText, c.OptionsJson })
				 .ToList();

			var controlMap = controls.ToDictionary(c => c.Id);
			var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

			var enriched = new
			{
				rows = doc.RootElement.GetProperty("rows").EnumerateArray().Select(row => new
				{
					id = row.GetProperty("id").GetString(),
					columnCount = row.GetProperty("columnCount").GetInt32(),
					columns = row.GetProperty("columns").EnumerateArray().Select(col =>
					{
						col.TryGetProperty("controlId", out var cidEl);
						Guid parsedId = Guid.Empty;
						if (cidEl.ValueKind != JsonValueKind.Null) cidEl.TryGetGuid(out parsedId);
						object control = null;
						if (parsedId != Guid.Empty && controlMap.TryGetValue(parsedId, out var ctrl)) control = ctrl;
						return new
						{
							id = col.GetProperty("id").GetString(),
							controlId = parsedId == Guid.Empty ? (Guid?)null : parsedId,
							labelOverride = col.TryGetProperty("labelOverride", out var lo) && lo.ValueKind != JsonValueKind.Null ? lo.GetString() : null,
							isRequiredOverride = col.TryGetProperty("isRequiredOverride", out var iro) && iro.ValueKind == JsonValueKind.True ? (bool?)true :
															 iro.ValueKind == JsonValueKind.False ? (bool?)false : null,
							control,
						};
					}).ToList(),
				}).ToList(),
			};

			return JsonSerializer.Serialize(enriched, opts);
		}
		catch { return layoutJson; }
	}
}

public class CertificateSaveRequest
{
	public string DataJson { get; set; }
}
