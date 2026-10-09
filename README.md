# PermitPro - Permit-to-Work (PTW)

A multi-tenant, enterprise web application for managing **Permit-to-Work (PTW)** systems and workplace safety permits. Built on ASP.NET Core 10.0 with role-based access control, workflow automation, real-time notifications, and full audit logging.

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Runtime** | .NET 10.0 |
| **Web Framework** | ASP.NET Core 10.0 (MVC + Razor Pages) |
| **ORM** | Entity Framework Core 10.0.8 (SQL Server) |
| **Auth** | ASP.NET Identity + JWT Bearer |
| **UI Components** | Bootstrap 5.3, DataTables 2, Tom Select, Flatpickr, Treeselect.js, Chart.js |
| **Real-time** | ASP.NET Core SignalR |
| **Background Jobs** | Hangfire 1.8.14 (currently disabled) |
| **Rate Limiting** | ASP.NET Core built-in rate limiter (fixed-window, per-IP) |
| **PDF Generation** | PuppeteerSharp 18.0.3 (old-system) + QuestPDF 2025.4.3 (new-system) |
| **Date Picker** | Flatpickr (CDN, new-system forms) |
| **Excel Export** | ClosedXML 0.105.0 |
| **Email** | MailKit 4.17.0 + MimeKit 4.17.0 |
| **Templating** | Scriban.Signed 5.10.0 |
| **JSON** | System.Text.Json (primary); Newtonsoft.Json 13.0.3 (PDF template mapping only) |
| **API Docs** | Swashbuckle / Swagger (referenced, not configured) |

---

## Project Structure

```
PermitPro-1.2/
├── Web/                        # ASP.NET Core MVC web application
│   ├── Controllers/            # 23 active MVC controllers
│   │   ├── Base/               # AppControllerBase (company scoping + ViewData)
│   │   ├── AdminController.cs  # Super Admin portal (login + company management)
│   │   ├── WorkflowController.cs   # Workflow CRUD + enhanced Overview/Manage/Builder
│   │   ├── ErrorController.cs      # Custom error pages
│   │   ├── MaintenanceController.cs
│   │   ├── ScheduledTaskController.cs
│   │   └── ToolsController.cs      # System menu management
│   ├── Extensions/             # Web-layer DI extensions (AddDatabase, AddIdentityConfiguration, AddJwtAuthentication, AddCloudflareClientIp, AddRateLimiting, AddWebServices)
│   ├── Hubs/                   # SignalR hubs (NotificationHub)
│   ├── Models/                 # AJAX request/response models, chart models, typed permit request models
│   ├── Services/               # Web-layer services (NotificationPushService)
│   ├── ViewModels/             # 33 typed view models
│   ├── Views/
│   │   ├── Admin/              # Super Admin views (Login, Companies, CompanyForm, CompanySettings)
│   │   ├── Shared/             # Layouts (_Layout, _LayoutAnon, _LayoutAdmin, _LayoutPdf)
│   │   ├── Workflow/           # Index, Edit, Overview, Manage, Builder (original + enhanced)
│   │   ├── Tools/              # System menu editor
│   │   └── <feature>/          # Razor templates per feature area (20 view folders, 74 views)
│   └── wwwroot/
│       └── img/logos/          # Uploaded company logo files
├── Core/                       # Domain logic class library
│   ├── Data/                   # ApplicationDbContext + soft-delete/restore logic
│   ├── Entities/               # 26 domain entities (including certificate and form subfolders)
│   ├── Enums/                  # PermitStatus, WorkflowStatus, SiteType, DurationType, LogType, ContactType, EmailInfo
│   ├── Interfaces/             # 14 service and entity interfaces
│   ├── Services/               # 10 core service implementations
│   ├── Filters/                # Authorization filters
│   ├── Helpers/                # Email, JWT, PTW settings helpers
│   ├── Interceptors/           # EF Core audit interceptor
│   ├── Extensions/             # Core DI registration (AddPermitProServices)
│   ├── Migrations/             # EF Core database migrations
│   └── PTW/                    # Permit-to-Work template data
├── .github/workflows/          # GitHub Actions (currently empty — no CI/CD)
└── PermitPro.sln
```

---

## Features

### Super Admin Portal
- Separate login page at `/admin/login` — no company selection required
- Restricted to accounts with the `SUPERUSER` role only
- Full company management across all tenants:
  - Create, edit, and soft-delete companies
  - Activate / deactivate companies (inactive companies are hidden from the tenant login page)
  - Upload a company logo (PNG, JPG, WebP, SVG — max 2 MB)
- Company logo is displayed in the tenant sidebar; falls back to a helmet-safety icon if none is set
- Dedicated `_LayoutAdmin` layout with a minimal dark navigation bar
- Super admins have no assigned `UserCompany`; all service calls that need a company ID resolve it automatically from the `{company}` route segment instead
- Global and per-company app settings management (`/admin/settings`, `/admin/companies/{id}/settings`) — see **App Settings** below
- System-wide log viewer at `/admin/logs` — paginated, filterable grid over `AuditLog` (type, category, entity, description, user), with a Bootstrap modal for full record detail (including raw `SerializedData`) fetched on demand via `/admin/logs/{id}`

### Permit Management
- Create and manage safety permits across 8 certificate types:
  - Hot Work, Confined Space, Radiation, Excavation, Isolation
  - Method Statement, Lifting & Hoisting, Override
- Permit lifecycle: Draft → Pending → Approved → Suspended → KIV → Closed / ClosedNoAction / Overdue / Rejected
- File attachments (PDF, DOCX, JPG, PNG — up to 5 files, 3 MB each)
- PDF export of permit documents via headless Chrome (isolated in `PermitPdfService`)

### Workflow Engine
- Configurable multi-step approval workflows per company
- Workflow history and full audit trail
- Background jobs for automatic permit closure and auto-resume logic (configurable threshold via `SUSPEND_AUTORESUME_DAYS`)
- Enhanced three-page workflow UI (runs alongside original routes):
  - **Overview** (`/workflow/overview`) — 4 KPI cards (Total, Active, Inactive, Permits Assigned), status doughnut chart, top-6 permits-by-workflow bar chart, summary grid
  - **Manage** (`/workflow/manage`) — filter pills (ALL / ACTIVE / INACTIVE), grid with step count and permit count columns, inline "New Workflow" modal that redirects to Builder on create
  - **Builder** (`/workflow/builder/{id}`) — two-column view: left = workflow info form with mini stat cards; right = visual step pipeline with inline Bootstrap collapse panels per step (General + Approvers tabs), approver picker modal

### Multi-Tenancy
- Each company operates in an isolated namespace
- Tenant route pattern: `/{company}/{controller}/{action}`
- All data scoped to the tenant company
- Company name and logo injected into every tenant view via `AppControllerBase`
- `ICurrentUserService.GetCurrentCompanyId()` resolves the active company for any service call:
  - Normal users → their assigned `UserCompany.Id`
  - Super admins (no assigned company) → `{company}` route value from the current request URL

### Real-time Notifications
- SignalR `NotificationHub` pushes live notifications to connected users
- In-app notification list with Title, Message, read/archived state
- SMTP email alerts via MailKit

### Sites & Location
- Manage work sites with GPS coordinates
- Site map dashboard overlay

### Recycle Bin & Soft Delete
- All core entities implement `ISoftDeletable` — deletions are soft by default
- Cascade soft-delete: removing a Company cascades to Sites, Workflows, Permits, Attachments, and WorkflowHistories
- `RecycleBinController` exposes restore functionality for soft-deleted records across 5 tabs:
  - **Permits** — company-scoped
  - **Users** — company-scoped
  - **Roles** — global entity (not company-scoped); shown across all tenants; includes `Description` and soft-delete audit fields
  - **Workflow Steps** — company-scoped
  - **Companies** — visible to Super Users only
- Cascade restore recovers children deleted within the same cascade operation (10-second window)

### App Settings
- Key-value configuration stored in `AppSettingCategory` / `AppSetting` entities, both with a nullable `CompanyId`
- Two tiers, merged at read time:
  - **Global** (`CompanyId == null`) — defaults inherited by every company
  - **Company** (`CompanyId == <company>`) — overrides the global value for that company only
- Default categories/keys:

| Category | Keys |
|---|---|
| **General** | `application_domain`, `user_create_limit`, `upload_max_file_size`, `upload_max_file_count`, `upload_allowed_file_types` |
| **Email** | `smtp_server`, `smtp_port`, `sender_name`, `sender_email`, `email_username`, `email_password` (encrypted) |
| **Workflow** | `suspended_autoresume_days` |

`email_password` (and any other `IsEncrypted` setting) is encrypted via ASP.NET Core Data Protection (`IDataProtector`, purpose `"AppSettings"`). Keys are persisted to the `DataProtectionKeys` table (`PersistKeysToDbContext<ApplicationDbContext>`, fixed `ApplicationName` of `"PermitPro"`) rather than the per-machine default key ring, so encrypted values decrypt consistently across every dev machine and deployed instance sharing the database. A value encrypted before this was in place (or under a different key ring) fails to decrypt — `Decrypt()` catches this and returns `null` rather than throwing — and must be re-saved through the Settings UI to re-encrypt it under the current shared key.

**Global settings with per-company override**
- Managed by super admins at `/admin/settings` (Global Settings) and `/admin/companies/{id}/settings` (per-company) — both pages share a **Global / Company** context-switcher dropdown in the header to jump between tiers without leaving the settings screen
- Tenant admins manage their own company's settings at `/{company}/settings` (unchanged route)
- Each company-scoped setting has a **"Use global settings value"** checkbox (`AppSetting.IsUsingGlobal`). When checked:
  - The effective value read by the app resolves to the current global value for that key, not the company row's stored `Value`
  - The company's last custom value is preserved (not wiped) so unchecking restores it instead of requiring re-entry
  - The checkbox only appears when a global row exists for that key (`HasGlobalOverrideAvailable`) — there's nothing to defer to otherwise
- `IAppSettingsService.GetValueAsync`/`GetIntAsync`/`GetSettingsAsync` resolve the effective value: company row wins unless it's deferring to global, in which case the global row is used (falling back to the company row if no global row exists)
- Global-row uniqueness (per category `Name`, per setting `CategoryId`+`Key`) is enforced via dedicated filtered unique indexes (`WHERE CompanyId IS NULL`) — EF Core's default nullable-column unique index auto-excludes `NULL` rows, so global rows needed their own index to prevent duplicates
- Cache invalidation on a global-row write clears every known company's cached settings (`AppSettingsService` tracks companies it has served via a static `ConcurrentDictionary`, since `IMemoryCache` has no key-enumeration API) — otherwise tenants would see a stale global value for up to the 30-minute cache TTL
- Settings are **not** auto-seeded for new companies today — `AppSettingsSeed.SeedDefaultsAsync` is dead code (call commented out in `Program.cs`, and it only ever targeted one hardcoded company). Use the Global/Company Settings UI to configure values manually until this is revisited.

**Category identity across tiers** — a company category and a global category with the same `Name` (e.g. `general`) are separate rows with different `Id`s; only `Name` links them. This has a few consequences handled in `AppSettingsService`:
- `GetSettingsAsync`/`GetGlobalSettingsAsync` resolve the sibling category by `Name` (`ResolveGlobalCategoryIdAsync`), not by raw `Id` — otherwise a company with a pre-existing category would never see any global values merge in at all
- The settings list returned to the UI pins `CategoryId` to whichever category is currently being viewed, even for rows inherited from global (which carry the global category's own `Id`) — so editing and saving an inherited row files the new override under the right category instead of silently vanishing
- Editing/deleting a row that turns out to belong to the global tier is guarded server-side (`SaveSetting`, `SaveCompanySetting`, and the `Delete*` actions in both `SettingsController` and `AdminController`): saving forks a fresh company-scoped row rather than overwriting the shared global one; deleting is rejected outright if the row isn't owned by the current company. Categories specifically can't be "forked" (their `Id` is the FK every setting under them points to), so editing/deleting a global category is only permitted from `/admin/settings`
- The delete/authorization checks also close a pre-existing gap where any tenant could delete an arbitrary category/setting row by `Id`, regardless of which company owned it

### Reporting & Export
- Grid-based permit report with date range, location, certificate type, permit holder, and status filters
- **Reports Dashboard** — activated on "View Report" click, showing:
  - 5 KPI cards: Total, Pending, Approved, Closed, Overdue (overdue card highlights red when count > 0)
  - Status distribution doughnut chart
  - Monthly trend stacked bar chart (always last 12 months, independent of date filter)
  - Top 8 locations horizontal bar chart
- 4 report tabs: All Permits (DataTables grid with Excel export and grouping), Overdue (paginated, 20 per page), By Holder (with approval rate progress bar), By Location
- Excel export via ClosedXML

### System Menu Management
- `SystemMenu` entity links menu items to roles, controlling sidebar visibility per user role
- Portal Admins can edit menu visibility via `/tools/editsystemmenus`
- Menu state evaluated server-side in `_SideMenuPartial.cshtml`

### Performance & Caching
`IMemoryCache` is used across several hot paths to avoid repeated database round-trips:

| Cache key pattern | TTL | Invalidation |
|---|---|---|
| `company:{id}:meta` | 15 min | `AdminController` on company edit/toggle |
| `appsettings:cats:{companyId}` | 30 min | `AppSettingsService` on category write |
| `appsettings:vals:{companyId}` | 30 min | `AppSettingsService` on setting write |
| `user:{userId}:roles` | 2 min | `CurrentUserService.InvalidateRolesCache()` |
| `workflows:{companyId}` | 5 min | `WorkflowController` on create/update/delete |
| `roles:dropdown` | 10 min | Not explicitly invalidated (role changes are infrequent) |
| `sites:dropdown:{companyId}` | 10 min | Not explicitly invalidated (site changes are infrequent) |

All EF Core read-only queries use `AsNoTracking()`. All async controller actions accept and propagate `CancellationToken`.

Response caching (HTTP layer) is applied to chart data and dropdown endpoints:
- Dashboard donut and bar chart endpoints — 30-second `Cache-Control`
- Reports permit-holder dropdown — 60-second `Cache-Control`

### Audit & Security
- Application events logged via `ILogService.LogMessageAsync`/`LogMessage` to the `AuditLog` table (type, category, entity, description, URL, serialized data, acting user, `CompanyId`, timestamp). `CompanyId` is stamped automatically by `LogService` — resolved from `user.UserCompany` or fetched from the DB if the nav property is not loaded. An index on `(CompanyId, LogType, CreatedWhen)` covers the dashboard recent-activity query and prevents timeouts on large tables.
- Automatic audit-field stamping (`CreatedWhen`/`CreatedBy`/`UpdatedWhen`/`UpdatedBy`) via an EF Core `SaveChangesInterceptor`, applied to every `IAuditableEntity` — a separate mechanism from the explicit `AuditLog` entries above
- Super admins can browse all `AuditLog` entries at `/admin/logs` (paginated, filterable by type/search, with a detail modal) — see **Super Admin Portal**
- Session-based access control with multi-tenant data isolation

### System Information (About Pages)
- **Super Admin** (`/admin/about`) — cross-tenant snapshot: total/active/inactive users, permit counts (Total/Open/Closed/Rejected + full per-status breakdown), app version, masked DB connection string, DB size, and live DB health (reuses the same `HealthCheckService` that backs the `/health` endpoint)
- **Tenant** (`/{company}/settings/about`, `Portal Admin`/`Super User`) — same shape scoped to the current company only; the DB card shows server/database/user individually instead of a connection string (via `SqlConnectionStringBuilder`, password never rendered), plus DB size and state (`DATABASEPROPERTYEX(..., 'Status')`, e.g. `ONLINE`). Currently only linked from the sidebar for Super Users, though the route itself also authorizes Portal Admins
- App version is sourced differently on each page: Super Admin reads the in-memory assembly version (`Assembly.GetExecutingAssembly().GetName().Version`); the tenant page reads the compiled DLL's file version resource directly (`FileVersionInfo.GetVersionInfo`), so it reflects whatever binary is actually running on disk
- Both ultimately reflect the `<Version>` MSBuild property in `Web/App.csproj` (`1.0.0`). CI used to override it per deploy; with no CI, every build reports `1.0.0` unless you pass `-p:Version=…` to `dotnet publish` (e.g. `-p:Version=1.0.42`; each part must be ≤ 65535, so a full date like `20261008` won't work as one part)

---

## User Roles

| Role | Description |
|---|---|
| **Super User** | Cross-tenant system access; manages companies via `/admin` portal |
| **Portal Admin** | Company-level administration |
| **User** | Standard access — view and submit permits |
| **Permit Issuer** | Can issue and approve permits |
| **Lead Permit Issuer** | Senior issuer with elevated approval authority |
| **Authorized Gas Tester** | Specialist role for gas-testing operations on confined space / hot work permits |
| **Permit Holder / Contractor** | Combined external role — treated as contractor for access control |
| **Worker** | Field worker — limited to assigned permits |
| **Contractor** | External contractor — restricted access |

Roles support soft-delete via `ISoftDeletable` and carry audit fields (`CreatedWhen`, `UpdatedWhen`, `CreatedBy`, `UpdatedBy`). The `IsSystemRole` flag marks roles that should not be deleted or renamed by tenant admins. Deleted roles are recoverable from the **Recycle Bin → Roles** tab.

`Role` is a shared entity (no `CompanyId`) — the same role catalog is used across all tenants. Because of this, the Roles grid and the "users in this role" panel on the Edit Role page both scope their user counts to the company currently being managed, not the global count across every tenant. The role-deletion guard, however, still checks the true global count, since deleting a shared role affects every company using it.

**Normalized Name (slug)**
- Set once at role creation via a dedicated **Normalized Name** field, separate from the display `Name`
- Auto-suggested in real time via JavaScript as the user types the Role Name (uppercased, non-letters stripped: `nameInput.value.toUpperCase().replace(/[^A-Z]/g, '')`), but remains manually editable until the user overrides it
- Validated server-side (`^[A-Z]+$` — uppercase letters only, no spaces/digits/symbols) and checked for uniqueness against `Role.NormalizedName`
- Immutable after creation: the Edit Role form renders it `readonly`, and `EditRole` (POST) explicitly restores the original value after `RoleManager.UpdateAsync` — ASP.NET Identity's `RoleManager` always re-derives `NormalizedName` from `Name` on every create/update, so the original slug is re-applied post-save to keep role lookups (e.g. `role.NormalizedName == "SUPERUSER"`) stable even if the display name changes later

**Unlimited Users flag**
- `Role.IsUnlimitedUsers` — set via a checkbox on the role create/edit form
- Users assigned a role marked unlimited are excluded from the company's `UserCreateLimit` count (see **User Creation Limits** below)

### User Creation Limits
- `UserCreateLimit` (from the `user_create_limit` app setting) caps the number of users per company
- The count excludes users whose role has `IsUnlimitedUsers = true`
- Once a company reaches its limit, the **New User** role dropdown is restricted server-side to only unlimited-users roles (`UsersController.GetSelectableUserRoles`) — an info banner explains why
- If no unlimited-users role exists, the New User page falls back to a full block message instead of an empty dropdown
- The restriction is re-validated on `POST` (not just the dropdown) to reject a tampered submission that selects a non-unlimited role after the limit is reached
- **Editing** an existing user's role is subject to the same limit, treated as reassigning one "slot" rather than a special case:
  - The user being edited is excluded from their own headcount (`excludeUserId`), and their *current* role always stays selectable (`keepSelectableRoleId`) even at capacity, so the dropdown never silently drops their existing value
  - At capacity (excluding themselves), the Edit dropdown only offers unlimited-users roles plus their current role — they can keep their role or move to an unlimited one, but can't take a limited slot from someone else
  - Re-validated on `POST` the same way as New — this closes a gap where editing was previously a backdoor around the New User limit (moving a user from an unlimited role into a limited one silently increased the counted headcount with no check)

---

## Enhancements (Phases 1–6 complete)

All seven phases of the enhancement have been implemented, including Phase 7 (Telerik removal).

### Phase 1 — Database & Entity Foundation ✅

New entities and migration (`AddFormBuilderFoundation`):

- **`FormControl`** — reusable UI control definition per company. Fields: `Id`, `CompanyId`, `Name`, `Label`, `ControlType` (enum: `TextBox`, `TextArea`, `Checkbox`, `MultiChoice`, `Select`, `DateTimeInput`, `FileUpload`, `Label`), `OptionsJson` (Select/MultiChoice/FileUpload config; for `Label` type: `{"headingLevel":"h3"}`), `IsRequired`, `Placeholder`, `HelpText`, `SortOrder`. Soft-deletable.
- **`FormTemplate`** — drag-drop layout for a permit or certificate form. Fields: `Id`, `CompanyId`, `Name`, `Description`, `TemplateType` (enum: `Permit`, `Certificate`), `LayoutJson` (rows/columns/control references), `HeaderConfigJson`, `FooterConfigJson`. No `IsActive` — all templates are always selectable. Auditable + soft-deletable.
- **`Permit.PermitDataJson`** (new nullable column) — stores filled form field values for new-system permits. Existing `PermitForm` column retained and untouched for old-system permits.
- New DbSets: `FormControls`, `FormTemplates`.
- New services registered in `CommonExtensions.AddPermitProServices()`: `IFormControlService`, `IFormTemplateService`.

`PermitDataJson` structure:
```json
{
  "templateId": "guid",
  "savedAt": "2026-07-27T10:00:00Z",
  "fields": {
    "col-uuid-1": "text value",
    "col-uuid-2": true,
    "col-uuid-3": ["option-a"],
    "col-uuid-4": "2026-07-28T08:00:00",
    "col-uuid-5": ["attachment-guid-1"]
  }
}
```

`LayoutJson` structure — each row declares a `columnCount` (1, 2, or 3) which divides the row into equal-width slots. A `columnCount: 1` row spans the full page width; `columnCount: 2` splits into two halves; `columnCount: 3` splits into thirds. All columns in a row share the same width.
```json
{
  "rows": [
    {
      "id": "row-uuid",
      "columnCount": 2,
      "columns": [
        { "id": "col-uuid-1", "controlId": "form-control-uuid", "labelOverride": null, "isRequiredOverride": null },
        { "id": "col-uuid-2", "controlId": null, "labelOverride": null, "isRequiredOverride": null }
      ]
    }
  ]
}
```
`columns` always has exactly `columnCount` entries. A slot with `controlId: null` renders as an empty placeholder. Reordering rows or changing `columnCount` in the builder re-initialises the column array (controls already placed in removed slots are returned to the library panel).

### Phase 2 — Custom Controls Management ✅

Routes: `/admin/formcontrols?companyId=<guid>` (Super Admin) · `/{company}/formcontrols` (tenant, Super User only).

- Plain HTML table + BS5 modal for create/edit. No Telerik.
- `ControlType` dropdown reveals Options panel (dynamic value/label rows) for `Select`/`MultiChoice`; switches to `maxFiles`/`allowedTypes` config for `FileUpload`; shows Heading Level selector (H1–H6, stored in `OptionsJson`) for `Label`.
- `Label` type hides the Required and Help Text fields — they are not applicable.
- Search by name/label, filter by type, 20-per-page pagination.
- Bootstrap Modal replaces all `confirm()`/`alert()` dialogs.

### Phase 3 — Form Template Builder ✅

Routes: `/admin/formtemplates` · `/{company}/formtemplates` (consolidated — permit and certificate templates shown together with type-filter tabs; `?type=permit|certificate` to filter).

- Three-panel builder: Library (Sortable.js drag source) → Canvas (rows with 1/2/3 column selector) → Properties (label/required overrides).
- Save via JSON `POST`. `TEMPLATE_ID` is `let` — updated after first create so subsequent saves on the same page perform an update instead of creating duplicates. URL updated via `history.replaceState`.
- Header/Footer config modal (permit templates only — hidden for certificate templates): logo, permit number, site name, date, custom text — stored in `HeaderConfigJson`/`FooterConfigJson`.
- Preview modal renders all 8 control types with live values.
- Index page "New Template" is a dropdown: Permit Form / Certificate — routes to the correct builder.
- Both Admin and tenant routes share the same views; layout (`_Layout` vs `_LayoutAdmin`) and save URL resolved at runtime from route data.
- No `IsActive` state — templates are simply created/deleted; delete is always allowed.

### Phase 4 — New-System Permit Form Rendering ✅

**Cutover date** — `AppSetting` (`general` / `new_permit_form_cutover_date`). Exposed on `ISystemConfigurationService` as `DateTime? NewPermitFormCutoverDate`. Null = new system disabled.

Bifurcation in `PermitsController`:
```csharp
private bool IsNewSystemPermit(Permit permit) =>
    _config.NewPermitFormCutoverDate.HasValue &&
    permit.CreatedWhen >= _config.NewPermitFormCutoverDate.Value;
```

- `New` and `Edit` actions redirect to `NewForm`/`EditForm` for new-system permits; old-system paths untouched.
- `EnrichLayout()` embeds full control metadata into `LayoutJson` server-side so the browser needs no extra API calls.
- New views (`NewForm.cshtml`, `EditForm.cshtml`) use `_NewFormScript.cshtml` partial — shared JS renders all 8 control types (incl. `Label`), handles Flatpickr date pickers, custom dropzone upload, inline validation with `is-invalid` per field.
- Attachments card is hidden on `NewForm.cshtml` — attachments are only available after the permit is first saved (edit mode).
- Fixed fields above the canvas: **Description** (required), **Start date/time** (required), **End date/time** (required). All three are stored directly on `Permit.Description`, `Permit.StartDateTime`, `Permit.EndDateTime`.
- `Permit.Title` is kept in sync with `Description` for grid display.
- Right-side panel (edit): Approve/Reject, Export PDF, Mark as Closed, Certificates link.
- Workflow history in collapsible BS accordion with entry count badge.
- Created/last-updated info block below workflow history.
- Attachments card: immediate upload (edit mode) via dedicated endpoint `POST /{company}/permits/{id}/attachments/upload`; staged upload (new permit) appended to FormData at save time. Uploaded file list with delete per row.

### Phase 5 — PDF Export (Dual-engine) ✅

| Permit age | PDF engine | Template source |
|---|---|---|
| Old (before cutover date) | PuppeteerSharp + Scriban | Existing `PermitPdfService` — unchanged apart from null-safe certificate parsing (see below) |
| New (on/after cutover date) | **QuestPDF** (`QuestPDF 2025.4.3`) | `FormTemplate.LayoutJson` + `Permit.PermitDataJson` |

`PermitsController.ExportPdf` delegates based on `IsNewSystemPermit`. QuestPDF document:
- **Header** — company logo (`FitWidth`), custom text, company name, permit number, site name, date (all individually togglable via `HeaderConfigJson`).
- **Body** — rows/columns from layout; each control type rendered appropriately (checkbox as filled square, multi-choice checklist, select shows matched label, datetime formatted, file upload lists filenames, `Label` renders bold text + horizontal rule).
- **Footer** — page numbers, print date, custom text (via `FooterConfigJson`).
- Community licence set on startup.
- Attached certificates are appended as additional pages. `ExportPdf` includes `Certificates` (non-deleted) with `ThenInclude(c => c.FormTemplate)`; `QuestPdfService` iterates new-system certificates and appends one page each (certificate name + permit number in header; same row/cell rendering; no footer config).

**Old-system certificate parsing is null-safe.** Ticking a certificate (A–H) on the old-system form stores only `{"name":"hotwork"}` in `PermitForm.general.certificates`; field data is merged in only when the certificate dialog is opened and saved. `PermitPdfService` previously assumed every field existed, so `(bool)cert["workChecklist"][0]["value"]` threw a `NullReferenceException` and the whole export returned 500. All certificate field access now uses null-conditional indexers (`cert["x"]?["y"]`) and checkbox values go through `IsChecked(JToken)` (missing/null → `false`). An unfilled certificate now prints as a blank form (empty checkboxes, "—" values) instead of breaking the export.

### Phase 6 — Certificate Form Customization ✅

- `Certificate` entity: added `FormTemplateId` (nullable FK → `FormTemplates`, `SET NULL` on delete) and `CertificateDataJson`. Migration: `AddCertificateFormBuilder`.
- `ICertificateService` / `CertificateService` — CRUD + `SaveDataAsync`.
- Routes at `/{company}/permits/{permitId}/certificates/*`:
  - `GET /` — list all certificates on a permit (table, add/delete)
  - `GET /{id}` — fill certificate form; assign template if none set; template switcher in card header
  - `POST /{id}/save` — saves `CertificateDataJson` via JSON body
  - `POST /{id}/template` — assign a different template
- Certificates link added to the right-side panel of `EditForm.cshtml`.
- Old hardcoded certificate partials (`_HotWork.cshtml` etc.) preserved for pre-cutover permits.
- **Templates** sidebar group added (collapsible): Form Controls, Form Templates, Certificate Templates — visible to Super Users.

### Phase 7 — Telerik (Kendo UI) Removal ✅ Done

Telerik/Kendo is gone: no NuGet package, CDN scripts/theme, local `wwwroot/lib/kendo-ui` library or CSP entries.

| Kendo Component | Replacement |
|---|---|
| Kendo Grid | DataTables 2 (Bootstrap 5), `wwwroot/js/datagrid.js`; Buttons (Excel) + RowGroup on Reports |
| Kendo DropDownList | Native `<select>` + Tom Select |
| Kendo DropDownTree | Treeselect.js (`wwwroot/js/location-picker.js`) |
| Kendo Date/Time pickers | Flatpickr |
| Kendo CheckBox / Switch / TextBox / NumericTextBox | Bootstrap form controls |
| Kendo Upload | Custom dropzone (JS `File[]` array, no `<input type="file">` in form) |
| Kendo Window/Dialog | BS5 modal |
| Kendo Notification | Toast via `showToast()` helper |
| Kendo Chart | Chart.js |

The old-system permit form (New/Edit, panels, certificates A–H) uses `wwwroot/js/form-controls.js`, which keeps Kendo's value semantics so saved permits and their PDFs are unchanged.

---

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server (local or remote instance)
- Node.js (optional, for asset builds)

### Local Setup

1. Clone the repository:
   ```bash
   git clone <repo-url>
   cd PermitPro-1.2
   ```

2. Configure the connection string in `Web/appsettings.Development.json`:
   ```json
   {
     "ConnectionStrings": {
       "Development": "Server=localhost;Database=PermitPro;Trusted_Connection=True;"
     }
   }
   ```

3. Configure `appsettings.json` sections:
   ```json
   {
     "JwtSettings": {
       "Issuer": "<issuer>",
       "Audience": "<audience>",
       "SecretKey": "<secret>"
     },
     "EmailSettings": { ... },
     "PTWSettings": { ... }
   }
   ```

4. Apply database migrations:
   ```bash
   dotnet ef database update --project Core --startup-project Web
   ```

5. Run the application:
   ```bash
   dotnet run --project Web
   ```

   Default URLs:
   - HTTP: `http://localhost:5144`
   - HTTPS: `https://localhost:7079`

### First-Time Setup

1. Create a `SUPERUSER` role and assign it to an admin account directly in the database (or via seed).
2. Navigate to `/admin/login` and sign in with that account.
3. Create your first company from `/admin/companies`.
4. Tenant users log in at `/account/login` and select their company from the dropdown.

### Docker / MSSQL

To restore a SQL Server backup into a Docker container:
```bash
docker cp sql.bak <container-id>:/var/opt/mssql/data
```

Then restore via `sqlcmd` or SQL Server Management Studio.

---

## Configuration

### Password Policy (enforced via ASP.NET Identity)
- Minimum 12 characters
- Requires uppercase, lowercase, digit, and non-alphanumeric character
- Email confirmation required before account activation
- 3 failed login attempts triggers a 10-minute lockout

### Session & Cookie Policy
- Default cookie lifetime: 3 hours (sliding expiration)
- Persistent "Remember Me" cookie: 30 days
- Session timeout: 60 minutes
- Cookies: HttpOnly, SameSite=Lax. Strict was dropped because it withheld the auth cookie when users opened links from other apps (email, WhatsApp), so they appeared logged out.
- Data protection tokens expire after 30 minutes
- A request whose first URL segment is not a company GUID returns 404 and never signs the user out. Previously, Android Chrome's fetch of the manifest icons (which pointed at non-existent root paths) fell through to `{company}/landing` and signed users out right after login.

### PTW Certificate Types (`appsettings.json` → `PTWSettings`)
Configurable certificate types mapped to permit form workflows.

---

## Database

Entity Framework Core with SQL Server. Key entities (26 total):

| Entity | Purpose |
|---|---|
| `Company` | Multi-tenant root; holds name, logo filename, and active state |
| `Site` | Work sites with GPS coordinates |
| `SitePermit` | Junction table linking sites to permits |
| `UserSite` | Junction table linking users to sites |
| `Permit` | Safety permits with status lifecycle |
| `Workflow` / `WorkflowStep` | Approval workflow definitions |
| `WorkflowHistory` | Audit trail of workflow transitions |
| `UserInfo` | Extended ASP.NET Identity user |
| `Certificate` | Safety certifications per permit |
| `Attachment` | Files attached to permits |
| `Notification` | User notification records (Title, Message, read/archived state) |
| `AuditLog` | Full CRUD change log |
| `LogInfo` | Application-level log records |
| `Division` / `Department` | Organisational structure |
| `Address` | Company address records |
| `Contact` | Company contact records (typed via `ContactTypeEnum`) |
| `PermitNumber` | Sequential permit numbering per company |
| `SystemMenu` | Role-based menu visibility configuration |
| `AppSettingCategory` | Per-company grouping for key-value settings |
| `AppSetting` | Per-company key-value configuration (unique on CompanyId + CategoryId + Key) |
| `HotWork` / `ConfinedSpace` | Certificate sub-entities (in `Entities/Certificates/`) |
| `FormControl` | Reusable form field definition per company (`ControlType`, `OptionsJson`, `IsRequired`, etc.) |
| `FormTemplate` | Drag-drop form layout (`LayoutJson`, `HeaderConfigJson`, `FooterConfigJson`, `TemplateType`) — no IsActive |
| `FormSectionInfo` | Permit form section metadata (in `Entities/Forms/`) |

All core entities support soft-delete via `ISoftDeletable`. Hard deletes are not performed by the application by default (`UseSoftDelete = true`). `Role` (via ASP.NET Identity) also implements `ISoftDeletable` with full audit fields.

### Migration history

| Migration | Description |
|---|---|
| `InitialCreate` | Base schema |
| `AddNewProperty-PreviousPermitStatus-Permit` | Tracks previous permit status |
| `AddedNewProperties-Permit-01` | Additional permit fields |
| `AddSoftDelete` | `IsDeleted` / `DeletedWhen` / `DeletedBy` on all core entities |
| `AddNotificationTitle` | `Title` column on `Notification` |
| `AddRoleAuditFields` | Audit fields on `Role` |
| `AddRoleDescription` | `Description` and `IsSystemRole` on `Role` |
| `AddAppSettings` | `AppSettingCategory` and `AppSetting` tables |
| `AddCompanyLogo` | `LogoFileName` on `Company` |
| `AddRoleIsUnlimitedUsers` | `IsUnlimitedUsers` flag on `Role` |
| `AddGlobalSettingsSupport` | `IsUsingGlobal` flag on `AppSetting` + filtered unique indexes for global (`CompanyId IS NULL`) categories/settings |
| `AddDataProtectionKeys` | `DataProtectionKeys` table — shared Data Protection key ring persisted to the database instead of per-machine storage |
| `AddFormBuilderFoundation` | `FormControls` + `FormTemplates` tables; `Permit.PermitDataJson` column |
| `AddPermitTitleColumn` | `Permit.Title` column + SQL backfill from `PermitForm` JSON for existing permits |
| `AddCertificateFormBuilder` | `Certificate.CertificateDataJson` + `Certificate.FormTemplateId` FK → `FormTemplates` (SET NULL on delete) |
| `AddAuditLogCompanyId` | `AuditLog.CompanyId` column (nullable FK) |
| `AddAuditLogCompanyIndex` | Composite index `(CompanyId, LogType, CreatedWhen)` on `AuditLogs` — covers the dashboard recent-activity query |
| `RemoveFormTemplateIsActive` | Drops `IsActive` column and the filtered unique index from `FormTemplates` |

Run migrations:
```bash
dotnet ef migrations add <MigrationName> --project Core --startup-project Web
dotnet ef database update --project Core --startup-project Web
```

---

## Deployment

### Current hosting: dedicated Windows server behind Cloudflare

- **Server:** a dedicated Windows Server running IIS, with the app hosted in-process. IIS binds `http:80` and `https:443` for `permitpro.app`.
- **TLS:** a Cloudflare Origin Certificate on the server. Cloudflare's SSL/TLS mode must be Full or Full (strict) for this to work.
- **DNS:** managed in Cloudflare and proxied. `permitpro.app` and `*.permitpro.app` resolve to the origin through Cloudflare.
- **Redirect rule** (Cloudflare): any hostname other than `permitpro.app` gets a 301 redirect to `https://permitpro.app` plus the request path. See issue #4: it currently drops the query string.
- **Client IP:** every request arrives from a Cloudflare edge IP. The app restores the real client IP from `CF-Connecting-IP` (`Web/Services/CloudflareClientIp.cs`), which the per-IP login rate limiter depends on.
- **Outbound access the server needs:** HTTPS to `api.cloudflare.com` (refreshes Cloudflare's IP ranges at startup and every 24 hours), plus SMTP for email. The old-system PDF export also needs internet access: PuppeteerSharp downloads headless Chrome on first use, and the rendered page loads its CSS and images from the app's own domain.
- **Deploys are manual.** There's no CI/CD (see below), so merging to `master` doesn't change the live site. Publish, then copy the `publish` folder to the server:

  ```bash
  dotnet publish Web/App.csproj --configuration Release --framework net10.0 --output ./publish --runtime win-x64 --self-contained -p:PublishReadyToRun=true
  ```

The **WebDeploy (SmarterASP.NET)** and **AWS EC2** sections below describe previous hosting and are no longer used. They are kept for reference.

### CI/CD (GitHub Actions) — currently none

There is **no automated deployment at the moment**. The `deploy-on-merge.yml` workflow was removed on 2026-08-01 (commit `7f866e2`), so merging a pull request into `master` does not deploy anything. Deploys are done manually.

For reference, the removed workflow ran on every merged PR into `master`. It restored, built and published (Release, self-contained win-x64, ReadyToRun, `-p:Version=1.0.<github.run_number>`), then deployed to SmarterASP.NET via WebDeploy.

### WebDeploy (Manual)

Target host: SmarterASP.NET  
Deployment user: `[YOUR_WEB_DEPLOY_USERNAME]`

### AWS EC2 (Manual, Visual Studio Publish)

Publish profiles under `Web/Properties/PublishProfiles/`: `PermitPro v1.2 (AWS).pubxml` and `PermitPro UAT (AWS).pubxml`. Both target the same Windows EC2 instance (`ec2-52-70-84-135.compute-1.amazonaws.com`) running IIS + Web Management Service (WMSVC, port 8172), each deploying to its own IIS application (`DeployIisAppPath`: `PermitPro v1.2` / `PermitPro UAT`).

Both profiles set `<AllowUntrustedCertificate>True</AllowUntrustedCertificate>` — WMSvc presents a self-signed certificate by default (distinct from the site's own HTTPS certificate), which Web Deploy otherwise rejects with `ERROR_CERTIFICATE_VALIDATION_FAILED`. The more correct long-term fix is binding a real certificate to the Management Service in IIS Manager on the EC2 instance; the untrusted-cert override just unblocks deploys in the meantime.

---

## Security Notes

- Do not commit credentials, API keys, or connection strings to source control
- Use secrets management (not source-controlled files) for sensitive configuration
- The `reserved_roles` app setting (General category) restricts certain role assignments (SUPERUSER, PORTALADMIN)
- All audit events are written to `AuditLog` and are non-deletable by application users
- JWT `SecretKey` must be stored securely and never committed to source control
- The Super Admin portal (`/admin/*`) is protected by the `SUPERUSER` role check on every action — regular tenant users are rejected at login, not just redirected
- Authentication endpoints (login, forgot/reset password, API token, admin login) are rate-limited: **20 requests per 1-minute window per client IP** (policy: `"auth"`, HTTP 429 on breach) — applied via ASP.NET Core's built-in `UseRateLimiter` middleware, which must run after `UseRouting()`. Until 2026-10-07 it ran before routing, so the policy was never applied.
- The client IP is taken from Cloudflare's `CF-Connecting-IP` header, trusted only when the connection comes from a Cloudflare edge range (`AddCloudflareClientIp`, `Web/Services/CloudflareClientIp.cs`). Without this, every request appears to come from a Cloudflare IP. The trusted ranges refresh automatically from `https://api.cloudflare.com/client/v4/ips` at startup and every 24 hours, so Cloudflare IP changes need no redeploy. The server needs outbound HTTPS to `api.cloudflare.com`; if that is blocked or fails, the built-in list is used and a warning is logged. The 20/min limit leaves headroom for many workers sharing one site Wi-Fi or mobile carrier IP at shift start.
- ASP.NET Core Data Protection keys are persisted to the `DataProtectionKeys` table (shared across every environment pointed at the same database) rather than the per-machine default — without this, encrypted `AppSetting` values (e.g. `email_password`) only decrypt on whichever single machine originally encrypted them

---

# Ubuntu 24.04 / ASP.NET Core 10 / SQL Server 2025 Express Deployment

This document covers provisioning and configuring the UAT environment on AWS EC2:
Ubuntu 24.04, ASP.NET Core 10 behind Nginx, SQL Server 2025 Express, and restoring
the database from a backup. Access is via AWS Systems Manager Session Manager
(no SSH inbound rule required).

**Stack summary**
- OS: Ubuntu Server 24.04 LTS (EC2)
- App: ASP.NET Core 10, run via systemd, reverse-proxied through Nginx
- DB: SQL Server 2025 Express (Linux), CU1 or later
- Access: AWS SSM Session Manager (no public SSH port)
- Domain: `uat.permitpro.app` → Nginx on port 80/443

---

## 1. Launch the EC2 instance

- **AMI:** Ubuntu Server 24.04 LTS, x86_64 (SQL Server on Linux does not support ARM/Graviton)
- **Instance type:** `t3.large` (2 vCPU / 8 GB) minimum. SQL Server Express caps its
  buffer pool at ~1,410 MB regardless of instance size, so bigger RAM mainly benefits
  the OS/app side, not SQL Server itself. Go `t3.xlarge` for heavier workloads.
- **Storage:** gp3 EBS, 30–50 GB minimum
- **IAM role:** attach a role with the `AmazonSSMManagedInstanceCore` managed policy
  (required for Session Manager access — no key pair / SSH needed for this)
- **Security group:**
  | Port | Source | Purpose |
  |------|--------|---------|
  | 80   | 0.0.0.0/0 | HTTP (Nginx) |
  | 443  | 0.0.0.0/0 | HTTPS (Nginx) |
  | 22   | *not required* | SSM replaces inbound SSH entirely |
  | 1433 | *not open* | SQL Server — local/internal access only, never public |

## 2. Connect via SSM Session Manager

**Console:** EC2 → select instance → **Connect** → **Session Manager** tab → **Connect**

**CLI** (requires the `session-manager-plugin` installed locally):
```bash
aws ssm start-session --target i-XXXXXXXXXXXXXXXXX
```

Notes:
- You land as `ssm-user`, not `ubuntu`. `ssm-user` has sudo access, so `sudo apt update`
  etc. all work fine.
- Confirm the instance shows as "Managed" first:
  ```bash
  aws ssm describe-instance-information --query "InstanceInformationList[?InstanceId=='i-XXXXXXXXXXXXXXXXX']"
  ```
  `PingStatus` should be `Online`. If it's missing, check the IAM role or the agent:
  ```bash
  sudo systemctl status snap.amazon-ssm-agent.amazon-ssm-agent.service
  ```

### Optional: SSH/SCP over SSM (if you want scp-style file transfer without opening port 22)
Add to your local `~/.ssh/config`:
```
Host i-*
  ProxyCommand sh -c "aws ssm start-session --target %h --document-name AWS-StartSSHSession --parameters 'portNumber=%p'"
  User ubuntu
  IdentityFile ~/.ssh/your-key.pem
```
Then `scp`/`ssh` work as normal, targeting the instance ID instead of an IP. Still
requires your public key in `~/.ssh/authorized_keys` on the instance and `sshd` running,
but no inbound port 22 in the security group — SSM carries the traffic.

## 3. Initial system setup

```bash
sudo apt update && sudo apt upgrade -y
sudo timedatectl set-timezone Asia/Kuala_Lumpur

# UFW as a second layer behind the security group
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
sudo ufw status verbose
```

## 4. Install AWS CLI (needed for pulling files from S3)

```bash
sudo apt install -y curl unzip
curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o "awscliv2.zip"
unzip awscliv2.zip
sudo ./aws/install
rm -rf awscliv2.zip aws/

aws --version
```
Installs to `/usr/local/bin/aws`, which is on the `sudo` secure path, so `sudo aws ...`
works too. With the instance's IAM role attached, `aws configure` is not needed —
credentials come from the instance role automatically.

> **Tip:** avoid running `aws s3 cp` with `sudo` when downloading into a directory
> you own — `sudo` won't see your working AWS session context the same way. Download
> to `/tmp` first, then `sudo mv` into protected directories.

## 5. Install and configure ASP.NET Core 10

Ubuntu 24.04 ships .NET 10 directly in its built-in apt feed — no need to add
Microsoft's package repo for this part.

```bash
sudo apt update
sudo apt install -y aspnetcore-runtime-10.0   # runtime only, for hosting
# OR: sudo apt install -y dotnet-sdk-10.0     # if building/publishing on this box

dotnet --list-runtimes
```
Expect both `Microsoft.AspNetCore.App 10.0.x` and `Microsoft.NETCore.App 10.0.x`.

### Deploy the published app (via S3)
```bash
sudo apt install -y unzip
sudo mkdir -p /var/www/permitpro

aws s3 cp s3://your-bucket/permitpro/publish.zip /tmp/publish.zip
sudo unzip -o /tmp/publish.zip -d /var/www/permitpro
sudo chown -R www-data:www-data /var/www/permitpro
rm /tmp/publish.zip
```

### systemd service
`/etc/systemd/system/permitpro.service`:
```ini
[Unit]
Description=PermitPro ASP.NET Core App
After=network.target

[Service]
WorkingDirectory=/var/www/permitpro
ExecStart=/usr/bin/dotnet /var/www/permitpro/PermitPro.App.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=permitpro
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production[Staging]
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
```
```bash
sudo systemctl daemon-reload
sudo systemctl enable --now permitpro
sudo systemctl status permitpro --no-pager
```

### Nginx reverse proxy
```bash
sudo apt install -y nginx

# remove the default site — otherwise it takes priority over your config
sudo rm -f /etc/nginx/sites-enabled/default
```
`/etc/nginx/sites-available/permitpro`:
```nginx
server {
    listen 80;
    server_name uat.permitpro.app;

    location / {
        proxy_pass         http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header    Upgrade $http_upgrade;
        proxy_set_header    Connection keep-alive;
        proxy_set_header    Host $host;
        proxy_set_header    X-Real-IP $remote_addr;
        proxy_set_header    X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header    X-Forwarded-Proto $scheme;
        proxy_cache_bypass  $http_upgrade;
    }
}
```
```bash
sudo ln -s /etc/nginx/sites-available/permitpro /etc/nginx/sites-enabled/permitpro
sudo nginx -t
sudo systemctl reload nginx
```

## 6. Install and configure SQL Server 2025 Express

Ubuntu 24.04 support went GA starting with SQL Server 2025 **CU1** — make sure the
production repo (not the old preview one) is used.

```bash
# GPG key
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | sudo gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg

# repo for Ubuntu 24.04
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/mssql-server-2025.list | sudo tee /etc/apt/sources.list.d/mssql-server-2025.list

sudo apt update
sudo apt install -y mssql-server

# interactive setup — choose "Express" edition when prompted
sudo /opt/mssql/bin/mssql-conf setup

systemctl status mssql-server --no-pager
```

### Command-line tools
```bash
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | sudo tee /etc/apt/trusted.gpg.d/microsoft.asc
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/prod.list | sudo tee /etc/apt/sources.list.d/mssql-release.list
sudo apt update
sudo apt install -y mssql-tools18 unixodbc-dev

echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' >> ~/.bashrc
source ~/.bashrc

sqlcmd -S localhost -U sa -P '<sa-password>' -No
```

> **Note:** SQL Server Express hard-caps the buffer pool at ~1,410 MB regardless of
> instance RAM. If dashboard/reporting queries start hitting `RESOURCE_SEMAPHORE`
> waits again under load, this cap — not instance size — is the likely cause.
> Developer edition (also free) has no such cap and is a good fallback for testing.

## 7. Restore the database from backup

```bash
# pull the .bak from S3
aws s3 cp s3://your-bucket/backups/MyDatabase.bak /tmp/MyDatabase.bak

# move into place with correct ownership
sudo mkdir -p /var/opt/mssql/backup
sudo mv /tmp/MyDatabase.bak /var/opt/mssql/backup/
sudo chown mssql:mssql /var/opt/mssql/backup/MyDatabase.bak
```

Inspect logical file names (needed because a Windows-origin `.bak` has Windows-style
paths baked in):
```bash
sqlcmd -S localhost -U sa -P '<sa-password>' -No -Q \
"RESTORE FILELISTONLY FROM DISK = N'/var/opt/mssql/backup/MyDatabase.bak'"
```

Restore, remapping file paths to Linux locations:
```bash
sqlcmd -S localhost -U sa -P '<sa-password>' -No -Q \
"RESTORE DATABASE MyDatabase
FROM DISK = N'/var/opt/mssql/backup/MyDatabase.bak'
WITH MOVE 'MyDatabase' TO '/var/opt/mssql/data/MyDatabase.mdf',
     MOVE 'MyDatabase_log' TO '/var/opt/mssql/data/MyDatabase_log.ldf',
     REPLACE;"
```
(Add a `MOVE` line per logical file listed in the previous step — there may be more
than two if the source DB had extra data files or full-text catalogs.)

Verify:
```bash
sqlcmd -S localhost -U sa -P '<sa-password>' -No -Q \
"SELECT name, state_desc FROM sys.databases WHERE name = 'MyDatabase'"
```
`state_desc` must show `ONLINE`.

### Create a dedicated app login (don't use `sa` in the connection string)
```sql
CREATE LOGIN permitpro_app WITH PASSWORD = 'Str0ng!Passw0rd';
USE MyDatabase;
CREATE USER permitpro_app FOR LOGIN permitpro_app;
ALTER ROLE db_owner ADD MEMBER permitpro_app;
```

### Update the app's connection string
`appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost,1433;Database=MyDatabase;User Id=permitpro_app;Password=Str0ng!Passw0rd;TrustServerCertificate=True;"
}
```
```bash
sudo systemctl restart permitpro
```

---

## 8. Verification checklist

Run through top to bottom — each layer should pass before checking the next.

**SSM / AWS**
```bash
aws sts get-caller-identity
aws ssm describe-instance-information --query "InstanceInformationList[?InstanceId=='i-XXXXXXXXXXXXXXXXX']"
```

**OS**
```bash
lsb_release -a
free -h
df -h
sudo ufw status verbose
```

**.NET runtime**
```bash
dotnet --list-runtimes
```

**App (systemd)**
```bash
sudo systemctl status permitpro --no-pager
sudo journalctl -u permitpro -n 50 --no-pager   # if not healthy
sudo ss -tlnp | grep 5000
curl -i http://localhost:5000/                  # bypasses Nginx — isolates app vs proxy
```

**Nginx**
```bash
sudo nginx -t
sudo systemctl status nginx --no-pager
ls -la /etc/nginx/sites-enabled/    # confirm "default" is NOT present, "permitpro" is
```
From outside the box:
```bash
curl -i http://uat.permitpro.app/
```

**SQL Server**
```bash
systemctl status mssql-server --no-pager
sqlcmd -S localhost -U sa -P '<sa-password>' -No -Q "SELECT @@VERSION"
sudo ss -tlnp | grep 1433
```

**Database**
```bash
sqlcmd -S localhost -U sa -P '<sa-password>' -No -Q \
"SELECT name, state_desc FROM sys.databases WHERE name = 'MyDatabase'"

sqlcmd -S localhost -U permitpro_app -P 'Str0ng!Passw0rd' -No -Q "SELECT DB_NAME()"
```

**End-to-end (app ↔ DB)**
```bash
curl -i http://localhost:5000/api/health   # or any route that queries the DB
```

**External exposure check** (from your local machine — only 80/443 should respond):
```powershell
Test-NetConnection -ComputerName uat.permitpro.app -Port 80
Test-NetConnection -ComputerName uat.permitpro.app -Port 1433   # should fail
Test-NetConnection -ComputerName uat.permitpro.app -Port 5000   # should fail
```

---

## 9. Known issues / troubleshooting log

| Symptom | Cause | Fix |
|---|---|---|
| `sudo: aws: command not found` | `sudo` uses a restricted `PATH` that may not include where `aws` lives | Install via the official installer (`/usr/local/bin/aws`), or run `aws` without `sudo` and only `sudo mv` the result |
| Browser shows "Welcome to nginx!" instead of the app | Default Nginx site still enabled and taking priority, or `server_name` mismatch | `sudo rm /etc/nginx/sites-enabled/default`, confirm `server_name` matches the domain, confirm the `permitpro` site is symlinked into `sites-enabled`, then `nginx -t && systemctl reload nginx` |
| Page unreachable via public DNS entirely | Security group missing the port rule, UFW blocking locally, or Nginx not listening on `0.0.0.0` | Check SG rules, `sudo ufw status verbose`, `sudo ss -tlnp \| grep :80` |

---

## 10. Follow-ups / hardening

- [ ] Attach an Elastic IP so the domain doesn't break on instance stop/start
- [ ] Enable HTTPS on `uat.permitpro.app` (Certbot, or terminate TLS at Cloudflare if proxying through it)
- [ ] Monitor for `RESOURCE_SEMAPHORE` waits — SQL Server Express's ~1,410 MB buffer pool cap is a known ceiling for this setup
- [ ] Confirm scheduled backups are in place for the restored database (not just the initial restore)
- [ ] Review IAM role permissions on the instance — scope S3 access to only the specific buckets/prefixes needed