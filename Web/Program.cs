using PermitPro.App.Extensions;
using PermitPro.App.Hubs;
using PermitPro.Core.Extensions;
using PermitPro.Core.Helpers;

var builder = WebApplication.CreateBuilder(args);
var appConfig = builder.Configuration;

var connectionString = appConfig.GetConnectionString("MSSQLConnection")!;
var ptwSettings = appConfig.GetSection("PTWSettings").Get<PTWSettings>()!;
var jwtSettings = appConfig.GetSection("JwtSettings").Get<JwtSettings>()!;

builder.Services.AddSingleton(ptwSettings);
builder.Services.AddSingleton(jwtSettings);

builder.Services.AddDatabase(connectionString);
builder.Services.AddIdentityConfiguration();
builder.Services.AddJwtAuthentication(jwtSettings);
builder.Services.AddRateLimiting();
builder.Services.AddWebServices(connectionString);
builder.Services.AddPermitProServices();

var app = builder.Build();

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

app.UseRateLimiter();
app.UseResponseCaching();

app.Use(async (context, next) =>
{
	var headers = context.Response.Headers;

	headers.XContentTypeOptions = "nosniff";
	headers.XFrameOptions = "DENY";
	headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
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
