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
public class TenantFormControlsController : AppControllerBase
{
	private readonly IFormControlService _formControls;

	public TenantFormControlsController(
		ApplicationDbContext dbContext,
		IHttpContextAccessor httpContextAccessor,
		SignInManager<UserInfo> signInManager,
		ISystemConfigurationService systemConfigurationService,
		IFormControlService formControls)
		: base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_formControls = formControls;
	}


	[HttpGet("{company}/formcontrols")]
	public async Task<IActionResult> Index(
		 string search = null,
		 FormControlTypeEnum? type = null,
		 int page = 1,
		 CancellationToken ct = default)
	{
		if (!IsSuperUserOrPortalAdmin()) return Forbid();

		const int pageSize = 20;
		var controls = await _formControls.GetAllAsync(CompanyID, ct);

		if (!string.IsNullOrWhiteSpace(search))
			controls = controls.Where(c =>
				 c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
				 c.Label.Contains(search, StringComparison.OrdinalIgnoreCase));

		if (type.HasValue)
			controls = controls.Where(c => c.ControlType == type.Value);

		var list = controls.ToList();
		var totalCount = list.Count;
		var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
		page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

		var vm = new FormControlsIndexViewModel
		{
			Controls = list
				  .Skip((page - 1) * pageSize)
				  .Take(pageSize)
				  .Select(c => new FormControlListViewModel
				  {
					  Id = c.Id,
					  Name = c.Name,
					  Label = c.Label,
					  ControlType = c.ControlType,
					  IsRequired = c.IsRequired,
					  SortOrder = c.SortOrder,
					  CreatedWhen = c.CreatedWhen,
				  })
				  .ToList(),
			Search = search,
			TypeFilter = type,
			Page = page,
			PageSize = pageSize,
			TotalCount = totalCount,
			TotalPages = totalPages,
			CompanyId = CompanyID,
		};

		return View("~/Views/AdminFormControls/Index.cshtml", vm);
	}


	[HttpGet("{company}/formcontrols/{id:guid}")]
	public async Task<IActionResult> Get(Guid id, CancellationToken ct)
	{
		if (!IsSuperUserOrPortalAdmin()) return Forbid();

		var control = await _formControls.GetByIdAsync(id, ct);
		if (control is null) return NotFound();

		return Ok(new
		{
			control.Id,
			control.Name,
			control.Label,
			ControlType = (int)control.ControlType,
			control.OptionsJson,
			control.IsRequired,
			control.Placeholder,
			control.HelpText,
			control.SortOrder,
		});
	}


	[HttpPost("{company}/formcontrols")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Create(FormControlFormViewModel model, CancellationToken ct)
	{
		if (!IsSuperUserOrPortalAdmin()) return Forbid();

		if (!ModelState.IsValid)
		{
			TempData["ErrorMessage"] = "Please correct the validation errors.";
			return Redirect($"/{CompanyID}/formcontrols");
		}

		var control = new FormControl
		{
			CompanyId = CompanyID,
			Name = model.Name,
			Label = model.Label,
			ControlType = model.ControlType,
			OptionsJson = NormaliseOptionsJson(model.OptionsJson),
			IsRequired = model.IsRequired,
			Placeholder = model.Placeholder,
			HelpText = model.HelpText,
			SortOrder = model.SortOrder,
			CreatedBy = GetCurrentUserId(),
		};

		await _formControls.CreateAsync(control, ct);
		TempData["SuccessMessage"] = $"Control \"{control.Label}\" created.";
		return Redirect($"/{CompanyID}/formcontrols");
	}


	[HttpPost("{company}/formcontrols/{id:guid}/edit")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Edit(Guid id, FormControlFormViewModel model, CancellationToken ct)
	{
		if (!IsSuperUserOrPortalAdmin()) return Forbid();

		var existing = await _formControls.GetByIdAsync(id, ct);
		if (existing is null) return NotFound();

		if (!ModelState.IsValid)
		{
			TempData["ErrorMessage"] = "Please correct the validation errors.";
			return Redirect($"/{CompanyID}/formcontrols");
		}

		existing.Name = model.Name;
		existing.Label = model.Label;
		existing.ControlType = model.ControlType;
		existing.OptionsJson = NormaliseOptionsJson(model.OptionsJson);
		existing.IsRequired = model.IsRequired;
		existing.Placeholder = model.Placeholder;
		existing.HelpText = model.HelpText;
		existing.SortOrder = model.SortOrder;
		existing.UpdatedBy = GetCurrentUserId();

		await _formControls.UpdateAsync(existing, ct);
		TempData["SuccessMessage"] = $"Control \"{existing.Label}\" updated.";
		return Redirect($"/{CompanyID}/formcontrols");
	}


	[HttpPost("{company}/formcontrols/{id:guid}/delete")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
	{
		if (!IsSuperUserOrPortalAdmin()) return Forbid();

		await _formControls.DeleteAsync(id, GetCurrentUserId() ?? Guid.Empty, ct);
		TempData["SuccessMessage"] = "Control deleted.";
		return Redirect($"/{CompanyID}/formcontrols");
	}


	private bool IsSuperUserOrPortalAdmin() => User.IsInRole("Super User") || User.IsInRole("SUPERUSER");

	private Guid? GetCurrentUserId()
	{
		var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
		return Guid.TryParse(id, out var guid) ? guid : null;
	}

	private static string NormaliseOptionsJson(string json)
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
