using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

using PermitPro.App.Controllers.Base;
using PermitPro.App.Services;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Interfaces;

using System.Text.Json;

namespace PermitPro.App.Controllers;

[Authorize]
public class SitemapController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;
	private readonly JsonSerializerOptions _jsonOptions;
	private readonly ICurrentUserService _currentUserService;

	private Company? _company;

	public SitemapController(
		ApplicationDbContext dbContext
		, IHttpContextAccessor httpContextAccessor
		, SignInManager<UserInfo> signInManager
		, ICurrentUserService currentUserService
		, ISystemConfigurationService systemConfigurationService) : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_dbContext = dbContext;
		_currentUserService = currentUserService;

		_jsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		};

		_company = dbContext.Companies.FirstOrDefault(e => e.Id == CompanyID);
	}


	#region "Views"

	// Google Maps JavaScript API: its published CSP includes 'unsafe-eval'.
	[AllowUnsafeEval]
	public IActionResult Index(Guid company)
	{
		var site = _dbContext.Sites
			.Include(e => e.SiteCompany)
			.FirstOrDefault(e => e.SiteCompany != null && e.SiteCompany.Id == company && e.ParentId == Guid.Empty);

		return View(new SitemapViewModel
		{
			CompanyId = company,
			CompanyName = site?.SiteCompany?.Name ?? string.Empty,
			Latitude = site?.Latitude?.ToString() ?? "3.139",
			Longitude = site?.Longitude?.ToString() ?? "101.687",
		});
	}

	#endregion


	#region "API"

	public JsonResult GetSitesDropDown()
	{
		List<SelectListItem> list = new();

		if (_company != null)
		{
			var parentSite = _dbContext.Sites.FirstOrDefault(e => e.SiteCompany == _company && e.Name == _company.Name);

			if (parentSite != null)
			{
				list = _dbContext.Sites
					.Where(e => e.ParentId == parentSite.Id && e.IsActive)
					.Select(e => new SelectListItem
					{
						Text = e.Name,
						Value = e.Name,
					}).ToList();

				list.Insert(0, new SelectListItem
				{
					Text = "(all)",
					Value = "",
				});
			}
		}

		return Json(list.ToList());
	}

	#endregion


}

