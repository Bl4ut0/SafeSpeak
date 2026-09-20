# SafeSpeak website deployment

This helper uploads only the ten public SafeSpeak website files from
`local-deployment/safespeak-web`. It never uploads the local README, ZIP archive,
credentials, application repository, or any file outside its explicit allowlist.
It does not delete remote files.

## Why this differs from the older website projects

The neighboring website projects point at the same FTP server, but their current
local `.env` files have `FTP_SECURE=false`. SafeSpeak must not copy that setting.
The server accepts explicit FTPS on port 21, and its certificate validates when
the public Project Hub PKI root in `server-ca.pem` is used with the certificate
hostname `test.pki.bl4ut0.dev`.

SafeSpeak therefore refuses plaintext FTP, refuses disabled certificate
validation, and loads the public CA certificate explicitly. Credentials remain
local to the ignored deployment configuration. The user authorized the existing
portfolio/bl4ut0 account for this site's deployment.

## One-time server setup

1. Create the `safespeak.bl4ut0.dev` site/subdomain on the existing server.
2. Create a dedicated FTP account jailed to
   `/domains/safespeak.bl4ut0.dev/public_html`.
3. Create the DNS record and enable public HTTPS for the subdomain.
4. Put the new username and password in `tools/website-deploy/.env`. That local
   file is ignored by Git. Do not copy another site's username or password.

If the hosting panel assigns a different document root, update
`FTP_REMOTE_DIR`, but keep it absolute and ending in `/public_html`.

## Local use

### Diagnostics intake switch

The receiver runs entirely on the website. Edit the private server file
`/domains/safespeak.bl4ut0.dev/diagnostics-private/config.php` using Virtualmin's
file manager or FTP. Change `'enabled' => false` to `'enabled' => true` to accept
submissions, then set it back to false when finished. No desktop helper or local
process is required. Deployments install this file only if missing and preserve
existing settings. Missing or invalid configuration closes intake.

Optional `allowedIp` restricts the connecting IP and `expires` sets a Unix expiry
timestamp; both default to null. Host-bound one-use support codes remain required.
Private receipt metadata records handshake and upload IP addresses. Closing
intake blocks handshakes/uploads and invalidates earlier tickets when observed.

To publish only the diagnostics PHP receiver using the same FTPS account:

```powershell
node deploy.js --receiver --dry-run
node deploy.js --receiver --check
node deploy.js --receiver
```

This profile allowlists only `local-deployment/safespeak-diagnostics/public/index.php`
to `public_html/diagnostics/index.php`. It verifies that GET returns HTTP 405 JSON.
The receiver profile creates `diagnostics-private` beside the site's `public_html`,
with private permissions, code/ticket/upload folders and private admin scripts.
The PHP endpoint defaults to that directory. The Virtualmin environment variable
can override it when needed. Transfer access does not configure PHP-FPM request
limits, scheduled retention cleanup or upload-code issuance.
The connection check also reports whether the account can access the site's parent
directory. No credentials are printed and no remote files are deleted.

From `tools/website-deploy`:

```powershell
npm install
npm run deploy:dry
npm run deploy:check
npm run deploy
npm run deploy:verify
```

- `deploy:dry` validates and lists the exact local allowlist without networking.
- `deploy:check` verifies FTPS, the server certificate, the dedicated account,
  and the existing remote document root without uploading.
- `deploy` uploads the allowlist with the root `index.html` last, then verifies
  all four public HTTPS pages.
- `deploy:verify` performs only the public HTTPS checks.

Do not run the real deployment until DNS, HTTPS, the dedicated account, and its
remote root have been created.

Current upload flow: one Upload diagnostics button, no support-code entry. The app signs host ID, ZIP size and SHA-256 using an embedded, clonable HMAC marker (version 1). The server checks it and issues a short-lived one-use transfer ticket automatically. This is not proof of app authenticity. Uploads include all available .log files from the last 10 days, including raw StreamAudit chat history, plus system/version/performance details automatically without requiring confirmation prompts. PHP intake configuration, rate limits and ZIP validation remain enforced. Legacy support-code handshakes are accepted for compatibility.
