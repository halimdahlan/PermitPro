using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;

using PermitPro.Core.Interfaces;

using System.Text.Json;

namespace PermitPro.App.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScheduledTaskController : Controller
{
	private readonly SignInManager<UserInfo> _signInManager;
	private readonly UserManager<UserInfo> _userManager;
	private readonly RoleManager<Role> _roleManager;
	private readonly ILogger<ScheduledTaskController> _logger;
	private readonly IConfiguration _configuration;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private readonly ApplicationDbContext _dbContext;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly PTWSettings _ptwSettings;
	private readonly JsonSerializerOptions _jsonOptions;
	private readonly ICurrentUserService _currentUserService;
	private readonly ILogService _logService;
	private readonly IAppSettingsService _appSettings;

	public ScheduledTaskController(
		UserManager<UserInfo> userManager
		, RoleManager<Role> roleManager
		, SignInManager<UserInfo> signInManager
		, ILogger<ScheduledTaskController> logger
		, IConfiguration configuration
		, IHttpContextAccessor httpContextAccessor
		, ApplicationDbContext dbContext
		, IWebHostEnvironment webHostEnvironment
		, PTWSettings ptwSettings
		, ICurrentUserService currentUserService
		, ILogService logService
		, IAppSettingsService appSettingsService)
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
		_logService = logService;
		_appSettings = appSettingsService;

		_jsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		};
	}

	[HttpGet("permits/close")]
	public async Task<IActionResult> ClosePermitsGet()
	{
		var user = await _userManager.FindByEmailAsync("halim.dahlan@outlook.com");

		var log = new AuditLog
		{
			Id = Guid.NewGuid(),
			EntityName = "AuditLog",
			Description = "Schedule task running...",
			Url = "HTTPS - GET",
			LogType = LogTypeEnum.Information,
			AuditLogUser = user,
			CreatedBy = Guid.Parse(user!.Id),
			CreatedWhen = DateTime.Now.ToUniversalTime(),
		};

		_dbContext.AuditLogs.Add(log);
		await _dbContext.SaveChangesAsync();

		return Ok();
	}


	[HttpGet("permits/suspended/check/{company}")]
	public async Task<IActionResult> CheckSuspendedPermits(Guid company)
	{
		var autoResumeDays = await _appSettings.GetIntAsync(company, "workflow", "suspended_autoresume_days");
		var systemAdmin = _dbContext.Users.FirstOrDefault(u => u.Email == "admin@permitpro.app");

		var suspendedPermits = _dbContext.Permits
			.Where(p => p.PermitStatus == PermitStatusEnum.Suspended && p.AutoResumeSuspended == true)
			.ToList();

		if (suspendedPermits is not null && suspendedPermits.Count > 0)
		{
			foreach (var permit in suspendedPermits)
			{
				PermitStatusEnum previousPermitStatus = permit.PreviousPermitStatus!.Value;

				if (permit.SuspendedDateTime!.Value.AddDays(autoResumeDays) >= DateTime.Now)
				{
					permit.PermitStatus = previousPermitStatus;
					permit.PreviousPermitStatus = null;
					permit.SuspendedDateTime = null;
					permit.AutoResumeSuspended = false;
					permit.UpdatedWhen = DateTime.Now.ToUniversalTime();
					permit.UpdatedBy = Guid.Parse(systemAdmin!.Id);

					_dbContext.Permits.Update(permit);

					var logMessage = $"Permit PTW{permit.RunningNumber:000000} has been resumed from suspension.";
					await _logService.LogMessageAsync(LogTypeEnum.Information, "PERMIT_SUSPEND", logMessage, systemAdmin!);
				}

				// if (permit.SuspendedDateTime <= DateTime.Now)
				// {
				// 	permit.PermitStatus = previousPermitStatus;
				// 	permit.PreviousPermitStatus = null;
				// 	permit.SuspendedDateTime = null;
				// 	permit.AutoResumeSuspended = false;
				// 	permit.UpdatedWhen = DateTime.Now.ToUniversalTime();
				// 	permit.UpdatedBy = Guid.Parse(systemAdmin!.Id);
				// }

				// _dbContext.Permits.Update(permit);

				// var logMessage = $"Permit PTW{permit.RunningNumber:000000} has been resumed from suspension.";
				// await _logService.LogMessageAsync(LogTypeEnum.Information, "PERMIT_SUSPEND", logMessage, systemAdmin!);
			}

			_dbContext.SaveChanges();

			return Ok("Update OK");
		}

		return Ok("Nothing to update");
	}

}
