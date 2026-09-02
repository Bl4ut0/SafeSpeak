# SafeSpeak website deployment

This helper uploads only the eight public SafeSpeak website files from
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
unique to the SafeSpeak deployment account.

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
