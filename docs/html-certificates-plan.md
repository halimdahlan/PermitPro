# HTML certificates — plan (parked)

**Status:** planning only, nothing built. Drafted 2026-10-18; parked until the user picks it up again. Open questions at the end need answers before any code is written.

## Requirements (from the user)

- A permit certificate is displayed from an **HTML file**, uploaded when a certificate is attached to a permit; the app renders it in the interface.
- Every input in that HTML is saved by the app, and saved data can be restored/retrieved.
- The certificate is exported as PDF together with the permit.
- The app must accept **modifications** to the HTML certificate.
- The HTML contains **no JavaScript** (unlike the current certificates); the app supplies all client scripting.

## Current state (2026-10-18)

- **New-system certificates** (`CertificatesController`, `Views/Certificates/Form.cshtml`) render from a form-builder `FormTemplate`; values saved to `Certificate.CertificateDataJson` as `{ "fields": { colUuid: value } }`; `QuestPdfService` appends one page per certificate to the permit PDF.
- **Old-system certificates A–H** (`Views/Permits/Forms/*`) are hand-written Razor markup driven by page JS (`form-controls.js`).
- `Certificate` has no `CompanyId`; tenancy only comes through its link to the permit.
- **Existing gaps to fix whatever happens:** `CertificatesController` loads certificates by id without checking they belong to the permit/company in the route (any logged-in user with an id could read/save another tenant's certificate); `SaveData` is `[IgnoreAntiforgeryToken]`.

## Approach

Add a third way to define a certificate: an **HTML certificate template**. The HTML is both the layout and the field definition. The app parses it once at upload (sanitize, extract fields) and from then on handles scripting, saving, restoring and PDF.

### 1. Authoring contract (rules for the HTML)

Plain standard HTML only:

| In the HTML | App behaviour |
|---|---|
| `<input type="text" name="emergency_contact">` | Text field |
| `<textarea name="…">` | Multi-line text |
| `<input type="checkbox" name="…">` | Yes/no, saved as `true`/`false` |
| Several checkboxes with the **same** `name`, different `value`s | Multi-choice, saved as an array |
| `<input type="radio" name="…">` | Single choice |
| `<select name="…">` | Dropdown |
| `type="date"` / `datetime-local` | Replaced by Flatpickr (no native date inputs, see CLAUDE.md); saved as UTC ISO string |
| `required`, `maxlength`, `placeholder` | Enforced by the app's validation |

**`name` is the storage key**, which is what lets the HTML change later without losing data.

Optional extensions (only if real certificates need them):
- `data-pp-bind="permit.number"` (location, start/end, holder…) — read-only values filled from the permit.
- `data-pp-show-if="gas_test=yes"` — declarative show/hide without JS.

### 2. Upload pipeline (server)

1. Parse with AngleSharp.
2. **Sanitize** with HtmlSanitizer (allowlist): strip `<script>`, `on*=` handlers, `javascript:` URLs, `<iframe>`, `<object>`, `<base>`, `<meta http-equiv>`, form `action`; no external resources (images embedded as data URIs or uploaded to the app) so the PDF is predictable and CSP stays intact.
3. **Extract the field list** (name, type, options, required) to JSON.
4. **Validate and report** before publishing: no fields found, inputs without `name`, one name used with conflicting types, scripts stripped, unsupported elements.
5. **Preview** page: rendered certificate + detected fields; uploader confirms.

### 3. Data model and versioning

- `HtmlTemplate` — CompanyId, Name, Description, CurrentVersionId.
- `HtmlTemplateVersion` — VersionNo, OriginalHtml, SanitizedHtml, FieldSchemaJson, UploadedBy/When, Notes. Stored in the **database**, not `wwwroot` (tenant isolation, included in backups).
- `Certificate.HtmlTemplateVersionId`; reuse `CertificateDataJson` as `{ "fields": { name: value } }`.

Uploading a modified HTML **creates a new version**. Existing certificates stay on the version they were filled in on (their PDF never changes retroactively); new certificates use the latest. Optional "upgrade to latest": fields carry over by `name`; values for fields no longer present are kept in the JSON and listed as "no longer in the template", never dropped.

### 4. Rendering and filling in

- **Server-side hydration:** saved values are written into the sanitized HTML (`value`, `checked`, `selected`, textarea content). Restore = render again; the PDF uses the same output.
- Shown in a **sandboxed iframe** (`srcdoc`, `sandbox="allow-same-origin"`, no `allow-scripts`): the certificate's CSS can't break the app layout, Bootstrap can't restyle the certificate, and nothing inside can execute even if the sanitizer missed something.
- App script in the parent page wires Flatpickr, required-field validation, unsaved-changes warning, `show-if`, and value collection for save.
- **Spike needed:** confirm Flatpickr popups and parent-attached event listeners work inside a script-less sandboxed iframe. Fallback: Shadow DOM + server-side sanitizing.

### 5. Saving

- Endpoint **validates posted values against the version's field schema**: unknown keys dropped, types checked, length limits.
- Fix the ownership check and antiforgery gaps noted above at the same time.

### 6. PDF — the main decision

QuestPDF can't draw HTML. Options:

| Option | For | Against |
|---|---|---|
| **A. Headless Chromium** (PuppeteerSharp/Playwright) renders the hydrated HTML; pages merged into the QuestPDF permit (QuestPDF `DocumentOperation` merge — to verify) | PDF matches the screen; any HTML/CSS works — what "accept any modification" really needs | Brings back the browser removed in #13: ~150 MB+ Chromium, a process on the server, IIS identity setup. The earlier failures came from network fetches; here the HTML is self-contained and all requests would be blocked |
| **B. Own HTML → QuestPDF converter** for a restricted subset (tables, headings, paragraphs, lists, bold/italic, embedded images, inputs printed as values/ticks, a few CSS properties) | No browser, fits the current stack | Lower fidelity; every future modification must stay within the subset (upload validator warns) |
| **C. Commercial .NET HTML→PDF** (e.g. iText pdfHTML) | No browser | AGPL or paid licence; still partial CSS |

Lean: **A, contained** (no network, pinned Chromium, only for HTML certificates). B only if the real certificates are simple table-style forms. Decide after the spike with real files.

### 7. Other points

- **Dark mode:** keep the certificate itself light, like a paper form; only the app around it follows the theme.
- **Mobile:** fixed-width certificate tables will scroll sideways in the iframe; authoring guidance or a fit-to-width zoom.
- **Locking:** read-only once the permit is approved/closed? (to decide)
- **Audit:** log template uploads/versions and certificate saves.

## Phases

0. **Spike** with the user's sample HTML: iframe + Flatpickr behaviour; render one certificate to PDF with options A and B and compare.
1. Entities + migration, upload/sanitize/validate/preview, template library UI, versioning.
2. Fill in / save / restore on the certificate page, plus the ownership and antiforgery fixes.
3. PDF export merged into the permit PDF.
4. Polish: read-only/locked states, mobile, docs (CLAUDE.md + an authoring guide for whoever writes the HTML).

## Open questions for the user

1. **Sample files:** one or two real certificate HTML files, including the current one with JS in it (none are in the repo). They decide the PDF option and whether `show-if` is needed.
2. **Where uploads happen:** each time a certificate is attached to a permit (as written in the requirements), or a company **template library** picked from when attaching? Recommended: library — per-permit uploads mean every permit carries its own copy and a template change can't reach them.
3. **PDF:** OK to bring Chromium back, limited to HTML certificates? Or must it stay browser-free (option B with its limits)?
4. **Scope:** replace form-builder certificates, sit alongside them, or eventually replace the old A–H certificates too?
5. **Who uploads** (Super User per company, Super Admin for all companies?), and should certificates lock after approval?
6. **Special fields:** signatures, auto-filled permit data, calculated values?
