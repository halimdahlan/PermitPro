#nullable disable

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Interfaces;

namespace PermitPro.App.Controllers.Base
{
	public class AppControllerBase : Controller
	{
		private readonly ApplicationDbContext _dbContext;
		private readonly IHttpContextAccessor _httpContextAccessor;
		private readonly ISystemConfigurationService _systemConfigService;
		private Guid _companyId;

		// Set in constructor from the company route segment.
		// Acted on in OnActionExecutionAsync so we can properly short-circuit.
		private bool _isInvalidCompany;   // segment is not a GUID, e.g. "/android-chrome-192x192.png"
		private bool _isEmptyCompany;     // Guid.Empty, e.g. the site root "/" (route default)

		public Guid CompanyID
		{
			get { return _companyId; }
		}


		public AppControllerBase(
			ApplicationDbContext dbContext
			, IHttpContextAccessor httpContextAccessor
			, SignInManager<UserInfo> signInManager
			, ISystemConfigurationService systemConfigurationService)
		{
			_dbContext = dbContext;
			_httpContextAccessor = httpContextAccessor;
			_systemConfigService = systemConfigurationService;

			_companyId = Guid.Empty;

			var routeValue = _httpContextAccessor!.HttpContext!.GetRouteValue("company");

			if (routeValue != null)
			{
				if (!Guid.TryParse(routeValue.ToString(), out _companyId))
					_isInvalidCompany = true;
				else if (_companyId == Guid.Empty)
					_isEmptyCompany = true;
			}
		}

		private static readonly TimeSpan CompanyMetaCacheDuration = TimeSpan.FromMinutes(15);

		// Async entry point: handles the not-found/redirect cases before the action executes.
		// base.OnActionExecutionAsync calls OnActionExecuting (ViewData setup) then next().
		//
		// Never sign out here. Any missing single-segment URL falls through to
		// {company}/landing/index — browsers and bots request these routinely (manifest icons,
		// apple-touch-icon probes) — and signing out on them kicked users out right after login.
		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			if (_isInvalidCompany)
			{
				context.Result = NotFound();
				return;
			}

			if (_isEmptyCompany)
			{
				// /account/login forwards an already-authenticated user to their own dashboard.
				context.Result = new RedirectResult("/account/login");
				return;
			}

			await base.OnActionExecutionAsync(context, next);
		}

		// Makes CompanyID and company branding available in every view.
		// Called by base.OnActionExecutionAsync before invoking next().
		public override void OnActionExecuting(ActionExecutingContext context)
		{
			ViewData["CompanyId"] = CompanyID;
			ViewData["CompanyIdStr"] = CompanyID.ToString();

			if (_companyId != Guid.Empty)
			{
				var cache = context.HttpContext.RequestServices.GetService<IMemoryCache>();
				var cacheKey = $"company:{_companyId}:meta";

				(string Name, string LogoFileName)? meta = null;

				if (cache != null && cache.TryGetValue(cacheKey, out (string Name, string LogoFileName) cached))
				{
					meta = cached;
				}
				else
				{
					var company = _dbContext.Companies
						.Where(c => c.Id == _companyId)
						.Select(c => new { c.Name, c.LogoFileName })
						.FirstOrDefault();

					if (company != null)
					{
						meta = (company.Name, company.LogoFileName);
						cache?.Set(cacheKey, meta.Value, CompanyMetaCacheDuration);
					}
				}

				if (meta.HasValue)
				{
					ViewData["CompanyName"] = meta.Value.Name;
					ViewData["CompanyLogoFileName"] = meta.Value.LogoFileName;
				}
			}

			base.OnActionExecuting(context);
		}

		// Call this from AdminController after editing or toggling a company.
		public static void InvalidateCompanyMetaCache(IMemoryCache cache, Guid companyId)
			=> cache?.Remove($"company:{companyId}:meta");
	}
}
