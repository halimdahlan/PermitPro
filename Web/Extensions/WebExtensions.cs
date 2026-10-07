using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using PermitPro.App.Services;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interceptors;
using PermitPro.Core.Interfaces;

using System.Text;
using System.Threading.RateLimiting;

namespace PermitPro.App.Extensions;

public static class WebExtensions
{
	public static IServiceCollection AddDatabase(this IServiceCollection services, string connectionString)
	{
		services.AddDbContext<ApplicationDbContext>((sp, options) =>
		{
			var auditableInterceptor = sp.GetService<UpdateAuditableEntitiesInterceptor>()!;
			options
				.UseSqlServer(connectionString)
				.AddInterceptors(auditableInterceptor);
		});

		services.AddDatabaseDeveloperPageExceptionFilter();

		return services;
	}

	public static IServiceCollection AddIdentityConfiguration(this IServiceCollection services)
	{
		services.AddIdentity<UserInfo, Role>(options =>
		{
			options.SignIn.RequireConfirmedEmail = true;
		})
			.AddRoles<Role>()
			.AddEntityFrameworkStores<ApplicationDbContext>()
			.AddDefaultTokenProviders();

		services.Configure<IdentityOptions>(options =>
		{
			options.Password.RequireDigit = true;
			options.Password.RequireLowercase = true;
			options.Password.RequireNonAlphanumeric = true;
			options.Password.RequireUppercase = true;
			options.Password.RequiredLength = 12;
			options.Password.RequiredUniqueChars = 1;

			options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
			options.Lockout.MaxFailedAccessAttempts = 3;
			options.Lockout.AllowedForNewUsers = true;

			options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+#$!%&*";
			options.User.RequireUniqueEmail = true;
		});

		services.ConfigureApplicationCookie(options =>
		{
			options.Cookie.HttpOnly = true;
			// Lax, not Strict: Strict withholds the cookie when a user opens a link from another
			// app (email, WhatsApp), so they appear logged out. Lax still blocks the cookie on
			// cross-site POSTs, which is what protects actions lacking [ValidateAntiForgeryToken].
			options.Cookie.SameSite = SameSiteMode.Lax;

			// Default session lifetime (RememberMe unchecked): 3 hours of inactivity.
			// When RememberMe IS checked, OnSigningIn overrides this to 30 days below.
			options.ExpireTimeSpan = TimeSpan.FromHours(3);
			options.SlidingExpiration = true;

			options.LoginPath = "/account/login";
			options.AccessDeniedPath = "/account/accessdenied";

			// Extend persistent-cookie lifetime when the user checks "Remember me".
			// Without this override, isPersistent:true would still expire after 3 hours,
			// defeating the purpose of the feature.
			options.Events.OnSigningIn = context =>
			{
				if (context.Properties.IsPersistent)
					context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);

				return Task.CompletedTask;
			};
		});

		services.Configure<DataProtectionTokenProviderOptions>(options =>
		{
			options.TokenLifespan = TimeSpan.FromMinutes(30);
		});

		return services;
	}

	public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, JwtSettings jwtSettings)
	{
		// JWT Bearer is an additional scheme alongside the default Identity cookie scheme.
		// Use [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] on
		// API endpoints that should be authenticated via Bearer token.
		services.AddAuthentication()
			.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
			{
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidateLifetime = true,
					ValidateIssuerSigningKey = true,
					ValidIssuer = jwtSettings.Issuer,
					ValidAudience = jwtSettings.Audience,
					IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
					ClockSkew = TimeSpan.Zero
				};
			});

		return services;
	}

	// Cloudflare's published edge ranges (https://www.cloudflare.com/ips-v4 and /ips-v6, fetched 2026-10-07).
	// Re-check occasionally — Cloudflare changes these rarely but does change them.
	private static readonly string[] CloudflareNetworks =
	[
		"173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22",
		"141.101.64.0/18", "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20",
		"197.234.240.0/22", "198.41.128.0/17", "162.158.0.0/15", "104.16.0.0/13",
		"104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22",
		"2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32",
		"2405:8100::/32", "2a06:98c0::/29", "2c0f:f248::/32",
	];

	public static IServiceCollection AddCloudflareForwardedHeaders(this IServiceCollection services)
	{
		// The site is proxied through Cloudflare, so without this every request appears to come
		// from a Cloudflare edge IP. Restore the real client IP from CF-Connecting-IP — but only
		// when the connection actually comes from Cloudflare: the origin IP is public, and a client
		// connecting directly could otherwise spoof the header.
		services.Configure<ForwardedHeadersOptions>(options =>
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
			options.ForwardedForHeaderName = "CF-Connecting-IP";

			foreach (var network in CloudflareNetworks)
				options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
		});

		return services;
	}

	public static IServiceCollection AddRateLimiting(this IServiceCollection services)
	{
		// Rate-limit auth endpoints: 20 requests per 1-minute window per client IP.
		// Identity's per-account lockout (3 attempts) handles brute-force per user;
		// this policy handles distributed/credential-stuffing attacks at the IP level.
		// Partitioned per IP (via AddCloudflareForwardedHeaders) rather than one shared bucket —
		// a shared bucket would throttle every user at once. The limit allows for many workers
		// behind one site Wi-Fi / mobile carrier IP logging in together at shift start.
		services.AddRateLimiter(options =>
		{
			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

			options.AddPolicy("auth", httpContext =>
				RateLimitPartition.GetFixedWindowLimiter(
					partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
					factory: _ => new FixedWindowRateLimiterOptions
					{
						PermitLimit = 20,
						Window = TimeSpan.FromMinutes(1),
						QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
						QueueLimit = 0,
					}));
		});

		return services;
	}

	public static IServiceCollection AddWebServices(this IServiceCollection services, string connectionString)
	{
		services.AddHealthChecks()
			.AddSqlServer(connectionString, name: "sqlserver", timeout: TimeSpan.FromSeconds(5));

		services.AddResponseCaching();
		services.AddSession(options => { options.IdleTimeout = TimeSpan.FromMinutes(60); });
		services.AddControllersWithViews();
		services.AddRazorPages();
		services.AddKendo();
		services.AddSignalR();
		services.AddScoped<INotificationPushService, NotificationPushService>();

		return services;
	}
}
