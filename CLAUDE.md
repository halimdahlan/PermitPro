# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Restore, build, run
dotnet restore
dotnet build
dotnet run --project Web

# Run in Release mode
dotnet build --configuration Release
dotnet publish Web/App.csproj --configuration Release --framework net10.0 --output ./publish --runtime win-x64 --self-contained -p:PublishReadyToRun=true

# EF Core migrations (Core holds DbContext, Web is the startup project)
dotnet ef migrations add <MigrationName> --project Core --startup-project Web
dotnet ef database update --project Core --startup-project Web

# Default URLs (Development)
# HTTP:  http://localhost:5144
# HTTPS: https://localhost:7079
```

There are no tests in this project.

## Architecture

Two-project solution targeting .NET 10.0:

- **Core** (`Core/Core.csproj`, namespace `PermitPro.Core`) — Domain entities, EF Core DbContext, service interfaces & implementations, migrations, enums, helpers. No web dependencies.
- **Web** (`Web/App.csproj`, namespace `PermitPro.App`) — ASP.NET Core MVC app. Controllers, Razor views, ViewModels, SignalR hubs, static assets. References Core.

### Multi-Tenancy

All tenant routes follow the pattern `/{company}/{controller}/{action}/{id?}` where `{company}` is a GUID. `AppControllerBase` extracts this from the route to scope all queries. Super Admin routes (`/admin/*`) have no company segment.

### Key Abstractions

- **`AppControllerBase`** — All tenant controllers inherit from this. Sets `CompanyID` from route, populates `ViewData` with company name/logo (cached 15 min), signs out if the GUID is invalid.
- **`ICurrentUserService`** — Resolves the acting user's company ID. Normal users get their `UserCompany.Id`; super admins resolve from the `{company}` route value.
- **`ISoftDeletable`** — All core entities support soft-delete. Hard deletes are never performed by the application.
- **`IAuditableEntity`** — Auto-stamped (`CreatedWhen`/`CreatedBy`/`UpdatedWhen`/`UpdatedBy`) via `UpdateAuditableEntitiesInterceptor` on `SaveChanges`.

### Service Registration

All core services are registered in `Core/Extensions/CommonExtensions.cs` → `AddPermitProServices()`. Web-specific services (e.g., `NotificationPushService`) are registered in `Program.cs`.

### Connection String Resolution

The connection string key matches the environment name: `appConfig.GetConnectionString(builder.Environment.EnvironmentName)` — so `appsettings.Development.json` uses `"Development"`, Production uses `"Production"`, etc.

### App Settings (Database-Stored Config)

Two-tier key-value system in `AppSettingCategory` / `AppSetting` tables. Global rows (`CompanyId IS NULL`) are inherited by all tenants; company rows override globals. Resolved via `IAppSettingsService.GetValueAsync`. Settings with `IsEncrypted = true` use ASP.NET Core Data Protection (purpose `"AppSettings"`, keys in `DataProtectionKeys` table).

### PDF Generation

`PermitPdfService` uses PuppeteerSharp (headless Chrome). Templates rendered via Scriban (`ITemplateService` → `RazorViewsTemplateService`). PDF-specific layout: `Views/Shared/_LayoutPdf.cshtml`.

### UI Framework

Kendo UI for ASP.NET Core (Telerik) — grids, dropdowns, modals, charts. Licensed DLLs in `Telerik/`. Chart.js used additionally on workflow/dashboard pages. `'unsafe-inline'` is required in CSP because Kendo emits inline scripts/styles.

### SignalR

`NotificationHub` at `/{company}/notificationHub`. Pushes real-time notifications.

### Background Jobs

Hangfire packages are installed and registered but the seed/execution is currently disabled (commented out in `Program.cs`).

## Conventions

- All EF Core read queries use `AsNoTracking()`.
- All async controller actions accept and propagate `CancellationToken`.
- JSON serialization uses `System.Text.Json` everywhere except PDF template mapping (Newtonsoft).
- The `Html/` folder contains static HTML prototypes — not part of the compiled app.
- The `SqlScripts/` folder holds ad-hoc SQL scripts for manual DB operations.
- CI/CD: `.github/workflows/deploy-on-merge.yml` triggers on merged PRs to `master`, publishes self-contained win-x64, deploys via WebDeploy.
- App version is set by CI via `-p:Version=1.0.<run_number>` — never bump `<Version>` in the csproj manually.
