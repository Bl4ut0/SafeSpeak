# SafeSpeak desktop packaging and Microsoft Store readiness

SafeSpeak has one release build entry point: `installer/Build-Release.ps1`. It reads the current four-part desktop version from `Directory.Build.props`, restores the pinned .NET SDK dependencies, runs the Core test suite, publishes a self-contained WPF application, creates the requested artifacts, and verifies the generated MSIX and MSI packages. It never starts or owns the TikFinity emulator.

## Prerequisites

- Windows 10 version 2004 (build 19041) or later.
- The .NET SDK selected by `global.json`.
- Windows 10 or Windows 11 SDK App Packaging tools (`makeappx.exe`) when building MSIX.
- WiX Toolset SDK 5.0.2, restored automatically from the pinned package reference when building MSI.
- A trusted Authenticode code-signing certificate when producing public MSI, portable ZIP, or sideloadable MSIX downloads. Microsoft signs the Store-delivered MSIX after certification, but that Store certificate and private key cannot be exported or reused for GitHub downloads. Signing credentials are never stored in this repository.

The portable ZIP is self-contained. Its users do not need to install .NET separately.

## Build verified release artifacts

From the repository root:

```powershell
./installer/Build-Release.ps1 -Architecture x64 -Format All
```

The command creates:

- `artifacts/SafeSpeak-<version>-win-x64/`: the expanded self-contained desktop application;
- `artifacts/SafeSpeak-<version>-win-x64.zip`: portable desktop distribution;
- `artifacts/SafeSpeak_<version>_x64.msix`: packaged desktop distribution;
- `artifacts/SafeSpeak_<version>_x64.msi`: Windows Installer distribution with upgrade, repair, and uninstall support;
- `artifacts/SafeSpeak-<version>-win-x64.release.json`: source provenance, executable and managed-runtime metadata, file sizes, SHA-256 hashes, local-model identity, runtime, and signature status;
- `artifacts/current-win-x64.json`: the atomic pointer to the exact verified current executable and release report.

After a successful current-version x64 build, `Launch-SafeSpeak.bat` starts only the executable named by `current-win-x64.json`. The schema-v2 launcher requires the pointer version to match `Directory.Build.props`; verifies the release report, app host, managed app/core assemblies, dependency/runtime manifests, and pinned moderation model/tokenizer; checks embedded four-part versions; and refuses to start a second SafeSpeak process. It never scans timestamps or starts an arbitrary executable from an old output directory.

For a local release test, the build-and-run helper performs the x64 ZIP build with tests and then invokes that same verified launcher:

```powershell
./installer/Build-And-Run.ps1
```

Use `-Format Zip` on a computer without the Windows SDK. `Both` preserves the original ZIP + MSIX output, while `All` adds MSI and `Msi` builds only the Windows Installer package after publishing the application. The release build runs both the Core suite and the WPF accessibility-contract suite by default. Use `-SkipTests` only after both suites have already passed in the same clean source revision. `-KeepStaging` preserves generated packaging layouts for diagnosis.

The publish profiles deliberately produce a multi-file, self-contained application. WPF, KokoroSharp, ONNX Runtime, NAudio, voice embeddings, and other native/runtime files are therefore included explicitly. Trimming and single-file bundling are disabled because both can hide missing native assets until runtime. The roughly 326 MB Kokoro model itself is installed only after an explicit in-app request and is not embedded in the base ZIP/MSIX/MSI.

## MSI installation, repair, and removal

The WiX MSI installs SafeSpeak per-machine under `Program Files\The Project Hub\SafeSpeak`, creates a Start-menu shortcut, and registers SafeSpeak with Windows Apps & Features. Windows Installer supplies repair, major upgrades, rollback, and uninstall; no separate uninstaller executable is bundled.

Windows Narrator is already included with Windows and is not redistributed inside the MSI. The setup uses standard accessible Windows Installer controls, and its welcome and maintenance screens tell users to press **Windows+Ctrl+Enter** to start Narrator for spoken setup.

Repair is also SafeSpeak's explicit reset operation. Running **Repair** restores the packaged application files and recursively removes `%LOCALAPPDATA%\SafeSpeak` for the user performing the repair. This permanently removes that user's settings, audit logs, and downloaded optional models so the next launch starts clean. Close SafeSpeak before repair. Ordinary uninstall removes the installed program and shortcut but preserves Local AppData.

The build opens the completed MSI database and verifies product identity, version, payload-file count, Start-menu shortcut, and major-upgrade metadata. `-CertificateThumbprint` signs the portable application's executable before ZIP/MSIX/MSI packaging and signs both MSI and MSIX containers when requested. Without a certificate, the MSI remains installable but Windows identifies it as coming from an unknown publisher.

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

Tagged GitHub releases import a separate CA-trusted Authenticode PFX from the encrypted repository secrets `WINDOWS_SIGNING_CERTIFICATE_BASE64` and `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`. The temporary PFX is deleted immediately after import and the certificate is removed from the ephemeral runner after packaging. Tagged builds fail closed if the certificate is missing or if the executable, MSI, or MSIX signature does not verify. A ZIP file has no Authenticode container signature; its included `SafeSpeak.App.exe` is signed and the release publishes a SHA-256 checksum for the ZIP.

## Microsoft Store submission

The first SafeSpeak submission was published on September 2, 2026. Partner Center assigned these non-secret package values:

- Identity Name: `TheProjectHub.SafeSpeak`
- Publisher: `CN=E5322575-F870-45EA-BB35-68B0B2DE563E`
- Publisher display name: `The Project Hub`
- Store product ID: `9MTFGCPQCQ86`
- Package family name: `TheProjectHub.SafeSpeak_tq1kt9e4wnq7e`

Before each Store build:

1. Confirm the package Identity Name, Publisher, and Publisher display name still match the assigned Partner Center product identity above.
2. Review and approve `installer/Assets/SafeSpeakIconMaster-v1.png`, then run `./installer/Generate-Assets.ps1` to refresh the MSIX tiles and multi-resolution executable icon. Partner Center listing artwork is prepared separately from these package assets.
3. Finish accessibility testing with Narrator, keyboard-only navigation, Windows High Contrast themes, 200% text scaling, and the complete first-run reader decision flow before making an accessibility conformance claim.
4. Keep the Store listing, privacy policy URL, support contact, age rating, screenshots, and `runFullTrust` justification current.
5. Decide whether the update will upload the generated `.msix` directly or add a `.msixupload` wrapper with symbols for improved Store crash analytics. Partner Center accepts both and currently recommends the upload wrapper.
6. Build a Store candidate. Microsoft Store versions require a non-zero major component and reserve the fourth component as zero:

```powershell
./installer/Build-Release.ps1 `
  -Architecture x64 `
  -PackageVersion 1.0.2.0 `
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

SafeSpeak is live, so it satisfies the first-publication prerequisite for Microsoft's GitHub Actions update path. That path currently supports free products, which matches SafeSpeak's release model.

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

`.github/workflows/store-publisher.yml` runs automatically only after the `Main release build` succeeds for a push to `main`. It checks out that exact successful commit, builds the Store version from `SafeSpeakStoreVersion`, and then waits at the protected environment. After the required reviewer approves, it uses `msstore apps get` to verify access to exactly `STORE_APP_ID`, uploads the verified bundle, and commits the update for Microsoft certification. Manual dispatch remains available: its defaults perform a read-only connection check, while `upload_draft` uploads with `--noCommit` unless `commit_submission` is deliberately selected. Do not remove the protected reviewer.

## Continuous packaging verification

`.github/workflows/development-build.yml` runs on pushes and pull requests targeting `develop`. It calls the same release entry point, runs both test suites, and uploads only an unsigned x64 portable ZIP and release report from `artifacts/development`. The artifact expires after seven days. This workflow has no Store credentials, protected environment, signing, release, or deployment step.

`.github/workflows/desktop-build.yml` runs the same release script and authoritative `Directory.Build.props` version on pull requests targeting `main`, pushes to `main`, and manual dispatches. GitHub Actions validates and packages x64 and ARM64 ZIP, MSI, MSIX, release reports, and the separate Stream Deck plug-in. A stable tag such as `v1.0.2.0` publishes a normal release; `v1.0.2.0-rc.1` publishes a prerelease for the same four-part package version. Every tagged build requires the two signing secrets and publishes only after both architecture reports prove valid executable, MSI, and MSIX signatures. `SHA256SUMS.txt` covers every downloadable artifact.

The branch and promotion rules are documented in [`docs/development-track.md`](../docs/development-track.md). A pull request from `develop` to `main` deliberately switches from the fast development artifact to the complete release-candidate matrix. A successful push build on `main` then triggers the separate protected Store publisher; `develop` cannot reach Partner Center.

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
