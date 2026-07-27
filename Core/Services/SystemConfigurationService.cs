#nullable disable

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using System.Text.Json;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Interfaces;

using System.Security.Claims;

namespace PermitPro.Core.Services;

public class SystemConfigurationService : ISystemConfigurationService
{
	private readonly UserManager<UserInfo> _userManager;
	private readonly ApplicationDbContext _context;
	private readonly ICurrentUserService _currentUserService;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly IAppSettingsService _appSettings;

	private List<SystemMenu> _authorizedMenus = new();


	public SystemConfigurationService(
		UserManager<UserInfo> userManager
		, ApplicationDbContext dbContext
		, ICurrentUserService currentUserService
		, IHttpContextAccessor httpContextAccessor
		, IWebHostEnvironment webHostEnvironment
		, IAppSettingsService appSettings)
	{
		_userManager = userManager;
		_context = dbContext;
		_currentUserService = currentUserService;
		_httpContextAccessor = httpContextAccessor;
		_webHostEnvironment = webHostEnvironment;
		_appSettings = appSettings;
	}


	public IEnumerable<SystemMenu> AuthorizedMenus
	{
		get
		{
			var sessionMenu = _httpContextAccessor.HttpContext.Session.GetString("AuthorizedMenu");

			if (string.IsNullOrEmpty(sessionMenu))
			{
				Init();
			}
			else
			{
				_authorizedMenus = JsonSerializer.Deserialize<List<SystemMenu>>(sessionMenu);
			}

			return _authorizedMenus.AsEnumerable();
		}
	}

	public IEnumerable<string> ReservedRoles
	{
		get
		{
			var val = _appSettings.GetValueAsync(GetCurrentCompanyId(), "general", "reserved_roles").GetAwaiter().GetResult();
			if (!string.IsNullOrEmpty(val))
				return val.Split(';', StringSplitOptions.RemoveEmptyEntries);
			return null;
		}
	}

	public string ApplicationDomain => _appSettings.GetValueAsync(GetCurrentCompanyId(), "general", "application_domain").GetAwaiter().GetResult();

	public int UserCreateLimit => _appSettings.GetIntAsync(GetCurrentCompanyId(), "general", "user_create_limit").GetAwaiter().GetResult();

   public int UploadMaxFileSize => _appSettings.GetIntAsync(GetCurrentCompanyId(), "general", "upload_max_file_size").GetAwaiter().GetResult();

   public int UploadMaxFileCount => _appSettings.GetIntAsync(GetCurrentCompanyId(), "general", "upload_max_file_count").GetAwaiter().GetResult();

   public string UploadAllowedFileTypes => _appSettings.GetValueAsync(GetCurrentCompanyId(), "general", "upload_allowed_file_types").GetAwaiter().GetResult();

   public string SMTPServer => _appSettings.GetValueAsync(GetCurrentCompanyId(), "email", "smtp_server").GetAwaiter().GetResult();

   public int SMTPPort => _appSettings.GetIntAsync(GetCurrentCompanyId(), "email", "smtp_port").GetAwaiter().GetResult();

   public string SenderName => _appSettings.GetValueAsync(GetCurrentCompanyId(), "email", "sender_name").GetAwaiter().GetResult();

	public string SenderEmail => _appSettings.GetValueAsync(GetCurrentCompanyId(), "email", "sender_email").GetAwaiter().GetResult();

   public string EmailUserName => _appSettings.GetValueAsync(GetCurrentCompanyId(), "email", "email_username").GetAwaiter().GetResult();

   public string EmailPassword => _appSettings.GetValueAsync(GetCurrentCompanyId(), "email", "email_password").GetAwaiter().GetResult();

   public int SuspendedAutoResume => _appSettings.GetIntAsync(GetCurrentCompanyId(), "workflow", "suspended_autoresume_days").GetAwaiter().GetResult();

   public DateTime? NewPermitFormCutoverDate
   {
       get
       {
           var val = _appSettings.GetValueAsync(GetCurrentCompanyId(), "general", "new_permit_form_cutover_date").GetAwaiter().GetResult();
           if (DateTime.TryParse(val, out var date))
               return date;
           return null;
       }
   }

   public void Init()
	{
		var userId = _httpContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

		if (!string.IsNullOrEmpty(userId))
		{
			var currentUser = _context.Users
				.Include(e => e.UserRoles)
				.FirstOrDefault(e => e.Id == userId);

			var currentUserRoles = _context.UserRoles
				.Include(e => e.Role)
				.Where(e => e.User == currentUser);

			List<string> roles = new();

			foreach (var role in currentUserRoles)
			{
				roles.Add(role.Role.Name);
			}

			_httpContextAccessor.HttpContext.Session.SetString("UserRoles", JsonSerializer.Serialize(roles));

			var menu = _context.SystemMenus
				.Include(e => e.Roles)
				.Where(e => e.Roles!.Any(f => roles.Contains(f.Name)) && e.ParentId == null)
				.OrderBy(e => e.MenuOrder)
				.Select(e => new SystemMenu
				{
					Id = e.Id,
					Name = e.Name,
					Description = e.Description,
					ControllerName = e.ControllerName,
					IconName = e.IconName,
					ParentId = e.ParentId,
					HasChildren = _context.SystemMenus.Include(f => f.Roles).Any(f => f.ParentId == e.Id),
					Children = _context.SystemMenus.Include(f => f.Roles)
						.Where(f => f.ParentId == e.Id)
						.OrderBy(f => f.MenuOrder)
						.Select(f => new SystemMenu
						{
							Id = f.Id,
							Name = f.Name,
							Description = f.Description,
							ControllerName = f.ControllerName,
							IconName = f.IconName,
						}).ToList(),
					MenuOrder = e.MenuOrder,
					CreatedBy = e.CreatedBy,
				})
				.ToList();

			_authorizedMenus = menu;

			_httpContextAccessor.HttpContext.Session.SetString("AuthorizedMenu", JsonSerializer.Serialize(menu));
		}
	}


	public bool HasAccess(string controller)
	{
		var sessionRoles = _httpContextAccessor.HttpContext.Session.GetString("UserRoles");

		if (string.IsNullOrEmpty(sessionRoles))
		{
			return false;
		}

		var userRoles = JsonSerializer.Deserialize<List<string>>(sessionRoles);

		var menu = _context.SystemMenus
			.Include(e => e.Roles)
			.FirstOrDefault(e => e.Roles!.Any(f => userRoles.Contains(f.Name)) && e.ControllerName.ToLower() == controller.ToLower());

		return menu != null;
	}
	

	private Guid GetCurrentCompanyId()
	{
		var routeValue = _httpContextAccessor.HttpContext?.Request.RouteValues["company"];
		if (routeValue != null && Guid.TryParse(routeValue.ToString(), out var companyId))
			return companyId;
		return Guid.Empty;
	}
}
