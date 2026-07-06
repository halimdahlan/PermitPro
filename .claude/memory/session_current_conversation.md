---
name: session-current-conversation
description: "2026-07-06: Set Password on Users/Edit fully implemented (route+antiforgery fix, then actual password-update logic) — UNCOMMITTED, build passing, not live-tested"
metadata:
  type: project
  originSessionId: ae0f4948-1987-41ca-8af0-8bc3647a93ae
---

The 2026-07-04 Super Admin Logs viewer work is committed as `7886a65`. Also committed since the last refresh (newest first): `4efaa82` audit log timestamp fix, `3314023` role management edit limits + user-creation-limit alert, `5ac48e7` Super Admin login link, `2126cd1` README docs. None of that needs tracking here — `git log`/`git show` is authoritative for committed work.

**Why:** Keep this memory pointed at only the *current* uncommitted work so it doesn't go stale the moment something lands.

**How to apply:** On a new machine, `git status --porcelain` should show exactly the 4 paths below if the working tree is synced. If not synced, the diff/code detail here is enough to recreate the feature. This file was observed reverting to a stale version once mid-session (likely an external sync clobbering a local edit) — if it looks stale, re-derive from `git diff`/`git status` rather than trusting it blindly.

## Current uncommitted work: Set Password on Users/Edit — now fully functional

Started as a broken form (wrong POST route + missing antiforgery token, so it 404'd/threw). Fixed that, then discovered the `SetPassword` controller action was a stub that validated the form and redirected without ever touching the database — implemented the real password-update logic.

### Files changed (uncommitted as of 2026-07-06)

- **`Web/Controllers/UsersController.cs`**
  - `SetPassword` route corrected: `{company}/users/setpassword/{id}/setpassword` → `{company}/users/edit/{id}/setpassword` (form's actual post target).
  - Extracted GET `Edit`'s user-loading query + role-dropdown setup into a new private `BuildEditUserFormAsync(Guid company, Guid id, string org)` helper (returns `ManageUserViewNodel`, null if user not found). GET `Edit` now just calls it.
  - `SetPassword` is now `async Task<IActionResult>`: on invalid `ModelState`, calls `BuildEditUserFormAsync` to populate `model.UserInfoForm` before `return View("Edit", model)` — this was the critical fix, since the old stub's `return View(model)` would `NullReferenceException` on the Edit view (which reads `Model.UserInfoForm.*` everywhere) because the Set Password form only posts `UserPasswordForm.*` fields.
  - On success: hashes `model.UserPasswordForm.NewPassword` via `_userManager.PasswordHasher.HashPassword`, sets it on the fetched `userInfo.PasswordHash`, stamps `UpdatedWhen`/`UpdatedBy`, `_userManager.UpdateAsync(userInfo)`, sets `TempData["SuccessMessage"]`, then `RedirectToAction("Edit", new { company, id, org })` — `org` derived from `model.UserPasswordForm.IsContractors` (`"c"` or `null`) so contractor users don't lose that URL context on redirect.
  - This bypasses ASP.NET Identity's built-in password-complexity validators (same pattern already used elsewhere in this file, e.g. `CreateUser` ~line 197 and another admin reset ~line 915 — not a new deviation, just consistent with existing style).
- **`Web/Views/Users/Edit.cshtml`**
  - Set Password `<form>` action corrected to the new route; added `@Html.AntiForgeryToken()` (action has `[ValidateAntiForgeryToken]`, token was missing → would 400).
  - Added a `TempData["SuccessMessage"]` alert block right after `@model` (same markup as `Users/Index.cshtml`'s existing pattern) — needed because `Edit.cshtml` had zero TempData rendering before, and the redirect-back-to-Edit-after-password-change needs somewhere to surface the message. `_Layout.cshtml` (the layout this page uses, as opposed to `_LayoutAdmin.cshtml`) has no global TempData handling — every page that shows these messages does it locally in the view.
- **`Web/Views/Shared/_SideMenuPartial.cshtml`** — `navSettings = "active-light";` (for `currentRoute` in `sites`/`users`/`workflow`) is commented out. **Unrelated to the Set Password work and still unexplained** — user hasn't stated intent; carried over from before this task started. Flag it before committing alongside the password fix, since it'll ride along in the same diff otherwise.

### Verification done
`dotnet build --no-restore -p:UseSharedCompilation=false` — succeeded, 0 warnings, 0 errors.

### Verification NOT done
No live browser test. `appsettings.Development.json` points at a real remote SQL Server (site4now.net) — no local/isolated DB available, and no dev login credentials were provided this session, so the full login → edit user → set password flow was never driven end-to-end. If resuming: either get credentials from the user or ask before mutating any real dev-DB user's password.

### Status
Feature complete per code review + successful build. Not committed. Not live-verified. Next step on resume: either (a) get dev credentials and drive the flow live, or (b) ask user to self-verify, then commit — and separately resolve/confirm the `_SideMenuPartial.cshtml` nav-highlight change before it goes in the same commit.
