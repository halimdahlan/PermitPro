using PermitPro.App.Extensions;
using PermitPro.App.Hubs;
using PermitPro.Core.Extensions;
using PermitPro.Core.Helpers;

var builder = WebApplication.CreateBuilder(args);
var appConfig = builder.Configuration;

var connectionString = appConfig.GetConnectionString("MSSQL")!;
var ptwSettings = appConfig.GetSection("PTWSettings").Get<PTWSettings>()!;
var jwtSettings = appConfig.GetSection("JwtSettings").Get<JwtSettings>()!;

builder.Services.AddSingleton(ptwSettings);
builder.Services.AddSingleton(jwtSettings);

builder.Services.AddDatabase(connectionString);
builder.Services.AddIdentityConfiguration();
builder.Services.AddJwtAuthentication(jwtSettings);
builder.Services.AddCloudflareClientIp();
builder.Services.AddRateLimiting();
builder.Services.AddWebServices(connectionString);
builder.Services.AddPermitProServices();

var app = builder.Build();

// First, so everything downstream (rate limiter, logging) sees the real client IP.
app.UseCloudflareClientIp();

//await PermitPro.Core.Services.AppSettingsSeed.SeedDefaultsAsync(app.Services, appConfig);

if (app.Environment.IsDevelopment())
{
	app.UseMigrationsEndPoint();
}
else
{
	app.UseExceptionHandler("/error");
	app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");

app.UseResponseCaching();

app.Use(async (context, next) =>
{
	var headers = context.Response.Headers;

	headers.XContentTypeOptions = "nosniff";
	headers.XFrameOptions = "DENY";
	headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
	headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

	// Content Security Policy:
	//   - Assets are served locally except for the CDNs listed below.
	//   - cdn.jsdelivr.net is allowed for Chart.js loaded on the workflow overview.
	//   - kendo.cdn.telerik.com serves Kendo UI (JS, theme CSS, and the font icons the CSS loads),
	//     referenced from _Layout.cshtml.
	//   - 'unsafe-inline' is required because Kendo UI and Razor emit inline scripts/styles.
	//   - connect-src includes wss: for SignalR in production and ws: for development.
	//   - static.cloudflareinsights.com is Cloudflare's edge-injected beacon (present when the
	//     host is proxied through Cloudflare); allowed in both script-src and connect-src since
	//     the beacon script loads then reports metrics via its own network call.
	headers.ContentSecurityPolicy =
		"default-src 'self'; " +
		"script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net https://cdn.datatables.net https://cdnjs.cloudflare.com https://unpkg.com https://kendo.cdn.telerik.com https://ka-f.webawesome.com https://maps.googleapis.com https://maps.gstatic.com https://static.cloudflareinsights.com; " +
		"style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdn.datatables.net https://unpkg.com https://kendo.cdn.telerik.com https://fonts.googleapis.com https://ka-f.webawesome.com https://maps.googleapis.com; " +
		"img-src 'self' data: blob: https://unpkg.com https://kendo.cdn.telerik.com https://*.googleapis.com https://*.gstatic.com https://*.google.com https://*.tile.openstreetmap.org; " +
		"font-src 'self' data: https://fonts.gstatic.com https://kendo.cdn.telerik.com https://ka-f.webawesome.com; " +
		"connect-src 'self' data: ws: wss: https://ka-f.webawesome.com https://maps.googleapis.com https://maps.gstatic.com https://cloudflareinsights.com https://kendo.cdn.telerik.com https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.datatables.net; " +
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

// Must come after UseRouting: [EnableRateLimiting("auth")] is endpoint metadata, and before
// routing there is no endpoint yet, so the policy was silently never applied.
app.UseRateLimiter();

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
