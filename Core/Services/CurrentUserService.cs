#nullable disable

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Interfaces;

using System.Security.Claims;

namespace PermitPro.Core.Services;

public class CurrentUserService : ICurrentUserService
{
	private readonly IHttpContextAccessor _dbContextAccessor;
	private readonly UserManager<UserInfo> _userManager;
	private readonly ApplicationDbContext _dbContext;
	private readonly IMemoryCache _cache;

	private static readonly TimeSpan RoleCacheDuration = TimeSpan.FromMinutes(2);

	public CurrentUserService(IHttpContextAccessor contextAccessor, UserManager<UserInfo> userManager, ApplicationDbContext context, IMemoryCache cache)
	{
		_dbContextAccessor = contextAccessor;
		_dbContext = context;
		_userManager = userManager;
		_cache = cache;
	}

	public async Task<bool> IsContractor()
	{
		var userRoles = await GetCurrentUserRoles();
		return userRoles.Contains("Contractor") || userRoles.Contains("Permit Holder / Contractor");
	}


	public async Task<bool> IsPermitIssuer()
	{
		var userRoles = await GetCurrentUserRoles();
		return userRoles.Contains("Permit Issuer");
	}


	public async Task<bool> IsLeadPermitIssuer()
	{
		var userRoles = await GetCurrentUserRoles();
		return userRoles.Contains("Lead Permit Issuer");
	}


	public async Task<bool> IsAGT()
	{
		var userRoles = await GetCurrentUserRoles();
		return userRoles.Contains("Authorized Gas Tester");
	}


	public async Task<UserInfo> GetCurrentUserAsync()
	{
		var userId = _dbContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
		return await _dbContext.Users
			.Include(f => f.UserCompany)
			.Include(f => f.UserRoles)
			.ThenInclude(f => f.Role)
			.FirstOrDefaultAsync(f => f.Id == userId);
	}


	public async Task<IList<string>> GetCurrentUserRoles()
	{
		var userId = _dbContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (userId == null) return new List<string>();

		var cacheKey = $"user:{userId}:roles";
		if (_cache.TryGetValue(cacheKey, out IList<string> cached))
			return cached;

		var user = await GetCurrentUserAsync();
		var roles = await _userManager.GetRolesAsync(user);
		_cache.Set(cacheKey, roles, RoleCacheDuration);
		return roles;
	}

	public void InvalidateRolesCache(string userId)
		=> _cache.Remove($"user:{userId}:roles");


	public Guid GetCurrentCompanyId()
	{
		// Prefer the user's assigned company (normal users)
		var user = GetCurrentUser();
		if (user?.UserCompany?.Id is Guid companyFromUser && companyFromUser != Guid.Empty)
			return companyFromUser;

		// Super admins have no company — read it from the route instead
		var routeValue = _dbContextAccessor.HttpContext?.Request.RouteValues["company"];
		if (routeValue != null && Guid.TryParse(routeValue.ToString(), out var companyFromRoute))
			return companyFromRoute;

		return Guid.Empty;
	}


	public UserInfo GetCurrentUser()
	{
		// No request (e.g. the permit rules background service): no current user.
		var userId = _dbContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
		if (userId == null) return null;

		return _dbContext.Users
			.Include(f => f.UserCompany)
			.Include(f => f.UserRoles)
			.ThenInclude(f => f.Role)
			.FirstOrDefault(f => f.Id == userId);
	}
}
