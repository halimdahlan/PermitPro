# PDF Export — Troubleshooting & Open Issues

## Open issue: old-system PDF reportedly missing certificates

**Reported 2026-10-05.** Exporting an **old-system** permit to PDF was said to produce only the permit pages, without the attached certificates (A–H: Hot Work, Confined Space, …).

**Fixed (commit `e8a970d`, deployed):** a certificate that is ticked but never filled in is stored in `PermitForm.general.certificates` as just `{"name":"hotwork"}`. `PermitPdfService` assumed every field existed and threw a `NullReferenceException`, so the export returned 500 and nothing downloaded (the Edit page ignores the failure). All certificate field access is now null-safe; an unfilled certificate prints as a blank form. Verified for all 8 certificate types.

**Still unexplained:** the reported symptom — a PDF that *downloads* but has no certificate pages — was never reproduced. Against the local database, every permit with certificate data exported its certificate pages correctly.

Ruled out:
- Newtonsoft.Json 13.0.3 → 13.0.4: `SelectToken("$.[?(@.name=='x')]")` behaves identically.
- Certificate names in data vs `PTWSettings.Certificates` in `appsettings.json`: they match.
- Code drift: the PDF code was moved unchanged from `PermitService` to `PermitPdfService` in commit `2983646` (July 2026).
- The new-system (QuestPDF) path: not involved for old-system permits.

**Next step if it recurs:** get the permit number (`PTWxxxxxx`) and environment (local / UAT / production), then inspect that permit's `PermitForm` JSON:

```sql
SELECT RunningNumber, CreatedWhen, JSON_QUERY(PermitForm, '$.general.certificates') AS Certs
FROM Permits
WHERE RunningNumber = 1163;   -- the permit number without the PTW prefix
```

## Reproducing an old-system PDF export without the web app

The export can be run outside the app — no login needed — with a small console harness that constructs `PermitPdfService` directly against a database. Create it **outside the repo** (or don't commit it):

`pdfharness.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <!-- Adjust to where the repo is cloned -->
    <ProjectReference Include="D:\Development\ThinkVisor\PermitPro-1.2\Core\Core.csproj" />
  </ItemGroup>
</Project>
```

`Program.cs`
```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;
using PermitPro.Core.Services;

// Usage: dotnet run -- <permit-guid> <out.pdf> [--all-name-only]
const string RepoRoot = @"D:\Development\ThinkVisor\PermitPro-1.2";   // adjust

var config = new ConfigurationBuilder()
	.AddJsonFile(Path.Combine(RepoRoot, "Web", "appsettings.json"))
	.AddJsonFile(Path.Combine(RepoRoot, "Web", "appsettings.Development.json"))
	.Build();

var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
	.UseSqlServer(config.GetConnectionString("MSSQL"))   // the key Program.cs actually uses
	.Options;
using var db = new ApplicationDbContext(opts);

var ptw = config.GetSection("PTWSettings").Get<PTWSettings>();
var permitId = args[0];

if (args.Contains("--all-name-only"))
{
	// In memory only — SaveChanges is never called. The service's own tracking query
	// returns this same modified instance, so it renders all 8 certificates with no data.
	var tracked = db.Permits.First(p => p.Id == Guid.Parse(permitId));
	var jo = Newtonsoft.Json.Linq.JObject.Parse(tracked.PermitForm);
	jo["general"]["certificates"] = new Newtonsoft.Json.Linq.JArray(
		ptw.Certificates.Select(c => new Newtonsoft.Json.Linq.JObject { ["name"] = c.Name }));
	tracked.PermitForm = jo.ToString(Newtonsoft.Json.Formatting.None);
}

var svc = new PermitPdfService(db, ptw, new Env(RepoRoot), new User(), new Settings(), NullLogger<PermitPdfService>.Instance);
var form = new FormCollection(new Dictionary<string, StringValues> { ["Id"] = permitId });
var pdf = await svc.GetPdfBytesAsync(form);
await File.WriteAllBytesAsync(args[1], pdf);
Console.WriteLine($"Wrote {pdf.Length} bytes to {args[1]}");

class Env(string repoRoot) : IWebHostEnvironment
{
	public string WebRootPath { get; set; } = Path.Combine(repoRoot, "Web", "wwwroot");
	public IFileProvider WebRootFileProvider { get; set; }
	public string ApplicationName { get; set; } = "harness";
	public IFileProvider ContentRootFileProvider { get; set; }
	public string ContentRootPath { get; set; } = Path.Combine(repoRoot, "Web");
	public string EnvironmentName { get; set; } = "Development";
}

class User : ICurrentUserService
{
	public Task<bool> IsContractor() => Task.FromResult(false);
	public Task<bool> IsPermitIssuer() => Task.FromResult(false);
	public Task<bool> IsLeadPermitIssuer() => Task.FromResult(false);
	public Task<UserInfo> GetCurrentUserAsync() => Task.FromResult<UserInfo>(null);
	public Task<IList<string>> GetCurrentUserRoles() => Task.FromResult<IList<string>>(new List<string>());
	public UserInfo GetCurrentUser() => null;
	public Guid GetCurrentCompanyId() => Guid.Empty;
}

// Only GetValueAsync is used by PermitPdfService (for "application_domain").
class Settings : IAppSettingsService
{
	public Task<string> GetValueAsync(Guid companyId, string categoryName, string key) => Task.FromResult("uat.permitpro.app");
	public Task<IEnumerable<AppSettingCategory>> GetCategoriesAsync(Guid companyId) => throw new NotImplementedException();
	public Task<IEnumerable<AppSettingCategory>> GetGlobalCategoriesAsync() => throw new NotImplementedException();
	public Task UpsertCategoryAsync(AppSettingCategory category) => throw new NotImplementedException();
	public Task DeleteCategoryAsync(Guid id) => throw new NotImplementedException();
	public Task<int> GetIntAsync(Guid companyId, string categoryName, string key) => throw new NotImplementedException();
	public Task<IEnumerable<AppSetting>> GetSettingsAsync(Guid companyId, Guid categoryId) => throw new NotImplementedException();
	public Task<IEnumerable<AppSetting>> GetGlobalSettingsAsync(Guid categoryId) => throw new NotImplementedException();
	public Task UpsertSettingAsync(AppSetting setting) => throw new NotImplementedException();
	public Task DeleteSettingAsync(Guid id) => throw new NotImplementedException();
}
```

Notes:
- Puppeteer loads Bootstrap CSS and checkbox images from `https://{application_domain}`, so the harness needs internet access. Its first run downloads headless Chrome.
- `--all-name-only` exercises every certificate type with no data, without writing to the database.
- Useful local test permits (as of 2026-10-07): **PTW001163** (Hot Work with data, exports 4 pages) and **PTW001158** (Hot Work ticked but never filled in).

## Local database facts (as of 2026-10-07)

- SQL Server Express `(local)\SQLExpress`, database `permitpro`. Credentials are in `Web/appsettings.Development.json` (key `MSSQL`).
- `sqlcmd` needs `-C` to trust the server certificate. For queries containing quotes, put the SQL in a file and run it with `-i file.sql` from PowerShell.
- The data is all old-system: about 1,115 permits, 0 rows in `Certificates`, and 0 permits with `PermitDataJson`. The new-system (QuestPDF) path can't be exercised locally without creating data first.
