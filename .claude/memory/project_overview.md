---
name: project-overview
description: "PermitPro-1.2 — tech stack, architecture, routing, and key domain patterns"
metadata: 
  node_type: memory
  type: project
  originSessionId: 685637db-3874-471d-892e-b3efb915554b
---

PermitPro is a multi-tenant Permit-to-Work (PTW) SaaS web app.

**Why:** Helps industrial/HSE companies manage PTW lifecycle — create, submit, approve/reject, track permits.

**How to apply:** All development must respect multi-tenancy, soft-delete, and the EF query filter pipeline described here.

## Tech Stack
- ASP.NET Core 10.0 MVC
- Entity Framework Core 10 with SQL Server
- ASP.NET Identity with custom `Role : IdentityRole` (string Id) and `UserInfo : IdentityUser`
- Bootstrap 5.3.8, DataTables 2, Tom Select, Flatpickr, Treeselect.js (Telerik/Kendo removed)
- Chart.js 4.4.4 (CDN: `https://cdn.jsdelivr.net/npm/chart.js@4.4.4/dist/chart.umd.min.js`)
- Bootstrap 5 tabs (`data-bs-toggle="tab"`)
- Font Awesome Pro 6.x (`fa-loader fa-spin` for inline spinners)

## Routing
Multi-tenant route pattern: `{company}/{controller}/{action}/{id?}` — the company slug is the first URL segment.

## Key Domain Patterns

### Soft Delete
- `ISoftDeletable` interface + EF query filters on `ApplicationDbContext`
- `ApplicationDbContext.UseSoftDelete` (bool) — set `false` to bypass interceptor for hard deletes
- `RestoreAsync(ISoftDeletable)` / `SoftDeleteAsync` for cascade soft-delete/restore
- Always use `.IgnoreQueryFilters()` when querying deleted items

### DeletedItem Projection
Flat model used by all recycle bin tabs:
```csharp
public class DeletedItem {
    public Guid Id;
    public string EntityType;
    public string DisplayName;
    public string Detail;
    public Guid CompanyId;     // Guid.Empty for global entities (e.g. Roles)
    public string CompanyName; // "(global)" for global entities
    public DateTime? DeletedWhen;
    public Guid? DeletedBy;
    public string DeletedByName; // resolved in controller
}
```

### Roles (ASP.NET Identity)
- `Role : IdentityRole` — Id is a **string** (GUID string)
- Global entity — not company-scoped → use `CompanyId = Guid.Empty`, `CompanyName = "(global)"`
- In DeletedItem projections: `Id = Guid.Parse(r.Id)`
- In Restore/Delete by id: look up with `r.Id == id.ToString()`
- DB FK cascade covers UserRoles, RoleClaims, SystemMenusRoles — no special cascade handling needed

### Permit Entity
- `PermitForm` is a **JSON blob** field — all form data (startDateTime, endDateTime, certificates, location) lives inside it
- Parse with `JObject.Parse(p.PermitForm)` — must be done **in-memory** after `.ToList()`, not in EF query
- `CreatedBy` (Guid) = permit holder — resolve names by joining `Users` table separately
- `PermitStatusEnum`: Draft=0, Pending=1, Approved=2, Rejected=3, Suspended=4, KIV=5, Closed=6, Unknown=7, Overdue=8, ClosedNoAction=9
- Terminal statuses: `{ Closed, Rejected, ClosedNoAction }`

### DateTime
- `GeneralHelper.GetDateInTimeZone(DateTime)` used to normalize all DateTime values for display

### Global Loading Overlay (Layout)
- `<div class="loading">` in `_Layout.cshtml`
- Show/hide: `$('.loading').show()` / `.hide()`
- Links with class `no-loading` skip it; buttons do NOT trigger it automatically

### Controller Base
- `AppControllerBase.OnActionExecuting` sets `ViewData["CompanyId"]` (Guid) and `ViewData["CompanyIdStr"]` (string)
- Available in partials via `new ViewDataDictionary(ViewData)`
