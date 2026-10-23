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
	//   - 'unsafe-inline' is required because the Razor views use inline scripts/styles.
	//   - 'unsafe-eval' is only sent for actions marked [AllowUnsafeEval] (the Google Maps site map).
	//     No other script needs it: the eval-like code in jQuery, DataTables/pdfmake, JSZip and SignalR
	//     is old-browser fallbacks. Set when the response starts, once routing has picked the endpoint.
	//   - default-src also allows cdn.datatables.net: Firefox reported its RowGroup script as blocked under
	//     default-src (not reproducible in Chrome or headless Firefox; likely DevTools re-fetching the file).
	//   - connect-src includes wss: for SignalR in production and ws: for development.
	//   - static.cloudflareinsights.com is Cloudflare's edge-injected beacon (present when the
	//     host is proxied through Cloudflare); allowed in both script-src and connect-src since
	//     the beacon script loads then reports metrics via its own network call.
	context.Response.OnStarting(() =>
	{
		var allowEval = context.GetEndpoint()?.Metadata.GetMetadata<PermitPro.App.Services.AllowUnsafeEvalAttribute>() != null;
		headers.ContentSecurityPolicy =
			"default-src 'self' https://cdn.datatables.net; " +
			$"script-src 'self' 'unsafe-inline'{(allowEval ? " 'unsafe-eval'" : "")} https://cdn.jsdelivr.net https://cdn.datatables.net https://cdnjs.cloudflare.com https://unpkg.com https://ka-f.webawesome.com https://maps.googleapis.com https://maps.gstatic.com https://static.cloudflareinsights.com; " +
			"style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdn.datatables.net https://unpkg.com https://fonts.googleapis.com https://ka-f.webawesome.com https://maps.googleapis.com; " +
			"img-src 'self' data: blob: https://unpkg.com https://*.googleapis.com https://*.gstatic.com https://*.google.com https://*.tile.openstreetmap.org; " +
			"font-src 'self' data: https://fonts.gstatic.com https://ka-f.webawesome.com; " +
			"connect-src 'self' data: ws: wss: https://ka-f.webawesome.com https://maps.googleapis.com https://maps.gstatic.com https://cloudflareinsights.com https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.datatables.net; " +
			"worker-src 'self' blob:; " +
			"frame-src 'none'; " +
			"object-src 'none'; " +
			"base-uri 'self';";
		return Task.CompletedTask;
	});

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

// Adds the email template settings (Settings > Email Templates) if they're missing; never overwrites edits.
try
{
	await PermitPro.Core.Services.EmailTemplateSeed.EnsureDefaultsAsync(app.Services);
}
catch (Exception ex)
{
	app.Logger.LogWarning(ex, "Could not add the default email template settings; the built-in templates are used.");
}

app.Run();
