---
name: feedback-workflow
description: "How the user wants changes made, tested, committed and shipped in PermitPro"
metadata:
  type: feedback
---

Working agreement that held for every change in 2026-09/10.

**Why:** The user tests every change themselves before it is committed, ships manually, and wants control over what reaches GitHub and production.

**How to apply:**

- **Branch per piece of work** off `master` (`feature/...`, `fix/...`, `chore/...`, `docs/...`). Never commit to `master` directly.
- **Commit only when the user says so.** They test first ("tested, all good" → commit). Then push and open a PR with `gh pr create`; the user merges. After "merge is done": `git checkout master && git pull --ff-only`, delete the branch locally and on origin.
- **Merging does not deploy.** Deployment is manual (self-contained win-x64 publish, see CLAUDE.md). Never say a merge puts something live.
- **Hand-off notes PRs only after a deploy** (user, 2026-10-13). After "merge is done", just update `master` and delete the branch; when the user says it's deployed, update [[project-status]] with everything merged since, as merged and live, in one `docs/...` PR.
- **Work section by section** on big jobs (e.g. the mobile pass): finish one section, report, wait for "tested, proceed" before the next.
- **Ask before** behaviour/policy decisions (e.g. what a permanent delete should do with references), offering a recommended option. Small, obvious fixes found along the way: fix and mention them.
- **Old-system PDF layout/field mapping**: change only with explicit approval.
- **Never** write test data (permits, users, settings…) to the dev DB without asking. Logging in and browsing with the dev test account is fine (see [[testing-approach]]).
- **Never** set `AllowedHosts` to `*`; don't commit licence keys or credentials.
- **Stay on Bootstrap 5.3.8**: no Bootstrap 6 until it is stable.
- **Don't build the real `Web`/`Core` projects** while Visual Studio may have them open (it locks `obj/`); build a scratch copy instead (see [[testing-approach]]).
- Reports to the user: short and plain — what changed, how it was tested, what's left.
