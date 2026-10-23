# PermitPro Mobile (v2.0.0) — Plan

Status: **planning**, no code yet (not scheduled). Written 2026-10-23.

## Goals

- New Flutter app for **PI and LPI** to **approve pending permits** (first release scope).
- Push notifications when a permit needs their approval.
- Login by **username/password** or **mobile number + OTP**.
- Dashboard with permit figures, an Approvals section, and a rich **permit detail view including attached certificates**.
- Android first, iOS later. More features will follow.

## Decisions

| Topic | Decision |
|---|---|
| Repo layout | Same git repo, `mobile/` folder, **not** part of the .NET solution |
| Mobile release tags | `mobile-vX.Y.Z` (own GitHub Releases); web keeps `vX.Y.Z` |
| OTP channels | **SMS and WhatsApp** both supported; the client pays the messaging cost |
| Approval rules | **Same as web**: the workflow step's approvers, with the same role/site checks. Mobile must not add a shortcut |
| Platforms | Android first, iOS after |

## Project structure

```
PermitPro/
├─ Core/                    unchanged (+ new entities/services below)
├─ Web/                     unchanged (+ Web/Api/V1 controllers)
├─ mobile/                  Flutter app, own pubspec + CI
│   └─ lib/
│       ├─ core/            theme, router, dio client, secure storage, push
│       ├─ features/        auth, dashboard, approvals, permit_detail, notifications
│       └─ shared/          widgets, models
└─ docs/api/openapi.json    generated API contract
```

Why a folder in the repo, not a solution project: one PR can change an endpoint and its client, while the Flutter and .NET toolchains stay separate.

### Versioning

- Mobile: `mobile-v1.0.0` for the first store release, SemVer after. Pubspec `version: x.y.z+build`, where the build number is the Play Store `versionCode`.
- Web: unchanged flow (`vX.Y.Z`, see CLAUDE.md → Releases). API additions are normal web releases (new feature → minor). The mobile app must say which API version it needs (`/api/v1`), and the server must keep v1 working for installed apps.
- "v2.0.0" is the programme name for this work. Whether the web tag really becomes `v2.0.0` is open: by the CLAUDE.md rules, a purely additive API is a minor bump.

## Server work (Web / Core)

1. **`Web/Api/V1/` controllers** — `Auth`, `Approvals`, `Permits`, `Dashboard`, `Devices`, `Notifications`. `[ApiController]`, JWT Bearer, DTOs (never EF entities). Company comes from token claims, not the `{company}` route.
2. **One approval path.** Approve/reject logic must live in a service (`IPermitService`/workflow service) shared by the web controller and the API, so rules cannot diverge. Check first how much sits inside `PermitsController` today.
3. **Tokens.** Access JWT (about 15 min) + rotating refresh token, hashed at rest, per device. Reuses the existing `JwtSettings` and Bearer scheme (`AddJwtAuthentication`).
4. **`DeviceRegistration`** table: user, device id, platform, FCM token, last seen, revoked. Enables "log out this phone".
5. **OTP.**
   - `LoginOtp` table: hashed code, channel, expiry (5 min), attempt count, resend throttle.
   - `IOtpSender` with SMS and WhatsApp implementations; the user (or company setting) picks the channel, with fallback to the other.
   - Provider settings in `AppSetting` (encrypted secrets), global with company override, like email.
   - Require a confirmed number, normalised to E.164 (existing numbers are free text and need cleaning).
   - New `"otp"` rate-limit policy per phone number and per IP, stricter than `"auth"`.
   - **Lead time:** sender-ID / WhatsApp template approval can take weeks; start early.
6. **Push.** FCM through the Firebase Admin SDK. Hook into the existing notification path (`NotificationPushService`/SignalR) so each notification also goes to the user's devices. Payload carries only `permitId` + `type`, with no permit content on the lock screen.
7. **"Needs attention" summary** per permit, computed server-side (missing certificates, window ends after hours, overlapping permits at the same location). Rule-based, not AI.
8. **OpenAPI** generated from the API and the Dart client generated from it.

## Flutter stack (proposed)

Riverpod · go_router · dio (+ refresh interceptor) · flutter_secure_storage · firebase_messaging · local_auth (biometrics) · PDF viewer · fl_chart · flutter_localizations (ARB; English first, Malay ready).

## UI / UX

**Principle:** an approval inbox, not a mini web app. Tap the push, review, decide in under 30 seconds with confidence.

**Mockups:** [mobile-mockups/overview.png](mobile-mockups/overview.png) (login, home, approvals, permit detail, certificates, confirm sheet). Source: [mobile-mockups/mockups.html](mobile-mockups/mockups.html), open it in a browser (add `?s=1` to `?s=6` for a single screen). Sample data only; colours and layout are a direction, not final.

**Navigation:** bottom bar — **Home · Approvals (badge) · Alerts · Me**. Future features become tabs or Home tiles.

### Login
- Segmented switch: **Username** | **Mobile**.
- Mobile: number → channel (SMS / WhatsApp) → 6-box OTP with auto-read and a 30 s resend timer.
- Offer biometric unlock after first login; refresh token in the Keystore; re-lock after N minutes in the background.
- Show the company logo/name after login (existing branding).

### Home
- Prominent **"Waiting for you: N"** card (amber) → Approvals, showing the oldest-waiting age.
- Tiles: Active today · Expiring soon (< 24 h) · Overdue · Approved by me this week.
- Small permits-by-status chart, Recent activity. Pull to refresh, skeleton loaders.

### Approvals
- Filter chips (All · Urgent · Today · Site/Type) + search.
- Card: permit no., title, requester, location, work window, type chip, **time waiting**; red edge when overdue.
- Swipe right = approve, left = reject — each opens a **confirmation sheet** (shows permit title; reject needs a reason; approve takes an optional comment). Haptics. Bulk approve only for permits with no unresolved warnings.
- After a decision: result card and **"Next: N left"** to clear the queue quickly.

### Permit detail (the crucial screen)
- **Sticky header:** permit no., status pill, **step tracker** ("Step 2 of 4 — you are here") from workflow history.
- **Sticky bottom bar:** `Reject` (outlined) / `Approve` (filled), always reachable.
- **"Needs your attention" checklist** at the top, e.g. "Hot work certificate attached ✔ · Gas test missing ✘ · Ends after 18:00 ⚠".
- Sections (tabs):
  1. **Overview** — who/what/where/when, site mini-map, duration bar ("starts in 3 h · runs 8 h"), overlap warnings.
  2. **Certificates** — horizontal strip of cards labelled by certificate title only (no A–H letters) with a status icon (complete / missing / not required) and a summary chip ("3 of 3 complete"). Tap → full-screen certificate with fields grouped as on the form; swipe between certificates. Old-system permits: parse `PermitForm` null-safely (any field may be missing); new-system: render from `FormTemplate.LayoutJson` + `CertificateDataJson`.
  3. **Attachments** — gallery with pinch-zoom, in-app PDF viewer, share.
  4. **History** — workflow timeline (names, times, comments).
- **View official PDF** in one tap (server's existing export, old and new system) — so form layouts need not be reproduced in Flutter.
- Later ideas: "changed since you last saw it" badges, side-by-side certificate compare on tablets, optional signature on approve, offline read cache for opened permits.

### Alerts
- Grouped Today / Earlier, unread state; same notification records as the web app.
- Settings: per-type toggles, quiet hours, reminder if not acted on within N hours (ties to the PendingEscalation rule).
- Push taps deep-link to the permit detail. Approve/Reject buttons in the notification shade are deliberately **out** — approving without opening the detail is risky.

### Visual
**Poppins** throughout the app (bundled as a font asset in `mobile/`, OFL licence, so it works offline; Poppins runs wider than Roboto, so check long titles); web palette and tokens, light and dark from day one; 48 dp touch targets; status never shown by colour alone (icon + label).

## Phasing

1. **API + auth:** versioned endpoints, refresh tokens, device table; password login only.
2. **Flutter shell:** login, Approvals list, native detail, approve/reject — already a usable MVP.
3. **Push (FCM)** and deep links.
4. **OTP login** (SMS + WhatsApp providers).
5. **Dashboard, certificate viewer polish,** offline cache, biometrics.
6. **Play Store closed testing**, then iOS (APNs, Apple Developer account).

## Open items

- Pick the SMS and WhatsApp providers (Malaysian coverage, sender-ID/template lead time, per-message cost to pass to the client). Decide whether the channel is chosen per user or per company.
- Clean up existing free-text phone numbers before OTP goes live.
- Check where approve/reject logic currently lives (controller vs service) before designing `Approvals` endpoints.
- Confirm the Android app id, Firebase project and Play Console ownership.
- Whether the web tag becomes `v2.0.0` (see Versioning).
