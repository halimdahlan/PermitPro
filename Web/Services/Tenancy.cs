using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using PermitPro.Core.Data;
using PermitPro.Core.Helpers;

using System.Security.Claims;

namespace PermitPro.App.Services;

// Each company is served on its own host: iotm.permitpro.app. TenantResolutionMiddleware (before routing)
// finds the company from the host and puts its id in front of the path, so the existing
// {company}/{controller}/... routes and controllers keep working unchanged. TenantAccessMiddleware (after
// authentication) makes sure a signed-in user only ever sees their own company's pages.
//
// Addresses on a company's host carry no company id (iotm.permitpro.app/permits): views build links with
// Context.CompanyPathBase() (scripts: the companyBase global), redirects from controllers are stripped of the
// id, and an old /{company id}/... address opened in the browser is redirected to the same page without it.
//
// The apex (permitpro.app) has no company: it shows the company address page and Super Admin (/admin),
// and sends old /{company id}/... links on to that company's host.


public sealed record TenantInfo(Guid Id, string Subdomain, string Name, string? Description, string? LogoFileName)
{
	public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Name : Description;
}


public sealed record TenantContext(TenantHostKind HostKind, TenantInfo? Tenant);


public static class TenantHttpContextExtensions
{
	private const string ItemKey = "PermitPro.TenantContext";

	public static TenantContext? GetTenantContext(this HttpContext context)
		=> context.Items.TryGetValue(ItemKey, out var value) ? value as TenantContext : null;

	// The company whose host this request came in on; null on the apex.
	public static TenantInfo? GetTenant(this HttpContext context) => context.GetTenantContext()?.Tenant;

	internal static void SetTenantContext(this HttpContext context, TenantContext tenantContext)
	{
		context.Items[ItemKey] = tenantContext;

		if (tenantContext.Tenant != null)
			context.Items[CompanyPaths.ItemKey] = "";
	}

	// Prefix for links to this company's pages: "" on its own host (/permits), "/{company id}" elsewhere.
	// Views: href="@Context.CompanyPathBase()/permits"; scripts use the companyBase global set in _Layout.
	public static string CompanyPathBase(this HttpContext context)
		=> context.Items.TryGetValue(CompanyPaths.ItemKey, out var value) && value is string pathBase ? pathBase
			: Guid.TryParse(context.GetRouteValue("company")?.ToString(), out var id) && id != Guid.Empty ? $"/{id}"
			: "";

	// Base URL of a company's site, keeping this request's scheme and port (iotm.localhost:5144 in development).
	public static string CompanyBaseUrl(this HttpRequest request, TenancySettings settings, TenantInfo tenant)
		=> $"{request.Scheme}://{settings.CompanyHost(tenant.Subdomain)}{(request.Host.Port is int port ? $":{port}" : "")}";

	public static string ApexBaseUrl(this HttpRequest request, TenancySettings settings)
		=> $"{request.Scheme}://{settings.RootDomain}{(request.Host.Port is int port ? $":{port}" : "")}";
}


/// <summary>Active companies that have a subdomain. There are few, so all are loaded and cached together.</summary>
public class TenantDirectory
{
	private const string CacheKey = "tenancy:directory";
	private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IMemoryCache _cache;

	public TenantDirectory(IServiceScopeFactory scopeFactory, IMemoryCache cache)
	{
		_scopeFactory = scopeFactory;
		_cache = cache;
	}

	public async Task<TenantInfo?> FindBySubdomainAsync(string subdomain, CancellationToken ct = default)
		=> (await LoadAsync(ct)).FirstOrDefault(t => string.Equals(t.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));

	public async Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct = default)
		=> (await LoadAsync(ct)).FirstOrDefault(t => t.Id == id);

	// Call after creating, editing, (de)activating or deleting a company.
	public void Invalidate() => _cache.Remove(CacheKey);

	private async Task<IReadOnlyList<TenantInfo>> LoadAsync(CancellationToken ct)
	{
		if (_cache.TryGetValue(CacheKey, out IReadOnlyList<TenantInfo>? cached) && cached != null)
			return cached;

		using var scope = _scopeFactory.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

		// The soft-delete query filter leaves out deleted companies.
		var tenants = await db.Companies
			.AsNoTracking()
			.Where(c => c.IsActive && c.Subdomain != null)
			.Select(c => new TenantInfo(c.Id, c.Subdomain!, c.Name, c.Description, c.LogoFileName))
			.ToListAsync(ct);

		_cache.Set(CacheKey, (IReadOnlyList<TenantInfo>)tenants, CacheDuration);
		return tenants;
	}
}


public class TenantResolutionMiddleware
{
	// Not company pages: served as they are on every host (static files are served before this runs).
	private static readonly string[] SharedPaths =
	[
		"/account/login", "/account/logout", "/account/forgot", "/account/reset-password", "/account/find",
		"/admin", "/api", "/error", "/health", "/_",
	];

	private readonly RequestDelegate _next;
	private readonly TenancySettings _settings;
	private readonly TenantDirectory _directory;

	public TenantResolutionMiddleware(RequestDelegate next, TenancySettings settings, TenantDirectory directory)
	{
		_next = next;
		_settings = settings;
		_directory = directory;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		var request = context.Request;
		var path = request.Path.Value ?? "/";
		var (kind, subdomain) = _settings.ParseHost(request.Host.Host);
		var pathCompany = Guid.TryParse(FirstSegment(path), out var id) && id != Guid.Empty ? id : (Guid?)null;

		if (kind is not (TenantHostKind.Company or TenantHostKind.Alias))
		{
			context.SetTenantContext(new TenantContext(kind, null));

			// Old links and bookmarks (permitpro.app/{company id}/permits) go to that company's own host, without
			// the id (iotm.permitpro.app/permits).
			if (pathCompany is Guid companyId && IsGetOrHead(request)
				&& await _directory.FindByIdAsync(companyId, context.RequestAborted) is { } linked)
			{
				var rest = path[(FirstSegment(path).Length + 1)..];
				context.Response.Redirect($"{request.CompanyBaseUrl(_settings, linked)}{(rest.Length == 0 ? "/" : rest)}{request.QueryString}");
				return;
			}

			await _next(context);
			return;
		}

		var tenant = subdomain == null ? null : await _directory.FindBySubdomainAsync(subdomain, context.RequestAborted);
		context.SetTenantContext(new TenantContext(kind, tenant));

		if (tenant == null)
		{
			// No active company here. Re-executed error pages and health checks still work.
			if (!IsUnder(path, "/error") && !IsUnder(path, "/health"))
				request.Path = "/error/company-not-found";

			await _next(context);
			return;
		}

		// Controllers redirect to /{company id}/... (RedirectToAction, LocalRedirect, the login page's ReturnUrl):
		// routing needs the id, the address bar on the company's own host doesn't.
		var tenantId = tenant.Id.ToString();
		context.Response.OnStarting(() =>
		{
			StripCompanyIdFromRedirect(context.Response, tenantId);
			return Task.CompletedTask;
		});

		if (pathCompany != null)
		{
			// Another company's id on this company's host.
			if (pathCompany != tenant.Id)
			{
				context.Response.StatusCode = StatusCodes.Status404NotFound;
				return;
			}

			// An old link or bookmark with the id in it: the same page without it. Scripts' requests (fetch,
			// $.ajax) aren't redirected; their /{company id}/... URLs keep working as they are.
			if (IsPageNavigation(request))
			{
				var rest = path[(FirstSegment(path).Length + 1)..];
				context.Response.Redirect($"{(rest.Length == 0 ? "/" : rest)}{request.QueryString}");
				return;
			}
		}
		else if (IsUnder(path, "/admin") && kind == TenantHostKind.Company)
		{
			// Super Admin lives on the apex. An alias host (the demo site) has no apex, so it keeps /admin.
			context.Response.Redirect($"{request.ApexBaseUrl(_settings)}{path}{request.QueryString}");
			return;
		}
		else if (!SharedPaths.Any(p => IsUnder(path, p)))
		{
			// /permits/edit/5 → /{company id}/permits/edit/5 for routing.
			request.Path = $"/{tenant.Id}{(path == "/" ? "" : path)}";
		}

		await _next(context);
	}

	private static string FirstSegment(string path)
	{
		var trimmed = path.TrimStart('/');
		var slash = trimmed.IndexOf('/');
		return slash < 0 ? trimmed : trimmed[..slash];
	}

	private static bool IsUnder(string path, string prefix)
		=> path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			&& (path.Length == prefix.Length || path[prefix.Length] == '/' || prefix == "/_");

	private static bool IsGetOrHead(HttpRequest request) => HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);

	// The browser loading a page (link, address bar, location.href), as opposed to a script's request.
	private static bool IsPageNavigation(HttpRequest request)
	{
		if (!IsGetOrHead(request)) return false;

		var fetchMode = request.Headers["Sec-Fetch-Mode"].ToString();
		return fetchMode.Length > 0
			? fetchMode == "navigate"
			: request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
	}

	// /{id}/permits → /permits, and the same inside an encoded ReturnUrl (%2F{id}%2Fpermits → %2Fpermits).
	private static void StripCompanyIdFromRedirect(HttpResponse response, string companyId)
	{
		var location = response.Headers.Location.ToString();
		if (location.Length == 0) return;

		var stripped = location;
		var prefix = $"/{companyId}";

		if (stripped.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			&& (stripped.Length == prefix.Length || stripped[prefix.Length] is '/' or '?' or '#'))
		{
			var rest = stripped[prefix.Length..];
			stripped = rest.StartsWith('/') ? rest : $"/{rest}";
		}

		stripped = stripped
			.Replace($"%2F{companyId}%2F", "%2F", StringComparison.OrdinalIgnoreCase)
			.Replace($"%2F{companyId}", "%2F", StringComparison.OrdinalIgnoreCase);

		if (stripped != location)
			response.Headers.Location = stripped;
	}
}


public class TenantAccessMiddleware
{
	private static readonly TimeSpan UserCacheDuration = TimeSpan.FromMinutes(2);

	private readonly RequestDelegate _next;

	public TenantAccessMiddleware(RequestDelegate next) => _next = next;

	public async Task InvokeAsync(HttpContext context, ApplicationDbContext db, IMemoryCache cache)
	{
		var tenant = context.GetTenant();
		var routeCompany = Guid.TryParse(context.GetRouteValue("company")?.ToString(), out var id) && id != Guid.Empty ? id : (Guid?)null;

		// Another company's id inside the path, e.g. /api/data/{other company}/users on this company's host.
		if (tenant != null && routeCompany != null && routeCompany != tenant.Id)
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		var companyId = tenant?.Id ?? routeCompany;
		var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

		if (companyId != null && userId != null && context.User.Identity?.IsAuthenticated == true)
		{
			var (userCompanyId, isSuperAdmin) = await GetUserAsync(db, cache, userId, context.RequestAborted);

			if (!isSuperAdmin && userCompanyId != companyId)
			{
				if (tenant != null)
				{
					// Signed in on this host but no longer one of its users (e.g. moved to another company).
					await context.SignOutAsync(IdentityConstants.ApplicationScheme);
					context.Response.Redirect("/account/login");
				}
				else
				{
					// On the apex, a company that has no subdomain yet: no peeking at another company's pages.
					context.Response.StatusCode = StatusCodes.Status404NotFound;
				}
				return;
			}
		}

		await _next(context);
	}

	private static async Task<(Guid? CompanyId, bool IsSuperAdmin)> GetUserAsync(ApplicationDbContext db, IMemoryCache cache, string userId, CancellationToken ct)
	{
		var cacheKey = $"user:{userId}:tenant";
		if (cache.TryGetValue(cacheKey, out (Guid? CompanyId, bool IsSuperAdmin) cached))
			return cached;

		var user = await db.Users
			.AsNoTracking()
			.Where(u => u.Id == userId)
			.Select(u => new
			{
				CompanyId = u.UserCompany == null ? (Guid?)null : u.UserCompany.Id,
				IsSuperAdmin = u.UserRoles.Any(r => r.Role!.NormalizedName == "SUPERUSER"),
			})
			.FirstOrDefaultAsync(ct);

		(Guid? CompanyId, bool IsSuperAdmin) result = (user?.CompanyId, user?.IsSuperAdmin ?? false);
		cache.Set(cacheKey, result, UserCacheDuration);
		return result;
	}
}
