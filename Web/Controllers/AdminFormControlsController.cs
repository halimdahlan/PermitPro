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
public class AdminFormControlsController : Controller
{
	private readonly IFormControlService _formControls;

	public AdminFormControlsController(IFormControlService formControls)
	{
		_formControls = formControls;
	}


	// ── Index ─────────────────────────────────────────────────────────────────

	[HttpGet("/admin/formcontrols")]
	public async Task<IActionResult> Index(
		 string search = null,
		 FormControlTypeEnum? type = null,
		 int page = 1,
		 CancellationToken ct = default)
	{
		if (!IsSuperUser()) return Forbid();

		const int pageSize = 20;

		var companyIdStr = Request.Query["companyId"].ToString();
		if (!Guid.TryParse(companyIdStr, out var companyId) || companyId == Guid.Empty)
			return Redirect("/admin/companies");

		var controls = await _formControls.GetAllAsync(companyId, ct);

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

		var viewModel = new FormControlsIndexViewModel
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
			CompanyId = companyId,
		};

		return View(viewModel);
	}


	// ── Get single (for edit dialog) ──────────────────────────────────────────

	[HttpGet("/admin/formcontrols/{id:guid}")]
	public async Task<IActionResult> Get(Guid id, CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

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


	// ── Create ────────────────────────────────────────────────────────────────

	[HttpPost("/admin/formcontrols")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Create(FormControlFormViewModel model, Guid companyId, CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

		if (!ModelState.IsValid)
		{
			TempData["ErrorMessage"] = "Please correct the validation errors.";
			return Redirect($"/admin/formcontrols?companyId={companyId}");
		}

		if (!ValidateOptionsJson(model.ControlType, model.OptionsJson, out var optionsError))
		{
			TempData["ErrorMessage"] = optionsError;
			return Redirect($"/admin/formcontrols?companyId={companyId}");
		}

		var control = new FormControl
		{
			CompanyId = companyId,
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
		return Redirect($"/admin/formcontrols?companyId={companyId}");
	}


	// ── Update ────────────────────────────────────────────────────────────────

	[HttpPost("/admin/formcontrols/{id:guid}/edit")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Edit(Guid id, FormControlFormViewModel model, Guid companyId, CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

		var existing = await _formControls.GetByIdAsync(id, ct);
		if (existing is null) return NotFound();

		if (!ModelState.IsValid)
		{
			TempData["ErrorMessage"] = "Please correct the validation errors.";
			return Redirect($"/admin/formcontrols?companyId={companyId}");
		}

		if (!ValidateOptionsJson(model.ControlType, model.OptionsJson, out var optionsError))
		{
			TempData["ErrorMessage"] = optionsError;
			return Redirect($"/admin/formcontrols?companyId={companyId}");
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
		return Redirect($"/admin/formcontrols?companyId={companyId}");
	}


	// ── Delete ────────────────────────────────────────────────────────────────

	[HttpPost("/admin/formcontrols/{id:guid}/delete")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Delete(Guid id, Guid companyId, CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

		var deletedBy = GetCurrentUserId() ?? Guid.Empty;
		await _formControls.DeleteAsync(id, deletedBy, ct);
		TempData["SuccessMessage"] = "Control deleted.";
		return Redirect($"/admin/formcontrols?companyId={companyId}");
	}


	// Controls list "Delete selected" (Super Users): the same soft delete as Delete(), for this company's controls only.
	[HttpPost("/admin/formcontrols/bulk-delete")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> BulkDelete(Guid companyId, [FromBody] PermitPro.App.Models.Ajax.AjaxBulkDeleteRequest request, CancellationToken ct)
	{
		if (!IsSuperUser()) return Forbid();

		var result = new PermitPro.App.Models.Ajax.AjaxBulkDeleteResult();
		var deletedBy = GetCurrentUserId() ?? Guid.Empty;

		foreach (var raw in (request?.Ids ?? new()).Distinct())
		{
			var control = Guid.TryParse(raw, out var id) ? await _formControls.GetByIdAsync(id, ct) : null;
			if (control == null || control.CompanyId != companyId)
			{
				result.Fail(raw, "Control", "Not found (it may already be deleted).");
				continue;
			}

			var blocked = await _formControls.GetDeleteBlockReasonAsync(control.Id, ct);
			if (blocked != null)
			{
				result.Fail(raw, control.Label, blocked);
				continue;
			}

			try
			{
				await _formControls.DeleteAsync(control.Id, deletedBy, ct);
				result.Deleted++;
			}
			catch (Exception ex)
			{
				result.Fail(raw, control.Label, ex.Message);
			}
		}

		return Ok(result);
	}


	// ── Helpers ───────────────────────────────────────────────────────────────

	private bool IsSuperUser()
		 => User.IsInRole("Super User") || User.IsInRole("SUPERUSER");

	private Guid? GetCurrentUserId()
	{
		var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
		return Guid.TryParse(id, out var guid) ? guid : null;
	}

	private static bool ValidateOptionsJson(FormControlTypeEnum type, string json, out string error)
	{
		error = null;
		if (string.IsNullOrWhiteSpace(json)) return true;

		try
		{
			JsonDocument.Parse(json);
		}
		catch
		{
			error = "Options JSON is not valid JSON.";
			return false;
		}

		return true;
	}

	private static string NormaliseOptionsJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;
		try
		{
			// Re-serialise to compact form
			using var doc = JsonDocument.Parse(json);
			return JsonSerializer.Serialize(doc.RootElement);
		}
		catch
		{
			return null;
		}
	}
}
