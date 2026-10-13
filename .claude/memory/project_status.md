---
name: project-status
description: "Where the work stands (2026-10-13): everything merged and deployed through #24; open items and how to resume on another machine"
metadata:
  type: project
---

Snapshot as of **2026-10-11**, written when the user moved to a different computer. `git log` is authoritative for committed work; this note only records what git doesn't: what's live, what's open, and what was agreed.

**Why:** The user works across machines; Claude's per-machine memory (`~/.claude/projects/...`) does not travel, so this repo copy is the hand-off.

**How to apply:** On a new machine, `git pull` on `master`, check `git status` is clean, then read [[feedback-workflow]] and [[testing-approach]] before starting. If anything here contradicts the code, trust the code and update this note.

## State

- `master` = everything; all work is merged **and deployed** (the user deploys manually) and was tested by the user. No feature branches open, no uncommitted work.
- Recent PRs (all merged and live):
  - #12 Telerik/Kendo removed (DataTables 2, Tom Select, Flatpickr, Treeselect, native controls)
  - #13 Old-system permit PDF: Puppeteer → QuestPDF (`LegacyPermitPdfService`), data-bug fixes; both PDFs use embedded Roboto and the company's uploaded logo; new-system PDF dates fixed
  - #14 Cleanup (unused template service, IronPdf key, unused libs) + Recycle Bin permanent delete blocks records still in use instead of throwing
  - #15 CSP: `'unsafe-eval'` only on `Sitemap/Index` (`[AllowUnsafeEval]`)
  - #16 Monday-first date pickers, full-month Reports date range, multi-level Group by on Reports
  - #17 Mobile-responsive layout across the app (conventions in CLAUDE.md → "Mobile")
  - #19 (2026-10-12) Dark mode: Light / Dark / System switch, per browser, all three layouts (conventions in CLAUDE.md → "Dark mode"). Merged, tested and deployed (2026-10-12).
  - #23 (2026-10-13) Admin Logs: the details modal works (it threw on every entry because `UpdatedWhen` is always null), pager links fixed (`Url.Action` produced `/00000000-…/Admin/Logs`), tenant-style pager with server-side paging (~17k log rows), Malaysia time in the grid.
  - #24 (2026-10-13) Cutover date: "date" data type in the three settings modals (Flatpickr, saves `yyyy-MM-dd`); Admin → Templates menu for Super Users once a cutover date is set (even a future one); "Permit form not set up" dialog on New permit when the new form is active but there's no permit template.
- GitHub issues #2–#7 (security review items) were deleted on 2026-10-12 at the user's request ("addressed outside this project"). The code they described was unchanged at the time: dashboard chart `[ResponseCache]` before auth, ~59 POST actions without antiforgery, auth cookie not `SecurePolicy.Always`, 301 redirect for email links in `PermitsController.Edit`.

## Open / possible next work

1. **Tablet & small-laptop pass (576–1279px).** Phone rules apply below 576px only. In this range some wide tables still scroll sideways inside their box, e.g. the Permits list below 1366px (it fits from 1366px up with headers on one line; the user rejected wrapped headers on desktop).
2. **New-system certificate pages** (`/{company}/permits/{id}/certificates/*`) were never checked on a phone: the dev DB has no new-system permits or form templates/controls.
3. Old-system PDF: the main form doesn't print the permit's start/end dates (same as the former HTML export; the user hasn't asked to add them).
4. IronPdf licence keys were committed in the past (removed from the tree in #14, still in git history); the user was told to treat them as exposed if still valid.
5. **Dark mode, never seen with data:** new-system permit/certificate pages, the form builder with real controls, notifications with items, the reset-password page.
6. `Permits/GetGridData` reads and parses every permit's `PermitForm` JSON (~10 MB for IOTM) because 996 old permits have no `StartDateTime`/`EndDateTime` columns filled and certificate names live only in the JSON. Slow in dev on a cold SQL cache; fast in production, and the user is fine with that. Fix if it ever matters: backfill the date columns, add a certificate-codes column, drop `PermitForm` from the grid query. The `try/catch` around the JSON parse was removed in 9f5a60a, so one malformed `PermitForm` would break the whole grid.
7. **Admin layout on phones:** the admin navbar's links don't wrap, so admin pages scroll sideways below ~540px (Logs 492px, Companies 541px wide at a 390px viewport). Never part of the mobile pass.
8. **Dev DB state (2026-10-13):** the global `new_permit_form_cutover_date` is `2026-10-13` (type `date`), so dev uses the new permit form; IOTM had no permit template at the time, so New permit shows the "Permit form not set up" dialog until one is built.

## Future enhancements (KIV)

- **Super Admin on its own host, `https://admin.permitpro.app`** (assessed 2026-10-12, parked by the user). Admin today: 3 controllers (`Admin`, `AdminFormControls`, `AdminFormTemplates`, ~1,350 lines, 31 `/admin/*` routes) and ~3,700 lines of views; same Identity user store and cookie as tenants, SuperUser role checked per action; the builder and form-controls/templates views are shared with 4 tenant actions.
  - **Option A (recommended, ~1 day):** same app, host-based routing: `/admin/*` only on the admin host (404 elsewhere), tenant routes on the admin host redirect to `/admin/login`, update the "Super Admin login" link, dev testing via `admin.localhost`. The auth cookie is host-only (no `Domain`), so admin and tenant sessions separate automatically. Pair with Cloudflare Access on the admin hostname.
  - **Option B (~4-7 days, medium risk):** separate project + IIS site referencing Core; shared views/startup/assets need a Razor class library or copies. New problems: company logo uploads go to the admin site's own `wwwroot/img/logos` while the tenant app (sidebar, both PDFs via `PdfLogo.Load`) reads its own; app settings (30 min) and company meta (15 min) caches can't be cleared across processes. Two deployments to keep in sync.
  - **Infra for either:** proxied `admin` DNS record; exception in the Cloudflare redirect rule (it currently sends every non-apex host to `permitpro.app`); confirm the Origin Certificate covers the name; IIS binding. `AllowedHosts` already allows `*.permitpro.app`.

