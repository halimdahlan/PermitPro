using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using PermitPro.App.Hubs;
using PermitPro.App.Services;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Extensions;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interceptors;
using PermitPro.Core.Interfaces;

using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var appConfig = builder.Configuration;

// Add services to the container.
var connectionString = appConfig.GetConnectionString(builder.Environment.EnvironmentName);
var ptwSettings = appConfig.GetSection("PTWSettings").Get<PTWSettings>();
var jwtSettings = appConfig.GetSection("JwtSettings").Get<JwtSettings>();

builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
	var auditableInterceptor = sp.GetService<UpdateAuditableEntitiesInterceptor>()!;

	options
		.UseSqlServer(connectionString)
		.AddInterceptors(auditableInterceptor);
});

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentity<UserInfo, Role>(options =>
{
	options.SignIn.RequireConfirmedEmail = true;
	//options.Tokens.ProviderMap.Add("CustomEmailConfirmation", new TokenProviderDescriptor(typeof(CustomEmailConfirmationTokenProvider<SiteUser>)));
	//options.Tokens.EmailConfirmationTokenProvider = "CustomEmailConfirmation";
})
	.AddRoles<Role>()
	.AddEntityFrameworkStores<ApplicationDbContext>()
	.AddDefaultTokenProviders();
	//.AddTokenProvider<CustomEmailConfirmationTokenProvider<SiteUser>>("CustomEmailConfirmation")
	//.AddDefaultUI();

// JWT Bearer is an additional scheme alongside the default Identity cookie scheme.
// Use [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] on
// API endpoints that should be authenticated via Bearer token.
builder.Services.AddAuthentication()
	.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
	{
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidateAudience = true,
			ValidateLifetime = true,
			ValidateIssuerSigningKey = true,
			ValidIssuer = jwtSettings!.Issuer,
			ValidAudience = jwtSettings.Audience,
			IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
			ClockSkew = TimeSpan.Zero
		};
	});

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddKendo();
builder.Services.AddSignalR();

builder.Services.Configure<IdentityOptions>(options =>
{
	// Password settings
	options.Password.RequireDigit = true;
	options.Password.RequireLowercase = true;
	options.Password.RequireNonAlphanumeric = true;
	options.Password.RequireUppercase = true;
	options.Password.RequiredLength = 12;
	options.Password.RequiredUniqueChars = 1;

	// Lockout settings
	options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
	options.Lockout.MaxFailedAccessAttempts = 3;
	options.Lockout.AllowedForNewUsers = true;

	// User settings
	options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+#$!%&*";
	options.User.RequireUniqueEmail = true;
});

builder.Services.ConfigureApplicationCookie(options =>
{
	// Cookie settings
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
		{
			context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
		}

		return Task.CompletedTask;
	};
});

builder.Services.AddSession(options =>
{
	options.IdleTimeout = TimeSpan.FromMinutes(60);
});

// Configure for ALL data protection tokens period to 30 minutes
#region "Data protection tokens"

builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
	options.TokenLifespan = TimeSpan.FromMinutes(30);
});

#endregion


builder.Services.AddSingleton(ptwSettings!);
builder.Services.AddSingleton(jwtSettings!);

// Rate-limit auth endpoints: 10 requests per 5-minute window per IP.
// Identity's per-account lockout (3 attempts) handles brute-force per user;
// this policy handles distributed/credential-stuffing attacks at the IP level.
builder.Services.AddRateLimiter(options =>
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

// Health checks
builder.Services.AddHealthChecks()
	.AddSqlServer(connectionString!, name: "sqlserver", timeout: TimeSpan.FromSeconds(5));

// Response caching for read-only endpoints
builder.Services.AddResponseCaching();

// Add PermitPro custom services
builder.Services.AddPermitProServices();
builder.Services.AddScoped<INotificationPushService, NotificationPushService>();

var app = builder.Build();

//await PermitPro.Core.Services.AppSettingsSeed.SeedDefaultsAsync(app.Services, appConfig);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseMigrationsEndPoint();
}
else
{
	app.UseExceptionHandler("/error");
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	app.UseHsts();
}

// Intercept non-success status codes (404, 403, etc.) and render the error view.
app.UseStatusCodePagesWithReExecute("/error/{0}");

app.UseRateLimiter();
app.UseResponseCaching();

app.Use(async (context, next) =>
{
	var headers = context.Response.Headers;

	// Prevent MIME-type sniffing
	headers.XContentTypeOptions = "nosniff";

	// Block clickjacking
	headers.XFrameOptions = "DENY";

	// Limit referrer information sent to other origins
	headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

	// Disable browser features not used by this app
	headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

	// Content Security Policy:
	//   - All assets (JS, CSS, fonts, images) are served locally.
	//   - cdn.jsdelivr.net is allowed for Chart.js loaded on the workflow overview.
	//   - 'unsafe-inline' is required because Kendo UI and Razor emit inline scripts/styles.
	//   - connect-src includes wss: for SignalR in production and ws: for development.
	//   - static.cloudflareinsights.com is Cloudflare's edge-injected beacon (present when the
	//     host is proxied through Cloudflare); allowed in both script-src and connect-src since
	//     the beacon script loads then reports metrics via its own network call.
	headers.ContentSecurityPolicy =
		"default-src 'self'; " +
		"script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net https://cdn.datatables.net https://cdnjs.cloudflare.com https://unpkg.com https://ka-f.webawesome.com https://maps.googleapis.com https://maps.gstatic.com https://static.cloudflareinsights.com; " +
		"style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdn.datatables.net https://unpkg.com https://fonts.googleapis.com https://ka-f.webawesome.com https://maps.googleapis.com; " +
		"img-src 'self' data: blob: https://unpkg.com https://*.googleapis.com https://*.gstatic.com https://*.google.com https://*.tile.openstreetmap.org; " +
		"font-src 'self' data: https://fonts.gstatic.com https://ka-f.webawesome.com; " +
		"connect-src 'self' data: ws: wss: https://ka-f.webawesome.com https://maps.googleapis.com https://maps.gstatic.com https://cloudflareinsights.com; " +
		"worker-src 'self' blob:; " +
		"frame-src 'none'; " +
		"object-src 'none'; " +
		"base-uri 'self';";

	await next();
});

app.UseSession();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
	name: "default",
	pattern: "{company}/{controller}/{action}/{id?}",
	defaults: new { company = Guid.Empty, controller = "landing", action = "index" });

app.MapHub<NotificationHub>("/{company}/notificationHub");

app.MapRazorPages();
app.MapHealthChecks("/health");

app.Run();