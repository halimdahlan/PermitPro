---
name: project-status
description: "Where the work stands (2026-10-24): v1.4.0 released and deployed (master through #52); releases are tagged; company subdomains in progress on feature/subdomain-tenancy; open items and how to resume on another machine"
metadata:
  type: project
---

First written **2026-10-11**, when the user moved to a different computer; updated after each deploy (latest 2026-10-19). `git log` is authoritative for committed work; this note only records what git doesn't: what's live, what's open, and what was agreed.

**Why:** The user works across machines; Claude's per-machine memory (`~/.claude/projects/...`) does not travel, so this repo copy is the hand-off.

**How to apply:** On a new machine, `git pull` on `master`, check `git status` is clean, then read [[feedback-workflow]] and [[testing-approach]] before starting. If anything here contradicts the code, trust the code and update this note.

## State

- `master` is merged **and deployed** through #52 as release **v1.4.0** (the user deploys manually from the tag; confirmed 2026-10-24). Releases are tagged since 2026-10-17: `v1.2.0` (everything through #36), `v1.2.1` (through #39), `v1.2.2` (through #43), `v1.3.0` (through #48) and `v1.4.0`; process in CLAUDE.md → "Releases". One feature branch is open: `feature/subdomain-tenancy` (company subdomains, not merged; its own copy of this note has the details and next steps).
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
  - #26 (2026-10-14) Permit list description length is the app setting `general / permit_list_description_words` (default 20 when missing); `SqlScripts/AddPermitListDescriptionWordsSetting.sql` adds the global row.
  - #27 (2026-10-14) Hangfire removed (never configured); no job framework.
  - #28 (2026-10-14) Permit rules (Admin → Rules, CLAUDE.md → "Permit Rules"): End-date reminder, Overdue, Approval escalation, Auto-archive (+ Unarchive, Rules → History). Hosted service every 5 min; IIS app pool set to AlwaysRunning / idle time-out 0 on production. Emails off (`PermitRules:SendEmails = false`); rules switched off on production until the user turns them on. Migration `AddPermitRules` also copied old permits' start/end dates from the JSON into the columns.
  - #31 (2026-10-16) Recycle Bin bulk actions: row checkboxes + select all, Restore selected / Delete selected / Delete all per tab; a record still referenced stays in the bin and is listed with the reason; actions only touch soft-deleted records in the user's scope.
  - #32 (2026-10-17) Recycle Bin restore/delete run in the background and redraw the page in place, staying on the current tab (every tab is redrawn: a restore can bring back records on other tabs).
  - #33 (2026-10-17) Locations table: "Num. of Permits" column (own count, plus the total incl. sub-locations or "no sub-location" on a second line). Drafts and archived permits counted, deleted ones not.
  - #35 (2026-10-17) NuGet vulnerability warnings (NU1902/NU1903) cleared: Microsoft packages 10.0.12 → 10.0.13, and a direct `System.Security.Cryptography.Pkcs` 10.0.13 reference in Core (MimeKit only asks for 10.0.0). Publish needs an SDK with the 10.0.13 runtime.
  - #36, #37, #39 (2026-10-17) Versioning: `<Version>` 1.2.0 then 1.2.1; CLAUDE.md "Releases" section (semver, release PR → annotated tag → GitHub Release, hotfix branch from a tag). Released as v1.2.0 and v1.2.1.
  - #38 (2026-10-17) Admin → About shows the connection string again (it read `GetConnectionString(EnvironmentName)` instead of `"MSSQL"`, so it was always empty); password masked with `SqlConnectionStringBuilder`, which also catches `Pwd=`. In v1.2.1.
  - #42, #43 (2026-10-19) Login pages (tenant and Super Admin) show the running version via `AppVersion.Current` (`Web/Services/AppVersion.cs`, from the assembly) instead of a hard-coded `v1.2`; Workflow edit returns 404 for an unknown workflow (was a 500 on a null model), which cleared the only build warning (CS8602). Released as v1.2.2.
  - #45, #47 (2026-10-21) Login pages (tenant and Super Admin) show the app logo (larger, no PERMITPRO text) instead of the icon; the Workflow History badge on the permit page is the same size as in the permits table; the permits table Submitted By column shows the time ("on 21 Oct, 2026 at 09:30 AM"). The login and badge fixes (#45) are tagged v1.2.3 (#46); the Submitted By time (#47) is in v1.3.0 (#48), the version deployed.
  - #51 (2026-10-24) Pending-approval email redesigned and stored in Settings (category `email_templates`, keys `pending_approval_subject`/`pending_approval_body`; company overrides global; built-in default in `Core/Resources/EmailTemplates`; rows seeded at startup by `EmailTemplateSeed`, never overwritten); new `html` data type with a CodeMirror editor and sandboxed Preview (sample data) on the three Settings pages; Scriban removed, replaced by `Core/Helpers/EmailTemplate`, and the reset-password and permit-rule emails use it too (CLAUDE.md → "Email Templates"). #52 is the v1.4.0 version bump.
- The project folder was renamed from `PermitPro-1.2` to `PermitPro` (by 2026-10-18). Claude Code keeps per-machine sessions and memory per folder path, so on another machine with the old path the history stays under the old name; the user's old sessions and per-machine memory were copied across on this machine.
- GitHub issues #2–#7 (security review items) were deleted on 2026-10-12 at the user's request ("addressed outside this project"). The code they described was unchanged at the time: dashboard chart `[ResponseCache]` before auth, ~59 POST actions without antiforgery, auth cookie not `SecurePolicy.Always`, 301 redirect for email links in `PermitsController.Edit`.

## Open / possible next work

1. **Tablet & small-laptop pass (576–1279px).** Phone rules apply below 576px only. In this range some wide tables still scroll sideways inside their box, e.g. the Permits list below 1366px (it fits from 1366px up with headers on one line; the user rejected wrapped headers on desktop).
2. **New-system certificate pages** (`/{company}/permits/{id}/certificates/*`) were never checked on a phone: the dev DB has no new-system permits or form templates/controls.
3. Old-system PDF: the main form doesn't print the permit's start/end dates (same as the former HTML export; the user hasn't asked to add them).
4. IronPdf licence keys were committed in the past (removed from the tree in #14, still in git history); the user was told to treat them as exposed if still valid.
5. **Dark mode, never seen with data:** new-system permit/certificate pages, the form builder with real controls, notifications with items, the reset-password page.
6. `Permits/GetGridData` still reads and parses every permit's `PermitForm` JSON (~10 MB for IOTM). Since #28 the old permits' `StartDateTime`/`EndDateTime` columns are filled (only 1 permit without dates left), so the grid could take dates from the columns; certificate names still live only in the JSON (a certificate-codes column would let the grid drop `PermitForm`). Slow in dev on a cold SQL cache, fast in production; the user is fine with that. The `try/catch` around the JSON parse was removed in 9f5a60a, so one malformed `PermitForm` would break the whole grid.
7. **Admin layout on phones:** the admin navbar's links don't wrap, so admin pages scroll sideways below ~540px (Logs 492px, Companies 541px wide at a 390px viewport). Never part of the mobile pass.
8. **Dev DB state (2026-10-14):** migrations applied through `AddPermitRules`. Global `new_permit_form_cutover_date` = `2026-10-13`, but IOTM has its own empty override row (not using global), so IOTM is back on the old permit form and the Templates menu is hidden for it. 892 IOTM permits are archived (the user ran Auto-archive at 90 days on 2026-10-13); all four rules are off.
9. **Production:** IIS app pool AlwaysRunning, idle time-out 0, and the site's **Preload Enabled = True** are all set (confirmed by the user 2026-10-14). Production has no `dotnet ef`: migrate with an idempotent script generated on the dev machine (`dotnet ef migrations script --idempotent --project Core --startup-project Web --output publish-migrations.sql`, run in SSMS, or sqlcmd with `-I`).
10. `ScheduledTaskController` (`api/ScheduledTask/*`): anonymous GET endpoints; the suspended-permit auto-resume check is inverted (resumes within `suspended_autoresume_days` instead of after) and ignores the company in the URL. Candidate for a permit rule.
11. **Workflow edit isn't tenant-scoped** (found 2026-10-19, not fixed): `WorkflowController.Edit` looks the workflow up by id only, without `CompanyID`, so a user who knows another company's workflow id can probably open it from their own company's URL. Check the other workflow actions (steps, save) too; fix on its own `fix/…` branch when the user asks.

## Future enhancements (KIV)

- **HTML certificates** (planned 2026-10-18, parked by the user): certificates defined by an uploaded HTML file (no JS in it), app renders it, saves/restores its inputs, exports it with the permit PDF, accepts modified versions. Full plan and the open questions to ask before coding: `docs/html-certificates-plan.md`. Main decision still open: PDF via contained headless Chromium vs. a browser-free HTML subset → QuestPDF.

- **Super Admin on its own host, `https://admin.permitpro.app`** (assessed 2026-10-12, parked by the user). Admin today: 3 controllers (`Admin`, `AdminFormControls`, `AdminFormTemplates`, ~1,350 lines, 31 `/admin/*` routes) and ~3,700 lines of views; same Identity user store and cookie as tenants, SuperUser role checked per action; the builder and form-controls/templates views are shared with 4 tenant actions.
  - **Option A (recommended, ~1 day):** same app, host-based routing: `/admin/*` only on the admin host (404 elsewhere), tenant routes on the admin host redirect to `/admin/login`, update the "Super Admin login" link, dev testing via `admin.localhost`. The auth cookie is host-only (no `Domain`), so admin and tenant sessions separate automatically. Pair with Cloudflare Access on the admin hostname.
  - **Option B (~4-7 days, medium risk):** separate project + IIS site referencing Core; shared views/startup/assets need a Razor class library or copies. New problems: company logo uploads go to the admin site's own `wwwroot/img/logos` while the tenant app (sidebar, both PDFs via `PdfLogo.Load`) reads its own; app settings (30 min) and company meta (15 min) caches can't be cleared across processes. Two deployments to keep in sync.
  - **Infra for either:** proxied `admin` DNS record; exception in the Cloudflare redirect rule (it currently sends every non-apex host to `permitpro.app`); confirm the Origin Certificate covers the name; IIS binding. `AllowedHosts` already allows `*.permitpro.app`.

