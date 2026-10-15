# Company subdomains: setup and deployment

Each company has its own address, for example `https://iotm.permitpro.app`. The apex, `https://permitpro.app`, has no company. It shows an "Enter your company's address" page and hosts Super Admin (`/admin`).

This guide covers what the app does, the steps to deploy it, the Cloudflare and IIS changes, the demo site, and local development.

## How it works

- **Company from the host name.** `Web/Services/Tenancy.cs` reads the host and looks up the active company whose `Company.Subdomain` matches (Super Admin → Companies → Edit). Unknown or inactive subdomains get a "There's no company at …" page.
- **Clean addresses.** Pages are at `iotm.permitpro.app/permits`, with no company id in the path. Internally the middleware adds the id back for routing. An old `/{company id}/...` link opened in the browser redirects to the same page without it.
- **Login.** On a company's host, the login form asks for email and password only. Users of other companies are refused with "Invalid login attempt". Super Admins (`SUPERUSER`) can sign in anywhere.
- **Isolation.** A signed-in user only sees their own company's pages. The sign-in cookie belongs to one host, so each company's site has its own session.
- **Old links keep working.**
  - `permitpro.app/{company id}/...` bookmarks redirect to the company's host.
  - The approval links in old emails (`/account/login?company=…&entity=permits&id=…`) open that permit after sign-in.
  - Notification links stored before this change redirect too.
- **Email links.**
  - Workflow emails use the host the action happened on.
  - Password-reset links use the user's own company host.
  - Permit-rule emails use `https://{subdomain}.{Tenancy:RootDomain}`.
- **Super Admin.** `/admin` lives on the apex. On a company's host it redirects there.

### Configuration (`appsettings.json` → `Tenancy`)

| Key | Production | Development (`appsettings.Development.json`) |
|---|---|---|
| `RootDomain` | `permitpro.app` | `localhost` |
| `Scheme` | `https` | `http` |
| `Port` | (unset) | `5144` |
| `HostAliases` | `{}` | `{}` |

- `Scheme` and `Port` are used only for links built without a request (permit-rule emails).
- `HostAliases` maps a full host name to one company's subdomain. Example: `{ "demo.permitpro.app": "iotm" }` serves IOTM at `demo.permitpro.app`. An alias host also keeps `/admin`, because it has no apex next to it.
- `AllowedHosts` already allows `permitpro.app;*.permitpro.app;localhost`. Never set it to `*`.

### Reserved subdomains

These can't be used as a company's subdomain: `www`, `admin`, `api`, `app`, `demo`, `uat`, `staging`, `dev`, `test`, `localhost`, `mail`, `email`, `smtp`, `imap`, `pop`, `mx`, `webmail`, `cf-bounce`, `ns1`, `ns2`, `static`, `cdn`, `assets`, `status`, `help`, `support`, `docs`, `login`, `auth`, `account`, `permitpro`.

The list is in `Core/Helpers/CompanySubdomain.cs`.

---

## Deploying to production

Do the steps in this order. Steps 1–4 change nothing users can see. Step 6 is the one that switches company subdomains on.

### 1. Database

Generate the idempotent script on the dev machine and run it on the production database (SSMS, or `sqlcmd -I`):

```bash
dotnet ef migrations script --idempotent --project Core --startup-project Web --output publish-migrations.sql
```

Migration `AddCompanySubdomain` adds:
- the `Companies.Subdomain` column, with a unique index
- `iotm` for the company named `IOTM`

### 2. A subdomain for every active company

A company without one can't be signed in to: the apex has no login form. After deploying (step 3):
1. Sign in at `https://permitpro.app/admin`.
2. Open **Companies → Edit**, set **Subdomain**, and save. The Address column shows **Not set** for companies that still need one.

You can also set it with SQL before deploying:

```sql
UPDATE Companies SET Subdomain = 'tmotm' WHERE Name = 'TMOTM' AND Subdomain IS NULL;
```

### 3. Publish and copy the app

This is the usual manual publish (see README → Deployment).

### 4. IIS: answer for every subdomain

Wildcard host names need IIS 10 (Windows Server 2016 or later).

1. Open **IIS Manager → Sites →** the PermitPro site **→ Bindings…** (right-hand panel).
2. **Add…**
   - Type: `https`
   - IP address: `All Unassigned`
   - Port: `443`
   - Host name: `*.permitpro.app`
   - Tick **Require Server Name Indication**.
   - SSL certificate: the same Cloudflare Origin Certificate the `permitpro.app` binding uses.
3. **Add…** again
   - Type: `http`
   - IP address: `All Unassigned`
   - Port: `80`
   - Host name: `*.permitpro.app`
4. Keep the existing `permitpro.app` bindings (80 and 443).

The bindings should then read:

| Type | Host Name | Port |
|---|---|---|
| http | permitpro.app | 80 |
| https | permitpro.app | 443 |
| http | *.permitpro.app | 80 |
| https | *.permitpro.app | 443 |

#### Check that the certificate covers `*.permitpro.app`

1. Open `certlm.msc` → **Personal → Certificates** and open the Cloudflare Origin Certificate.
2. Go to **Details → Subject Alternative Name**. It should list both `DNS Name=*.permitpro.app` and `DNS Name=permitpro.app`. Cloudflare includes both by default.

If the wildcard is missing:
1. In Cloudflare, go to **SSL/TLS → Origin Server → Create Certificate**, with host names `permitpro.app, *.permitpro.app`.
2. Download the certificate and key as PEM.
3. Convert them to a `.pfx`: `openssl pkcs12 -export -in origin.pem -inkey origin.key -out origin.pfx`
4. Import it in IIS Manager → the server → **Server Certificates → Import**.
5. Select it in all https bindings.

### 5. Cloudflare: DNS and certificates (check only)

- **DNS → Records:** the `*` A record (`14.192.71.41`) and the `permitpro.app` A record must both be **Proxied** (orange cloud). Nothing else changes. The MX, DKIM, SPF and DMARC records are unrelated. Explicit records always win over `*`; `cf-bounce` has only mail records, so it's unaffected.
- **SSL/TLS → Overview:** keep the mode on **Full (strict)**.
- **SSL/TLS → Edge Certificates:** the Universal certificate (`permitpro.app, *.permitpro.app`) should be **Active**. Universal SSL covers one level of subdomain, so it covers `iotm.permitpro.app` but not `x.demo.permitpro.app`.

### 6. Cloudflare: let subdomains through (switches it on)

Today a redirect rule sends every host other than `permitpro.app` to `https://permitpro.app`. Change it so it only catches `www`:

1. Go to **Rules → Overview** (or **Rules → Redirect Rules**) and edit the rule that redirects to `permitpro.app`.
2. Under **If incoming requests match… → Custom filter expression**, set:
   - Field `Hostname`, operator `equals`, value `www.permitpro.app`.
   - Expression: `(http.host eq "www.permitpro.app")`
3. Keep the target and add the query string:
   - Type `Dynamic`, expression `concat("https://permitpro.app", http.request.uri.path)`
   - Status 301
   - Tick **Preserve query string**. This also fixes the old issue of the query string being dropped.
4. **Save**. It takes effect within a minute.

### 7. Check it

- `https://permitpro.app` shows the company address page. Entering `iotm` goes to `https://iotm.permitpro.app/account/login`, with IOTM's logo and name.
- Signing in lands on `https://iotm.permitpro.app/dashboard`. Menu links have no company id in them.
- `https://nosuchcompany.permitpro.app` shows "There's no company at nosuchcompany.permitpro.app".
- `https://iotm.permitpro.app/admin` redirects to `https://permitpro.app/admin`.
- An old bookmark, `https://permitpro.app/92cc7520-…/permits`, redirects to `https://iotm.permitpro.app/permits`.

Tell users their new address. Everyone signs in once more, because the old session belonged to `permitpro.app`.

### Rolling back

1. Change the Cloudflare redirect rule back to "Hostname not equals `permitpro.app`".
2. Redeploy the previous build.

The new `Subdomain` column is harmless to the old build, so the database can stay as it is.

---

## Demo site: `demo.permitpro.app` (until it has its own domain)

The demo is a single host. It can't have company subdomains under it: `iotm.demo.permitpro.app` would need Cloudflare Advanced Certificate Manager (paid) and a new origin certificate. Instead, `demo` is reserved, and the demo site serves one company through `Tenancy:HostAliases`.

1. **Database.** Use a separate demo database, with migrations applied and its company's `Subdomain` set (e.g. `iotm`).
2. **App settings.** Give the demo site's IIS app its own environment, e.g. `ASPNETCORE_ENVIRONMENT=Staging` in its `web.config`. In `appsettings.Staging.json`, set:

   ```json
   {
     "ConnectionStrings": { "MSSQL": "<demo database>" },
     "Tenancy": {
       "RootDomain": "permitpro.app",
       "HostAliases": { "demo.permitpro.app": "iotm" }
     }
   }
   ```

   All links the demo builds, including emails, then point at `demo.permitpro.app`, never at production. Super Admin works at `https://demo.permitpro.app/admin`.
3. **IIS (same server).** Create a separate site for the demo with these bindings:
   - `https`, port 443, host `demo.permitpro.app`, SNI ticked, the same origin certificate
   - `http`, port 80, host `demo.permitpro.app`

   IIS prefers an exact host name over `*.permitpro.app`, so the demo site gets `demo` and production gets every other subdomain.
4. **DNS.**
   - Same server: nothing to do, because `*` already points there.
   - Different server: add a proxied **A** record `demo` → that server's IP.
5. **Cloudflare redirect rule.** It must no longer catch `demo.permitpro.app`; step 6 above already handles that.
6. **Moving to its own domain later.**
   1. Point the new domain (and `*.newdomain`) at the demo site, with its own origin certificate and IIS bindings.
   2. Set `Tenancy:RootDomain` to the new domain and remove the alias.
   3. Add the new domain to `AllowedHosts` (`newdomain;*.newdomain`).

---

## Local development

`appsettings.Development.json` is git-ignored, so on each machine add:

```json
"AllowedHosts": "localhost;*.localhost",
"Tenancy": { "RootDomain": "localhost", "Scheme": "http", "Port": 5144 }
```

- Run the **http** profile and open `http://localhost:5144` (address page) or `http://iotm.localhost:5144` (IOTM).
- Browsers send any `*.localhost` name to this machine, so there's no hosts-file change.
- Use `http`. The HTTPS dev certificate covers only `localhost`, so `https://iotm.localhost:7079` shows a certificate warning.
- Use `localhost`, not `127.0.0.1`. `AllowedHosts` rejects bare IP addresses.
