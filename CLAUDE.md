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

Service registration is split across two extension classes:

- **Core services** — `Core/Extensions/CommonExtensions.cs` → `AddPermitProServices()` registers domain services (`IPermitService`, `IUserService`, `IAppSettingsService`, etc.), EF Core interceptors, and Data Protection.
- **Web services** — `Web/Extensions/WebExtensions.cs` provides five focused extension methods:
  - `AddDatabase(connectionString)` — DbContext + developer exception filter
  - `AddIdentityConfiguration()` — ASP.NET Identity, password policy, cookie settings, token lifetime
  - `AddJwtAuthentication(jwtSettings)` — JWT Bearer scheme
  - `AddRateLimiting()` — fixed-window auth rate limiter
  - `AddWebServices(connectionString)` — health checks, response caching, session, MVC/Razor/Kendo/SignalR, `NotificationPushService`

`Program.cs` calls these extension methods in sequence — no DI registration logic lives in `Program.cs` directly.

### Connection String Resolution

The connection string key matches the environment name: `appConfig.GetConnectionString(builder.Environment.EnvironmentName)` — so `appsettings.Development.json` uses `"Development"`, Production uses `"Production"`, etc.

### App Settings (Database-Stored Config)

Two-tier key-value system in `AppSettingCategory` / `AppSetting` tables. Global rows (`CompanyId IS NULL`) are inherited by all tenants; company rows override globals. Resolved via `IAppSettingsService.GetValueAsync`. Settings with `IsEncrypted = true` use ASP.NET Core Data Protection (purpose `"AppSettings"`, keys in `DataProtectionKeys` table).

### PDF Generation

Two engines coexist:
- **Old-system permits** (created before `new_permit_form_cutover_date`) — `PermitPdfService` using PuppeteerSharp (headless Chrome) + Scriban templates. `ITemplateService` → `RazorViewsTemplateService`. PDF-specific layout: `Views/Shared/_LayoutPdf.cshtml`. Do not modify or remove this path.
- **New-system permits** (on/after cutover date) — `QuestPdfService` using QuestPDF, driven by `FormTemplate.LayoutJson` and `Permit.PermitDataJson`. Both implement `IPermitPdfService`; `PermitsController.ExportPdf` selects the correct implementation via `IsNewSystemPermit(permit)`.

### UI Framework

**In progress — Telerik removal (Phase 7).** Kendo UI for ASP.NET Core (Telerik) is being replaced across all views. Replacements: plain `<table>` + AJAX pagination for grids, native `<select>` + Tom Select for dropdowns, native `<dialog>` for modals, `wwwroot/js/toast.js` for notifications, Chart.js for charts, native `<input type="file">` for uploads. Licensed DLLs remain in `Telerik/` until all views are migrated; `'unsafe-inline'` in CSP remains required until Kendo is fully removed. New views (Phases 2–6) must not use Telerik/Kendo.

### Form Builder

New-system permits and certificates use a drag-drop form builder:
- **`FormControl`** entity (`Core/Entities/FormControl.cs`) — reusable control definitions per company. `ControlType` enum: `TextBox`, `TextArea`, `Checkbox`, `MultiChoice`, `Select`, `DateTimeInput`, `FileUpload`.
- **`FormTemplate`** entity (`Core/Entities/FormTemplate.cs`) — layout JSON (`LayoutJson`) defining rows and control placements. Each row has a `columnCount` (1, 2, or 3) — all columns in a row are equal width. A slot with `controlId: null` renders as empty. `TemplateType` enum: `Permit`, `Certificate`. One active template per company+type at a time.
- **`Permit.PermitDataJson`** — filled field values for new-system permits, keyed by column UUID from `LayoutJson`. `Permit.PermitForm` (old field) unchanged.
- Admin routes: `/admin/formcontrols` (manage controls), `/admin/formtemplates` (permit layout builder), `/admin/certificatetemplates` (certificate layout builder).
- Cutover date: `AppSetting` category `general`, key `new_permit_form_cutover_date`. Exposed as `ISystemConfigurationService.NewPermitFormCutoverDate`. Null = new system disabled.

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
