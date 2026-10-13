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
  - `AddWebServices(connectionString)` — health checks, response caching, session, MVC/Razor/SignalR, `NotificationPushService`

`Program.cs` calls these extension methods in sequence — no DI registration logic lives in `Program.cs` directly.

### Connection String Resolution

`Program.cs` always reads the `"MSSQL"` key: `appConfig.GetConnectionString("MSSQL")`. Each `appsettings.{Environment}.json` overrides that same key (e.g. `appsettings.Development.json`, `appsettings.Staging.json`). `appsettings.Development.json` also holds an unused alternative `"XMSSQL"` entry.

### App Settings (Database-Stored Config)

Two-tier key-value system in `AppSettingCategory` / `AppSetting` tables. Global rows (`CompanyId IS NULL`) are inherited by all tenants; company rows override globals. Resolved via `IAppSettingsService.GetValueAsync`. Settings with `IsEncrypted = true` use ASP.NET Core Data Protection (purpose `"AppSettings"`, keys in `DataProtectionKeys` table).

### Email (SMTP)

Outgoing email uses MailKit with SMTP settings from `AppSettings` category `email` (`smtp_server`, `smtp_port`, `email_username`, `email_password` [encrypted], `sender_email`, `sender_name`). Company rows override the global row. Workflow emails (`MessageService`) use the company's settings; forgot-password uses the global ones. Every SMTP connection must go through `Core/Helpers/SmtpHelper.ConnectAsync`: port 465 uses implicit TLS, and other ports use STARTTLS when the server offers it. The provider is **Cloudflare Email Service**:
- host `smtp.mx.cloudflare.net`, port `465` with implicit TLS (plaintext and STARTTLS are rejected)
- username: the literal text `api_token`; password: a Cloudflare API token with **Email Sending: Edit**
- the `sender_email` domain must be onboarded for Email Sending, or Cloudflare rejects the message with `550 5.7.1`

`MessageService` BCCs every email to `workflow@permitpro.app`, so password-reset emails must not be sent through it.

Links in emails (password reset, workflow approvals) are built from the incoming request: `{Request.Scheme}://{Request.Host}`. Don't use the `application_domain` setting for links: its seed default is `uat.permitpro.app`. Building links from the request is only safe because `AllowedHosts` in `appsettings.json` is restricted to `permitpro.app;*.permitpro.app;localhost`. Never set it back to `*`, because a forged `Host` header would then put a valid reset token in a link to an attacker's site (password-reset poisoning). Requests with any other host get 400, including requests by bare IP address, so use `localhost`, not `127.0.0.1`, locally.

### PDF Generation

Both engines use QuestPDF (no browser; Puppeteer was removed). `PermitsController.ExportPdf` picks one by the permit's `CreatedWhen` against the cutover date:
- **Old-system permits** (created before `new_permit_form_cutover_date`) — `IPermitPdfService` → `Core/Services/LegacyPermitPdf/LegacyPermitPdfService` (`GenerateAsync(permitId)`, or `Render(permit)`), drawn by `LegacyPermitPdfDocument` (main form: 3 pages; then one section per attached certificate A–H, in `PTWSettings.Certificates` order from `appsettings.json`). Do not change the layout or field mapping without explicit approval: it reproduces the former Puppeteer/HTML export (sizes from that HTML, 1 CSS px = 0.683 pt). It reads `Permit.PermitForm` through `PermitFormReader`, which must stay null-safe: a certificate ticked but never filled in is stored as just `{"name":"..."}`, and any field may be missing. Dates are saved as UTC strings and printed in Asia/Kuala_Lumpur. The operational-information checkboxes are looked up by id, last copy wins (older permits store each id several times).
- **Font** — QuestPDF (2026.9+) does not use system fonts. Both PDFs use Roboto 2.137 Regular/Bold (Apache 2.0), embedded in Core from `Core/Resources/Fonts/` and registered by `Core/Helpers/PdfFonts.EnsureRegistered()` (`PdfFonts.Family`); characters Roboto lacks (e.g. `₂`) fall back to QuestPDF's built-in Lato. Any other font family must be shipped and registered, or generation throws.
- **Header** — both PDFs print the permit's company: name (`Description`, else `Name`) and logo via `Core/Helpers/PdfLogo.Load` — the company's uploaded logo (`wwwroot/img/logos/{Company.LogoFileName}`, PNG/JPG/WebP/SVG), falling back to `wwwroot/img/app-logo.jpg` when there is none or the file is missing/unreadable.
- **New-system permits** (on/after cutover date) — `IQuestPdfService` → `QuestPdfService`, driven by `FormTemplate.LayoutJson` and `Permit.PermitDataJson`.
- **Troubleshooting** — a console harness that renders an old-system permit without the web app (read-only), and the history of the "missing certificate pages" report: `docs/pdf-export-troubleshooting.md`.

### UI Framework

Telerik / Kendo UI has been removed (no NuGet package, CDN, local library or CSP entries). Do not add it back. Bootstrap 5.3.8 throughout. Replacements:
- **Grids** — DataTables 2 (Bootstrap 5 styling, client-side search/sort/paging over the existing JSON endpoints) via `wwwroot/js/datagrid.js` (`DataGrid.create`) + `css/datagrid.css`, included with `_DataGridStyles` / `_DataGridScripts`. Reports adds Buttons (Excel export, uses the JSZip loaded in `_Layout`) and RowGroup.
- **Dropdowns** — native `<select>` + Tom Select. **Location trees** — Treeselect.js (`wwwroot/js/location-picker.js`, `_LocationPicker*` partials) fed by `GET /{company}/sites/tree`.
- **Dates** — Flatpickr 4.6.13, loaded only through the `_FlatpickrStyles` / `_FlatpickrScripts` partials, which also set app-wide defaults (weeks start on Monday). Don't use native `<input type="date">` (its first weekday follows the browser/OS, not the app). **Switches/checkboxes** — Bootstrap `form-check`/`form-switch`. Modals: Bootstrap; notifications: `wwwroot/js/toast.js`; charts: Chart.js; uploads: native file input / custom dropzone.
- **Old-system permit form** (`Permits/New`, `Permits/Edit`, `Panels/*`, `Forms/*` certificates A–H) — native controls marked `data-control="..."`, set up by `wwwroot/js/form-controls.js` (`_FormControls*` partials). Page code reads them with `$('#id').data('control').value()`; the adapter reproduces Kendo's `value()` semantics (null for untouched text fields, Date objects, booleans), so the saved `PermitForm` JSON — and the old-system PDF built from it — is unchanged. Change handlers are named in markup (`data-change="fn"`) and registered with `FormControls.handlers({ fn })`. Keep that JSON shape when changing these pages.

**Mobile (phones < 576px)** — shared rules: page gutters, card padding, the light sidebar toggler icon and compact `.stat-card`s are in `site.css`. List grids show each row as a card on phones: `DataGrid.create` does it automatically (`.dt-cards` + per-cell `data-label` from the column header; `mobileCards: false` opts out); a plain `new DataTable(...)` opts in with `DataGrid.enableCards(tbl)`, a script-built HTML table with `DataGrid.labelTable(table)` (both inside `.data-grid`); a server-rendered table sits in `.table-cards-wrap` with `class="dt-cards"`, `data-label` on cells and `.dt-card-actions` on the action cell (page also loads `datagrid.css`). The first cell is the card title; a cell with an "Action(s)" or empty header is the action row. Filter bars use `d-flex flex-wrap` with the primary button `order-1 order-sm-2 ms-auto`; icon buttons next to a filter use `.grid-icon-btn`. Flatpickr runs with `disableMobile: true` (set in `_FlatpickrScripts`), so phones get the same calendar as desktop. In a Razor `<style>` block write `@@media`.

**Dark mode** — Light / Dark / System (default System), saved per browser in `localStorage` (`pp-theme`), switched from the top bar (`_ThemeToggle` partial; also in the admin navbar and floating on `_LayoutAnon` pages). `wwwroot/js/theme.js` is loaded in `<head>` of all three layouts, before any CSS, so pages never flash the wrong theme. It sets `data-bs-theme` on `<html>` (Bootstrap 5.3's own dark mode) and `wa-dark`/`wa-light` (webawesome.css colours native elements such as `<label>` from its own theme), keeps Chart.js default text/grid colours in sync and redraws charts on change (`pp:themechange` event; `PPTheme.color('--text')` reads a theme colour for canvas drawing). Colours live as tokens in `site.css` (`--bg`, `--card`, `--border`, `--text`, `--muted`, `--label`, `--surface-2`, `--tint-*`, `--input-focus-*`, `--input-disabled-bg`…), redefined under `[data-bs-theme="dark"]`, which also points Bootstrap's dark variables at the same palette. Don't hard-code light colours in new styles or inline `style=""`: use the tokens, or add a light value plus a `[data-bs-theme="dark"]` override. `bg-light`, `bg-white`, `table-light` and `text-dark` are remapped in dark mode; `bg-warning`/`bg-info` keep dark text. Third-party dark overrides sit next to where each library is loaded: Tom Select in `datagrid.css`, Treeselect in `_LocationPickerStyles`, Flatpickr in `_FlatpickrStyles`. Map pins and Google's info window on the site map stay white on purpose.

CSP still has `'unsafe-inline'` (Razor views use inline scripts/styles). `'unsafe-eval'` is sent only for actions marked `[AllowUnsafeEval]` (`Web/Services/AllowUnsafeEvalAttribute.cs`) — currently just `Sitemap/Index`, because the Google Maps JavaScript API's published CSP includes it. The CSP header is written in `Response.OnStarting` (in `Program.cs`), once routing has chosen the endpoint. Don't add eval-based libraries elsewhere; mark the action instead if one is unavoidable.

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

- **New permit**: files staged in JS `pendingFiles` array; appended to `FormData` manually at submit (no `<input type="file">` inside the form). Field name: `permitFiles`.
- **Existing permit**: immediate upload via `POST /{company}/permits/{id}/attachments/upload` (`IFormFileCollection files`); file list reloads automatically after each upload. Delete via existing `POST /{company}/permits/attachments/remove`.
- `SaveAttachmentsAsync` filters zero-length entries; uses `Path.GetFileName` cross-platform; only registers uploaded filenames (not pre-existing folder contents).

### SignalR

`NotificationHub` at `/{company}/notificationHub`. Pushes real-time notifications.

### Permit Rules

Per-company preset rules, managed by Super Users at **Admin → Rules** (`/{company}/rules`, `PermitRulesController`, `Views/PermitRules/Index.cshtml`). Phase 1 has two presets (`PermitRuleTypeEnum`), each switched on/off with settings stored as JSON (`PermitRule.SettingsJson`, models in `Core/Models/PermitRules`):
- **Overdue** — permits whose `EndDateTime` has passed and whose status isn't Closed, Closed – no action or Rejected (the Reports definition). Emails (template `wwwroot/templates/html/permit-overdue.html`, via `MessageService` with the company's SMTP) and in-app notifications go to the creator (their own permits), the company's Super Users and Portal Admins, and extra addresses (all overdue permits), one digest per recipient. Notifies once, then every `RepeatEveryDays` (default 1) until closed; `PermitRuleRun` (unique per rule + permit) tracks the last reminder. Never changes the permit's status (the `Overdue` status isn't handled by any screen).
- **Auto-archive** — Closed, Closed – no action and Rejected permits `DaysAfterClosed` days (default 30) after closing (rejection date, else the latest Closed/Rejected workflow history entry, else `UpdatedWhen`) get `Permit.IsArchived` / `ArchivedWhen` (bulk `ExecuteUpdate`). Archived permits are hidden from everyone but Super Users: permit list (Super Users have a "Show archived" switch, `GetGridData?archived=true`), site map, Dashboard recent permits, Reports' All Permits grid, and Edit / EditForm / ExportPdf return 404. Dashboard and report figures still count them. No auto-close (the user's decision).
- The Rules page has a dry run (**Preview**) showing which permits a rule would act on now; presets are created disabled on first visit (`IPermitRuleService.GetOrCreatePresetsAsync`).
- **Emails off by default** — `PermitRules:SendEmails` (`appsettings.json`, `false`): until the user turns it on, the Overdue rule sends in-app notifications only (the Rules page says so). Don't switch it on without the user's say-so.
- **Runner** — `Web/Services/PermitRulesBackgroundService` (hosted service, no job framework) calls `IPermitRuleService.RunAllAsync` every `PermitRules:IntervalMinutes` (default 5, first run 1 minute after start). A SQL Server app lock (`sp_getapplock`, `PermitPro.PermitRules`) stops two app instances running rules at once during an app pool recycle. It needs IIS to keep the app running: app pool **Start Mode = AlwaysRunning**, **Idle Time-out = 0**, site **Preload Enabled = True** (README → Deployment). Each run writes `PERMIT_RULE` audit log entries with the company set, and `PermitRule.LastRunWhen` / `LastRunSummary` (shown on the page).
- No HTTP request in the runner: `ICurrentUserService.GetCurrentUser()` returns null without one, email links use `App:BaseUrl` (unset by default: `https://localhost:7079` in Development, `https://permitpro.app` otherwise), and in-app notification links are relative.

### Background Jobs

Hangfire packages are installed and registered but the seed/execution is currently disabled (commented out in `Program.cs`).

## Conventions

- All EF Core read queries use `AsNoTracking()`.
- All async controller actions accept and propagate `CancellationToken`.
- JSON serialization uses `System.Text.Json` everywhere except the old-system PDF reader (`PermitFormReader`, Newtonsoft).
- The `Html/` folder contains static HTML prototypes — not part of the compiled app.
- The `SqlScripts/` folder holds ad-hoc SQL scripts for manual DB operations.
- Hosting: a dedicated Windows Server (IIS, in-process) behind Cloudflare: proxied DNS, a Cloudflare Origin Certificate, and a redirect rule sending non-apex hostnames to `permitpro.app`. SmarterASP.NET and AWS EC2 are no longer used; README keeps their old deployment sections for reference.
- Deployment: **no automated deployment at the moment.** `.github/workflows/deploy-on-merge.yml` was removed on 2026-08-01, so merging a PR to `master` does **not** deploy — the user deploys manually (self-contained win-x64 publish, see the publish command above). Never tell the user a merge will put a change live.
- App version: `Web/App.csproj` has `<Version>1.0.0</Version>`, shown on the About/Settings pages. CI used to override it with `-p:Version=1.0.<run_number>`; with manual deploys every build reports 1.0.0 unless `-p:Version=…` is passed to `dotnet publish`.
