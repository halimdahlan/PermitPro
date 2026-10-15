#nullable disable

using MailKit.Net.Smtp;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using MimeKit;

using PermitPro.App.Services;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

using Scriban;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace PermitPro.App.Controllers;

public class AccountController : Controller
{

	private readonly SignInManager<UserInfo> _signInManager;
	private readonly UserManager<UserInfo> _userManager;
	private readonly IUserStore<UserInfo> _userStore;
	private readonly IUserEmailStore<UserInfo> _emailStore;
	private readonly ILogger<AccountController> _logger;
	private readonly IConfiguration _configuration;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private readonly ApplicationDbContext _context;
	private readonly ICurrentUserService _currentUserService;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly IAppSettingsService _appSettings;
	private readonly ILogService _logService;
	private readonly JwtSettings _jwtSettings;
	private readonly ISystemConfigurationService _systemConfigurationService;
	private readonly TenancySettings _tenancy;
	private readonly TenantDirectory _tenants;


	public AccountController(
		UserManager<UserInfo> userManager
		, IUserStore<UserInfo> userStore
		, SignInManager<UserInfo> signInManager
		, ILogger<AccountController> logger
		, IConfiguration configuration
		, IHttpContextAccessor httpContextAccessor
		, ApplicationDbContext context
		, ICurrentUserService currentUserService
		, IWebHostEnvironment webHostEnvironment
		, ISystemConfigurationService systemConfigurationService
		, IAppSettingsService appSettings
		, ILogService logService
		, JwtSettings jwtSettings
		, TenancySettings tenancy
		, TenantDirectory tenants)
	{
		_tenancy = tenancy;
		_tenants = tenants;
		_userManager = userManager;
		_userStore = userStore;
		_emailStore = GetEmailStore();
		_signInManager = signInManager;
		_logger = logger;
		_configuration = configuration;
		_httpContextAccessor = httpContextAccessor;
		_context = context;
		_currentUserService = currentUserService;
		_webHostEnvironment = webHostEnvironment;
		_appSettings = appSettings;
		_logService = logService;
		_jwtSettings = jwtSettings;
		_systemConfigurationService = systemConfigurationService;

		Guid company = Guid.Empty;
		var routeValue = _httpContextAccessor!.HttpContext!.GetRouteValue("company");

		if (routeValue != null)
		{
			Guid.TryParse(routeValue.ToString(), out company);

			// Redirect only — never sign out on a malformed URL (see AppControllerBase).
			if (company == Guid.Empty)
				_httpContextAccessor!.HttpContext!.Response.Redirect("/account/login");
		}
	}


	// On a company's host (iotm.permitpro.app): email and password. On the apex (permitpro.app): the
	// company address page, as the apex has no company to sign in to.
	[HttpGet("account/login")]
	public async Task<IActionResult> Login(string returnUrl, string url, string company, string entity, string id, CancellationToken ct)
	{
		var tenant = HttpContext.GetTenant();

		// Workflow emails link to /account/login?company={id}&entity=permits&id={permit id}: open that
		// permit after signing in. (PermitsController.Edit sends ?origin=email links here with ?url=.)
		returnUrl ??= url;
		if (string.IsNullOrEmpty(returnUrl) && entity == "permits" && Guid.TryParse(id, out var permitId))
			returnUrl = $"/permits/edit/{permitId}";

		if (tenant == null)
		{
			// Old email links on the apex, and users already signed in there: on to the company's own host.
			var linkedId = Guid.TryParse(company, out var companyId) ? companyId : await CurrentUserCompanyIdAsync(ct);
			if (linkedId != null && await _tenants.FindByIdAsync(linkedId.Value, ct) is { } linked)
				return Redirect($"{Request.CompanyBaseUrl(_tenancy, linked)}/account/login{Request.QueryString}");

			return View("CompanyAddress", new CompanyAddressViewModel { HostSuffix = HostSuffix() });
		}

		// Already signed in here (TenantAccessMiddleware has checked it's one of this company's users).
		if (User.Identity?.IsAuthenticated == true)
			return LocalRedirect(SafeReturnUrl(returnUrl));

		return View(new SignInViewModel
		{
			Email = "",
			Password = "",
			ReturnUrl = returnUrl,
		});
	}


	// The company address form on the apex: iotm → https://iotm.permitpro.app/account/login
	[HttpGet("account/find")]
	[EnableRateLimiting("auth")]
	public async Task<IActionResult> FindCompany(string subdomain, CancellationToken ct)
	{
		var model = new CompanyAddressViewModel { Subdomain = subdomain, HostSuffix = HostSuffix() };

		var normalized = CompanySubdomain.Normalize(subdomain);
		if (normalized == null)
		{
			model.ErrorMessage = "Enter your company's address.";
			return View("CompanyAddress", model);
		}

		var tenant = await _tenants.FindBySubdomainAsync(normalized, ct);
		if (tenant == null)
		{
			model.ErrorMessage = $"There's no company at {normalized}{model.HostSuffix}. Check the address with your administrator.";
			return View("CompanyAddress", model);
		}

		return Redirect($"{Request.CompanyBaseUrl(_tenancy, tenant)}/account/login");
	}


	[HttpPost("account/login")]
	[ValidateAntiForgeryToken]
	[EnableRateLimiting("auth")]
	public async Task<IActionResult> Login(SignInViewModel model)
	{
		var tenant = HttpContext.GetTenant();

		// No company on the apex: back to the company address page.
		if (tenant == null)
			return Redirect("/account/login");

		if (!ModelState.IsValid)
		{
			return View(model);
		}

		var user = _context.Users
			.Include(e => e.UserCompany)
			.Include(e => e.UserRoles)
			.ThenInclude(e => e.Role)
			.FirstOrDefault(e => e.Email == model.Email);

		if (user == null)
		{
			ModelState.AddModelError("invalid_user", "Invalid user");
			return View(model);
		}

		if (!user.IsActive)
		{
			ModelState.AddModelError("inactive_user", "User is inactive. Contact system administrator.");
			return View(model);
		}

		// Only this host's company's users may sign in here; Super Admins (SUPERUSER) anywhere.
		var isSuperUser = user.UserRoles.Any(e => e.Role.NormalizedName == "SUPERUSER");

		if (!isSuperUser && user.UserCompany?.Id != tenant.Id)
		{
			await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Error, "SIGN_IN", $"Sign-in attempt on another company's site ({tenant.Subdomain}).", user);

			ModelState.AddModelError(string.Empty, "Invalid login attempt.");
			return View(model);
		}

		var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

		if (result.Succeeded)
		{
			await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Information, "SIGN_IN", $"User ({user.UserName}) has successfully logged in.", user);

			return LocalRedirect(SafeReturnUrl(model.ReturnUrl));
		}

		if (result.IsLockedOut)
		{
			await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Error, "SIGN_IN", $"User ({user.UserName}) account has been locked out.", user);

			ModelState.AddModelError(string.Empty, "Your account has been locked out. Please try again in 10 minutes.");
			return View(model);
		}
		else
		{
			await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Error, "SIGN_IN", $"Invalid login attempt.", user);

			ModelState.AddModelError(string.Empty, "Invalid login attempt.");
			return View(model);
		}
	}


	[HttpGet("account/logout")]
	public async Task<IActionResult> Logout()
	{
		await _signInManager.SignOutAsync();
		return LocalRedirect("/account/login");
	}


	[HttpGet("account/forgot")]
	public IActionResult ForgotPassword()
	{
		return View(new ForgotPasswordViewModel { Email = string.Empty, });
	}


	[HttpPost("account/forgot")]
	[ValidateAntiForgeryToken]
	[EnableRateLimiting("auth")]
	public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
	{
		if (!ModelState.IsValid)
		{
			return View(model);
		}

		// Look up by email, not user name: a user name isn't always the email address
		// (FindByNameAsync left such users unable to reset). Emails are unique (RequireUniqueEmail).
		var user = await _userManager.FindByEmailAsync(model.Email.Trim());

		// Every outcome below shows the same message, so this form can't be used to discover
		// which emails have accounts. The real outcome goes to the audit log only.
		if (user == null)
		{
			await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Error, "FORGOT_PASSWORD", $"User not found.", null);
			return ForgotPasswordSent(model);
		}

		if (!user.IsActive)
		{
			await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Error, "FORGOT_PASSWORD", $"Account is not active.", user);
			return ForgotPasswordSent(model);
		}

		var token = await _userManager.GeneratePasswordResetTokenAsync(user);
		var tokenLifespan = HttpContext.RequestServices
			.GetRequiredService<Microsoft.Extensions.Options.IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan;

		// Get email template
		var htmlTemplate = System.IO.File.ReadAllText(Path.Combine(_webHostEnvironment.WebRootPath, "templates", "html", "reset-password.html"));
		var template = Template.Parse(htmlTemplate);

		// The link goes to the user's own company's host (so "Back to login" after the reset lands on their
		// login page, wherever they asked), keeping this request's scheme and port. Users without one (Super
		// Admins) get this request's host. Safe only because AllowedHosts (appsettings.json) restricts the
		// Host header to our own domains — otherwise a forged Host would put the reset token in a link to an
		// attacker's site (password-reset poisoning).
		var userTenant = await _context.Users.AsNoTracking()
			.Where(u => u.Id == user.Id)
			.Select(u => u.UserCompany == null ? (Guid?)null : u.UserCompany.Id)
			.FirstOrDefaultAsync() is Guid userCompanyId
				? await _tenants.FindByIdAsync(userCompanyId)
				: null;
		var resetBaseUrl = userTenant != null ? Request.CompanyBaseUrl(_tenancy, userTenant) : $"{Request.Scheme}://{Request.Host}";

		var templateData = new
		{
			RecipientName = $"{user.FirstName} {user.LastName}",
			ResetLink = $"{resetBaseUrl}/account/reset-password?token={Base64UrlEncoder.Encode(token)}&id={WebUtility.UrlEncode(user.Id)}",
			ExpiryMinutes = (int)tokenLifespan.TotalMinutes,
		};

		var renderedHtml = template.Render(templateData);

		var bodyBuilder = new BodyBuilder();
		bodyBuilder.HtmlBody = renderedHtml;

		try
		{
			var smtpServer = await _appSettings.GetValueAsync(Guid.Empty, "email", "smtp_server");
			var smtpPort = int.TryParse(await _appSettings.GetValueAsync(Guid.Empty, "email", "smtp_port"), out var p) ? p : 25;
			var senderName = await _appSettings.GetValueAsync(Guid.Empty, "email", "sender_name");
			var senderEmail = await _appSettings.GetValueAsync(Guid.Empty, "email", "sender_email");
			var smtpUser = await _appSettings.GetValueAsync(Guid.Empty, "email", "email_username");
			var smtpPass = await _appSettings.GetValueAsync(Guid.Empty, "email", "email_password");

			var message = new MimeMessage();
			message.From.Add(new MailboxAddress(senderName, senderEmail));
			message.To.Add(new MailboxAddress($"{user.FirstName} {user.LastName}", user.Email));
			message.Subject = "Request for password reset";
			message.Body = bodyBuilder.ToMessageBody();

			using var client = new SmtpClient();

			await SmtpHelper.ConnectAsync(client, smtpServer, smtpPort);
			await client.AuthenticateAsync(smtpUser, smtpPass);

			await client.SendAsync(message);
			await client.DisconnectAsync(true);
		}
		catch (Exception ex)
		{
			// Still show the generic message: an error shown only for real accounts would reveal they exist.
			_logger.LogError(ex, "Failed to send password reset email for user {UserId}", user.Id);
			await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Error, "FORGOT_PASSWORD", $"Failed to send reset password email: {ex.Message}", user);
			return ForgotPasswordSent(model);
		}

		await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Information, "FORGOT_PASSWORD", $"Reset password link has been sent successfully.", user);

		return ForgotPasswordSent(model);
	}


	private IActionResult ForgotPasswordSent(ForgotPasswordViewModel model)
	{
		model.Email = string.Empty;
		model.ErrorMessage = string.Empty;
		model.EmailSent = true;
		model.Message = "If an account exists for that email, a password reset link has been sent to it. Please check your inbox (and spam folder).";

		return View(nameof(ForgotPassword), model);
	}


	private const string InvalidResetLinkMessage =
		"This password reset link is invalid or has expired. Please <a href=\"/account/forgot\">request a new one</a>.";


	[HttpGet("account/reset-password")]
	public IActionResult ResetPassword(string id, string token)
	{
		var model = new ResetPasswordViewModel
		{
			Password = string.Empty,
			ConfirmPassword = string.Empty,
			Id = id,
			ResetToken = token,
			ErrorMessage = string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token) ? InvalidResetLinkMessage : string.Empty,
		};

		return View(model);
	}


	[HttpPost("account/reset-password")]
	[ValidateAntiForgeryToken]
	[EnableRateLimiting("auth")]
	public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
	{
		if (!ModelState.IsValid)
		{
			return View(model);
		}

		// Always re-render with the model on failure: the view reads Model, and the hidden
		// Id/ResetToken fields must survive so the user can retry (e.g. after a too-weak password).
		if (string.IsNullOrEmpty(model.Id) || string.IsNullOrEmpty(model.ResetToken))
		{
			model.ErrorMessage = InvalidResetLinkMessage;
			return View(model);
		}

		var user = await _userManager.FindByIdAsync(model.Id);

		if (user == null)
		{
			await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Error, "RESET_PASSWORD", $"User not found.", null);

			model.ErrorMessage = InvalidResetLinkMessage;
			return View(model);
		}

		string token;
		try
		{
			token = Base64UrlEncoder.Decode(model.ResetToken);
		}
		catch (Exception ex) when (ex is FormatException or ArgumentException)
		{
			await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Error, "RESET_PASSWORD", $"Reset password failed: malformed token.", user);

			model.ErrorMessage = InvalidResetLinkMessage;
			return View(model);
		}

		var _result = await _userManager.ResetPasswordAsync(user, token, model.Password);

		if (!_result.Succeeded)
		{
			foreach (var error in _result.Errors)
			{
				if (error.Code == nameof(IdentityErrorDescriber.InvalidToken))
					model.ErrorMessage = InvalidResetLinkMessage;
				else
					// Empty key so the view's ModelOnly validation summary shows it (e.g. password policy errors).
					ModelState.AddModelError(string.Empty, error.Description);
			}

			await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Error, "RESET_PASSWORD", $"Reset password failed: {string.Join(", ", _result.Errors.Select(e => e.Code))}.", user);

			return View(model);
		}

		model.ResetOK = true;
		model.Message = "Your password has been reset successfully.";

		await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Information, "RESET_PASSWORD", $"Password has been reset successfully.", user);

		return View(model);
	}


	[Authorize]
	[HttpGet("{company}/account/profile")]
	public async Task<IActionResult> Profile()
	{
		var currentUser = await _currentUserService.GetCurrentUserAsync();
		var profileImageUrl = string.Empty;

		// Check for profile image if exists
		if (currentUser.ProfileImage != null)
		{
			profileImageUrl = Path.Combine(_webHostEnvironment.WebRootPath, "img", "profiles", currentUser.ProfileImage);
		}

		var imageFile = currentUser.ProfileImage;

		var model = new ProfileMainViewModel
		{
			ProfileForm = new ProfileViewModel
			{
				UserId = currentUser.Id,
				FirstName = currentUser.FirstName,
				LastName = currentUser.LastName,
				Email = currentUser.Email,
				Designation = currentUser.Designation,
				ProfileImageUrl = System.IO.File.Exists(profileImageUrl) ? $"/img/profiles/{currentUser.ProfileImage}" : "/img/user-default.png",
				HasProfileImage = !string.IsNullOrEmpty(currentUser.ProfileImage) && currentUser.ProfileImage != "/img/user-default.png",
				RoleNames = currentUser.UserRoles.Select(r => r.Role!.Name!).ToList(),
				IsSuperAdmin = currentUser.UserRoles.Any(r => r.Role!.NormalizedName == "SUPERUSER"),
				PhoneNumber = currentUser.PhoneNumber,
			},
			ProfilePasswordForm = new ProfilePasswordViewModel
			{
				UserId = currentUser.Id,
				CurrentPassword = string.Empty,
				Password = string.Empty,
				ConfirmPassword = string.Empty,
				ProfileImageUrl = System.IO.File.Exists(profileImageUrl) ? $"/img/profiles/{currentUser.ProfileImage}" : "/img/user-default.png",
			}
		};

		return View(model);
	}


	[Authorize]
	[HttpPost("{company}/account/profile")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Profile(Guid company, ProfileMainViewModel model)
	{
		ModelState.Remove((model.ProfileForm == null) ? "ProfileForm" : "ProfilePasswordForm");
			
		if (!ModelState.IsValid)
			return View(model);

		if (model.ProfileForm != null)
		{
			var user = await _context.Users.FirstAsync(e => e.Id == model.ProfileForm.UserId);

			if (user == null)
				return NotFound("User not found");

			var profileImageUrl = string.Empty;
			if (user.ProfileImage != null)
			{
				profileImageUrl = Path.Combine(_webHostEnvironment.WebRootPath, "img", "profiles", user.ProfileImage);
			}
			
			var form = model.ProfileForm;
			
			user.FirstName = form.FirstName;
			user.LastName = form.LastName;
			user.Designation = form.Designation;
			user.PhoneNumber = form.PhoneNumber;

			_context.Users.Update(user);
			await _context.SaveChangesAsync();

			model.ProfilePasswordForm = new ProfilePasswordViewModel
			{
				UserId = user.Id,
				CurrentPassword = string.Empty,
				Password = string.Empty,
				ConfirmPassword = string.Empty,
				ProfileImageUrl = System.IO.File.Exists(profileImageUrl) ? $"/img/profiles/{user.ProfileImage}" : "/img/user-default.png",
			};

			TempData["SuccessMessage"] = "Information has been successfully updated.";
		}
		else
		{
			var user = await _context.Users.FirstAsync(e => e.Id == model.ProfilePasswordForm.UserId);

			if (user == null)
				return NotFound("User not found");

			var profileImageUrl = string.Empty;
			if (user.ProfileImage != null)
			{
				profileImageUrl = Path.Combine(_webHostEnvironment.WebRootPath, "img", "profiles", user.ProfileImage);
			}

			var form = model.ProfilePasswordForm;
			//var user = await _userManager.FindByIdAsync(form.UserId);

			var result = await _userManager.ChangePasswordAsync(user, form.CurrentPassword, form.ConfirmPassword);

			model.ProfileForm = new ProfileViewModel
			{
				UserId = user.Id,
				FirstName = user.FirstName,
				LastName = user.LastName,
				Email = user.Email,
				Designation = user.Designation,
				ProfileImageUrl = System.IO.File.Exists(profileImageUrl) ? $"/img/profiles/{user.ProfileImage}" : "/img/user-default.png",
				HasProfileImage = !string.IsNullOrEmpty(user.ProfileImage) && user.ProfileImage != "/img/user-default.png",
				RoleNames = user.UserRoles.Select(r => r.Role!.Name!).ToList(),
				IsSuperAdmin = user.UserRoles.Any(r => r.Role!.NormalizedName == "SUPERUSER"),
				PhoneNumber = user.PhoneNumber,
			};

			if (!result.Succeeded)
			{
				TempData["ErrorMessage"] = string.Join(", ", result.Errors.Select(e => new { e.Description }).ToList());
				return View(model);
			}

			TempData["SuccessMessage"] = "Successfully changed your password. For security reasons, you are encouraged to log out and log in back using the new password.";			
		}

		return View(model);
	}


	[Authorize]
	[HttpPost("{company}/account/profile/change-password")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ChangePassword(Guid company, ProfileMainViewModel model)
	{
		ModelState.Remove("ProfileForm");

		if (!ModelState.IsValid)
			return View(nameof(Profile), model);

		var form = model.ProfilePasswordForm;

		var user = await _userManager.FindByIdAsync(form.UserId);

		if (user == null)
			return NotFound("User not found!");

		var result = await _userManager.ChangePasswordAsync(user, form.CurrentPassword, form.ConfirmPassword);

		if (!result.Succeeded)
		{
			TempData["ErrorMesssage"] = string.Join(", ", result.Errors.Select(e => new { Message = e.Description}).ToList());
			return Redirect($"/{company}/account/profile");
		}

		TempData["SuccessMessage"] = "Successfully changed your password. For security reasons, you are encouraged to log out and log in back using the new password.";

		return View(nameof(Profile), model);
	}

	[HttpPut("{company}/account/profile")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Profile(Guid company)
	{
		try
		{
			var req = Request.Form;

			if (req == null)
			{
				return BadRequest("Request is empty");
			}

			var user = await _userManager.FindByIdAsync(req["UserId"]);

			if (user == null)
			{
				return BadRequest("User not found");
			}

			// Update user's properties
			user.FirstName = req["FirstName"];
			user.LastName = req["LastName"];
			user.Designation = req["Designation"];
			user.Email = req["Email"];

			await _userManager.UpdateAsync(user);

			// Update user's email address
			await _userManager.UpdateNormalizedEmailAsync(user);

			return Ok();
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}


	[Authorize]
	[HttpPut("{company}/account/changepassword")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ChangePasswordX(Guid company)
	{
		try
		{
			var req = Request.Form;

			if (req == null)
			{
				return BadRequest(new
				{
					Message = "Request is empty",
				});
			}

			var user = await _userManager.FindByIdAsync(req["UserId"]);

			if (user == null)
			{
				return BadRequest(new
				{
					Message = "User not found",
				});
			}

			var result = await _userManager.ChangePasswordAsync(user, req["Current"], req["New"]);

			if (!result.Succeeded)
			{
				return BadRequest(new
				{
					Message = "Unable to process your request due to:",
					Errors = result.Errors.ToList(),
				});
			}

			return Ok();
		}
		catch (Exception ex)
		{
			return BadRequest(new
			{
				Message = ex.Message,
			});
		}
	}


	[Authorize()]
	[HttpPut("{company}/account/removeimage")]
	public async Task<IActionResult> RemoveImage(Guid company)
	{
		var currentUser = _currentUserService.GetCurrentUser();

		if (currentUser == null)
		{
			return BadRequest(new
			{
				Message = "User not found"
			});
		}

		if (!string.IsNullOrEmpty(currentUser.ProfileImage))
		{
			var fullPath = Path.Combine(_webHostEnvironment.WebRootPath, "img", "profile", currentUser.ProfileImage);

			if (System.IO.File.Exists(fullPath))
			{
				System.IO.File.Delete(fullPath);
			}

			currentUser.ProfileImage = null;
			await _userManager.UpdateAsync(currentUser);

			return Ok(new
			{
				FileRemoved = true,
			});
		}

		return Ok(new
		{
			FileRemoved = false,
		});
	}


	public IActionResult AccessDenied()
	{
		var userId = _httpContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
		var currentUser = _context.Users.Include(f => f.UserCompany).Single(f => f.Id == userId);

		return LocalRedirect($"~/{currentUser.UserCompany.Id}/restricted/accessdenied");
	}


	public async Task<ActionResult> UploadFile(IEnumerable<IFormFile> files)
	{
		var newFileName = string.Empty;

		// The Name of the Upload component is "files"
		if (files != null)
		{
			var currentUser = _currentUserService.GetCurrentUser();

			foreach (var file in files)
			{
				var fileContent = ContentDispositionHeaderValue.Parse(file.ContentDisposition);

				// Some browsers send file names with full path.
				// We are only interested in the file name.
				var fileName = Path.GetFileName(fileContent.FileName.ToString().Trim('"'));
				var extension = Path.GetExtension(fileContent.FileName.ToString().Trim('"'));
				newFileName = $"{Guid.NewGuid()}{extension}";

				var physicalPath = Path.Combine(_webHostEnvironment.WebRootPath, "img", "profile", newFileName);

				// The files are not actually saved in this demo
				using (var fileStream = new FileStream(physicalPath, FileMode.Create))
				{
					await file.CopyToAsync(fileStream);

					currentUser.ProfileImage = newFileName;
					await _userManager.UpdateAsync(currentUser);
				}
			}
		}

		return Ok(new
		{
			FileName = newFileName,
		});
	}


	// ── Avatar upload (AJAX) ──────────────────────────────────────────────────────

	[HttpPost("{company}/account/profile/upload-avatar")]
	public async Task<IActionResult> UploadAvatar(Guid id, IFormFile file)
	{
		if (!CanAccess(UserId)) return Forbid();

		var user = await _context.Users.FirstOrDefaultAsync(e => e.Id == UserId.ToString());
		if (user == null) return NotFound();

		if (file == null || file.Length == 0)
			return BadRequest(new { error = "No file provided." });

		var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
		if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp"))
			return BadRequest(new { error = "Only image files are allowed." });

		await using var stream = file.OpenReadStream();
		var (fileName, path, _) = await FileSaveAsync(stream, file.FileName, "profiles");

		if (!string.IsNullOrEmpty(user.ProfileImage))
			await FileDeleteAsync(user.ProfileImage);

		user.ProfileImage = fileName;
		_context.Users.Update(user);

		await _context.SaveChangesAsync();

		return Ok(new { path = $"{fileName}" });
	}


	#region Token (JWT)

	/// <summary>
	/// Issues a signed JWT for API/mobile clients.
	/// POST /api/auth/token
	/// Body: { "email": "...", "password": "...", "companyId": "..." }
	/// </summary>
	[HttpPost("api/auth/token")]
	[AllowAnonymous]
	[EnableRateLimiting("auth")]
	public async Task<IActionResult> GetToken([FromBody] TokenRequest request)
	{
		var user = _context.Users
			.Include(e => e.UserCompany)
			.Include(e => e.UserRoles).ThenInclude(e => e.Role)
			.FirstOrDefault(e => e.Email == request.Email);

		if (user == null)
			return Unauthorized(new { message = "Invalid credentials" });

		if (!user.IsActive)
			return Unauthorized(new { message = "Account is inactive" });

		var isSuperUser = user.UserRoles.Any(e => e.Role.NormalizedName == "SUPERUSER");

		if (!isSuperUser && request.CompanyId != null)
		{
			if (user.UserCompany?.Id.ToString() != request.CompanyId)
				return Unauthorized(new { message = "Invalid company" });
		}

		var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

		if (!result.Succeeded)
		{
			if (result.IsLockedOut)
				return Unauthorized(new { message = "Account is locked. Please try again in 10 minutes." });

			return Unauthorized(new { message = "Invalid credentials" });
		}

		var roles = user.UserRoles.Select(r => r.Role!.Name!).ToList();

		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, user.Id),
			new(ClaimTypes.Email, user.Email!),
			new(ClaimTypes.Name, user.UserName!),
			new("company", user.UserCompany?.Id.ToString() ?? string.Empty),
		};
		claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

		var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
		var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
		var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiresInMinutes);

		var token = new JwtSecurityToken(
			issuer: _jwtSettings.Issuer,
			audience: _jwtSettings.Audience,
			claims: claims,
			expires: expires,
			signingCredentials: creds);

		await _logService.LogMessageAsync(Core.Enums.LogTypeEnum.Information, "TOKEN", $"JWT issued for user ({user.UserName}).", user);

		return Ok(new
		{
			token = new JwtSecurityTokenHandler().WriteToken(token),
			expires,
			companyId = user.UserCompany?.Id
		});
	}

	#endregion


	#region Private methods/functions

	// Only local URLs (no open redirect); the company's home page otherwise.
	private string SafeReturnUrl(string returnUrl)
		=> !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";


	// ".permitpro.app" (".localhost:5144" in development), shown after the address box.
	private string HostSuffix()
		=> $".{_tenancy.RootDomain}{(Request.Host.Port is int port ? $":{port}" : "")}";


	private async Task<Guid?> CurrentUserCompanyIdAsync(CancellationToken ct)
	{
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (userId == null) return null;

		return await _context.Users.AsNoTracking()
			.Where(u => u.Id == userId)
			.Select(u => u.UserCompany == null ? (Guid?)null : u.UserCompany.Id)
			.FirstOrDefaultAsync(ct);
	}


	private async Task<(string storedName, string path, long size)> FileSaveAsync(Stream stream, string originalFileName, string folder, CancellationToken ct = default)
	{
		var ext = Path.GetExtension(originalFileName);
		var storedName = $"{Guid.NewGuid()}{ext}";
		var dir = Path.Combine(_webHostEnvironment.WebRootPath, "img", folder);

		Directory.CreateDirectory(dir);

		var fullPath = Path.Combine(dir, storedName);

		await using var fs = new FileStream(fullPath, FileMode.Create);
		await stream.CopyToAsync(fs, ct);

		return (storedName, fullPath, fs.Length);
	}


	private Task FileDeleteAsync(string filePath, CancellationToken ct = default)
	{
		if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
		return Task.CompletedTask;
	}


	protected Guid UserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

	private bool CanAccess(Guid targetUserId) => targetUserId == UserId || IsInRole("Portal Admin") || IsInRole("Super User");


	protected bool IsInRole(string role) => User.IsInRole(role);


	private UserInfo CreateUser()
	{
		try
		{
			return Activator.CreateInstance<UserInfo>();
		}
		catch
		{
			throw new InvalidOperationException($"Can't create an instance of '{nameof(UserInfo)}'. " +
				 $"Ensure that '{nameof(UserInfo)}' is not an abstract class and has a parameterless constructor, or alternatively " +
				 $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
		}
	}


	private IUserEmailStore<UserInfo> GetEmailStore()
	{
		if (!_userManager.SupportsUserEmail)
		{
			throw new NotSupportedException("The default UI requires a user store with email support.");
		}

		return (IUserEmailStore<UserInfo>)_userStore;
	}

	#endregion

}
