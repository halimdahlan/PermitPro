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

- **`AppControllerBase`** — All tenant controllers inherit from this. Sets `CompanyID` from route, populates `ViewData` with company name/logo (cached 15 min). A non-GUID company segment returns 404; `Guid.Empty` (the site root `/`) redirects to `/account/login`. It must **never sign out**: any missing single-segment URL (e.g. `/android-chrome-192x192.png`) falls through to `{company}/landing/index`, and signing out there kicked users out right after login (Android Chrome fetches manifest icons on page load).
- **Auth cookie** — `SameSite=Lax` (not Strict, which made users appear logged out when opening links from email/WhatsApp; not None, which would expose the many POST actions lacking `[ValidateAntiForgeryToken]` to CSRF). Web manifest at `wwwroot/img/icons/site.webmanifest` — icon `src` paths must point at `/img/icons/…`.
- **`ICurrentUserService`** — Resolves the acting user's company ID. Normal users get their `UserCompany.Id`; super admins resolve from the `{company}` route value.
- **`ISoftDeletable`** — All core entities support soft-delete. Hard deletes are never performed by the application.
- **`IAuditableEntity`** — Auto-stamped (`CreatedWhen`/`CreatedBy`/`UpdatedWhen`/`UpdatedBy`) via `UpdateAuditableEntitiesInterceptor` on `SaveChanges`.

### Service Registration

Service registration is split across two extension classes:

- **Core services** — `Core/Extensions/CommonExtensions.cs` → `AddPermitProServices()` registers domain services (`IPermitService`, `IUserService`, `IAppSettingsService`, `IFormControlService`, `IFormTemplateService`, `ICertificateService`, `IQuestPdfService`, etc.), EF Core interceptors, and Data Protection.
- **Web services** — `Web/Extensions/WebExtensions.cs` provides six focused extension methods:
  - `AddDatabase(connectionString)` — DbContext + developer exception filter
  - `AddIdentityConfiguration()` — ASP.NET Identity, password policy, cookie settings, token lifetime
  - `AddJwtAuthentication(jwtSettings)` — JWT Bearer scheme
  - `AddCloudflareClientIp()` / `app.UseCloudflareClientIp()` (first middleware) — restores the real client IP from `CF-Connecting-IP`, trusted only from Cloudflare edge ranges (origin IP is public, so the header is spoofable otherwise). Implementation in `Web/Services/CloudflareClientIp.cs`: `CloudflareIpRanges` singleton (built-in fallback list + loopback), `CloudflareIpRangesRefresher` hosted service (refreshes from `https://api.cloudflare.com/client/v4/ips` at startup and every 24h, retries hourly on failure, never replaces a good list with an empty/bad response), `CloudflareClientIpMiddleware`. Custom middleware rather than `UseForwardedHeaders`, because `ForwardedHeadersOptions.KnownIPNetworks` can't be safely changed at runtime.
  - `AddRateLimiting()` — `"auth"` policy: 20 requests/minute **per client IP** (partitioned, not one shared bucket). `app.UseRateLimiter()` must stay **after** `UseRouting()` — before it, `[EnableRateLimiting]` endpoint metadata isn't available and the policy silently never applies (it was inactive until 2026-10-07).
  - `AddWebServices(connectionString)` — health checks, response caching, session, MVC/Razor/Kendo/SignalR, `NotificationPushService`

`Program.cs` calls these extension methods in sequence — no DI registration logic lives in `Program.cs` directly.

### Connection String Resolution

The connection string key matches the environment name: `appConfig.GetConnectionString(builder.Environment.EnvironmentName)` — so `appsettings.Development.json` uses `"Development"`, Production uses `"Production"`, etc.

### App Settings (Database-Stored Config)

Two-tier key-value system in `AppSettingCategory` / `AppSetting` tables. Global rows (`CompanyId IS NULL`) are inherited by all tenants; company rows override globals. Resolved via `IAppSettingsService.GetValueAsync`. Settings with `IsEncrypted = true` use ASP.NET Core Data Protection (purpose `"AppSettings"`, keys in `DataProtectionKeys` table).

### PDF Generation

Two engines coexist:
- **Old-system permits** (created before `new_permit_form_cutover_date`) — `PermitPdfService` using PuppeteerSharp (headless Chrome) + Scriban templates. `ITemplateService` → `RazorViewsTemplateService`. PDF-specific layout: `Views/Shared/_LayoutPdf.cshtml`. Do not modify or remove this path without explicit approval. Certificates are read from `PermitForm.general.certificates` (matched by name against `PTWSettings.Certificates` in `appsettings.json`) and rendered from `wwwroot/templates/html/certificates/{name}.html` into the `__FORM_A__`…`__FORM_H__` placeholders of `permit-main.html`. A certificate ticked but never filled in is stored as just `{"name":"..."}`, so all certificate field access must stay null-safe (`cert["x"]?["y"]`, `IsChecked(token)` instead of `(bool)` casts).
- **New-system permits** (on/after cutover date) — `QuestPdfService` using QuestPDF, driven by `FormTemplate.LayoutJson` and `Permit.PermitDataJson`. Both implement `IPermitPdfService`; `PermitsController.ExportPdf` selects the correct implementation via `IsNewSystemPermit(permit)`.

### UI Framework

**In progress — Telerik removal (Phase 7).** Kendo UI for ASP.NET Core (Telerik) is being replaced across all views. Replacements: plain `<table>` + AJAX pagination for grids, native `<select>` + Tom Select for dropdowns, native `<dialog>` for modals, `wwwroot/js/toast.js` for notifications, Chart.js for charts, native `<input type="file">` for uploads. Licensed DLLs remain in `Telerik/` until all views are migrated; `'unsafe-inline'` in CSP remains required until Kendo is fully removed. New views (Phases 2–6) must not use Telerik/Kendo.

### Form Builder

New-system permits and certificates use a drag-drop form builder:
- **`FormControl`** entity (`Core/Entities/FormControl.cs`) — reusable control definitions per company. `ControlType` enum: `TextBox`, `TextArea`, `Checkbox`, `MultiChoice`, `Select`, `DateTimeInput`, `FileUpload`, `Label`. The `Label` type renders a heading + `<hr>` separator (no input); its heading level (H1–H6) is stored in `OptionsJson` as `{"headingLevel":"h3"}`.
- **`FormTemplate`** entity (`Core/Entities/FormTemplate.cs`) — layout JSON (`LayoutJson`) defining rows and control placements. Each row has a `columnCount` (1, 2, or 3) — all columns in a row are equal width. A slot with `controlId: null` renders as empty. `TemplateType` enum: `Permit`, `Certificate`. There is no `IsActive` concept — all templates are selectable.
- **`Permit.PermitDataJson`** — filled field values for new-system permits, keyed by column UUID from `LayoutJson`. `Permit.PermitForm` (old field) unchanged.
- **`Permit.Title`** / **`Permit.Description`** / **`Permit.StartDateTime`** / **`Permit.EndDateTime`** — populated for both old and new system permits. Used by the grid to avoid JSON parsing. `Title` mirrors `Description` for new-system permits.
- **`Certificate.CertificateDataJson`** / **`Certificate.FormTemplateId`** — new-system certificate fields. Old `SerializedData` field unchanged.
- Admin routes: `/admin/formcontrols` · `/admin/formtemplates` (consolidated — includes certificate templates with type filter tabs)
- Tenant routes (Super User): `/{company}/formcontrols` · `/{company}/formtemplates` (consolidated)
- Certificate routes: `/{company}/permits/{permitId}/certificates/*`
- Cutover date: `AppSetting` category `general`, key `new_permit_form_cutover_date`. Exposed as `ISystemConfigurationService.NewPermitFormCutoverDate`. Null = new system disabled.
- Builder duplicate-create bug: `TEMPLATE_ID` is `let` (not `const`) so after the first save it is updated in-place; subsequent saves on the same page update the existing record instead of creating a new one.

### New-System Permit Form

- `PermitsController.IsNewSystemActive()` — true when cutover date is set and `DateTime.UtcNow >= cutoverDate`.
- `PermitsController.IsNewSystemPermit(permit)` — true when `permit.CreatedWhen >= cutoverDate`.
- `PermitsController.EnrichLayout(layoutJson, companyId)` — merges full control metadata into layout JSON server-side so the browser JS needs no extra fetch.
- Views: `Views/Permits/NewForm.cshtml` and `EditForm.cshtml` — both use the shared `Views/Permits/Partials/_NewFormScript.cshtml` partial.
- `_NewFormScript.cshtml` — renders all 8 control types (including `Label`), manages Flatpickr date pickers (start → end min-date constraint, default 00:00), custom dropzone (`pendingFiles` array; immediate upload in edit mode via `POST /{company}/permits/{id}/attachments/upload`), inline BS validation with per-field error messages.
- Attachments card is hidden (`d-none`) on `NewForm.cshtml` — files can only be attached after the permit is first saved (edit mode).
- Fixed required fields above the canvas: Description, Start date/time, End date/time.
- `IPermitService.CreateNewSystemAsync` / `UpdateNewSystemAsync` — accept description, startDateTime, endDateTime, permitDataJson, files.

### Certificates (New System)

- `ICertificateService` / `CertificateService` (`Core/Services/CertificateService.cs`) — CRUD + `SaveDataAsync`.
- `CertificatesController` — tenant-scoped at `/{company}/permits/{permitId}/certificates/*`.
- `Views/Certificates/Index.cshtml` — list + add (BS modal) + delete.
- `Views/Certificates/Form.cshtml` — fill certificate form; assign/switch template; Flatpickr for date controls; saves via JSON `POST`.
- Attach/detach template via `POST /{id}/template`; save data via `POST /{id}/save` (`[IgnoreAntiforgeryToken]`, JSON body).
- Certificate PDF pages are appended to the permit PDF on export. `PermitsController.ExportPdf` includes `Certificates` (filtered `!IsDeleted`) with `ThenInclude(c => c.FormTemplate)`. `QuestPdfService` appends one QuestPDF page per new-system certificate (those with both `FormTemplate` and `CertificateDataJson` set).

### Attachment Upload

- **New permit**: files staged in JS `pendingFiles` array; appended to `FormData` manually at submit (no `<input type="file">` inside the form — Kendo hijacks those). Field name: `permitFiles`.
- **Existing permit**: immediate upload via `POST /{company}/permits/{id}/attachments/upload` (`IFormFileCollection files`); file list reloads automatically after each upload. Delete via existing `POST /{company}/permits/attachments/remove`.
- `SaveAttachmentsAsync` filters zero-length entries; uses `Path.GetFileName` cross-platform; only registers uploaded filenames (not pre-existing folder contents).

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
