---
name: patterns
description: "Key coding conventions, EF patterns, Kendo UI usage, and gotchas specific to this codebase"
metadata: 
  node_type: memory
  type: project
  originSessionId: 685637db-3874-471d-892e-b3efb915554b
---

**Why:** These patterns are not always obvious from the code and save time when working on this repo.

**How to apply:** Follow these before writing any new controller action, EF query, or Kendo component.

## EF Core Patterns

- Always use `.IgnoreQueryFilters()` when querying soft-deleted entities
- `ApplicationDbContext.UseSoftDelete = false` before hard-deleting (bypasses interceptor)
- Never translate `JObject.Parse(p.PermitForm)` in EF — materialize with `.ToList()` first, then parse in-memory
- When resolving user names from a Guid foreign key, do a separate `_dbContext.Users.Where(...)` query after materializing the main query, build a `Dictionary<string, string>` (id → name), and use O(1) lookups

## ASP.NET Identity Roles

- `Role.Id` is a **string** (not Guid), e.g. `"3fa85f64-5717-4562-b3fc-2c963f66afa6"`
- When projecting to `DeletedItem.Id` (Guid): `Guid.Parse(r.Id)`
- When looking up by Guid for Restore/Delete: `r.Id == id.ToString()`

## Kendo UI

- DropDownList width is not set on the Kendo helper — set via `HtmlAttributes(new { @style = "width:Xpx" })`
- `ComponentSize.Medium`, `Rounded.Medium`, `FillMode.Solid` — standard styling for all pickers
- DataSource reads use `.Action("ActionName", "ControllerName")` without company prefix (routing handles it)
- To reset a DropDownList to first item: `$('#id').data('kendoDropDownList').select(0);`
- To apply client-side grid filter: `var grid = $('#gridResult').data('kendoGrid'); grid.dataSource.filter({...});`

## JavaScript / AJAX

- Company ID available as Razor variable: `var companyId = '@companyId';`
- Chart.js: always `chart.destroy()` before recreating (avoid canvas reuse errors)
- `chartColors` object defined globally for color consistency across charts
- Global loading overlay: `$('.loading').show()/.hide()` — avoid for partial updates (see [[feedback-loading-indicator]])

## Permissions / ViewData

- `AppControllerBase.OnActionExecuting` sets `ViewData["CompanyId"]` and `ViewData["CompanyIdStr"]`
- Pass to partials: `new ViewDataDictionary(ViewData)`
- SuperUser check: `ViewBag.IsSuperUser` or similar — check RecycleBin/Index for exact pattern

## TempData Messages

- No global TempData rendering in `_Layout.cshtml` — each view that wants to show `TempData["SuccessMessage"]`/`["ErrorMessage"]` renders its own alert block (see `Users/Index.cshtml`, `Sites/Index.cshtml`, `Contractors/Index.cshtml`). `_LayoutAdmin.cshtml` (Super Admin section only) is the exception — it does render both globally.
- Standard markup: `<div class="alert alert-success alert-dismissible fade show" role="alert"><i class="fa-solid fa-circle-check me-2"></i> @TempData["SuccessMessage"]<button type="button" class="btn-close" data-bs-dismiss="alert"></button></div>`
- If an action redirects somewhere new (e.g. back to an Edit page rather than the usual Index), check that the target view actually has one of these blocks before relying on `TempData["SuccessMessage"]` to reach the user — it silently does nothing otherwise.

## Common Gotchas

- `DurationDays` is a computed property (no setter) — cannot be set in object initializer, only derived from StartDate/EndDate
- After editing a model file, check for duplicate property declarations — Edit tool can accidentally double-add properties if the old_string doesn't match exactly
- PermitHolderId must be `.ToLower()` for case-insensitive match between Kendo dropdown value and grid filter
- `byMonth` trend intentionally ignores the date filter — always last 12 months for trend continuity
