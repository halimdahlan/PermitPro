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
using PermitPro.Core.Interfaces;

using System.Text.Json;

namespace PermitPro.App.Controllers;

[Authorize]
public class SitesController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;


	public SitesController(
		ApplicationDbContext dbContext
		, IHttpContextAccessor httpContextAccessor
		, SignInManager<UserInfo> signInManager
		, ISystemConfigurationService systemConfigurationService
	) : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_dbContext = dbContext;
	}


	[Route("{company}/sites/{parentId?}")]
	public IActionResult Index(Guid company, string parentId = null)
	{
		var sites = _dbContext.Sites
			.Include(f => f.SiteCompany)
			.Where(x => x.IsActive == true && x.SiteType == SiteTypeEnum.Site && x.SiteCompany.Id == company);

		if (parentId == null)
		{
			sites = sites.Where(x => x.ParentId == Guid.Empty);
		}

		var siteList = sites
			.Select(x => new
			{
				x.Id,
				x.Name,
			})
			.ToList();

		siteList.Insert(0, new { Id = Guid.Empty, Name = "(select parent)" });

		ViewBag.SiteList = siteList;


		return View(new SitesViewModel
		{
			CompanyId = company,
			ParentId = parentId,
			SiteParent = parentId != null ? _dbContext.Sites.FirstOrDefault(e => e.Id == Guid.Parse(parentId)) : null,
		});
	}


	[HttpPost("{company}/sites/{parentId?}/edit/{id}")]
	public async Task<IActionResult> Edit(Guid company, Guid? parentId, Guid id, SitesNewViewModel model)
	{
		if (!ModelState.IsValid) return View("New", model);

		var site = await _dbContext.Sites.FirstOrDefaultAsync(e => e.Id == id);
		if (site == null) return NotFound();

		site.Name = model.Name;
		site.Description = model.Description;
		site.ContactEmail = model.ContactEmail;
		site.ContactName = model.ContactName;
		site.Latitude = model.Latitude;
		site.Longitude = model.Longitude;
		site.IsActive = model.IsActive;
		site.ShowInMap = model.ShowInMap;

		_dbContext.Sites.Update(site);
		await _dbContext.SaveChangesAsync();

		TempData["SuccessMessage"] = "Site has been updated successfully.";

		return RedirectToAction("Index", new { company = model.CompanyId, parentId = model.ParentId });
	}


	public IActionResult Site2()
	{
		return View();
	}


	[HttpGet("{company}/sites/{parentId?}/edit/{id}")]
	public async Task<IActionResult> Edit(Guid company, Guid? parentId, Guid id)
	{
		var site = await _dbContext.Sites.FirstOrDefaultAsync(e => e.Id == id);
		if (site == null) return NotFound();

		var model = SitesNewViewModel.FromSite(site);
		model.CompanyId = company;
		model.ParentId = parentId;

		ViewBag.SiteId = id;
		ViewBag.CompanyId = company;

		return View("New", model);
	}


	[HttpGet("{company}/sites/{parentId?}/new")]
	public IActionResult New(Guid company, Guid? parentId = null)
	{
		ViewBag.SiteId = null;
		ViewBag.CompanyId = company;
		ViewBag.ParentId = parentId;

		return View(new SitesNewViewModel { CompanyId = company });
	}


	[HttpPost]
	public JsonResult GetSites(Guid? id)
	{
		var locations = _dbContext.Sites
			.Where(e => id.HasValue ? e.ParentId == id : e.ParentId == Guid.Empty)
			.OrderBy(e => e.Name)
			.Select(e => new
			{
				id = e.Id,
				name = e.Name,
				image = $"/img/site-type/16x16/{e.SiteType.ToString().ToLower()}.png",
				siteType = e.SiteType,
				parentId = e.ParentId,
				expanded = e.Name.ToLower() == "iot",
				hasChildren = _dbContext.Sites.Where(f => f.ParentId == e.Id).Count() > 0
			})
			.ToList();

		return Json(locations.ToList());
	}


	[HttpGet("{company}/sites/grid/{parentId?}")]
	public async Task<JsonResult> GetLocationGridView(Guid company, Guid parentId)
	{
		var rows = await _dbContext.Sites
			.AsNoTracking()
			.Include(e => e.Permits)
			.Include(e => e.Users)
			.Where(e => e.SiteType == SiteTypeEnum.Site && e.SiteCompany.Id == company && e.ParentId == parentId)
			.Select(e => new
			{
				e.Id,
				e.Name,
				e.Description,
				e.ContactName,
				e.ContactEmail,
				e.IsActive,
				e.ParentId,
				e.SiteType,
				CompanyId = e.SiteCompany.Id,
				HasPermits = e.Permits.Any(),
				HasUsers = e.Users.Any(),
			})
			.ToListAsync();

		var sites = rows.Select(e => new SitesGridViewModel
		{
			Id = e.Id,
			Name = e.Name,
			Description = e.Description,
			ContactName = e.ContactName,
			ContactEmail = e.ContactEmail,
			IsActive = e.IsActive,
			ParentId = e.ParentId,
			SiteType = e.SiteType,
			ActionIcons = GetActionIcons(e.Id, e.CompanyId, e.ParentId, e.HasPermits, e.HasUsers),
		}).ToList();

		return new JsonResult(sites, new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		});
	}


	// Whole location tree in one call, shaped for Treeselect.js (user New/Edit "Locations",
	// Reports "Location" filter): [{ name, value, children: [...] }]. Values are lowercase
	// GUID strings, matching how a user's locations are stored (UserInfoForm.Locations).
	[HttpGet("{company}/sites/tree")]
	public async Task<JsonResult> GetLocationsTree(Guid company, CancellationToken cancellationToken)
	{
		var sites = await _dbContext.Sites
			.AsNoTracking()
			.Where(e => e.SiteType == SiteTypeEnum.Site && e.SiteCompany.Id == company)
			.OrderBy(e => e.Name)
			.Select(e => new { e.Id, e.Name, e.ParentId })
			.ToListAsync(cancellationToken);

		var byParent = sites.ToLookup(e => e.ParentId);
		var visited = new HashSet<Guid>();

		List<object> Build(Guid parentId) => byParent[parentId]
			.Where(e => visited.Add(e.Id)) // guards against a cycle in bad data
			.Select(e => (object)new
			{
				name = e.Name,
				value = e.Id.ToString(),
				children = Build(e.Id),
			})
			.ToList();

		return new JsonResult(Build(Guid.Empty));
	}


	//[HttpGet("{companyId}/sites/{parentId?}")]
	//public IActionResult GetSites(Guid companyId, string parentId = null)
	//{
	//	var sites = _dbContext.Sites
	//		.Include(e => e.SiteCompany)
	//		.Where(e => e.SiteType == SiteTypeEnum.Site && e.SiteCompany.Id == companyId)
	//		.Select(e => new
	//		{
	//			e.Id,
	//			SiteName = e.Name,
	//			SiteDesc = e.Description,
	//			e.ContactName,
	//			e.ContactEmail,
	//			e.IsActive,
	//			e.ParentId
	//		});

	//	if (parentId != null)
	//	{
	//		sites = sites.Where(e => e.ParentId == Guid.Parse(parentId));
	//	}
	//	else
	//	{
	//		sites = sites.Where(e => e.ParentId == Guid.Empty);
	//	}

	//	return Ok(new
	//	{
	//		Data = sites.ToList()
	//	});
	//}


	[HttpGet("{companyId}/sites/all")]
	public ActionResult<IEnumerable<object>> GetAllSites(Guid companyId)
	{
		var locations = _dbContext.Sites
			.Include(e => e.SiteCompany)
			.Where(e => e.SiteType == SiteTypeEnum.Site && e.IsActive == true && e.SiteCompany.Id == companyId)
			.Select(e => new
			{
				Id = e.Id.ToString().ToLower(),
				e.Name
			})
			.ToList();

		return locations;
	}


	[HttpPost("{company}/sites/{parentId?}/new")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> New(SitesNewViewModel model)
	{
		if (!ModelState.IsValid) return View(model);

		var company = _dbContext.Companies.FirstOrDefault(e => e.Id == model.CompanyId);

		Site site = new()
		{
			Name = model.Name,
			Description = model.Description,
			ContactEmail = model.ContactEmail,
			ContactName = model.ContactName,
			Latitude = model.Latitude,
			Longitude = model.Longitude,
			IsActive = model.IsActive,
			ShowInMap = model.ShowInMap,
			SiteType = SiteTypeEnum.Site,
			ParentId = model.ParentId ?? Guid.Empty,
			SiteCompany = company,
		};

		await _dbContext.Sites.AddAsync(site);
		await _dbContext.SaveChangesAsync();

		TempData["SuccessMessage"] = "Site has been created successfully.";

		return RedirectToAction("Index", new { company = model.CompanyId, parentId = model.ParentId });
	}


	[HttpPost("{companyId}/sites")]
	[ValidateAntiForgeryToken]
	public IActionResult ManageSite(Guid companyId)
	{
		var req = Request.Form;

		try
		{
			//SiteTypeEnum siteType;
			Enum.TryParse(req["SiteType"].ToString(), out SiteTypeEnum siteType);

			if (req["Mode"] == "new")
			{
				Site site = new()
				{
					Name = req["Name"],
					Description = req["Description"],
					ContactEmail = req["Email"],
					ContactName = req["Contact"],
					Latitude = Convert.ToDecimal(req["Latitude"]),
					Longitude = Convert.ToDecimal(req["Longitude"]),
					IsActive = Convert.ToBoolean(req["IsActive"]),
					ShowInMap = Convert.ToBoolean(req["ShowInMap"]),
					SiteType = siteType,
					ParentId = Guid.Parse(req["ParentId"]),
					SiteCompany = _dbContext.Companies.FirstOrDefault(e => e.Id == Guid.Parse(req["CompanyId"])),
				};

				_dbContext.Sites.Add(site);
			}
			else
			{
				var site = _dbContext.Sites.FirstOrDefault(x => x.Id == Guid.Parse(req["Id"]));

				if (site != null)
				{
					site.Name = req["Name"];
					site.Description = req["Description"];
					site.ContactEmail = req["Email"];
					site.ContactName = req["Contact"];
					site.Latitude = Convert.ToDecimal(req["Latitude"]);
					site.Longitude = Convert.ToDecimal(req["Longitude"]);
					site.IsActive = Convert.ToBoolean(req["IsActive"]);
					site.ParentId = Guid.Parse(req["ParentId"]);
					site.ShowInMap = Convert.ToBoolean(req["ShowInMap"]);
					site.UpdatedWhen = DateTime.UtcNow.ToUniversalTime();
				}

				_dbContext.Sites.Update(site);
			}

			_dbContext.SaveChanges();

			return Ok(new
			{
				Data = "OK"
			});
		}
		catch (Exception ex)
		{
			return BadRequest(new
			{
				ErrorMessage = ex.Message,
			});
		}
	}


	[HttpDelete("{companyId}/sites/{id}")]
	public async Task<IActionResult> DeleteSite(Guid companyId, Guid id)
	{
		try
		{
			var site = _dbContext.Sites
				.Include(e => e.Permits)
				.FirstOrDefault(x => x.Id == id);

			if (site != null)
			{
				_dbContext.Sites.Remove(site);
				await _dbContext.SaveChangesAsync();

				return Ok(new
				{
					Data = "OK"
				});
			}
			else
			{
				return NotFound(new
				{
					Message = "Unable to find site"
				});
			}

		}
		catch (Exception ex)
		{
			return BadRequest(new
			{
				ErrorMessage = ex.Message,
			});
		}
	}


	[HttpGet("{companyId}/sites/info/{id}")]
	public JsonResult GetSite(Guid companyId, Guid id)
	{
		try
		{
			var siteInfo = _dbContext.Sites
				.Select(e => new
				{
					e.Id,
					e.Name,
					e.Description,
					e.SiteType,
					e.ParentId,
					e.IsActive,
					e.ContactName,
					e.ContactEmail,
					e.Latitude,
					e.Longitude,
					e.ShowInMap,
					Parent = _dbContext.Sites
						.Select(s => new
						{
							s.Id,
							s.Name,
							s.Description,
							s.SiteType,
							s.IsActive,
							s.ContactName,
							s.ContactEmail,
						})
						.FirstOrDefault(s => s.Id == e.ParentId)
				})
				.FirstOrDefault(e => e.Id == id);

			// return Ok(new
			// {
			// 	Data = siteInfo
			// });

			return Json(siteInfo);
		}
		catch (Exception ex)
		{
			var errorMessage = ex.Message;
			return Json(null);
		}
	}


	#region "Map API"

	[HttpGet("{companyId}/sites/map/locations")]
	public JsonResult GetPermitsByLocationAsync(Guid companyId, Guid locationId)
	{
		try
		{
			var sites = _dbContext.Sites
				.Include(e => e.Permits)
				.Where(x => x.Permits.Count > 0 && x.Latitude != null && x.Longitude != null && x.ShowInMap == true && x.SiteCompany.Id == companyId)
				.Select(x => new
				{
					x.Id,
					x.Name,
					x.Latitude,
					x.Longitude,
					Permits = x.Permits.Select(p => new
					{
						p.Id,
						p.PermitNo,
						p.PermitForm,
						p.PermitStatus,
					})
				})
				.ToList();

			return Json(sites);
		}
		catch (Exception ex)
		{
			var errorMessage = ex.Message;
			return Json(null);
		}
	}

	#endregion


	#region Private methods/functions

	public static string GetActionIcons(Guid id, Guid company, Guid parentId, bool hasPermits, bool hasUsers)
	{
		var icons = $"<a href=\"/{company}/sites/{parentId}/edit/{id}\" class=\"no-loading text-secondary me-3\"><i class=\"fa-solid fa-money-check-pen fa-lg\"></i></a>";

		if (hasPermits || hasUsers)
		{
			icons += "<a href=\"javascript:;\" class=\"no-loading text-danger\" data-bs-toggle=\"modal\" data-bs-target=\"#dlgDeleteWarnLoc\"><i class=\"fa-solid fa-trash-xmark fa-lg\"></i></a>";
		}
		else
		{
			icons += $"<a href=\"javascript:;\" onclick=\"deleteItem('{id}')\" class=\"no-loading text-danger\"><i class=\"fa-solid fa-trash-xmark fa-lg\"></i></a>";
		}

		return icons;
	}

	#endregion

}
