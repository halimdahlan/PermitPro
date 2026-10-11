---
name: session-reports
description: "Reports section: KPI cards, charts, aggregation tabs, holder filter, overdue paging, GetReportGrid N+1 fix"
metadata: 
  node_type: memory
  type: project
  originSessionId: 685637db-3874-471d-892e-b3efb915554b
---

The Reports section (`/reports/index`) was significantly enhanced with a stats dashboard.

**Why:** The original page was a filter form + Kendo Grid with no visual summary. Added KPI cards, 3 charts, 4 tabs (All / Overdue / By Holder / By Location), and additional grid columns.

**Update 2026-10:** Kendo is gone. The All Permits grid is now DataTables via `DataGrid.create` (client-side filtering over `GetReportGrid`, Buttons for Excel, RowGroup with multi-level "Group by"); dropdowns are Tom Select; the date range is Flatpickr. Mentions of "Kendo Grid/DropDownList" below are historical.

**How to apply:** All chart/stats data comes from a single `GetChartData` AJAX call. The Kendo grid uses a separate `GetReportGrid` endpoint. The two calls run independently when "View Report" is clicked.

## Files Modified

### `Web/Models/Reports/ReportGridModel.cs`
Added `PermitHolderId`, `StartDate`, `EndDate`, `LocationId` and computed `DurationDays`:
```csharp
public string? PermitHolderId { get; set; }
public DateTime? StartDate { get; set; }
public DateTime? EndDate { get; set; }
public int? DurationDays => (StartDate.HasValue && EndDate.HasValue)
    ? (int?)Math.Round((EndDate.Value - StartDate.Value).TotalDays)
    : null;
public string? LocationId { get; set; }
```

### `Web/Controllers/ReportsController.cs`

**New endpoint: `GetDropdownPermitHolders(Guid company)`**
Returns `[{text, value}]` list of permit holders (users who have submitted permits). Used to populate the Permit Holder dropdown.

**`GetReportGrid` changes**
- `PermitHolderId` normalized to `.ToString().ToLower()` for case-insensitive matching with dropdown filter

**New endpoint: `GetChartData(...)`**
Parameters: `company`, `useDateRange`, `month`, `year`, `startDate`, `endDate`, `locationId`, `certificateType`, `permitStatus`, `holderId`

Loads all permits with `.ToList()` (in-memory), then parses `PermitForm` JSON for StartDate/EndDate/Location/Certificates. Resolves holder names from `Users` table via Dictionary lookup.

Returns:
```json
{
  "summary": { "total", "pending", "approved", "closed", "overdue" },
  "byMonth": [{ "label", "approved", "pending", "other" }],   // last 12 months always
  "byLocation": [{ "label", "count", "approved", "pending", "other" }],  // top 8
  "byHolder": [{ "name", "total", "approved", "pending", "other" }],
  "overduePermits": [{ "id", "permitNo", "holderName", "location", "endDate", "daysOverdue", "status" }]
}
```

**Important notes:**
- `byMonth` (trend chart) always shows last 12 months regardless of date filter — intentional, for trend continuity
- `byLocation` is top 8 by count — used for both the chart and the By Location table
- `overduePermits` = permits where `EndDate < now` AND status is NOT in terminal statuses

**Private class `ChartPermitData`:**
```csharp
private class ChartPermitData {
    public Guid Id;
    public string PermitNo;
    public string HolderUserId;
    public string HolderName;
    public PermitStatusEnum Status;
    public DateTime CreatedWhen;
    public string LocationId;
    public string LocationName;
    public string Certificates;
    public DateTime? StartDate;
    public DateTime? EndDate;
}
```

### `Web/Views/Reports/Index.cshtml`

**Filter additions:**
- Permit Holder dropdown (Kendo DropDownList, `ddlPermitHolder`) between Location and Certificate Type
- Width: 380px, reads from `GetDropdownPermitHolders`

**Inline loading indicator:**
```html
<div id="reportLoading" class="text-center py-5" style="display:none">
    <i class="fa-solid fa-loader fa-spin text-secondary" style="font-size:2rem"></i>
    <p class="text-secondary mt-2 mb-0 small">Loading statistics…</p>
</div>
```
Shown between `<hr/>` and KPI section. See [[feedback-loading-indicator]].

**KPI Cards section (`#kpiSection`, hidden until View Report clicked):**
5 cards: Total (text-primary), Pending (#f6c343), Approved (text-success), Closed (text-secondary), Overdue (text-danger). Overdue card has `id="kpiOverdueCard"` — gets `.kpi-card-danger` class when count > 0.

**Charts section (`#chartsSection`, hidden until View Report clicked):**
- `statusChart` (col-md-3) — Doughnut: Pending / Approved / Closed / Other
- `monthlyChart` (col-md-6) — Stacked Bar: Approved / Pending / Other per month
- `locationChart` (col-md-3) — Horizontal Bar: top 8 locations

Chart.js CDN: `https://cdn.jsdelivr.net/npm/chart.js@4.4.4/dist/chart.umd.min.js`
Font: `{ family: "'Poppins', sans-serif", size: 11 }`

**Report Tabs (Bootstrap 5, below charts):**
- All Permits (wraps existing Kendo Grid)
- Overdue (badge `#badgeOverdue` shows count, red)
- By Holder (`#holderContainer`)
- By Location (`#locationContainer`)

**Kendo Grid new columns (added after Status):**
```csharp
c.Bound(p => p.StartDate).Format("{0:dd MMM, yyyy}").Width(120);
c.Bound(p => p.EndDate).Format("{0:dd MMM, yyyy}").Width(120);
c.Bound(p => p.DurationDays)
    .ClientTemplate("# if (DurationDays !== null ...) { # #= DurationDays # d # } else { # — # } #")
    .Width(100);
```

**JavaScript logic:**
- `loadChartData(params)` — GET `/${companyId}/reports/getchartdata`, populates all KPI/chart/table sections
- "View Report" click: shows `#reportLoading`, hides KPI/charts, calls both `GetReportGrid` (via Kendo grid read) and `loadChartData`
- "Clear filter" click: resets all dropdowns incl. `ddlPermitHolder`, hides stats sections, resets `_overduePermits = []`
- `renderOverduePage(page)` — replaces old `renderOverdueTable`; renders a paginated slice of `_overduePermits` with Bootstrap 5 `pagination-sm` and "Showing X–Y of Z records" label
- `renderHolderTable(holders)` — HTML table with approval rate progress bar
- `renderLocationTable(locations)` — same structure as holderTable

**Overdue table paging (added 2026-06-25):**
- `var _overduePermits = []` — module-level store for all overdue permits returned by `GetChartData`
- `const OVERDUE_PAGE_SIZE = 20` — default page size
- On `loadChartData` success: `_overduePermits = data.overduePermits || []; renderOverduePage(1);`
- Pagination clicks use `$(document).on('click', '.overdue-page', ...)` — event delegation since `#overdueContainer` content is replaced per render
- Pagination bar only rendered when `totalPages > 1`

### `Web/Controllers/ReportsController.cs` — `GetReportGrid` N+1 fix (2026-06-25)

**Root cause:** The original `Select` projection included a correlated `_dbContext.Users` subquery per permit row — N+1 queries.

**Fix pattern** (same as `GetChartData`):
1. Query permits without user names (`.ToList()`)
2. Collect distinct `PermitHolderId` strings
3. One batch `Users WHERE Id IN (...)` query → `Dictionary<string, string>`
4. `holderNames.TryGetValue(permit.PermitHolderId, out var name)` per row

**Other improvements:**
- `.AsEnumerable()` → `.ToList()` for explicit materialization
- `new List<ReportGridModel>(rawPermits.Count)` — pre-allocated capacity
- 8-branch `if` cert chain → `Dictionary<string, string> certMap` built once
- Null-safe `json["general"]?["startDateTime"] as JValue`

### `Web/wwwroot/css/reports.css`
Added CSS for:
- `.kpi-card`, `.kpi-card:hover`, `.kpi-value`, `.kpi-label`, `.kpi-card-danger`
- `.chart-section-title`, `.chart-wrap` (height: 230px)
- `.report-table`, `.report-table thead th`, `.report-table tbody tr:hover td`, `.report-table .progress`
