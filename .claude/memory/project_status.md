---
name: project-status
description: "Where the work stands (2026-10-11): everything merged and deployed, nothing uncommitted; open items and how to resume on another machine"
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

## Open / possible next work

1. **Tablet & small-laptop pass (576–1279px).** Phone rules apply below 576px only. In this range some wide tables still scroll sideways inside their box, e.g. the Permits list below 1366px (it fits from 1366px up with headers on one line; the user rejected wrapped headers on desktop).
2. **New-system certificate pages** (`/{company}/permits/{id}/certificates/*`) were never checked on a phone: the dev DB has no new-system permits or form templates/controls.
3. Old-system PDF: the main form doesn't print the permit's start/end dates (same as the former HTML export; the user hasn't asked to add them).
4. IronPdf licence keys were committed in the past (removed from the tree in #14, still in git history); the user was told to treat them as exposed if still valid.
