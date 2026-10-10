# PDF Export — Troubleshooting & Open Issues

## Open issue: old-system PDF reportedly missing certificates

**Reported 2026-10-05.** Exporting an **old-system** permit to PDF was said to produce only the permit pages, without the attached certificates (A–H: Hot Work, Confined Space, …).

**Fixed (commit `e8a970d`, deployed):** a certificate that is ticked but never filled in is stored in `PermitForm.general.certificates` as just `{"name":"hotwork"}`. `PermitPdfService` assumed every field existed and threw a `NullReferenceException`, so the export returned 500 and nothing downloaded (the Edit page ignores the failure). All certificate field access is now null-safe; an unfilled certificate prints as a blank form. Verified for all 8 certificate types.

**Exporter replaced (2026-10):** the old-system PDF is now drawn with QuestPDF (`LegacyPermitPdfService`) instead of Puppeteer, with no network requests, so a failed CSS/image download or browser problem can no longer drop pages.

**Still unexplained:** the reported symptom — a PDF that *downloads* but has no certificate pages — was never reproduced. Against the local database, every permit with certificate data exported its certificate pages correctly.

Ruled out:
- Newtonsoft.Json 13.0.3 → 13.0.4: `SelectToken("$.[?(@.name=='x')]")` behaves identically.
- Certificate names in data vs `PTWSettings.Certificates` in `appsettings.json`: they match.
- Code drift: the PDF code was moved unchanged from `PermitService` to `PermitPdfService` in commit `2983646` (July 2026).
- The new-system path (`QuestPdfService`): not involved for old-system permits.

**Next step if it recurs:** get the permit number (`PTWxxxxxx`) and environment (local / UAT / production), then inspect that permit's `PermitForm` JSON:

```sql
SELECT RunningNumber, CreatedWhen, JSON_QUERY(PermitForm, '$.general.certificates') AS Certs
FROM Permits
WHERE RunningNumber = 1163;   -- the permit number without the PTW prefix
```

## Reproducing an old-system PDF export without the web app

The export can be run outside the app — no login needed — with a small console harness that constructs `LegacyPermitPdfService` directly against a database. It only reads: `SaveChanges` is never called. Create it **outside the repo** (or don't commit it). Build it against a copy of `Core` if Visual Studio has the solution open, so the two builds don't share `Core\obj`.

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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

using PermitPro.Core.Data;
using PermitPro.Core.Helpers;
using PermitPro.Core.Services.LegacyPermitPdf;

// Usage: dotnet run -- <permit-guid> <out.pdf> [--form <permitform.json>] [--all-name-only]
//   --form           render with this PermitForm JSON instead (in memory only)
//   --all-name-only  attach all 8 certificates with no data (in memory only)
const string RepoRoot = @"D:\Development\ThinkVisor\PermitPro-1.2";   // adjust
QuestPDF.Settings.EnableDebugging = true;   // layout problems fail with a report instead of looping

var config = new ConfigurationBuilder()
	.AddJsonFile(Path.Combine(RepoRoot, "Web", "appsettings.json"))
	.AddJsonFile(Path.Combine(RepoRoot, "Web", "appsettings.Development.json"))
	.Build();

var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
	.UseSqlServer(config.GetConnectionString("MSSQL"))   // the key Program.cs actually uses
	.Options;
using var db = new ApplicationDbContext(opts);

var ptw = config.GetSection("PTWSettings").Get<PTWSettings>();
var permit = db.Permits.Include(p => p.Company).First(p => p.Id == Guid.Parse(args[0]));

var formIdx = Array.IndexOf(args, "--form");
if (formIdx >= 0)
	permit.PermitForm = File.ReadAllText(args[formIdx + 1]);

if (args.Contains("--all-name-only"))
{
	var jo = Newtonsoft.Json.Linq.JObject.Parse(permit.PermitForm);
	jo["general"]["certificates"] = new Newtonsoft.Json.Linq.JArray(
		ptw.Certificates.Select(c => new Newtonsoft.Json.Linq.JObject { ["name"] = c.Name }));
	permit.PermitForm = jo.ToString(Newtonsoft.Json.Formatting.None);
}

var svc = new LegacyPermitPdfService(db, ptw, new Env(RepoRoot), NullLogger<LegacyPermitPdfService>.Instance);
var pdf = svc.Render(permit);
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
```

Notes:
- No internet or browser is needed: the logo and checkbox images come from `Web/wwwroot/img`, and the Roboto font is embedded in Core.
- `--all-name-only` exercises every certificate type with no data.
- Useful local test permits (as of 2026-10-07): **PTW001163** (Hot Work with data) and **PTW001158** (Hot Work ticked but never filled in).
- A layout that doesn't fit (e.g. a long value in a row of fixed-size items) throws `DocumentLayoutException` with the element path when `EnableDebugging` is on; without it the render can loop.

## Local database facts (as of 2026-10-07)

- SQL Server Express `(local)\SQLExpress`, database `permitpro`. Credentials are in `Web/appsettings.Development.json` (key `MSSQL`).
- `sqlcmd` needs `-C` to trust the server certificate. For queries containing quotes, put the SQL in a file and run it with `-i file.sql` from PowerShell.
- The data is all old-system: about 1,115 permits, 0 rows in `Certificates`, and 0 permits with `PermitDataJson`. The new-system (QuestPDF) path can't be exercised locally without creating data first.
