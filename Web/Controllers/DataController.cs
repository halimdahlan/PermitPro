#nullable disable


using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PermitPro.App.Models.Ajax;
using PermitPro.App.Models.Charts;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace PermitPro.App.Controllers;

[Route("api/[controller]")]
[Produces("application/json")]
[ApiController]
public class DataController : ControllerBase
{
	private readonly SignInManager<UserInfo> _signInManager;
	private readonly UserManager<UserInfo> _userManager;
	private readonly RoleManager<Role> _roleManager;
	private readonly ILogger<DataController> _logger;
	private readonly IConfiguration _configuration;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private readonly ApplicationDbContext _dbContext;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly PTWSettings _ptwSettings;
	private readonly JsonSerializerOptions _jsonOptions;
	private readonly ICurrentUserService _currentUserService;

	public DataController(
		UserManager<UserInfo> userManager
		, RoleManager<Role> roleManager
		, SignInManager<UserInfo> signInManager
		, ILogger<DataController> logger
		, IConfiguration configuration
		, IHttpContextAccessor httpContextAccessor
		, ApplicationDbContext dbContext
		, IWebHostEnvironment webHostEnvironment
		, PTWSettings ptwSettings
		, ICurrentUserService currentUserService)
	{
		_userManager = userManager;
		_roleManager = roleManager;
		_signInManager = signInManager;
		_logger = logger;
		_configuration = configuration;
		_httpContextAccessor = httpContextAccessor;
		_dbContext = dbContext;
		_webHostEnvironment = webHostEnvironment;
		_ptwSettings = ptwSettings;
		_currentUserService = currentUserService;

		_jsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		};
	}


	#region "Users"

	[HttpGet("{company}/workflowusers/all")]
	public ActionResult<IEnumerable<object>> GetWorkflowUsers(Guid company)
	{
		var users = _dbContext.Users
			.Include(e => e.UserCompany)
			.Include(e => e.UserRoles)
			.ThenInclude(e => e.Role)
			.Where(e => e.UserRoles.Any(f => f.Role.NormalizedName == "PERMITISSUER" || f.Role.NormalizedName == "LEADPERMITISSUER" || f.Role.NormalizedName == "PORTALADMIN") && e.IsActive && e.UserCompany.Id == company)
			.OrderBy(e => e.FirstName)
			.Select(e => new
			{
				e.Id,
				e.FirstName,
				e.LastName,
				e.Email,
			})
			.ToList();

		return Ok(new
		{
			Data = users
		});
	}


	[HttpGet("{company}/users/{searchBy?}/value/{searchValue?}")]
	public ActionResult GetUsers(Guid company, string searchBy = "all", string searchValue = null)
	{
		var siteUsers = _dbContext.Users
			.Include(e => e.UserCompany)
			.Include(e => e.UserRoles)
			.ThenInclude(e => e.Role)
			.Where(e => e.UserRoles.Any(e => e.Role.NormalizedName != "SUPERUSER") && e.UserCompany.Id == company)
			.ToList();

		if (searchBy != "all")
		{
			if (searchBy == "isActive")
			{
				siteUsers = siteUsers.Where(e => e.IsActive == Convert.ToBoolean(searchValue)).ToList();
			}
			else
			{
				siteUsers = siteUsers.Where(e => e.UserRoles.Any(e => e.RoleId == searchValue)).ToList();
			}
		}

		var users = siteUsers
			.OrderByDescending(e => e.CreatedWhen)
			.Select(e => new
			{
				e.Id,
				Name = string.Format("{0} {1}", e.FirstName, e.LastName).Trim(),
				e.Email,
				IsSecured = e.PasswordHash != null,
				e.IsActive,
				Sites = string.Join(", ", _dbContext.Sites.Include(site => site.Users).Where(site => site.Users.Select(user => user.Id).Contains(e.Id)).Select(x => x.Name).ToList()),
				Roles = string.Join(", ", e.UserRoles.Select(role => role.Role.Name).ToList()),
				e.Designation,
				CreatedWhen = GeneralHelper.GetDateInTimeZone(e.CreatedWhen).ToString("dd MMM, yyyy"),
				UpdatedWhen = e.UpdatedWhen?.ToLocalTime().ToString("dd MMM, yyyy"),
				CreatedWhenTicks = FormatDateTimeTicks(GeneralHelper.GetDateInTimeZone(e.CreatedWhen)),
				UpdatedWhenTicks = FormatDateTimeTicks(e.UpdatedWhen),
			})
			.ToList();

		return Ok(new
		{
			Data = users
		});
	}


	[HttpGet("{company}/users/{id}/info")]
	public async Task<IActionResult> GetUser(string id)
	{
		try
		{
			var user = await _dbContext.Users
				.Include(e => e.UserRoles)
				.ThenInclude(e => e.Role)
				.FirstOrDefaultAsync(e => e.Id == id);

			if (user == null)
			{
				return NotFound();
			}

			return Ok(new
			{
				User = new
				{
					user.Id,
					user.FirstName,
					user.LastName,
					user.Designation,
					user.Email,
					user.IsActive,
					IsSecured = user.PasswordHash != null,
					Roles = user.UserRoles.Select(e => new { e.RoleId, e.Role.Name }).ToList(),
					Sites = _dbContext.Sites.Include(site => site.Users)
								.Where(site => site.Users.Select(user => user.Id).Contains(user.Id))
								.Select(e => new { e.Id, e.Name }).ToList(),
				}
			});
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}


	[HttpPost("{company}/users/{mode}")]
	public async Task<IActionResult> ManageUser(Guid company, [FromBody] AjaxUserModel request, string mode = "new")
	{
		try
		{
			var companyInfo = _dbContext.Companies.SingleOrDefault(f => f.Id == company);

			if (mode == "new")
			{
				var userInfo = new UserInfo
				{
					FirstName = request.FirstName,
					LastName = request.LastName,
					Email = request.Email,
					IsActive = request.IsActive,
					UserName = request.Email,
					Designation = request.Designation,
					NormalizedUserName = request.Email.ToUpper(),
					NormalizedEmail = request.Email.ToUpper(),
					EmailConfirmed = true,
					UserCompany = companyInfo,
					CreatedBy = Guid.Parse(_httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)),
					CreatedWhen = DateTime.UtcNow.ToUniversalTime(),
				};

				_dbContext.Users.Add(userInfo);
				_dbContext.SaveChanges();

				// Add user to selected role
				var role = _roleManager.Roles.FirstOrDefault(e => e.Id == request.Role);
				await _userManager.AddToRoleAsync(userInfo, role.NormalizedName);

				// Add user to selected site(s)
				foreach (var location in request.Locations)
				{
					var loc = _dbContext.Sites.FirstOrDefault(e => e.Id == Guid.Parse(location));
					userInfo.Sites.Add(loc);
				}

				_dbContext.SaveChanges();

				// Set user password if any
				if (request.Password != null && request.Password != "")
				{
					userInfo.PasswordHash = _userManager.PasswordHasher.HashPassword(userInfo, request.Password);
					await _userManager.UpdateAsync(userInfo);
				}

			}

			if (mode == "edit")
			{
				var userInfo = await _dbContext.Users
					.Include(e => e.UserRoles)
					.ThenInclude(e => e.Role)
					.Include(e => e.Sites)
					.FirstOrDefaultAsync(e => e.Id == request.Id);

				if (userInfo == null)
				{
					return NotFound("Unable to find user");
				}

				var oldUserRole = userInfo.UserRoles.FirstOrDefault(e => e.User == userInfo);

				userInfo.FirstName = request.FirstName;
				userInfo.LastName = request.LastName;
				userInfo.Email = request.Email;
				userInfo.Designation = request.Designation;
				userInfo.NormalizedEmail = request.Email.ToUpper();
				userInfo.IsActive = request.IsActive;
				userInfo.UpdatedWhen = DateTime.UtcNow.ToUniversalTime();
				userInfo.UpdatedBy = Guid.Parse(_httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier));

				await _userManager.UpdateAsync(userInfo);

				// Update user's role
				await _userManager.RemoveFromRoleAsync(userInfo, oldUserRole.Role.NormalizedName);

				var role = _roleManager.Roles.FirstOrDefault(e => e.Id == request.Role);
				await _userManager.AddToRoleAsync(userInfo, role.NormalizedName);


				// Update user's site(s)
				var sitesToRemove = userInfo.Sites.ToList();

				sitesToRemove.ForEach(site =>
				{
					userInfo.Sites.Remove(site);
				});

				_dbContext.SaveChanges();


				foreach (var location in request.Locations)
				{
					var loc = _dbContext.Sites.FirstOrDefault(e => e.Id == Guid.Parse(location));
					userInfo.Sites.Add(loc);
				}

				_dbContext.SaveChanges();
			}

			return Ok(new
			{
				Data = "OK"
			});
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}

	}


	[HttpPost("{company}/users/{id}/setpassword")]
	public async Task<IActionResult> SetUserPassword(Guid company, [FromBody] AjaxUserPasswordModel request, string id)
	{
		try
		{
			var user = await _userManager.FindByIdAsync(id);

			if (user == null)
			{
				return NotFound("Unable to find user");
			}

			user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, request.Password);
			await _userManager.UpdateAsync(user);

			//var result = await _userManager.ChangePasswordAsync(user, "", request.Password);

			return Ok(new
			{
				Data = "OK"
			});
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}


	[HttpDelete("{company}/users/{id}")]
	public async Task<IActionResult> DeleteUser(Guid company, string id)
	{
		try
		{
			var user = await _userManager.FindByIdAsync(id);

			if (user == null)
			{
				return NotFound();
			}

			await _userManager.DeleteAsync(user);

			return Ok(new
			{
				Data = "OK"
			});
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}

	#endregion


	#region "Roles"

	[HttpGet("roles/{id?}")]
	public async Task<IActionResult> GetRoleById(string id)
	{
		var role = await _dbContext.Roles.FirstOrDefaultAsync(e => e.Id == id);

		if (role == null)
		{
			return NotFound();
		}

		return Ok(new
		{
			role.Name,
			Description = ""
		});
	}


	[HttpPost("roles/{mode?}")]
	public async Task<IActionResult> ManageRole([FromBody] AjaxRoleModel request, string mode = "new")
	{
		try
		{
			if (mode == "new")
			{
				var normalizedName = request.Name.ToUpper();
				var existingRole = await _dbContext.Roles.FirstOrDefaultAsync(e => e.NormalizedName == normalizedName);

				if (existingRole == null)
				{
					var role = new Role
					{
						Name = request.Name,
						ConcurrencyStamp = Guid.NewGuid().ToString(),
					};

					await _roleManager.CreateAsync(role);

					return Ok(new
					{
						Message = "Successfully created role."
					});
				}
				else
				{
					return BadRequest(new
					{
						ErrorMessage = "Found existing role. Unable to proceed."
					});
				}
			}
			else
			{
				var role = await _roleManager.FindByIdAsync(request.Id);

				if (role == null)
				{
					return NotFound();
				}

				role.Name = request.Name;
				await _roleManager.UpdateAsync(role);

				return Ok(new
				{
					Message = "Successfully updated role."
				});
			}
		}
		catch (Exception ex)
		{
			return BadRequest(new
			{
				ErrorMessage = ex.Message
			});
		}
	}

	#endregion


	#region "Contractors"

	[HttpGet("{company}/contractors/{filterBy?}/value/{filterValue?}")]
	public IActionResult GetContractors(Guid company, string filterBy = "all", string filterValue = null)
	{
		var contractors = _dbContext.Users
			.Include(e => e.UserCompany)
			.Include(e => e.UserRoles)
			.ThenInclude(e => e.Role)
			.Where(e => e.UserCompany.Id == company)
			.ToList();

		if (filterBy != "all")
		{
			if (filterBy == "isActive")
			{
				contractors = contractors.Where(e => e.IsActive == Convert.ToBoolean(filterValue)).ToList();
			}
		}

		var users = contractors
			.Where(e => e.UserRoles.Any(e => e.Role.NormalizedName == "CONTRACTOR"))
			.OrderByDescending(e => e.CreatedWhen)
			.Select(e => new
			{
				e.Id,
				Name = string.Format("{0} {1}", e.FirstName, e.LastName).Trim(),
				e.Email,
				IsSecured = e.PasswordHash != null,
				e.IsActive,
				Sites = string.Join(", ", _dbContext.Sites.Include(site => site.Users).Where(site => site.Users.Select(user => user.Id).Contains(e.Id)).Select(x => x.Name).ToList()),
				CreatedWhen = GeneralHelper.GetDateInTimeZone(e.CreatedWhen).ToString("dd MMM, yyyy"),
				UpdatedWhen = e.UpdatedWhen?.ToLocalTime().ToString("dd MMM, yyyy"),
				CreatedWhenTicks = FormatDateTimeTicks(GeneralHelper.GetDateInTimeZone(e.CreatedWhen)),
				UpdatedWhenTicks = FormatDateTimeTicks(e.UpdatedWhen),
			})
			.ToList();

		return Ok(new
		{
			Data = users
		});
	}


	[HttpGet("{company}/contractors/grid")]
	public JsonResult ContractorsGridView(Guid company)
	{
		var contractors = _dbContext.Users
			.Include(e => e.UserCompany)
			.Include(e => e.UserRoles)
			.ThenInclude(e => e.Role)
			.Include(e => e.Sites)
			.Where(e => e.UserCompany.Id == company && e.UserRoles.Any(ur => ur.Role.NormalizedName == "CONTRACTOR"))
			.OrderByDescending(e => e.CreatedWhen)
			.Select(e => new ContractorListViewModel
			{
				Id = e.Id,
				Name = string.Format("{0} {1}", e.FirstName.Trim(), e.LastName.Trim()),
				Email = e.Email,
				Location = string.Join(", ", e.Sites.Select(s => s.Name).ToList()),
				IsSecured = e.PasswordHash != null ? "<i class=\"fa-regular fa-user-lock fa-lg\"></i>" : "<i class=\"fa-regular fa-user-unlock fa-lg\"></i>",
				IsActiveIcons = e.IsActive ? "<i class=\"fa-sharp fa-solid fa-circle-check fa-lg text-success\"></i>" : "<i class=\"fa-sharp fa-solid fa-circle-check fa-lg text-warning\"></i>",
				IsActive = e.IsActive,
				CreatedWhen = GeneralHelper.GetDateInTimeZone(e.CreatedWhen),
				ActionIcons = ContractorsGridActions(e.Id, e.PasswordHash != null),
			});

		return new JsonResult(contractors, _jsonOptions);
	}

	#endregion


	#region "Locations/Sites"

	[HttpGet("{company}/sites/grid/{parentId?}")]
	public JsonResult GetLocationGridView(Guid company, Guid parentId)
	{
		var data = _dbContext.Sites
			.Include(e => e.SiteCompany)
			.Where(e => e.SiteType == SiteTypeEnum.Site && e.SiteCompany.Id == company && e.ParentId == parentId);

		//if (parentId != Guid.Empty)
		//{
		//	data = data.Where(e => e.ParentId == parentId);
		//}

		var sites = data
			.Select(e => new SitesGridViewModel
			{
				Id = e.Id,
				Name = e.Name,
				Description = e.Description,
				ContactName = e.ContactName,
				ContactEmail = e.ContactEmail,
				IsActive = e.IsActive,
				ParentId = e.ParentId,
			});


		return new JsonResult(sites, _jsonOptions);
	}


	[HttpGet("{company}/sites/hierarchical")]
	public JsonResult GetLocationsHierarchical(Guid company, Guid? id)
	{
		Guid? parentId = Guid.Empty;
		if (id != null) parentId = id;

		var data = _dbContext.Sites
			.Include(e => e.SiteCompany)
			.Where(e => e.SiteType == SiteTypeEnum.Site && e.SiteCompany.Id == company && e.ParentId == parentId)
			.OrderBy(e => e.ParentId)
			.OrderBy(e => e.Name)
			.Select(e => new
			{
				id = e.Id,
				name = e.Name,
				hasChildren = _dbContext.Sites.Any(f => f.SiteType == SiteTypeEnum.Site && f.SiteCompany.Id == company && f.ParentId == e.Id)
			});

		return new JsonResult(data, _jsonOptions);
	}


	[HttpGet("{companyId}/sites/{parentId?}")]
	public IActionResult GetSites(Guid companyId, string parentId = null)
	{
		var sites = _dbContext.Sites
			.Include(e => e.SiteCompany)
			.Where(e => e.SiteType == SiteTypeEnum.Site && e.SiteCompany.Id == companyId)
			.Select(e => new
			{
				e.Id,
				SiteName = e.Name,
				SiteDesc = e.Description,
				e.ContactName,
				e.ContactEmail,
				e.IsActive,
				e.ParentId
			});

		if (parentId != null)
		{
			sites = sites.Where(e => e.ParentId == Guid.Parse(parentId));
		}
		else
		{
			sites = sites.Where(e => e.ParentId == Guid.Empty);
		}

		return Ok(new
		{
			Data = sites.ToList()
		});
	}


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


	[HttpPost("{companyId}/sites")]
	public IActionResult AddSite(Guid companyId, [FromBody] AjaxSiteModel request)
	{
		try
		{
			if (request.Mode == "new")
			{
				Site site = new()
				{
					Name = request.Name,
					Description = request.Description,
					ContactEmail = request.Email,
					ContactName = request.Contact,
					IsActive = request.IsActive,
					SiteType = (SiteTypeEnum)request.SiteType,
					ParentId = Guid.Parse(request.ParentId),
					SiteCompany = _dbContext.Companies.FirstOrDefault(e => e.Id == Guid.Parse(request.CompanyId)),
				};

				_dbContext.Sites.Add(site);
			}
			else
			{
				var site = _dbContext.Sites.FirstOrDefault(x => x.Id == Guid.Parse(request.Id));

				if (site != null)
				{
					site.Name = request.Name;
					site.Description = request.Description;
					site.ContactEmail = request.Email;
					site.ContactName = request.Contact;
					site.IsActive = request.IsActive;
					site.ParentId = Guid.Parse(request.ParentId);
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


	[HttpPost("{companyId}/sites/{id}")]
	public ActionResult GetSite(Guid companyId, Guid id)
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

			return Ok(new
			{
				Data = siteInfo
			});
		}
		catch (Exception ex)
		{
			return NotFound(ex.Message);
		}
	}

	#endregion


	#region "Workflow"

	#region "GET"

	[HttpGet("{company}/workflows")]
	public IActionResult GetWorkflows(Guid company)
	{
		var workflows = _dbContext.Workflows
			.Include(e => e.WorkflowCompany)
			.Where(e => e.WorkflowCompany.Id == company)
			.OrderByDescending(e => e.CreatedWhen)
			.Select(e => new
			{
				e.Id,
				WorkflowName = e.Name,
				WorkflowDesc = e.Description,
				e.IsActive,
				e.HasCertificate,
				CreatedWhen = GeneralHelper.GetDateInTimeZone(e.CreatedWhen).ToString("dd MMM, yyyy"),
				UpdatedWhen = e.UpdatedWhen.Value.ToLocalTime().ToString("dd MMM, yyyy"),
				CreatedWhenTicks = FormatDateTimeTicks(GeneralHelper.GetDateInTimeZone(e.CreatedWhen)),
				UpdatedWhenTicks = FormatDateTimeTicks(e.UpdatedWhen),
			})
			.ToList();

		return Ok(new
		{
			Data = workflows
		});
	}


	[HttpGet("{company}/workflows/{workflowId}/steps")]
	public ActionResult GetWorkflowSteps(Guid company, Guid workflowId)
	{
		var workflowSteps = _dbContext.WorkflowSteps
			.Where(e => e.WorkflowStepWorkflow.Id == workflowId)
			.OrderBy(e => e.StepOrder)
			.Select(e => new
			{
				e.Id,
				e.Name,
				e.Description,
				e.Duration,
				DurationType = e.DurationType.ToString(),
				e.StepOrder,
				e.IsFirst,
				e.IsLast,
				NumOfSteps = _dbContext.WorkflowSteps.Count(x => x.WorkflowStepWorkflow.Id == workflowId)
			})
			.ToList();

		return Ok(new
		{
			Data = workflowSteps
		});
	}


	[HttpGet("{company}/workflowSteps/{id}")]
	public async Task<ActionResult<object>> GetWorkflowStepById(Guid company, Guid id)
	{
		var workflowStep = await _dbContext.WorkflowSteps
			.Include(e => e.Approvers)
			.Select(e => new
			{
				e.Id,
				e.Name,
				e.Description,
				e.Duration,
				e.DurationType,
				e.StepOrder,
				e.IsFirst,
				e.IsLast,
				e.AllowDelete,
				e.AllowMove,
				e.CreatedBy,
				CreatedWhen = GeneralHelper.GetDateInTimeZone(e.CreatedWhen).ToString("dd/MM/yyyy"),
				e.UpdatedBy,
				UpdatedWhen = e.UpdatedWhen.Value.ToLocalTime().ToString("dd/MM/yyyy"),
				CreateWhenTicks = FormatDateTimeTicks(GeneralHelper.GetDateInTimeZone(e.CreatedWhen)),
				UpdatedWhenTicks = FormatDateTimeTicks(e.UpdatedWhen),
				Approvers = e.Approvers
					.Select(f => new
					{
						f.Id,
						f.FirstName,
						f.LastName,
						f.Email
					})
					.ToList()
			})
			.FirstOrDefaultAsync(e => e.Id == id);

		return workflowStep;
	}


	[HttpGet("{company}/workflows/grid")]
	public JsonResult GetWorkflowsGrid(Guid company)
	{
		var workflows = _dbContext.Workflows
			.Include(e => e.WorkflowCompany)
			.Where(e => e.WorkflowCompany.Id == company)
			.OrderByDescending(e => e.CreatedWhen)
			.Select(e => new WorkflowGridViewModel
			{
				Id = e.Id,
				Name = e.Name,
				Description = e.Description,
				IsActive = e.IsActive,
				HasCertificates = e.HasCertificate,
				CreatedWhen = GeneralHelper.GetDateInTimeZone(e.CreatedWhen),
				ActionIcons = WorkflowsGridActionIcons(e.Id.ToString(), company),
			})
			.ToList();

		return new JsonResult(workflows, _jsonOptions);
	}

	#endregion


	#region "POST"

	[HttpPost("{company}/workflows")]
	public async Task<IActionResult> CreateWorkflow(Guid company, AjaxWorkflowRequest request)
	{
		try
		{
			var comp = _dbContext.Companies.SingleOrDefault(e => e.Id == company);

			var workflow = new Workflow
			{
				Name = request.Name,
				Description = request.Description,
				IsActive = request.IsActive,
				HasCertificate = request.HasCertificate,
				WorkflowCompany = comp,
			};

			var draftStep = new WorkflowStep
			{
				Name = "Draft",
				Description = "Draft",
				AllowDelete = false,
				AllowMove = false,
				StepOrder = 0,
				IsFirst = true,
				IsLast = false,
				WorkflowStepWorkflow = workflow,
			};

			workflow.WorkflowSteps.Add(draftStep);

			var doneStep = new WorkflowStep
			{
				Name = "Completed",
				Description = "Completed",
				AllowDelete = false,
				AllowMove = false,
				StepOrder = 100,
				IsFirst = false,
				IsLast = true,
				WorkflowStepWorkflow = workflow,
			};

			workflow.WorkflowSteps.Add(doneStep);

			_dbContext.Workflows.Add(workflow);

			await _dbContext.SaveChangesAsync();

			return Ok(new
			{
				HasError = false,
				WorkflowId = workflow.Id,
			});
		}
		catch (Exception ex)
		{
			return BadRequest(new
			{
				HasError = true,
				ErrorMessage = ex.Message
			});
		}

	}


	[HttpPost("{company}/workflows/{workflowId}/steps")]
	public async Task<ActionResult> CreateWorkflowStep(Guid company, AjaxWorkflowStepRequest request, Guid workflowId)
	{
		try
		{
			var workflow = _dbContext.Workflows.SingleOrDefault(e => e.Id == workflowId);
			var currentStep = _dbContext.WorkflowSteps
				.Where(e => e.WorkflowStepWorkflow == workflow && !e.IsLast)
				.Max(e => e.StepOrder);

			currentStep++;

			var workflowStep = new WorkflowStep
			{
				Name = request.Name,
				Description = request.Description,
				Duration = request.Duration,
				DurationType = (DurationTypeEnum)request.DurationType,
				StepOrder = currentStep,
				AllowDelete = true,
				AllowMove = true,
				IsFirst = false,
				IsLast = false,
				WorkflowStepWorkflow = workflow
			};

			_dbContext.WorkflowSteps.Add(workflowStep);
			await _dbContext.SaveChangesAsync();

			return Ok(new
			{
				HasError = false
			});
		}
		catch (Exception ex)
		{
			return Ok(new
			{
				HasError = true,
				ErrorMessage = ex.Message
			});
		}

	}



	#endregion


	#region "PUT"

	[HttpPut("{company}/workflows/{id}/edit")]
	public async Task<IActionResult> UpdateWorkflow(Guid company, AjaxWorkflowRequest request, Guid id)
	{
		try
		{
			var workflow = _dbContext.Workflows.SingleOrDefault(e => e.Id == id);

			workflow.Name = request.Name;
			workflow.Description = request.Description;
			workflow.IsActive = request.IsActive;
			workflow.HasCertificate = request.HasCertificate;

			_dbContext.Workflows.Update(workflow);
			await _dbContext.SaveChangesAsync();

			return Ok(new
			{
				HasError = false,
				WorkflowId = workflow.Id,
			});
		}
		catch (Exception ex)
		{
			return Ok(new
			{
				HasError = true,
				ErrorMessage = ex.Message
			});
		}

	}


	[HttpPut("{company}/workflowsteps/{id}/approvers")]
	public async Task<ActionResult> UpdateWorkflowStepApprovers(Guid company, AjaxWorkflowStepApproverRequest request, Guid id)
	{
		var workflowStep = _dbContext.WorkflowSteps
			.Include(e => e.Approvers)
			.SingleOrDefault(e => e.Id == id);

		if (workflowStep != null)
		{
			workflowStep.Approvers.Clear();

			foreach (var userId in request.Approvers)
			{
				var user = _dbContext.Users.SingleOrDefault(u => u.Id == userId);

				if (user != null)
				{
					workflowStep.Approvers.Add(user);
				}
			}

			_dbContext.WorkflowSteps.Update(workflowStep);
			await _dbContext.SaveChangesAsync();
		}

		return Ok(new
		{
			Data = "OK"
		});
	}


	[HttpPut("{company}/workflows/{workflowId}/steps/{stepId}/move")]
	public async Task<ActionResult> StepMove(Guid company, AjaxStepMoveRequest request, Guid workflowId, Guid stepId)
	{
		var workflowStep = _dbContext.WorkflowSteps.SingleOrDefault(e => e.Id == stepId);

		if (workflowStep != null)
		{
			int moveTo = 0;

			if (request.MoveUp)
			{
				moveTo = request.Previous;
			}
			else
			{
				moveTo = request.Next;
			}

			var stepToMove = _dbContext.WorkflowSteps.SingleOrDefault(e => e.WorkflowStepWorkflow.Id == workflowId && e.StepOrder == moveTo);

			stepToMove.StepOrder = request.Current;
			_dbContext.Update(stepToMove);

			workflowStep.StepOrder = moveTo;
			_dbContext.Update(workflowStep);

			await _dbContext.SaveChangesAsync();

			return Ok(new
			{
				HasError = false
			});
		}

		return NotFound();
	}


	[HttpPut("{company}/workflows/steps")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> UpdateWorkflowStep(Guid company, AjaxWorkflowStepRequest request)
	{
		try
		{
			var workflowStep = _dbContext.WorkflowSteps.SingleOrDefault(e => e.Id == request.Id);

			if (workflowStep == null)
			{
				return BadRequest("Workflow step not found");
			}

			workflowStep.Name = request.Name;
			workflowStep.Description = request.Description;
			workflowStep.Duration = request.Duration;
			workflowStep.DurationType = (DurationTypeEnum)request.DurationType;

			_dbContext.WorkflowSteps.Update(workflowStep);
			await _dbContext.SaveChangesAsync();

			return Ok(new
			{
				Success = true,
			});
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}

	#endregion


	#region "DELETE"

	[HttpDelete("{company}/workflows/{id}")]
	public async Task<ActionResult> DeleteWorkflow(Guid company, Guid id)
	{
		try
		{
			var workflow = _dbContext.Workflows.SingleOrDefault(e => e.Id == id);

			_dbContext.Workflows.Remove(workflow);
			await _dbContext.SaveChangesAsync();

			return Ok(new
			{
				HasError = false
			});
		}
		catch (Exception ex)
		{
			return Ok(new
			{
				HasError = true,
				ErrorMessage = ex.Message
			});
		}
	}


	[HttpDelete("{company}/workflows/{workflowId}/steps/{stepId}")]
	public async Task<ActionResult> DeleteWorkflowStep(Guid company, Guid workflowId, Guid stepId)
	{
		try
		{
			var workflowStep = _dbContext.WorkflowSteps.SingleOrDefault(e => e.Id == stepId);
			_dbContext.WorkflowSteps.Remove(workflowStep);

			await _dbContext.SaveChangesAsync();

			// Get remaining workflow steps to re-arrange order
			var workflowSteps = _dbContext.WorkflowSteps
				.Where(e => e.WorkflowStepWorkflow.Id == workflowId && e.StepOrder != 0 && e.StepOrder != 100)
				.Select(e => new
				{
					e.Id
				})
				.ToList();

			int counter = 1;

			foreach (var step in workflowSteps)
			{
				workflowStep = _dbContext.WorkflowSteps.SingleOrDefault(e => e.Id == step.Id);
				workflowStep.StepOrder = counter;

				_dbContext.WorkflowSteps.Update(workflowStep);

				counter++;
			}

			await _dbContext.SaveChangesAsync();

			return Ok(new
			{
				HasError = false
			});
		}
		catch (Exception ex)
		{
			return Ok(new
			{
				HasError = true,
				ErrorMessage = ex.Message
			});
		}
	}


	[HttpDelete("{company}/workflowsteps/{workflowStepId}/approvers/{userId}")]
	public async Task<ActionResult> DeleteWorkflowStepApprover(Guid company, Guid workflowStepId, string userId)
	{
		var workflowStep = _dbContext.WorkflowSteps
			.Include(e => e.Approvers)
			.SingleOrDefault(e => e.Id == workflowStepId);

		var user = await _userManager.FindByIdAsync(userId);

		workflowStep.Approvers.Remove(user);

		_dbContext.WorkflowSteps.Update(workflowStep);
		await _dbContext.SaveChangesAsync();

		return Ok();
	}

	#endregion

	#endregion


	#region "Notifications"

	[HttpPut("{company}/notifications/read")]
	public async Task<IActionResult> UpdateNotificationAsRead(AjaxNotificationRequest request)
	{
		if (request.Selected != null && request.Selected != string.Empty)
		{
			var tmp = request.Selected.Split(";");

			List<Notification> markAsRead = new();
			List<string> markSuccess = new();

			foreach (var item in tmp)
			{
				var notification = _dbContext.Notifications.SingleOrDefault(e => e.Id.ToString().ToLower() == item.ToLower());
				notification.IsRead = true;

				markAsRead.Add(notification);

				markSuccess.Add(item);
			}

			_dbContext.Notifications.UpdateRange(markAsRead);
			await _dbContext.SaveChangesAsync();

			return Ok(new
			{
				Data = "OK",
				MarkedItems = string.Join(";", markSuccess),
			});
		}

		return BadRequest();
	}

	#endregion


	#region "Reports"

	#region "GET"
	#endregion

	#region "POST"
	#endregion

	#region "PUT"
	#endregion

	#region "DELETE"
	#endregion

	#endregion


	#region "Charts"

	[HttpGet("{company}/dashboard/charts/donut/permit/status")]
	public JsonResult GetDashboardDonutChartData(Guid company, string startDate, string endDate)
	{
		var allPermits = _dbContext.Permits
			.Where(e => e.Company.Id == company)
			.Count();

		var chartData = _dbContext.Permits
			.Where(e => e.Company.Id == company);

		if (startDate != null && endDate != null)
		{
			var rangeStart = DateTime.Parse(startDate);
			var rangeEnd = DateTime.Parse(endDate);

			allPermits = _dbContext.Permits
			.Where(e => e.Company.Id == company && (e.CreatedWhen >= rangeStart && e.CreatedWhen <= rangeEnd))
			.Count();

			chartData = _dbContext.Permits
			.Where(e => e.Company.Id == company && (e.CreatedWhen >= rangeStart && e.CreatedWhen <= rangeEnd));
		}

		var groups = chartData
			.GroupBy(e => e.PermitStatus)
			.Select(e => new
			{
				Category = e.Key.ToString().ToUpper(),
				Value = allPermits > 0 ? string.Format("{0:N1}", ((float)e.Count() / (float)allPermits) * 100) : "0.0",
				Count = e.Count(),
				Color = GetCategoryColor(e.Key),
			})
			.ToList();

		return new JsonResult(groups, _jsonOptions);
	}


	[HttpGet("{company}/dashboard/charts/bar/permit/location")]
	public IActionResult GetDashboardBarChartDataByLocation(Guid company, string startDate, string endDate)
	{
		try
		{
			var allPermits = _dbContext.Permits
				.Where(e => e.Company.Id == company)
				.Count();

			var chartData = _dbContext.Permits
				.Include(e => e.Site)
				.Where(e => e.Company.Id == company && e.Site != null);

			if (startDate != null && endDate != null)
			{
				var rangeStart = DateTime.Parse(startDate);
				var rangeEnd = DateTime.Parse(endDate);

				allPermits = _dbContext.Permits
				.Where(e => e.Company.Id == company && (e.CreatedWhen >= rangeStart && e.CreatedWhen <= rangeEnd))
				.Count();

				chartData = _dbContext.Permits
				.Where(e => e.Company.Id == company && (e.CreatedWhen >= rangeStart && e.CreatedWhen <= rangeEnd));
			}

			var groups = chartData
				.GroupBy(e => e.Site.Name)
				.Select(e => new BarChartModel
				{
					Category = e.Key.ToString(),
					TotalClosed = e.Where(f => f.PermitStatus == PermitStatusEnum.Closed).Count(),
					TotalPending = e.Where(f => f.PermitStatus == PermitStatusEnum.Pending).Count(),
					TotalActive = e.Where(f => f.PermitStatus != PermitStatusEnum.KIV && f.PermitStatus != PermitStatusEnum.Closed && f.PermitStatus != PermitStatusEnum.Suspended).Count(),
				})
				.ToList();

			return new JsonResult(groups, _jsonOptions);
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}

	#endregion


	#region "Scheduled Tasks"

	#endregion


	#region "Private functions"


	private string GetUserRoles(UserInfo user)
	{
		var roles = string.Empty;

		roles = string.Join(", ", _userManager.GetRolesAsync(user).Result.ToList());

		return roles;
	}


	private static string GetUserSites(string userId)
	{
		var sites = string.Empty;

		return sites;
	}


	private static string GetDisplayName(string firstName, string lastName)
	{
		var name = string.Format("{0} {1}", firstName, lastName);
		return name.Trim();
	}


	private static long FormatDateTimeTicks(DateTime? date)
	{
		long formatted = 0;

		if (date.HasValue)
		{
			formatted = date.Value.Ticks;
		}

		return formatted;
	}


	private static string GetStatusBadge(string status)
	{
		var statusBg = "primary";

		if (status == "PENDING" || status == "DRAFT") statusBg = "secondary";
		if (status == "APPROVED") statusBg = "success";
		if (status == "REJECTED") statusBg = "danger";
		if (status == "SUSPENDED" || status == "KIV") statusBg = "warning";
		if (status == "CLOSED") statusBg = "info";

		return $"<span class=\"badge fs-5 bg-{statusBg}\">{status}</span>";
	}


	private static string ContractorsGridActions(string id, bool isSecured)
	{
		var icons = string.Empty;
		var disabled = isSecured ? " disabled" : "";

		StringBuilder sb = new();

		sb.AppendLine("<div class=\"d-flex flex-row action-icons\">");
		sb.AppendLine("<a href=\"javascript:;\" onclick=\"editUser('" + id + "')\" class=\"no-loading text-secondary\"><i class=\"fa-solid fa-money-check-pen fa-lg\"></i></a>");
		sb.AppendLine("<a href=\"javascript:;\" class=\"no-loading text-danger\" onclick=\"deleteUser('" + id + "')\"><i class=\"fa-solid fa-trash-xmark fa-lg\"></i></a>");
		sb.AppendLine("<a href=\"#\" class=\"no-loading text-secondary\" data-bs-toggle=\"dropdown\"><i class=\"fa-solid fa-gear fa-lg\"></i></a>");
		sb.AppendLine("<ul class=\"dropdown-menu\">");
		sb.AppendLine("<li class=\"dropdown-item" + disabled + "\">");

		if (isSecured)
		{
			sb.AppendLine("Set password");
		}
		else
		{
			sb.AppendLine("<a href=\"javascript:;\" class=\"no-loading\" onclick=\"setPassword(this)\" data-user-id=\"" + id + "\">Set password</a>");
		}

		sb.AppendLine("</li>");
		sb.AppendLine("</ul>");
		sb.AppendLine("</div>");


		icons = sb.ToString();

		return icons;
	}


	private static string WorkflowsGridActionIcons(string id, Guid companyId)
	{
		var icons = string.Empty;

		icons += "<div class=\"d-flex flex-row action-icons justify-content-center\">";
		icons += $"<a href=\"/{companyId}/workflow/edit/{id}\" class=\"no-loading text-secondary\"><i class=\"fa-solid fa-money-check-pen fa-lg\"></i></a>";
		icons += $"<a href=\"javascript:;\" class=\"no-loading text-danger\" onclick=\"deleteWorkflow('{id}')\"><i class=\"fa-solid fa-trash-xmark fa-lg\"></i></a>";
		icons += "</div>";

		return icons;
	}


	private static string GetCategoryColor(PermitStatusEnum permitStatus)
	{
		var color = "#000000";

		switch (permitStatus)
		{
			case PermitStatusEnum.Approved:
				color = "#00d97e";
				break;
			case PermitStatusEnum.Closed:
				color = "#39afd1";
				break;
			case PermitStatusEnum.Pending:
				color = "#f6c343";
				break;
			case PermitStatusEnum.Rejected:
				color = "#e63757";
				break;
			case PermitStatusEnum.Draft:
				color = "#899bb4";
				break;
		}

		return color;
	}

	#endregion

}
