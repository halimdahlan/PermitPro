#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PermitPro.App.Controllers.Base;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

namespace PermitPro.App.Controllers;

[Authorize(Roles = "Super User,Portal Admin")]
public class RecycleBinController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;
	private readonly UserManager<UserInfo> _userManager;

	public RecycleBinController(
		 ApplicationDbContext dbContext
		 , IHttpContextAccessor httpContextAccessor
		 , SignInManager<UserInfo> signInManager
		 , UserManager<UserInfo> userManager
		 , ISystemConfigurationService systemConfigurationService)
		 : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_dbContext = dbContext;
		_userManager = userManager;
	}


	[HttpGet("{company}/recyclebin")]
	public async Task<IActionResult> Index(Guid company)
	{
		var isSuperUser = User.IsInRole("Super User");

		var permits = await _dbContext.Permits
			 .IgnoreQueryFilters()
			 .Include(p => p.Company)
			 .Include(p => p.Site)
			 .Where(p => p.IsDeleted && (isSuperUser || p.Company.Id == company))
			 .OrderByDescending(p => p.DeletedWhen)
			 .Select(p => new DeletedItem
			 {
				 Id = p.Id,
				 EntityType = "Permit",
				 DisplayName = $"PTW{p.PermitNo}",
				 Detail = p.Site != null ? p.Site.Name : "(no site)",
				 CompanyId = p.Company != null ? p.Company.Id : Guid.Empty,
				 CompanyName = p.Company != null ? p.Company.Name : "(none)",
				 DeletedWhen = GeneralHelper.GetDateInTimeZone(p.DeletedWhen.Value),
				 DeletedBy = p.DeletedBy,
			 })
			 .ToListAsync();

		var sites = await _dbContext.Sites
			 .IgnoreQueryFilters()
			 .Include(s => s.SiteCompany)
			 .Where(s => s.IsDeleted && (isSuperUser || s.SiteCompany.Id == company))
			 .OrderByDescending(s => s.DeletedWhen)
			 .Select(s => new DeletedItem
			 {
				 Id = s.Id,
				 EntityType = "Site",
				 DisplayName = s.Name,
				 Detail = s.Description,
				 CompanyId = s.SiteCompany != null ? s.SiteCompany.Id : Guid.Empty,
				 CompanyName = s.SiteCompany != null ? s.SiteCompany.Name : "(none)",
				 DeletedWhen = GeneralHelper.GetDateInTimeZone(s.DeletedWhen.Value),
				 DeletedBy = s.DeletedBy,
			 })
			 .ToListAsync();

		var workflows = await _dbContext.Workflows
			 .IgnoreQueryFilters()
			 .Include(w => w.WorkflowCompany)
			 .Where(w => w.IsDeleted && (isSuperUser || w.WorkflowCompany.Id == company))
			 .OrderByDescending(w => w.DeletedWhen)
			 .Select(w => new DeletedItem
			 {
				 Id = w.Id,
				 EntityType = "Workflow",
				 DisplayName = w.Name,
				 Detail = w.Description,
				 CompanyId = w.WorkflowCompany != null ? w.WorkflowCompany.Id : Guid.Empty,
				 CompanyName = w.WorkflowCompany != null ? w.WorkflowCompany.Name : "(none)",
				 DeletedWhen = GeneralHelper.GetDateInTimeZone(w.DeletedWhen.Value),
				 DeletedBy = w.DeletedBy,
			 })
			 .ToListAsync();

		var workflowSteps = await _dbContext.WorkflowSteps
			 .IgnoreQueryFilters()
			 .Include(s => s.WorkflowStepWorkflow)
				  .ThenInclude(w => w.WorkflowCompany)
			 .Where(s => s.IsDeleted && (isSuperUser || s.WorkflowStepWorkflow.WorkflowCompany.Id == company))
			 .OrderByDescending(s => s.DeletedWhen)
			 .Select(s => new DeletedItem
			 {
				 Id = s.Id,
				 EntityType = "WorkflowStep",
				 DisplayName = s.Name,
				 Detail = s.WorkflowStepWorkflow != null ? s.WorkflowStepWorkflow.Name : "(unknown workflow)",
				 CompanyId = s.WorkflowStepWorkflow != null ? s.WorkflowStepWorkflow.WorkflowCompany.Id : Guid.Empty,
				 CompanyName = s.WorkflowStepWorkflow != null ? s.WorkflowStepWorkflow.WorkflowCompany.Name : "(none)",
				 DeletedWhen = GeneralHelper.GetDateInTimeZone(s.DeletedWhen.Value),
				 DeletedBy = s.DeletedBy,
			 })
			 .ToListAsync();

		var users = await _dbContext.Users
			 .IgnoreQueryFilters()
			 .Include(u => u.UserCompany)
			 .Where(u => u.IsDeleted && (isSuperUser || u.UserCompany.Id == company))
			 .OrderByDescending(u => u.DeletedWhen)
			 .Select(u => new DeletedItem
			 {
				 Id = Guid.Parse(u.Id),
				 EntityType = "User",
				 DisplayName = $"{u.FirstName} {u.LastName}".Trim(),
				 Detail = u.Email,
				 CompanyId = u.UserCompany != null ? u.UserCompany.Id : Guid.Empty,
				 CompanyName = u.UserCompany != null ? u.UserCompany.Name : "(none)",
				 DeletedWhen = GeneralHelper.GetDateInTimeZone(u.DeletedWhen.Value),
				 DeletedBy = u.DeletedBy,
			 })
			 .ToListAsync();

		var roles = await _dbContext.Roles
			 .IgnoreQueryFilters()
			 .Where(r => r.IsDeleted)
			 .OrderByDescending(r => r.DeletedWhen)
			 .Select(r => new DeletedItem
			 {
				 Id = Guid.Parse(r.Id),
				 EntityType = "Role",
				 DisplayName = r.Name,
				 Detail = r.Description,
				 CompanyId = Guid.Empty,
				 CompanyName = "(global)",
				 DeletedWhen = GeneralHelper.GetDateInTimeZone(r.DeletedWhen.Value),
				 DeletedBy = r.DeletedBy,
			 })
			 .ToListAsync();

		// Resolve DeletedBy user names in a single query
		var allDeletedByIds = permits.Concat(sites).Concat(workflows)
			 .Concat(workflowSteps).Concat(users).Concat(roles)
			 .Where(x => x.DeletedBy.HasValue)
			 .Select(x => x.DeletedBy!.Value.ToString())
			 .Distinct()
			 .ToList();

		var deletedByNames = await _dbContext.Users
			 .IgnoreQueryFilters()
			 .Where(u => allDeletedByIds.Contains(u.Id))
			 .ToDictionaryAsync(u => Guid.Parse(u.Id), u => $"{u.FirstName} {u.LastName}".Trim());

		foreach (var item in permits.Concat(sites).Concat(workflows).Concat(workflowSteps).Concat(users).Concat(roles))
		{
			if (item.DeletedBy.HasValue && deletedByNames.TryGetValue(item.DeletedBy.Value, out var name))
				item.DeletedByName = name;
		}

		// Companies tab — only Super User sees this
		var companies = new List<DeletedItem>();
		if (isSuperUser)
		{
			companies = await _dbContext.Companies
				 .IgnoreQueryFilters()
				 .Where(c => c.IsDeleted)
				 .OrderByDescending(c => c.DeletedWhen)
				 .Select(c => new DeletedItem
				 {
					 Id = c.Id,
					 EntityType = "Company",
					 DisplayName = c.Name,
					 Detail = c.Description,
					 CompanyId = c.Id,
					 CompanyName = c.Name,
					 DeletedWhen = GeneralHelper.GetDateInTimeZone(c.DeletedWhen.Value),
					 DeletedBy = c.DeletedBy,
				 })
				 .ToListAsync();
		}

		ViewBag.Permits = permits;
		ViewBag.Sites = sites;
		ViewBag.Workflows = workflows;
		ViewBag.WorkflowSteps = workflowSteps;
		ViewBag.Users = users;
		ViewBag.Roles = roles;
		ViewBag.Companies = companies;
		ViewBag.IsSuperUser = isSuperUser;

		return View();
	}


	[HttpPost("{company}/recyclebin/restore/{entityType}/{id}")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Restore(Guid company, string entityType, Guid id)
	{
		ISoftDeletable entity = entityType switch
		{
			"Permit" => await _dbContext.Permits.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id),
			"Site" => await _dbContext.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id),
			"Workflow" => await _dbContext.Workflows.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id),
			"WorkflowStep" => await _dbContext.WorkflowSteps.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id),
			"Company" when User.IsInRole("Super User") => await _dbContext.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id),
			"User" => await _dbContext.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id.ToString()),
			"Role" => await _dbContext.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id.ToString()),
			_ => null
		};

		if (entity == null)
			return NotFound();

		await _dbContext.RestoreAsync(entity);

		TempData["RecycleBinMessage"] = $"{entityType} restored successfully.";
		return RedirectToAction(nameof(Index), new { company });
	}


	[Authorize(Roles = "Super User")]
	[HttpPost("{company}/recyclebin/delete/{entityType}/{id}")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Delete(Guid company, string entityType, Guid id)
	{
		object entity = entityType switch
		{
			"Permit" => await _dbContext.Permits.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id),
			"Site" => await _dbContext.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id),
			"Workflow" => await _dbContext.Workflows.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id),
			"WorkflowStep" => await _dbContext.WorkflowSteps.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id),
			"Company" => await _dbContext.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id),
			"User" => await _dbContext.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id.ToString()),
			"Role" => await _dbContext.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id.ToString()),
			_ => null
		};

		if (entity == null)
			return NotFound();

		// Records still referenced by permits, approval history or child locations can't be removed:
		// those FKs have no delete rule, and clearing them would lose audit data or orphan locations.
		var blockedReason = await GetHardDeleteBlockerAsync(entity, id);
		if (blockedReason != null)
		{
			TempData["RecycleBinError"] = blockedReason;
			return RedirectToAction(nameof(Index), new { company });
		}

		// Enable hard delete mode (bypass soft delete interceptor)
		_dbContext.UseSoftDelete = false;

		try
		{
			// Handle hard delete with FK cascade to NULL
			if (entity is Permit permit)
			{
				// Delete the permit's attachment folder
				var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "attachments", "permits", permit.Id.ToString().ToLower());
				if (Directory.Exists(folder))
				{
					Directory.Delete(folder, true);
				}
			}
			else if (entity is Site site)
			{
				// Set SiteId to NULL for all permits tied to this site
				var permits = await _dbContext.Permits.IgnoreQueryFilters()
					 .Where(p => p.Site!.Id == id).ToListAsync();

				foreach (var p in permits)
				{
					p.Site = null;
				}
				_dbContext.Permits.UpdateRange(permits);

				// Divisions have no screens, but their FK to Sites has no delete rule either.
				var divisions = await _dbContext.Divisions.IgnoreQueryFilters()
					 .Where(d => d.DivisionSite!.Id == id).ToListAsync();

				foreach (var d in divisions)
				{
					d.DivisionSite = null;
				}
				_dbContext.Divisions.UpdateRange(divisions);
			}
			else if (entity is Workflow workflow)
			{
				// Set WorkflowId to NULL for all workflow steps tied to this workflow
				var workflowSteps = await _dbContext.WorkflowSteps.IgnoreQueryFilters()
					 .Where(s => s.WorkflowStepWorkflow!.Id == id).ToListAsync();

				foreach (var s in workflowSteps)
				{
					s.WorkflowStepWorkflow = null;
				}
				_dbContext.WorkflowSteps.UpdateRange(workflowSteps);
			}
			else if (entity is Company)
			{
				// Hard delete all company data recursively with FK to NULL
				await HardDeleteCompanyAsync(id);
				_dbContext.Remove(entity);
				await _dbContext.SaveChangesAsync();

				TempData["RecycleBinMessage"] = $"Company permanently deleted.";
				return RedirectToAction(nameof(Index), new { company });
			}

			_dbContext.Remove(entity);
			await _dbContext.SaveChangesAsync();

			TempData["RecycleBinMessage"] = $"{entityType} permanently deleted.";

			return RedirectToAction(nameof(Index), new { company });
		}
		finally
		{
			// Reset flag to default (soft delete)
			_dbContext.UseSoftDelete = true;
		}
	}

	/// <summary>
	/// Why <paramref name="entity"/> can't be permanently deleted, or null when it can. Permits and approval
	/// history keep their workflow/step (audit trail), and a location with child locations would orphan them.
	/// Soft-deleted referencing rows count too: the FK applies to them as well.
	/// </summary>
	private async Task<string> GetHardDeleteBlockerAsync(object entity, Guid id)
	{
		var usages = new List<string>();
		string name;

		switch (entity)
		{
			case WorkflowStep step:
				name = $"Workflow step \"{step.Name}\"";
				AddUsage(usages, await _dbContext.Permits.IgnoreQueryFilters().CountAsync(p => p.PermitWorkflowStep!.Id == id), "permit", "permits");
				AddUsage(usages, await _dbContext.WorkflowHistories.IgnoreQueryFilters().CountAsync(h => h.HistoryWorkflowStep!.Id == id), "approval history entry", "approval history entries");
				break;

			case Workflow workflow:
				name = $"Workflow \"{workflow.Name}\"";
				AddUsage(usages, await _dbContext.Permits.IgnoreQueryFilters().CountAsync(p => p.PermitWorkflow!.Id == id), "permit", "permits");
				AddUsage(usages, await _dbContext.WorkflowHistories.IgnoreQueryFilters().CountAsync(h => h.HistoryWorkflow!.Id == id), "approval history entry", "approval history entries");
				break;

			case Site site:
				name = $"Location \"{site.Name}\"";
				AddUsage(usages, await _dbContext.Sites.IgnoreQueryFilters().CountAsync(s => s.ParentId == id), "child location", "child locations");
				break;

			default:
				return null;
		}

		return usages.Count == 0
			? null
			: $"{name} can't be permanently deleted: it is used by {string.Join(" and ", usages)}. It stays in the Recycle Bin.";

		static void AddUsage(List<string> usages, int count, string singular, string plural)
		{
			if (count > 0)
				usages.Add($"{count} {(count == 1 ? singular : plural)}");
		}
	}

	/// <summary>
	/// Hard delete a company and update related records to set FK to NULL instead of deleting.
	/// </summary>
	private async Task HardDeleteCompanyAsync(Guid companyId)
	{
		// Set CompanyId to NULL for all permits tied to this company
		var permits = await _dbContext.Permits.IgnoreQueryFilters()
			 .Where(p => p.Company!.Id == companyId).ToListAsync();

		foreach (var permit in permits)
		{
			// Delete attachment folders
			var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "attachments", "permits", permit.Id.ToString().ToLower());
			if (Directory.Exists(folder))
			{
				Directory.Delete(folder, true);
			}
			permit.Company = null;
		}
		_dbContext.Permits.UpdateRange(permits);

		// Set SiteCompanyId to NULL for all sites tied to this company
		var sites = await _dbContext.Sites.IgnoreQueryFilters()
			 .Where(s => s.SiteCompany!.Id == companyId).ToListAsync();

		foreach (var site in sites)
		{
			site.SiteCompany = null;
		}
		_dbContext.Sites.UpdateRange(sites);

		// Set WorkflowCompanyId to NULL for all workflows tied to this company
		var workflows = await _dbContext.Workflows.IgnoreQueryFilters()
			 .Where(w => w.WorkflowCompany.Id == companyId).ToListAsync();

		foreach (var workflow in workflows)
		{
			workflow.WorkflowCompany = null;
		}
		_dbContext.Workflows.UpdateRange(workflows);

		// Set AddressCompanyId to NULL for all addresses tied to this company
		var addresses = await _dbContext.Addresses.IgnoreQueryFilters()
			 .Where(a => a.AddressCompany!.Id == companyId).ToListAsync();

		foreach (var address in addresses)
		{
			address.AddressCompany = null;
		}
		_dbContext.Addresses.UpdateRange(addresses);

		// Set UserCompanyId to NULL for all users tied to this company
		var users = await _dbContext.Users.IgnoreQueryFilters()
			 .Where(u => u.UserCompany!.Id == companyId).ToListAsync();

		foreach (var user in users)
		{
			user.UserCompany = null;
		}
		_dbContext.Users.UpdateRange(users);

		await _dbContext.SaveChangesAsync();
	}
}


/// <summary>Flat projection used to display any entity type in the recycle bin grid.</summary>
public class DeletedItem
{
	public Guid Id { get; set; }
	public string EntityType { get; set; }
	public string DisplayName { get; set; }
	public string Detail { get; set; }
	public Guid CompanyId { get; set; }
	public string CompanyName { get; set; }
	public DateTime? DeletedWhen { get; set; }
	public Guid? DeletedBy { get; set; }
	public string DeletedByName { get; set; }
}
