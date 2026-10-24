#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using PermitPro.App.Controllers.Base;
using PermitPro.App.Models.Ajax;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

using System.Security.Claims;
using System.Text.Json;

namespace PermitPro.App.Controllers;

[Authorize]
public class UsersController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private readonly SignInManager<UserInfo> _signInManager;
	private readonly ISystemConfigurationService _systemConfiguration;
	private readonly UserManager<UserInfo> _userManager;
	private readonly RoleManager<Role> _roleManager;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly IMemoryCache _cache;

	private static readonly TimeSpan DropdownCacheDuration = TimeSpan.FromMinutes(10);

	//private readonly JsonSerializerOptions _jsonOptions;

	public UsersController(
		ApplicationDbContext dbContext
		, IHttpContextAccessor httpContextAccessor
		, SignInManager<UserInfo> signInManager
		, ISystemConfigurationService systemConfigurationService
		, UserManager<UserInfo> userManager
		, RoleManager<Role> roleManager
		, IWebHostEnvironment webHostEnvironment
		, IMemoryCache cache
	) : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_dbContext = dbContext;
		_httpContextAccessor = httpContextAccessor;
		_signInManager = signInManager;
		_systemConfiguration = systemConfigurationService;
		_userManager = userManager;
		_roleManager = roleManager;
		_webHostEnvironment = webHostEnvironment;
		_cache = cache;
	}

	[HttpGet("{company}/users")]
	public IActionResult Index(Guid company)
	{
		var rolesCacheKey = "roles:dropdown";
		if (!_cache.TryGetValue(rolesCacheKey, out object userRolesRaw))
		{
			userRolesRaw = _dbContext.Roles
				.AsNoTracking()
				.Where(e => e.NormalizedName != "SUPERUSER")
				.Select(e => new { e.Id, e.Name })
				.ToList();
			_cache.Set(rolesCacheKey, userRolesRaw, DropdownCacheDuration);
		}

		var userRoles = ((IEnumerable<dynamic>)userRolesRaw!).ToList();
		userRoles.Insert(0, new { Id = string.Empty, Name = "(select a role)" });
		ViewBag.UserRoles = userRoles;

		var sitesCacheKey = $"sites:dropdown:{company}";
		if (!_cache.TryGetValue(sitesCacheKey, out object locationsRaw))
		{
			locationsRaw = _dbContext.Sites
				.AsNoTracking()
				.Include(e => e.SiteCompany)
				.Where(e => e.IsActive == true && e.SiteType == SiteTypeEnum.Site && e.SiteCompany.Id == company)
				.Select(e => new { Id = e.Id.ToString(), e.Name })
				.ToList();
			_cache.Set(sitesCacheKey, locationsRaw, DropdownCacheDuration);
		}

		var locations = ((IEnumerable<dynamic>)locationsRaw!).ToList();
		locations.Insert(0, new { Id = string.Empty, Name = "All" });
		ViewBag.Locations = locations;

		var totalUsers = _dbContext.Users
			.Include(e => e.UserCompany)
			.Include(e => e.UserRoles).ThenInclude(e => e.Role)
			.Count(e => e.UserCompany.Id == company && !e.UserRoles.Any(ur => ur.Role.IsUnlimitedUsers));

		var model = new UsersViewModel
		{
			CompanyId = company,
			Roles = _dbContext.Roles.Where(role => role.NormalizedName != "SUPERUSER").ToList(),
			LimitReached = totalUsers > _systemConfiguration.UserCreateLimit,
		};


		return View(model);
	}


	[HttpGet("{company}/users/new")]
	public async Task<IActionResult> New(Guid company, string org)
	{
		var model = new NewUserViewModel
		{
			FirstName = string.Empty,
			LastName = string.Empty,
			Email = string.Empty,
			UserRole = string.Empty,
		};

		PrepareNewUserForm(company, model);
		model.OriginFromContractors = !string.IsNullOrEmpty(org) && org == "c";

		if (org == "c")
		{
			var selectedRole = await _dbContext.Roles.FirstOrDefaultAsync(e => e.NormalizedName == "CONTRACTOR");
			model.UserRole = selectedRole?.Id ?? string.Empty;
			model.DefaultContractorRole = selectedRole?.Id ?? string.Empty;
		}

		return View(model);
	}


	// What the New User form needs that isn't posted back with it: the company (used in the
	// Cancel link and the locations picker URL) and the selectable roles.
	private void PrepareNewUserForm(Guid company, NewUserViewModel model)
	{
		model.CompanyID = company;
		model.Roles = GetSelectableUserRoles(company, out var hasExceededLimit, out var noRolesAvailable);
		model.MaxNumOfUsers = _systemConfiguration.UserCreateLimit;
		model.HasExceededLimit = hasExceededLimit;
		model.NoRolesAvailable = noRolesAvailable;
	}


	[HttpPost("{company}/users/new")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> New(Guid company, NewUserViewModel model)
	{
		if (model.OriginFromContractors)
		{
			var contractorRole = await _dbContext.Roles.FirstOrDefaultAsync(e => e.NormalizedName == "CONTRACTOR");
			if (contractorRole != null)
			{
				model.UserRole = contractorRole.Id;
				ModelState.Remove(nameof(model.UserRole));
			}
		}

		if (!ModelState.IsValid)
		{
			PrepareNewUserForm(company, model);
			return View(model);
		}

		var selectedRole = await _dbContext.Roles.FirstOrDefaultAsync(e => e.Id == model.UserRole);
		GetSelectableUserRoles(company, out var hasExceededLimit, out _);

		// Defense-in-depth: the dropdown already only offers unlimited-users roles once the
		// limit is reached, but re-check server-side in case of a raw/tampered submission.
		if (hasExceededLimit && (selectedRole == null || !selectedRole.IsUnlimitedUsers))
		{
			ModelState.AddModelError(nameof(model.UserRole), "You have reached the user creation limit. Only roles marked \"Unlimited users\" can be selected.");
			PrepareNewUserForm(company, model);
			return View(model);
		}

		// The email is the user name, which is unique across all companies (including deleted
		// users); without this check the insert failed with a duplicate-key exception.
		var normalizedEmail = model.Email.ToUpper();
		var emailInUse = await _dbContext.Users
			.IgnoreQueryFilters()
			.AnyAsync(e => e.NormalizedUserName == normalizedEmail || e.NormalizedEmail == normalizedEmail);

		if (emailInUse)
		{
			ModelState.AddModelError(nameof(model.Email), "A user with this email already exists.");
			PrepareNewUserForm(company, model);
			return View(model);
		}

		var userCompany = await _dbContext.Companies.FirstOrDefaultAsync(e => e.Id == company);

		var userInfo = new UserInfo
		{
			Id = Guid.NewGuid().ToString(),
			FirstName = model.FirstName,
			LastName = model.LastName,
			Email = model.Email,
			Designation = model.Designation,
			UserName = model.Email,
			NormalizedUserName = model.Email.ToUpper(),
			NormalizedEmail = model.Email.ToUpper(),
			EmailConfirmed = true,
			IsActive = true,
			UserCompany = userCompany,
			CreatedBy = Guid.Parse(_httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)),
			CreatedWhen = DateTime.UtcNow.ToUniversalTime(),
		};

		_dbContext.Users.Add(userInfo);
		await _dbContext.SaveChangesAsync();

		// Add user to the selected role
		await _userManager.AddToRoleAsync(userInfo, selectedRole.NormalizedName);

		// Add user to selected site(s)
		if (!string.IsNullOrEmpty(model.Locations))
		{
			var locations = JsonSerializer.Deserialize<List<string>>(model.Locations);

			foreach (var location in locations)
			{
				var loc = _dbContext.Sites.FirstOrDefault(e => e.Id == Guid.Parse(location));
				userInfo.Sites.Add(loc);
			}
		}

		await _dbContext.SaveChangesAsync();

		// Set user password if any
		userInfo.PasswordHash = _userManager.PasswordHasher.HashPassword(userInfo, model.ConfirmPassword);

		await _userManager.UpdateAsync(userInfo);
		await _userManager.CheckPasswordAsync(userInfo, model.ConfirmPassword);

		TempData["SuccessMessage"] = $"User ({userInfo.Email}) has been successfully created.";

		if (model.OriginFromContractors)
			return Redirect($"/{company}/contractors");

		return RedirectToAction("Index", new { company });
	}


	/// <summary>
	/// Builds the role dropdown for user creation/edit. Once the company has reached its user
	/// create limit, only roles marked "unlimited users" remain selectable, since those are the
	/// only roles that don't count against the limit.
	/// <paramref name="excludeUserId"/> excludes that user's own slot from the count (so editing
	/// a user doesn't count them against themselves), and <paramref name="keepSelectableRoleId"/>
	/// keeps that role selectable regardless (so the user's current role never disappears from
	/// the list even when there's no room to move anyone else into it).
	/// </summary>
	private List<RoleDropDown> GetSelectableUserRoles(Guid company, out bool hasExceededLimit, out bool noRolesAvailable, string excludeUserId = null, string keepSelectableRoleId = null)
	{
		var usersQuery = _dbContext.Users
			.Include(e => e.UserCompany)
			.Include(e => e.UserRoles).ThenInclude(e => e.Role)
			.Where(e => e.UserCompany.Id == company && !e.UserRoles.Any(ur => ur.Role.IsUnlimitedUsers));

		if (!string.IsNullOrEmpty(excludeUserId))
			usersQuery = usersQuery.Where(e => e.Id != excludeUserId);

		var totalUsers = usersQuery.Count();

		hasExceededLimit = totalUsers >= _systemConfiguration.UserCreateLimit;

		var allRoles = _dbContext.Roles
			.Where(e => e.NormalizedName != "SUPERUSER")
			.Select(e => new RoleDropDown
			{
				Id = e.Id,
				Name = e.Name,
				IsUnlimitedUsers = e.IsUnlimitedUsers,
			})
			.ToList();

		if (!hasExceededLimit)
		{
			noRolesAvailable = false;
			return allRoles;
		}

		var selectable = allRoles.Where(r => r.IsUnlimitedUsers || r.Id == keepSelectableRoleId).ToList();
		noRolesAvailable = selectable.Count == 0;

		return selectable;
	}


	private async Task<ManageUserViewNodel> BuildEditUserFormAsync(Guid company, Guid id, string org)
	{
		var user = await _dbContext.Users
			.Include(e => e.UserRoles)
			.Include(e => e.Sites)
			.Select(e => new ManageUserViewNodel
			{
				Id = e.Id,
				FirstName = e.FirstName,
				LastName = e.LastName,
				Email = e.Email,
				Designation = e.Designation,
				UserRole = e.UserRoles.FirstOrDefault().RoleId ?? string.Empty,
				CompanyID = company,
				Locations = JsonSerializer.Serialize(e.Sites.Select(s => s.Id).ToList()),
				IsActive = e.IsActive
			})
			.FirstOrDefaultAsync(e => e.Id == id.ToString());

		if (user == null)
			return null;

		var userRoles = GetSelectableUserRoles(company, out var hasExceededLimit, out var noRolesAvailable, excludeUserId: user.Id, keepSelectableRoleId: user.UserRole);

		user.Roles = userRoles;
		user.IsEdit = true;
		user.OriginFromContractors = !string.IsNullOrEmpty(org) && org == "c";
		user.MaxNumOfUsers = _systemConfiguration.UserCreateLimit;
		user.HasExceededLimit = hasExceededLimit;
		user.NoRolesAvailable = noRolesAvailable;

		return user;
	}


	[HttpGet("{company}/users/edit/{id}")]
	public async Task<IActionResult> Edit(Guid company, Guid id, string org)
	{
		var user = await BuildEditUserFormAsync(company, id, org);

		if (user == null)
			return NotFound("User not found!");

		var model = new ManageUserMainViewModel
		{
			UserInfoForm = user,
			UserPasswordForm = new ManageUserPasswordViewModel
			{
				NewPassword = string.Empty,
				ConfirmPassword = string.Empty,
				IsContractors = !string.IsNullOrEmpty(org) && org == "c"
			}
		};

		return View("Edit", model);
	}


	// Redisplaying the Edit form after a failed save: restore what isn't posted back with it
	// (company, user id, email, selectable roles, password form) and keep what the user entered.
	private async Task PrepareEditUserFormAsync(Guid company, Guid id, ManageUserMainViewModel model)
	{
		var org = model.UserInfoForm?.OriginFromContractors == true ? "c" : null;
		var saved = await BuildEditUserFormAsync(company, id, org);

		if (saved == null)
			return;

		var form = model.UserInfoForm ??= saved;
		form.Id = saved.Id;
		form.CompanyID = company;
		form.Email = saved.Email;
		form.Roles = saved.Roles;
		form.IsEdit = true;
		form.MaxNumOfUsers = saved.MaxNumOfUsers;
		form.HasExceededLimit = saved.HasExceededLimit;
		form.NoRolesAvailable = saved.NoRolesAvailable;

		model.UserPasswordForm ??= new ManageUserPasswordViewModel
		{
			NewPassword = string.Empty,
			ConfirmPassword = string.Empty,
			IsContractors = form.OriginFromContractors
		};
	}


	[HttpPost("{company}/users/edit/{id}")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Edit(Guid company, Guid id, ManageUserMainViewModel model)
	{
		ModelState.Remove("UserPasswordForm");

		if (!ModelState.IsValid)
		{
			await PrepareEditUserFormAsync(company, id, model);
			return View(model);
		}

		var userInfo = await _dbContext.Users
			.Include(e => e.UserRoles)
			.ThenInclude(e => e.Role)
			.Include(e => e.Sites)
			.FirstOrDefaultAsync(e => e.Id == id.ToString());

		if (userInfo == null)
			return NotFound("User not found!");

		var oldUserRole = userInfo.UserRoles.FirstOrDefault(e => e.User == userInfo);

		// Defense-in-depth: the dropdown already restricts role choices once the limit is
		// reached (excluding this user's own current slot), but re-check server-side in case
		// of a raw/tampered submission — otherwise editing is a backdoor around the New User
		// limit, since moving a user out of an unlimited role into a limited one increases the
		// counted headcount.
		var selectedRole = await _dbContext.Roles.FirstOrDefaultAsync(e => e.Id == model.UserInfoForm.UserRole);
		GetSelectableUserRoles(company, out var hasExceededLimit, out _, excludeUserId: userInfo.Id, keepSelectableRoleId: oldUserRole?.RoleId);
		var isRoleChangeAllowed = !hasExceededLimit
			|| (selectedRole != null && (selectedRole.IsUnlimitedUsers || selectedRole.Id == oldUserRole?.RoleId));

		if (!isRoleChangeAllowed)
		{
			ModelState.AddModelError("UserInfoForm.UserRole", "You have reached the user creation limit. This user can only be moved to a role marked \"Unlimited users\".");
			await PrepareEditUserFormAsync(company, id, model);
			return View(model);
		}

		userInfo.FirstName = model.UserInfoForm.FirstName;
		userInfo.LastName = model.UserInfoForm.LastName;
		userInfo.Designation = model.UserInfoForm.Designation;
		userInfo.IsActive = model.UserInfoForm.IsActive;
		userInfo.UpdatedWhen = DateTime.UtcNow.ToUniversalTime();
		userInfo.UpdatedBy = Guid.Parse(_httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier));

		await _userManager.UpdateAsync(userInfo);

		// Update user's role
		await _userManager.RemoveFromRoleAsync(userInfo, oldUserRole.Role.NormalizedName);

		await _userManager.AddToRoleAsync(userInfo, selectedRole.NormalizedName);

		// Update user's site(s)
		var sitesToRemove = userInfo.Sites.ToList();

		sitesToRemove.ForEach(site =>
		{
			userInfo.Sites.Remove(site);
		});

		if (!string.IsNullOrEmpty(model.UserInfoForm.Locations))
		{
			var locations = JsonSerializer.Deserialize<List<string>>(model.UserInfoForm.Locations);
			var hasLocations = locations.Any();
			var locationCount = locations.Count();

			if (locations.Any() && locations.Count() > 0)
			{
				foreach (var location in locations)
				{
					var loc = _dbContext.Sites.FirstOrDefault(e => e.Id == Guid.Parse(location));
					userInfo.Sites.Add(loc);
				}
			}
		}

		await _dbContext.SaveChangesAsync();

		TempData["SuccessMessage"] = "User information has been successfully updated.";

		if (model.UserInfoForm.OriginFromContractors)
			return Redirect($"/{company}/contractors");

		return RedirectToAction("Index", new { company });
	}


	[HttpPost("{company}/users/edit/{id}/setpassword")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SetPassword(Guid company, Guid id, ManageUserMainViewModel model)
	{
		ModelState.Remove("UserInfoForm");

		var org = model.UserPasswordForm?.IsContractors == true ? "c" : null;

		if (!ModelState.IsValid)
		{
			model.UserInfoForm = await BuildEditUserFormAsync(company, id, org);
			return View("Edit", model);
		}

		var userInfo = await _dbContext.Users.FirstOrDefaultAsync(e => e.Id == id.ToString());

		if (userInfo == null)
			return NotFound("User not found!");

		userInfo.PasswordHash = _userManager.PasswordHasher.HashPassword(userInfo, model.UserPasswordForm.NewPassword);
		userInfo.UpdatedWhen = DateTime.UtcNow.ToUniversalTime();
		userInfo.UpdatedBy = Guid.Parse(_httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier));

		await _userManager.UpdateAsync(userInfo);

		TempData["SuccessMessage"] = "Password has been successfully updated.";

		return RedirectToAction("Edit", new { company, id, org });
	}


	[HttpGet("{company}/users/roles")]
	public IActionResult UserRoles()
	{
		return View(new UsersViewModel());
	}


	[HttpGet("{company}/users/roles/grid")]
	public IActionResult GetRolesGrid(Guid company)
	{
		var roles = _dbContext.Roles
			.Include(e => e.UserRoles)
			.ThenInclude(e => e.User)
			.ThenInclude(u => u.UserCompany)
			.OrderByDescending(e => e.CreatedWhen)
			.ThenBy(e => e.Name)
			.ToList()
			.Select(e => new RolesGridViewModel
			{
				Id = e.Id,
				Name = e.Name,
				Description = e.Description,
				// Roles are shared across companies, so the displayed count is scoped to this company.
				NumOfUsers = e.UserRoles?.Count(ur => ur.User?.UserCompany?.Id == company) ?? 0,
				IsSystemRole = e.IsSystemRole,
				IsUnlimitedUsers = e.IsUnlimitedUsers,
				CreatedWhen = GeneralHelper.GetDateInTimeZone(e.CreatedWhen),
				// The delete guard must reflect the real (global) constraint enforced in DeleteRole,
				// otherwise the icon could offer a direct delete that the server then rejects.
				ActionIcons = RolesGridActionIcons(company, e.Id, e.IsSystemRole, e.UserRoles?.Count ?? 0),
			})
			.ToList();

		return new JsonResult(roles, new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		});
	}


	[HttpGet("{company}/users/grid")]
	public async Task<JsonResult> GetUsersGrid(Guid company)
	{
		var users = await _dbContext.Users
			.AsNoTracking()
			.Include(e => e.UserCompany)
			.Include(e => e.UserRoles)
			.ThenInclude(e => e.Role)
			.Include(e => e.Sites)
			.Where(e => e.UserCompany.Id == company && e.UserRoles.Any(r => r.Role.NormalizedName != "SUPERUSER"))
			.OrderByDescending(e => e.CreatedWhen)
			.ToListAsync();

		var userIds = users.Select(u => Guid.Parse(u.Id)).ToHashSet();

		var usersWithPermits = await _dbContext.Permits
			.AsNoTracking()
			.Where(p => p.CreatedBy.HasValue && userIds.Contains(p.CreatedBy.Value))
			.Select(p => p.CreatedBy.Value)
			.Distinct()
			.ToListAsync();

		var permitSet = usersWithPermits.ToHashSet();

		var siteUsers = users.Select(e =>
		{
			var userId = Guid.Parse(e.Id);
			return new UsersGridViewModel
			{
				Id = e.Id,
				Name = $"{e.FirstName} {e.LastName}".Trim(),
				Email = e.Email,
				IsSecured = e.PasswordHash != null,
				IsActive = e.IsActive,
				Sites = string.Join(", ", e.Sites.Select(s => s.Name)),
				Roles = string.Join(", ", e.UserRoles.Select(r => r.Role.Name)),
				Designation = e.Designation,
				CreatedWhen = GeneralHelper.GetDateInTimeZone(e.CreatedWhen),
				HasPermits = permitSet.Contains(userId),
				ActionIcons = UsersGridActionIcons(company, e.Id, e.PasswordHash != null, permitSet.Contains(userId)),
			};
		}).ToList();

		return new JsonResult(siteUsers, new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		});
	}


	[HttpGet("{company}/users/workflowusers/all")]
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
				CreatedWhenTicks = GeneralHelper.FormatDateTimeTicks(GeneralHelper.GetDateInTimeZone(e.CreatedWhen)),
				UpdatedWhenTicks = GeneralHelper.FormatDateTimeTicks(e.UpdatedWhen),
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


	[HttpGet("{company}/users/roles/new")]
	public IActionResult NewRole()
	{
		return View("ManageRole", new ManageRoleViewModel { Name = string.Empty, NormalizedName = string.Empty, IsEdit = false });
	}


	[HttpPost("{company}/users/roles/new")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> CreateRole(Guid company, ManageRoleViewModel model)
	{
		model.IsEdit = false;

		if (!ModelState.IsValid)
			return View("ManageRole", model);

		var normalizedName = model.NormalizedName.Trim().ToUpper();
		var existingRole = await _dbContext.Roles.FirstOrDefaultAsync(e => e.NormalizedName == normalizedName);

		if (existingRole != null)
		{
			ModelState.AddModelError(nameof(model.NormalizedName), "A role with this normalized name already exists.");
			return View("ManageRole", model);
		}

		var currentUserId = Guid.Parse(_httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier));

		var role = new Role
		{
			Name = model.Name.Trim(),
			Description = model.Description,
			ConcurrencyStamp = Guid.NewGuid().ToString(),
			IsSystemRole = false,
			IsUnlimitedUsers = model.IsUnlimitedUsers,
			CreatedBy = currentUserId,
			CreatedWhen = DateTime.UtcNow,
		};

		await _roleManager.CreateAsync(role);

		// RoleManager overwrites NormalizedName from Name using the default normalizer;
		// re-apply the user-supplied slug so it sticks.
		role.NormalizedName = normalizedName;
		await _dbContext.SaveChangesAsync();

		TempData["SuccessMessage"] = $"Role '{role.Name}' has been successfully created.";

		return RedirectToAction("UserRoles", new { company });
	}


	[HttpGet("{company}/users/roles/{id}/edit")]
	public async Task<IActionResult> EditRole(Guid company, string id)
	{
		var role = await _dbContext.Roles
			.Include(e => e.UserRoles)
			.ThenInclude(e => e.User)
			.ThenInclude(u => u.UserCompany)
			.FirstOrDefaultAsync(e => e.Id == id);

		if (role == null)
			return NotFound();

		// Roles are shared across companies, so only show users belonging to the
		// company currently being managed, not every tenant's users on this role.
		var usersInRole = role.UserRoles?
			.Where(ur => ur.User?.UserCompany?.Id == company)
			.Select(ur => $"{ur.User?.FirstName} {ur.User?.LastName}".Trim())
			.Where(name => !string.IsNullOrEmpty(name))
			.OrderBy(name => name)
			.ToList() ?? [];

		return View("ManageRole", new ManageRoleViewModel
		{
			Id = role.Id,
			Name = role.Name,
			NormalizedName = role.NormalizedName ?? string.Empty,
			Description = role.Description,
			IsSystemRole = role.IsSystemRole,
			IsUnlimitedUsers = role.IsUnlimitedUsers,
			IsEdit = true,
			UsersInRole = usersInRole,
		});
	}


	[HttpPost("{company}/users/roles/{id}/edit")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EditRole(Guid company, string id, ManageRoleViewModel model)
	{
		model.IsEdit = true;

		if (!ModelState.IsValid)
			return View("ManageRole", model);

		var role = await _dbContext.Roles.FirstOrDefaultAsync(e => e.Id == id);

		if (role == null)
			return NotFound();

		var currentUserId = Guid.Parse(_httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier));
		var originalNormalizedName = role.NormalizedName;

		role.Name = model.Name.Trim();
		role.Description = model.Description;
		role.IsSystemRole = model.IsSystemRole;
		role.IsUnlimitedUsers = model.IsUnlimitedUsers;
		role.UpdatedBy = currentUserId;
		role.UpdatedWhen = DateTime.UtcNow;

		await _roleManager.UpdateAsync(role);

		// RoleManager re-derives NormalizedName from Name on every update; restore the
		// original immutable slug regardless of whether the display Name changed.
		if (role.NormalizedName != originalNormalizedName)
		{
			role.NormalizedName = originalNormalizedName;
			await _dbContext.SaveChangesAsync();
		}

		TempData["SuccessMessage"] = $"Role '{role.Name}' has been successfully updated.";

		return RedirectToAction("UserRoles", new { company });
	}


	[HttpPost("{company}/users/{mode}")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ManageUser(Guid company, string mode = "new")
	{
		try
		{
			var req = Request.Form;
			var companyInfo = _dbContext.Companies.SingleOrDefault(f => f.Id == company);

			if (mode == "new")
			{
				var userInfo = new UserInfo
				{
					Id = Guid.NewGuid().ToString(),
					FirstName = req["FirstName"].ToString(),
					LastName = req["LastName"].ToString(),
					Email = req["Email"].ToString(),
					IsActive = Convert.ToBoolean(req["IsActive"]),
					UserName = req["Email"].ToString(),
					Designation = req["Designation"].ToString(),
					NormalizedUserName = req["Email"].ToString().ToUpper(),
					NormalizedEmail = req["Email"].ToString().ToUpper(),
					EmailConfirmed = true,
					UserCompany = companyInfo,
					CreatedBy = Guid.Parse(_httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)),
					CreatedWhen = DateTime.UtcNow.ToUniversalTime(),
				};

				_dbContext.Users.Add(userInfo);
				_dbContext.SaveChanges();

				// Add user to selected role
				var role = _roleManager.Roles.FirstOrDefault(e => e.Id == req["Role"].ToString());
				await _userManager.AddToRoleAsync(userInfo, role.NormalizedName);

				// Add user to selected site(s)
				if (!string.IsNullOrEmpty(req["Locations"].ToString()))
				{
					var locations = req["Locations"].ToString().Split(',');

					foreach (var location in locations)
					{
						var loc = _dbContext.Sites.FirstOrDefault(e => e.Id == Guid.Parse(location));
						userInfo.Sites.Add(loc);
					}
				}

				_dbContext.SaveChanges();

				// Set user password if any
				if (!string.IsNullOrEmpty(req["Password"].ToString()) && req["Password"].ToString() != "")
				{
					userInfo.PasswordHash = _userManager.PasswordHasher.HashPassword(userInfo, req["Password"].ToString());
					await _userManager.UpdateAsync(userInfo);

					await _userManager.CheckPasswordAsync(userInfo, req["Password"].ToString());
				}

			}

			if (mode == "edit")
			{
				var userInfo = await _dbContext.Users
					.Include(e => e.UserRoles)
					.ThenInclude(e => e.Role)
					.Include(e => e.Sites)
					.FirstOrDefaultAsync(e => e.Id == req["Id"].ToString());

				if (userInfo == null)
				{
					return NotFound("Unable to find user");
				}

				var oldUserRole = userInfo.UserRoles.FirstOrDefault(e => e.User == userInfo);

				userInfo.FirstName = req["FirstName"].ToString();
				userInfo.LastName = req["LastName"].ToString();
				userInfo.Email = req["Email"].ToString();
				userInfo.Designation = req["Designation"].ToString();
				userInfo.NormalizedEmail = req["Email"].ToString().ToUpper();
				userInfo.IsActive = Convert.ToBoolean(req["IsActive"]);
				userInfo.UpdatedWhen = DateTime.UtcNow.ToUniversalTime();
				userInfo.UpdatedBy = Guid.Parse(_httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier));

				await _userManager.UpdateAsync(userInfo);

				// Update user's role
				await _userManager.RemoveFromRoleAsync(userInfo, oldUserRole.Role.NormalizedName);

				var role = _roleManager.Roles.FirstOrDefault(e => e.Id == req["Role"].ToString());
				await _userManager.AddToRoleAsync(userInfo, role.NormalizedName);


				// Update user's site(s)
				var sitesToRemove = userInfo.Sites.ToList();

				sitesToRemove.ForEach(site =>
				{
					userInfo.Sites.Remove(site);
				});

				if (!string.IsNullOrEmpty(req["Locations"].ToString()))
				{
					var locations = req["Locations"].ToString().Split(',');

					foreach (var location in locations)
					{
						var loc = _dbContext.Sites.FirstOrDefault(e => e.Id == Guid.Parse(location));
						userInfo.Sites.Add(loc);
					}
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


	[HttpPost("{company}/users/{id}/setpasswordX")]
	public async Task<IActionResult> SetUserPassword(Guid company, [FromBody] AjaxUserPasswordModel request, string id)
	{
		try
		{
			var user = await _userManager.FindByIdAsync(id);

			if (user == null)
			{
				return NotFound("Unable to find user");
			}

			//var isPasswordOk = await _userManager.CheckPasswordAsync(user, request.Password);

			//if (!isPasswordOk)
			//{
			//	return Ok(new
			//	{
			//		Data = "NOT_OK",
			//	});
			//}

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


	[HttpDelete("users/roles/{id}")]
	public async Task<IActionResult> DeleteRole(string id)
	{
		try
		{
			var role = await _dbContext.Roles
				.Include(e => e.UserRoles)
				.FirstOrDefaultAsync(e => e.Id == id);

			if (role == null)
				return NotFound();

			if (role.IsSystemRole)
				return BadRequest(new { ErrorMessage = "System roles cannot be deleted." });

			if (role.UserRoles?.Count > 0)
				return BadRequest(new { ErrorMessage = "Cannot delete a role that has users assigned to it. Please reassign all users first." });

			role.IsDeleted = true;
			role.DeletedWhen = DateTime.UtcNow;
			role.DeletedBy = Guid.Parse(_httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier));

			await _roleManager.UpdateAsync(role);

			return Ok(new { Data = "OK" });
		}
		catch (Exception ex)
		{
			return BadRequest(new { ErrorMessage = ex.Message });
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


	// Grid "Delete selected" (Super Users). Same soft delete as the row action (UserService.DeleteAsync), plus the
	// rules the row icon enforces in the browser only: a user with permits stays (deactivate instead). Also skipped:
	// your own account and Super User accounts. Records are loaded one at a time so a failure can't detach the rest.
	[Authorize(Roles = "Super User")]
	[HttpPost("{company}/users/bulk-delete")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> BulkDeleteUsers(Guid company, [FromBody] AjaxBulkDeleteRequest request, CancellationToken ct)
	{
		var result = new AjaxBulkDeleteResult();
		var me = User.FindFirstValue(ClaimTypes.NameIdentifier);

		foreach (var id in (request?.Ids ?? new()).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.UserCompany.Id == company, ct);
			if (user == null)
			{
				result.Fail(id, "User", "Not found (it may already be deleted).");
				continue;
			}

			var name = $"{user.FirstName} {user.LastName}".Trim();

			if (string.Equals(user.Id, me, StringComparison.OrdinalIgnoreCase))
			{
				result.Fail(id, name, "You can't delete your own account.");
				continue;
			}

			if (await _userManager.IsInRoleAsync(user, "Super User"))
			{
				result.Fail(id, name, "Super User accounts can't be deleted here.");
				continue;
			}

			if (Guid.TryParse(user.Id, out var userGuid) && await _dbContext.Permits.AnyAsync(p => p.CreatedBy == userGuid, ct))
			{
				result.Fail(id, name, "Has permits. Deactivate the user instead.");
				continue;
			}

			try
			{
				await _dbContext.SoftDeleteAsync(user, Guid.Parse(me));
				result.Deleted++;
			}
			catch (Exception ex)
			{
				_dbContext.ChangeTracker.Clear();
				result.Fail(id, name, ex.Message);
			}
		}

		return Ok(result);
	}


	// Same rules as DeleteRole: system roles and roles that still have users (in any company) stay.
	[Authorize(Roles = "Super User")]
	[HttpPost("{company}/users/roles/bulk-delete")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> BulkDeleteRoles(Guid company, [FromBody] AjaxBulkDeleteRequest request, CancellationToken ct)
	{
		var result = new AjaxBulkDeleteResult();
		var me = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

		foreach (var id in (request?.Ids ?? new()).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var role = await _dbContext.Roles.Include(r => r.UserRoles).FirstOrDefaultAsync(r => r.Id == id, ct);
			if (role == null)
			{
				result.Fail(id, "Role", "Not found (it may already be deleted).");
				continue;
			}

			if (role.IsSystemRole)
			{
				result.Fail(id, role.Name, "System roles can't be deleted.");
				continue;
			}

			if (role.UserRoles?.Count > 0)
			{
				result.Fail(id, role.Name, "Has users assigned. Reassign them first.");
				continue;
			}

			try
			{
				role.IsDeleted = true;
				role.DeletedWhen = DateTime.UtcNow;
				role.DeletedBy = me;

				var update = await _roleManager.UpdateAsync(role);
				if (update.Succeeded)
					result.Deleted++;
				else
					result.Fail(id, role.Name, string.Join(" ", update.Errors.Select(e => e.Description)));
			}
			catch (Exception ex)
			{
				_dbContext.ChangeTracker.Clear();
				result.Fail(id, role.Name, ex.Message);
			}
		}

		return Ok(result);
	}


	#region "Private static functions"

	private static string UsersGridActionIcons(Guid company, string id, bool isSecured, bool hasPermits)
	{
		var icons = string.Empty;

		icons += "<div class=\"d-flex flex-row action-icons\">";
		icons += $"<a href=\"/{company}/users/edit/{id}\" class=\"no-loading text-secondary\"><i class=\"fa-solid fa-money-check-pen fa-lg\"></i></a>";

		if (hasPermits)
		{
			icons += $"<a href=\"javascript:;\" class=\"no-loading text-danger\" data-bs-toggle=\"modal\" data-bs-target=\"#dlgDeleteWarnUser\"><i class=\"fa-solid fa-trash-xmark fa-lg\"></i></a>";
		}
		else
		{
			icons += $"<a href=\"javascript:;\" class=\"no-loading text-danger\" onclick=\"deleteUser('{id}')\"><i class=\"fa-solid fa-trash-xmark fa-lg\"></i></a>";
		}

		icons += "</div>";
		return icons;
	}


	private static string RolesGridActionIcons(Guid company, string id, bool isSystemRole, int numOfUsers)
	{
		var icons = "<div class=\"d-flex flex-row action-icons justify-content-center\">";
		icons += $"<a href=\"/{company}/users/roles/{id}/edit\" class=\"no-loading text-secondary\"><i class=\"fa-solid fa-money-check-pen fa-lg\"></i></a>";

		if (!isSystemRole)
		{
			if (numOfUsers > 0)
			{
				icons += $"<a href=\"#\" class=\"no-loading text-danger\" data-bs-toggle=\"modal\" data-bs-target=\"#dlgDeleteWarnRole\"><i class=\"fa-solid fa-trash-xmark fa-lg\"></i></a>";
			}
			else
			{
				icons += $"<a href=\"javascript:;\" class=\"no-loading text-danger\" onclick=\"deleteRole('{id}')\"><i class=\"fa-solid fa-trash-xmark fa-lg\"></i></a>";
			}
		}

		icons += "</div>";
		return icons;
	}

	#endregion

}