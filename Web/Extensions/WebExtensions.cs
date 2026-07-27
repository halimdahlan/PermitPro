using Microsoft.AspNetCore.Authentication.JwtBearer;
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
			options.Cookie.SameSite = SameSiteMode.Strict;

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

	public static IServiceCollection AddRateLimiting(this IServiceCollection services)
	{
		// Rate-limit auth endpoints: 10 requests per 5-minute window per IP.
		// Identity's per-account lockout (3 attempts) handles brute-force per user;
		// this policy handles distributed/credential-stuffing attacks at the IP level.
		services.AddRateLimiter(options =>
		{
			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

			options.AddFixedWindowLimiter("auth", opt =>
			{
				opt.PermitLimit = 10;
				opt.Window = TimeSpan.FromMinutes(5);
				opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
				opt.QueueLimit = 0;
			});
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
