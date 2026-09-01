# SafeSpeak desktop packaging and Microsoft Store readiness

SafeSpeak has one release build entry point: `installer/Build-Release.ps1`. It reads the current four-part desktop version from `Directory.Build.props`, restores the pinned .NET SDK dependencies, runs the Core test suite, publishes a self-contained WPF application, creates the requested artifacts, and verifies the structure of the generated MSIX by unpacking it again. It never starts or owns the TikFinity emulator.

## Prerequisites

- Windows 10 version 2004 (build 19041) or later.
- The .NET SDK selected by `global.json`.
- Windows 10 or Windows 11 SDK App Packaging tools (`makeappx.exe`) when building MSIX.
- A trusted code-signing certificate only when producing a sideloadable signed MSIX. Partner Center identity values and signing credentials are not stored in this repository.

The portable ZIP is self-contained. Its users do not need to install .NET separately.

## Build verified release artifacts

From the repository root:

```powershell
./installer/Build-Release.ps1 -Architecture x64 -Format Both
```

The command creates:

- `artifacts/SafeSpeak-<version>-win-x64/`: the expanded self-contained desktop application;
- `artifacts/SafeSpeak-<version>-win-x64.zip`: portable desktop distribution;
- `artifacts/SafeSpeak_<version>_x64.msix`: packaged desktop distribution;
- `artifacts/SafeSpeak-<version>-win-x64.release.json`: source provenance, executable and managed-runtime metadata, file sizes, SHA-256 hashes, local-model identity, runtime, and signature status;
- `artifacts/current-win-x64.json`: the atomic pointer to the exact verified current executable and release report.

After a successful current-version x64 build, `Launch-SafeSpeak.bat` starts only the executable named by `current-win-x64.json`. The schema-v2 launcher requires the pointer version to match `Directory.Build.props`; verifies the release report, app host, managed app/core assemblies, dependency/runtime manifests, and pinned moderation model/tokenizer; checks embedded four-part versions; and refuses to start a second SafeSpeak process. It never scans timestamps or starts an arbitrary executable from an old output directory.

For a local release test, the build-and-run helper performs the x64 ZIP build with tests and then invokes that same verified launcher:

```powershell
./installer/Build-And-Run.ps1
```

Use `-Format Zip` on a computer without the Windows SDK. The release build runs both the Core suite and the WPF accessibility-contract suite by default. Use `-SkipTests` only after both suites have already passed in the same clean source revision. `-KeepStaging` preserves the generated MSIX layout for diagnosis.

The publish profiles deliberately produce a multi-file, self-contained application. WPF, KokoroSharp, ONNX Runtime, NAudio, voice embeddings, and other native/runtime files are therefore included explicitly. Trimming and single-file bundling are disabled because both can hide missing native assets until runtime. The roughly 326 MB Kokoro model itself is installed only after an explicit in-app request and is not embedded in the base ZIP/MSIX.

## Signed test package

The repository default publisher (`CN=SafeSpeak`) is a development placeholder. To create a signed package, the manifest publisher must exactly match the subject of the certificate:

```powershell
./installer/Build-Release.ps1 `
  -Architecture x64 `
  -Format Msix `
  -Publisher "CN=Exact certificate subject" `
  -CertificateThumbprint "CERTIFICATE_THUMBPRINT"
```

The script signs through the current user's certificate store and verifies the signature with `signtool`. It does not create, import, or export certificates. An unsigned package can be structurally tested and submitted to a signing pipeline, but Windows will not accept it for ordinary sideload installation.

## Microsoft Store submission

Before the first Store build:

1. Reserve the SafeSpeak product name in Partner Center.
2. Copy the package Identity Name, Publisher, and Publisher display name from Partner Center. These values are assigned by Microsoft and must not be guessed.
3. Review and approve `installer/Assets/SafeSpeakIconMaster-v1.png`, then run `./installer/Generate-Assets.ps1` to refresh the MSIX tiles and multi-resolution executable icon. Partner Center listing artwork is prepared separately from these package assets.
4. Finish accessibility testing with Narrator, keyboard-only navigation, Windows High Contrast themes, 200% text scaling, and the complete first-run reader decision flow before making an accessibility conformance claim.
5. Prepare the Store listing, privacy policy URL, support contact, age rating, screenshots, and a clear justification for the restricted `runFullTrust` capability.
6. Decide whether the first submission will upload the generated `.msix` directly or add a `.msixupload` wrapper with symbols for improved Store crash analytics. Partner Center accepts both and currently recommends the upload wrapper.
7. Build a Store candidate. Microsoft Store versions require a non-zero major component and reserve the fourth component as zero:

```powershell
./installer/Build-Release.ps1 `
  -Architecture x64 `
  -PackageVersion 1.0.0.0 `
  -Format Msix `
  -StoreSubmission `
  -IdentityName "PARTNER_CENTER_IDENTITY_NAME" `
  -Publisher "PARTNER_CENTER_PUBLISHER" `
  -PublisherDisplayName "PARTNER_CENTER_DISPLAY_NAME"
```

`-StoreSubmission` rejects placeholder identities, a zero major version, and non-zero revision components. The generated package is not a Store submission by itself; Partner Center association, certification answers, production artwork, and release credentials remain external requirements. Microsoft signs the package distributed to Store customers; a trusted matching signature is still required for ordinary direct sideload distribution.

Run the Windows App Certification Kit against the final candidate from an elevated Windows session. Structural `makeappx` verification is part of the build script, but it does not replace WACK API, manifest, launch, and platform-compliance checks.

The `runFullTrust` capability is required because SafeSpeak is a desktop WPF application that uses local WebSocket/IPC and Windows audio APIs. It permits full-trust execution; it does not by itself guarantee that TikFinity or another local service is installed, running, or reachable.

### GitHub-to-Store publisher track

The first SafeSpeak submission must be created, completed, certified, and made live through Partner Center. Microsoft's current GitHub Actions update path applies only after the product is live and currently supports free products, which matches SafeSpeak's release model.

After Partner Center assigns the product values, configure these non-secret GitHub repository variables:

- `STORE_APP_ID`
- `STORE_IDENTITY_NAME`
- `STORE_PUBLISHER`
- `STORE_PUBLISHER_DISPLAY_NAME`

Create a protected GitHub Environment named `microsoft-store-production`, require a reviewer, and add these environment secrets:

- `PARTNER_CENTER_TENANT_ID`
- `PARTNER_CENTER_SELLER_ID`
- `PARTNER_CENTER_CLIENT_ID`
- `PARTNER_CENTER_CLIENT_SECRET`

The Entra application represented by those credentials must be associated with Partner Center and assigned the Manager role. Never commit these values, write them to release reports, or expose them as ordinary repository variables.

`installer/Build-StoreBundle.ps1` runs the release entry point for x64 and ARM64, performs the full test pass once, creates a neutral `.msixbundle`, unbundles it for structural verification, and records hashes in a Store bundle report. It rejects placeholder identity values and Store-incompatible versions.

`.github/workflows/store-publisher.yml` is manual-only. Its safe default builds and retains a candidate without contacting Partner Center. `upload_draft` requires approval through the protected environment and uploads with `--noCommit`; `commit_submission` must also be deliberately selected to send that upload to Microsoft certification. Do not enable a push-to-Store trigger or remove the protected reviewer.

## Continuous packaging verification

`.github/workflows/desktop-build.yml` runs the same release script and authoritative `Directory.Build.props` version on pull requests, pushes to `main`, and manual dispatches. GitHub Actions also validates and packages the separate Stream Deck plug-in, then uploads the ZIP, unsigned MSIX, plug-in installer, and release report for inspection. The workflow intentionally does not publish or sign a public release because those actions require protected project credentials and an explicit release decision.

## WinGet publication

Do not publish a manifest containing placeholder hashes or URLs. After the signed MSIX is available at a stable HTTPS release URL, generate the WinGet multi-file manifest:

```powershell
./installer/New-WingetManifests.ps1 `
  -PackageVersion 1.0.0 `
  -InstallerUrl "https://github.com/Bl4ut0/SafeSpeak/releases/download/v1.0.0/SafeSpeak_1.0.0.0_x64.msix" `
  -InstallerSha256 (Get-FileHash ./artifacts/SafeSpeak_1.0.0.0_x64.msix -Algorithm SHA256).Hash

winget validate --manifest installer/winget
```

Only submit the generated manifest after installing and upgrading that exact public artifact on a clean Windows user account.

## Optional integrations

- The reviewed Apache-2.0 moderation classifier is pinned, hashed, licensed, and embedded in the desktop package. Optional speech models belong under SafeSpeak's application-data model directory and are not embedded unless their license and package-size impact have been reviewed.
- A Stream Deck plug-in is distributed separately. Installing an MSIX must not silently modify an existing Stream Deck profile.
- A virtual audio cable remains optional; SafeSpeak must continue to launch and provide single-output audio without it.

## Microsoft references

- [Generate an MSIX package with command-line tools](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-manual-conversion)
- [Microsoft Store MSIX package and version requirements](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [Full-trust capability declarations](https://learn.microsoft.com/windows/apps/package-and-deploy/app-capability-declarations)
- [Upload MSIX packages in Partner Center](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/upload-app-packages)
