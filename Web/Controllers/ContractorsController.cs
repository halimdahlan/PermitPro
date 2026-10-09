#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PermitPro.App.Controllers.Base;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

using System.Text;
using System.Text.Json;

namespace PermitPro.App.Controllers;

[Authorize]
public class ContractorsController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;

	public ContractorsController(
		ApplicationDbContext dbContext
		, IHttpContextAccessor httpContextAccessor
		, SignInManager<UserInfo> signInManager
		, ISystemConfigurationService systemConfigurationService
	) : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_dbContext = dbContext;
	}

	public IActionResult Index(Guid company)
	{
		var sites = _dbContext.Sites
			.AsNoTracking()
			.Include(x => x.SiteCompany)
			.Where(x => x.IsActive == true && x.SiteType == SiteTypeEnum.Site && x.SiteCompany.Id == company);

		var locations = sites
			.Select(e => new
			{
				Id = e.Id.ToString(),
				e.Name,
			})
			.ToList();

		locations.Insert(0, new { Id = string.Empty, Name = "All" });

		ViewBag.Locations = locations;

		var roleContractor = _dbContext.Roles.FirstOrDefault(e => e.NormalizedName == "CONTRACTOR");
		
		if (roleContractor == null)
			return NotFound("The Contractor role has not been seeded for this environment.");

		var model = new ContractorsViewModel
		{
			CompanyId = CompanyID,
			Role = roleContractor
		};

		return View(model);
	}


	public IActionResult New()
	{
		return View();
	}


	public IActionResult Edit()
	{
		return View();
	}


	[HttpGet("{company}/contractors/grid")]
	public async Task<JsonResult> ContractorsGridView(Guid company)
	{
		var contractors = await _dbContext.Users
			.AsNoTracking()
			.Where(e => e.UserCompany.Id == company && e.UserRoles.Any(ur => ur.Role.NormalizedName == "CONTRACTOR"))
			.OrderByDescending(e => e.CreatedWhen)
			.Select(e => new
			{
				e.Id,
				FirstName = e.FirstName ?? "",
				LastName = e.LastName ?? "",
				e.Email,
				SiteNames = e.Sites.Select(s => s.Name),
				e.PasswordHash,
				e.IsActive,
				e.CreatedWhen
			})
			.ToListAsync();

		var viewModelList = contractors.Select(e => new ContractorListViewModel
		{
			Id = e.Id,
			Name = $"{e.FirstName.Trim()} {e.LastName.Trim()}".Trim(),
			Email = e.Email,
			Location = string.Join(", ", e.SiteNames),
			IsSecured = e.PasswordHash != null ? "<i class=\"fa-regular fa-user-lock fa-lg\"></i>" : "<i class=\"fa-regular fa-user-unlock fa-lg\"></i>",
			IsActiveIcons = e.IsActive ? "<i class=\"fa-sharp fa-solid fa-circle-check fa-lg text-success\"></i>" : "<i class=\"fa-sharp fa-solid fa-circle-check fa-lg text-warning\"></i>",
			IsActive = e.IsActive,
			CreatedWhen = GeneralHelper.GetDateInTimeZone(e.CreatedWhen),
			ActionIcons = ContractorsGridActions(company, e.Id, e.PasswordHash != null),
		}).ToList();

		return new JsonResult(viewModelList, new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		});
	}



	#region "Private static functions/methods"

	private static string ContractorsGridActions(Guid company, string id, bool isSecured)
	{
		var icons = string.Empty;
		var disabled = isSecured ? " disabled" : "";

		StringBuilder sb = new();

		sb.AppendLine("<div class=\"d-flex flex-row action-icons\">");
		sb.AppendLine($"<a href=\"/{company}/users/edit/{id}?org=c\" class=\"text-secondary\"><i class=\"fa-solid fa-money-check-pen fa-lg\"></i></a>");
		sb.AppendLine($"<a href=\"javascript:;\" class=\"no-loading text-danger\" onclick=\"deleteUser('{id}')\"><i class=\"fa-solid fa-trash-xmark fa-lg\"></i></a>");
		sb.AppendLine("</div>");


		icons = sb.ToString();

		return icons;
	}

	#endregion

}
