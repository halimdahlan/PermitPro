---
name: testing-approach
description: "How changes are verified: scratch-copy builds, the app run locally against the dev DB, headless Chrome/Firefox driving real pages, PDF harnesses"
metadata:
  type: reference
---

There are no automated tests. Changes were verified like this (the scripts lived in Claude's per-session scratchpad, so recreate them as needed).

**Why:** Visual Studio may hold the repo's `obj/` folders, and the dev DB is shared, so builds and test runs happen outside the repo and read-only.

**How to apply:**

- **Build** a scratch copy: copy `git ls-files -co --exclude-standard` (skip `Html/`, `SqlScripts/`, `docs/`) into a temp folder, plus `Web/appsettings.Development.json` (git-ignored), then `dotnet build` there. After edits, re-copy the changed files and rebuild; stop the running app first (it locks the DLL). In Razor `<style>` blocks write `@@media`.
- **Run** it: `ASPNETCORE_ENVIRONMENT=Development dotnet bin/Debug/net10.0/PermitPro.App.dll --urls http://localhost:5312 --contentRoot <copy>/Web`. Use `localhost`, never `127.0.0.1` (AllowedHosts).
- **Log in** with the dev **test account**. The user provides the credentials; they are deliberately not stored in the repo, so ask the user on a new machine. Login form: `#CompanyId` (pick "IOTM"), `#Email`, `#passwordInput`, submit `#loginForm`. Browsing is fine; don't save or submit anything.
- **Drive pages** with headless Chrome over CDP from Node (`--remote-debugging-port`, `Runtime.evaluate`, `Page.captureScreenshot`). Phone checks: `Emulation.setDeviceMetricsOverride` 390×844 (and 360) with `mobile: true`; measure `document.documentElement.scrollWidth` and list elements whose right edge passes the viewport. Disable the cache (`Network.setCacheDisabled`), since reused profiles serve stale CSS. Code run through `Runtime.evaluate` is exempt from CSP, so test CSP from the page's own inline script. Firefox: `firefox --headless --remote-debugging-port` plus WebDriver BiDi (`session.new`, `script.evaluate`).
- **Find ids read-only** from a grid's data in the browser (`$('#tblPermits').DataTable().rows().data()`), e.g. a Draft permit to open in Edit.
- **PDFs**: a console harness renders old-system permits without the web app (see `docs/pdf-export-troubleshooting.md`); batch-rendering every permit caught layout overflows. The new-system PDF was tested with an EF in-memory DB seeded with one control of each type.
- **No sample data** in the dev DB for form controls/templates or new-system permits: inject sample rows into the page DOM (browser only) to check layouts.
