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
	private readonly ILogger<RecycleBinController> _logger;

	public RecycleBinController(
		 ApplicationDbContext dbContext
		 , IHttpContextAccessor httpContextAccessor
		 , SignInManager<UserInfo> signInManager
		 , UserManager<UserInfo> userManager
		 , ISystemConfigurationService systemConfigurationService
		 , ILogger<RecycleBinController> logger)
		 : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_dbContext = dbContext;
		_userManager = userManager;
		_logger = logger;
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
	public async Task<IActionResult> Restore(Guid company, string entityType, Guid id, CancellationToken cancellationToken)
	{
		var entity = await FindDeletedAsync(company, entityType, id, cancellationToken);
		if (entity == null)
			return NotFound();

		await _dbContext.RestoreAsync((ISoftDeletable)entity);

		TempData["RecycleBinMessage"] = $"{Capitalize(Label(entityType, 1))} restored successfully.";
		return BackToTab(company, entityType);
	}


	/// <summary>
	/// Restores the records ticked on one tab. <paramref name="ids"/> is comma-separated: one form field,
	/// so a long list doesn't hit the form's value-count limit.
	/// </summary>
	[HttpPost("{company}/recyclebin/restore/{entityType}")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> RestoreSelected(Guid company, string entityType, [FromForm] string ids, CancellationToken cancellationToken)
	{
		if (!EntityTypes.ContainsKey(entityType))
			return NotFound();

		var restored = 0;
		foreach (var id in ParseIds(ids))
		{
			// Null when it's no longer in the bin, e.g. restored a moment ago along with its parent.
			var entity = await FindDeletedAsync(company, entityType, id, cancellationToken);
			if (entity == null)
				continue;

			await _dbContext.RestoreAsync((ISoftDeletable)entity);
			restored++;
		}

		if (restored > 0)
			TempData["RecycleBinMessage"] = $"{Capitalize(Label(entityType, restored))} restored.";
		else
			TempData["RecycleBinError"] = "Nothing was restored: the selected records are no longer in the Recycle Bin.";

		return BackToTab(company, entityType);
	}


	[Authorize(Roles = "Super User")]
	[HttpPost("{company}/recyclebin/delete/{entityType}/{id}")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Delete(Guid company, string entityType, Guid id, CancellationToken cancellationToken)
	{
		var entity = await FindDeletedAsync(company, entityType, id, cancellationToken);
		if (entity == null)
			return NotFound();

		var error = await TryHardDeleteAsync(entity, id, cancellationToken);
		if (error != null)
			TempData["RecycleBinError"] = error;
		else
			TempData["RecycleBinMessage"] = $"{Capitalize(Label(entityType, 1))} permanently deleted.";

		return BackToTab(company, entityType);
	}


	/// <summary>
	/// Permanently deletes the records ticked on one tab ("Delete all" sends every record listed).
	/// Each record is deleted on its own: one that can't go is reported and stays in the bin, the rest still go.
	/// </summary>
	[Authorize(Roles = "Super User")]
	[HttpPost("{company}/recyclebin/delete/{entityType}")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> DeleteSelected(Guid company, string entityType, [FromForm] string ids, CancellationToken cancellationToken)
	{
		if (!EntityTypes.ContainsKey(entityType))
			return NotFound();

		var pending = ParseIds(ids);
		var deleted = 0;
		var failures = new Dictionary<Guid, string>();

		// A location can't go while it has child locations. When parents and children are both selected,
		// go round again while a pass still deletes something: each pass clears the next level up.
		while (pending.Count > 0)
		{
			var blocked = new List<Guid>();
			foreach (var id in pending)
			{
				var entity = await FindDeletedAsync(company, entityType, id, cancellationToken);
				if (entity == null)
				{
					failures.Remove(id);
					continue;
				}

				var error = await TryHardDeleteAsync(entity, id, cancellationToken);
				if (error == null)
				{
					deleted++;
					failures.Remove(id);
				}
				else
				{
					failures[id] = error;
					blocked.Add(id);
				}
			}

			if (entityType != "Site" || blocked.Count == pending.Count)
				break;
			pending = blocked;
		}

		if (deleted > 0)
			TempData["RecycleBinMessage"] = $"{Capitalize(Label(entityType, deleted))} permanently deleted.";

		if (failures.Count == 1)
		{
			TempData["RecycleBinError"] = failures.Values.Single();
		}
		else if (failures.Count > 1)
		{
			// First line is the heading, the rest a list (see Index.cshtml).
			const int maxListed = 10;
			var lines = failures.Values.Take(maxListed).ToList();
			if (failures.Count > maxListed)
				lines.Add($"…and {failures.Count - maxListed} more.");

			TempData["RecycleBinError"] = $"{Label(entityType, failures.Count)} couldn't be permanently deleted and stay in the Recycle Bin:\n"
				+ string.Join("\n", lines);
		}
		else if (deleted == 0)
		{
			TempData["RecycleBinError"] = "Nothing was deleted: the selected records are no longer in the Recycle Bin.";
		}

		return BackToTab(company, entityType);
	}


	// Recycle Bin tab per entity type: tab id (Index.cshtml) and the words used in messages.
	private static readonly Dictionary<string, (string Tab, string One, string Many)> EntityTypes = new()
	{
		["Permit"] = ("tab-permits", "permit", "permits"),
		["Site"] = ("tab-sites", "location", "locations"),
		["Workflow"] = ("tab-workflows", "workflow", "workflows"),
		["WorkflowStep"] = ("tab-workflowsteps", "workflow step", "workflow steps"),
		["User"] = ("tab-users", "user", "users"),
		["Role"] = ("tab-roles", "role", "roles"),
		["Company"] = ("tab-companies", "company", "companies"),
	};

	/// <summary>"permit" when <paramref name="count"/> is 1, else e.g. "3 permits".</summary>
	private static string Label(string entityType, int count)
	{
		var (_, one, many) = EntityTypes.TryGetValue(entityType, out var t) ? t : (null, "record", "records");
		return count == 1 ? one : $"{count} {many}";
	}

	private static string Capitalize(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];

	/// <summary>Back to the Recycle Bin on the tab the action came from.</summary>
	private IActionResult BackToTab(Guid company, string entityType)
		 => RedirectToAction(nameof(Index), null, new { company }, EntityTypes.TryGetValue(entityType, out var t) ? t.Tab : null);

	private static List<Guid> ParseIds(string ids)
		 => (ids ?? string.Empty)
			  .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			  .Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty)
			  .Where(id => id != Guid.Empty)
			  .Distinct()
			  .ToList();

	/// <summary>
	/// The record the Recycle Bin lists under <paramref name="entityType"/>, or null when it isn't there: not deleted,
	/// already gone, or another company's for a Portal Admin. Same scope as <see cref="Index"/>.
	/// </summary>
	private async Task<object> FindDeletedAsync(Guid company, string entityType, Guid id, CancellationToken cancellationToken)
	{
		var isSuperUser = User.IsInRole("Super User");
		var stringId = id.ToString();

		object entity = entityType switch
		{
			"Permit" => await _dbContext.Permits.IgnoreQueryFilters()
				 .FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted && (isSuperUser || e.Company.Id == company), cancellationToken),
			"Site" => await _dbContext.Sites.IgnoreQueryFilters()
				 .FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted && (isSuperUser || e.SiteCompany.Id == company), cancellationToken),
			"Workflow" => await _dbContext.Workflows.IgnoreQueryFilters()
				 .FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted && (isSuperUser || e.WorkflowCompany.Id == company), cancellationToken),
			"WorkflowStep" => await _dbContext.WorkflowSteps.IgnoreQueryFilters()
				 .FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted && (isSuperUser || e.WorkflowStepWorkflow.WorkflowCompany.Id == company), cancellationToken),
			"User" => await _dbContext.Users.IgnoreQueryFilters()
				 .FirstOrDefaultAsync(e => e.Id == stringId && e.IsDeleted && (isSuperUser || e.UserCompany.Id == company), cancellationToken),
			// Roles are global; the bin lists them to Portal Admins too.
			"Role" => await _dbContext.Roles.IgnoreQueryFilters()
				 .FirstOrDefaultAsync(e => e.Id == stringId && e.IsDeleted, cancellationToken),
			"Company" when isSuperUser => await _dbContext.Companies.IgnoreQueryFilters()
				 .FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted, cancellationToken),
			_ => null
		};

		return entity;
	}

	/// <summary>
	/// Permanently deletes one soft-deleted record. Returns why it couldn't be deleted, or null when it was.
	/// </summary>
	private async Task<string> TryHardDeleteAsync(object entity, Guid id, CancellationToken cancellationToken)
	{
		// Records still referenced by permits, approval history or child locations can't be removed:
		// those FKs have no delete rule, and clearing them would lose audit data or orphan locations.
		var blockedReason = await GetHardDeleteBlockerAsync(entity, id);
		if (blockedReason != null)
			return blockedReason;

		// Enable hard delete mode (bypass soft delete interceptor)
		_dbContext.UseSoftDelete = false;

		try
		{
			// Handle hard delete with FK cascade to NULL
			if (entity is Site)
			{
				// Set SiteId to NULL for all permits tied to this site
				var permits = await _dbContext.Permits.IgnoreQueryFilters()
					 .Where(p => p.Site!.Id == id).ToListAsync(cancellationToken);

				foreach (var p in permits)
				{
					p.Site = null;
				}
				_dbContext.Permits.UpdateRange(permits);

				// Divisions have no screens, but their FK to Sites has no delete rule either.
				var divisions = await _dbContext.Divisions.IgnoreQueryFilters()
					 .Where(d => d.DivisionSite!.Id == id).ToListAsync(cancellationToken);

				foreach (var d in divisions)
				{
					d.DivisionSite = null;
				}
				_dbContext.Divisions.UpdateRange(divisions);
			}
			else if (entity is Workflow)
			{
				// Set WorkflowId to NULL for all workflow steps tied to this workflow
				var workflowSteps = await _dbContext.WorkflowSteps.IgnoreQueryFilters()
					 .Where(s => s.WorkflowStepWorkflow!.Id == id).ToListAsync(cancellationToken);

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
			}

			_dbContext.Remove(entity);
			await _dbContext.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException ex)
		{
			// Some other table still points at the record (e.g. a user named on a certificate). Drop the
			// unsaved changes so the next record of a bulk delete starts from a clean context.
			_logger.LogWarning(ex, "Permanent delete of {EntityType} {Id} failed", entity.GetType().Name, id);
			_dbContext.ChangeTracker.Clear();
			return $"{Describe(entity)} can't be permanently deleted: other records still refer to it. It stays in the Recycle Bin.";
		}
		finally
		{
			// Reset flag to default (soft delete)
			_dbContext.UseSoftDelete = true;
		}

		// Only once the record is gone, so a failed delete keeps the permit's files.
		if (entity is Permit permit)
		{
			var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "attachments", "permits", permit.Id.ToString().ToLower());
			if (Directory.Exists(folder))
			{
				Directory.Delete(folder, true);
			}
		}

		return null;
	}

	/// <summary>How a record is named in Recycle Bin messages, e.g. <c>Location "Plant 2"</c>.</summary>
	private static string Describe(object entity) => entity switch
	{
		Permit p => $"Permit PTW{p.PermitNo}",
		Site s => $"Location \"{s.Name}\"",
		Workflow w => $"Workflow \"{w.Name}\"",
		WorkflowStep s => $"Workflow step \"{s.Name}\"",
		UserInfo u => $"User \"{$"{u.FirstName} {u.LastName}".Trim()}\"",
		Role r => $"Role \"{r.Name}\"",
		Company c => $"Company \"{c.Name}\"",
		_ => "This record"
	};

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
