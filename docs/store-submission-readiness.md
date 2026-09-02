# SafeSpeak mobile store readiness

This document separates the repository work that is complete from account-owned
steps that must wait for Apple Developer Program and Google Play Console access.

## Stable application identity

| Field | Value |
| --- | --- |
| Product name | SafeSpeak |
| Android package name | `com.theprojecthub.safespeak.mobile` |
| Apple bundle ID | `com.theprojecthub.safespeak.mobile` |
| Current display version | `0.1.0` |
| Current build number | `4` |
| Publisher display name | The Project Hub, subject to the verified store account name |

Treat the package/bundle ID as permanent once either store record is created.
Changing it later creates a different application instead of an update.

## Public URLs

The upload-ready local website is generated under
`local-deployment/safespeak-web`. That folder is intentionally ignored by Git.
After uploading it to an HTTPS web host, record the final addresses here and in
the store consoles:

| Purpose | Final URL |
| --- | --- |
| Product page | `https://YOUR-DOMAIN.example/` |
| Privacy policy | `https://YOUR-DOMAIN.example/privacy/` |
| Support | `https://YOUR-DOMAIN.example/support/` |
| Accessibility | `https://YOUR-DOMAIN.example/accessibility/` |

Do not submit placeholder addresses. Test each final URL while signed out and in
a private browser window before store review.

The planned same-server deployment target is `safespeak.bl4ut0.dev`. Its guarded
FTPS helper and account-creation instructions are in `tools/website-deploy`.
DNS, HTTPS, the site document root, and the dedicated SafeSpeak transfer account
must exist before replacing the placeholders above with final URLs.

## Google Play account-owned setup

1. Enroll in full Google Play distribution and create the application with the
   package name above.
2. Complete identity verification, the store listing, app-access declaration,
   content rating, target-audience declaration, ads declaration, and Data Safety
   form.
3. Create the upload keystore once, back it up outside the repository, and opt in
   to Google Play App Signing.
4. Upload the first signed AAB through Play Console. Google requires the package
   to exist before API-based updates can use the publisher workflow.
5. Enable the Google Play Android Developer API, create or federate a service
   account, and grant only the SafeSpeak application permissions it needs.
6. Protect the `google-play-production` GitHub environment with a required human
   reviewer, then add the configuration below.

### Google Play GitHub configuration

| Type | Name | Contents |
| --- | --- | --- |
| Environment secret | `ANDROID_KEYSTORE_BASE64` | Base64 encoding of the upload keystore file |
| Environment secret | `ANDROID_KEY_ALIAS` | Alias inside the upload keystore |
| Environment secret | `ANDROID_KEY_PASSWORD` | Upload key password |
| Environment secret | `ANDROID_STORE_PASSWORD` | Keystore password |
| Environment secret | `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` | Google service-account JSON; needed only for upload |

Run **Google Play release candidate** manually from `android/main`. Leave
`upload_to_play` off to create a signed artifact for manual inspection. Turn it
on only after the initial Play Console upload and API linkage are complete; the
workflow deliberately targets the internal track as a draft.

## Apple account-owned setup

1. Join the Apple Developer Program and accept current agreements.
2. Register the bundle ID above, create the App Store Connect app record, and
   complete the privacy, age-rating, availability, export-compliance, listing,
   and review-contact sections.
3. Create an Apple Distribution certificate and an App Store provisioning
   profile for the exact bundle ID. Export the certificate and private key as a
   password-protected `.p12` file.
4. Request App Store Connect API access and create an API key with only the role
   needed to upload TestFlight builds.
5. Protect the `apple-app-store-production` GitHub environment with a required
   human reviewer, then add the configuration below.

For the free App Store release, use Apple's Standard EULA rather than submitting
the repository source-visible license as a Custom EULA. The standard EULA covers
the installed App Store binary, while the SafeSpeak license continues to govern
the visible source, original assets, documentation, and authorized direct
downloads. See `docs/store-listing/en-US/licensing.md`.

### Apple GitHub configuration

| Type | Name | Contents |
| --- | --- | --- |
| Environment secret | `APPLE_DISTRIBUTION_CERTIFICATE_BASE64` | Base64 encoding of the exported `.p12` |
| Environment secret | `APPLE_CERTIFICATE_PASSWORD` | Password used when exporting the `.p12` |
| Environment secret | `APPLE_PROVISIONING_PROFILE_BASE64` | Base64 encoding of the App Store `.mobileprovision` file |
| Environment variable | `APPLE_CODESIGN_IDENTITY` | Full certificate identity shown by Keychain, including team ID |
| Environment variable | `APPLE_PROVISIONING_PROFILE_NAME` | Provisioning-profile name used by the build |
| Environment variable | `APPSTORE_API_KEY_ID` | App Store Connect API key ID; needed only for upload |
| Environment variable | `APPSTORE_ISSUER_ID` | App Store Connect issuer ID; needed only for upload |
| Environment secret | `APPSTORE_API_PRIVATE_KEY` | Complete `.p8` API private key; needed only for upload |

Run **Apple TestFlight release candidate** manually from `ios/main`. Leave
`upload_to_testflight` off for the first signed artifact inspection. The upload
switch sends the same verified IPA to TestFlight; App Store release remains a
separate human decision in App Store Connect.

## Current privacy declarations

For the present mobile test application:

- no SafeSpeak account is created;
- no advertising, tracking, developer analytics, or telemetry is included;
- no data is sent to or retained by The Project Hub;
- text entered in the test screen is moderated on-device;
- speech uses the operating system text-to-speech service;
- no live platform account is connected.

On this build, Google Play Data Safety should report no data collected and no
data shared, while still linking to the privacy policy. Apple App Privacy should
report no data collected. Re-audit both declarations before adding any connector,
authentication SDK, crash reporter, analytics package, advertising library, or
cloud moderation service.

The iOS package now includes the required .NET MAUI privacy manifest reasons for
file timestamp, system boot time, and disk-space APIs. It also states that the
current app does not track or collect data. Review that manifest whenever mobile
code or dependencies change.

## Release gates still required

- Real Android TalkBack and iOS VoiceOver testing on supported physical devices
- Final phone and tablet screenshots captured from the release candidate
- Store-account identity and legal-name verification
- Paid enrollment and all current agreements
- Production privacy/support URLs on stable HTTPS hosting
- First manual Play Console upload and App Store Connect record
- Signing key backups and GitHub environment approvals
- Store review credentials or reviewer instructions if future connectors add sign-in
- Re-review of platform terms before any direct livestream connector ships

## Generated store graphics

Run `tools/Generate-MobileStoreAssets.ps1` on Windows to generate the current
brand-derived Apple 1024-by-1024 icon, Google Play 512-by-512 icon, and Google
Play 1024-by-500 feature graphic under `artifacts/mobile-store-assets`. The Apple
asset is flattened to an opaque background because App Store artwork cannot rely
on transparency. The source remains the existing reviewed SafeSpeak icon.

Screenshots are intentionally not fabricated. Capture them from the final signed
build on the exact phone and tablet sizes requested by each store after the
screen-reader and layout checks pass.
