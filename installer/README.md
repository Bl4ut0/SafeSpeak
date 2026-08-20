# SafeSpeak desktop packaging and Microsoft Store readiness

SafeSpeak has one release entry point: `installer/Build-Release.ps1`. It restores the pinned .NET SDK dependencies, runs the test suite, publishes a self-contained WPF application, creates the requested artifacts, and verifies the structure of the generated MSIX by unpacking it again.

## Prerequisites

- Windows 10 version 2004 (build 19041) or later.
- The .NET SDK selected by `global.json`.
- Windows 10 or Windows 11 SDK App Packaging tools (`makeappx.exe`) when building MSIX.
- A trusted code-signing certificate only when producing a sideloadable signed MSIX. Partner Center identity values and signing credentials are not stored in this repository.

The portable ZIP is self-contained. Its users do not need to install .NET separately.

## Build verified release artifacts

From the repository root:

```powershell
./installer/Build-Release.ps1 -Architecture x64 -PackageVersion 0.1.0.0 -Format Both
```

The command creates:

- `artifacts/SafeSpeak-0.1.0.0-win-x64/`: the expanded self-contained desktop application;
- `artifacts/SafeSpeak-0.1.0.0-win-x64.zip`: portable desktop distribution;
- `artifacts/SafeSpeak_0.1.0.0_x64.msix`: packaged desktop distribution;
- `artifacts/SafeSpeak-0.1.0.0-win-x64.release.json`: file sizes, SHA-256 hashes, runtime, and signature status.

After building, `Launch-SafeSpeak.bat` starts the newest verified x64 application under `artifacts`. It no longer starts an arbitrary, potentially stale executable from the ignored `dist` directory.

Use `-Format Zip` on a computer without the Windows SDK. Use `-SkipTests` only after tests have already passed in the same clean source revision. `-KeepStaging` preserves the generated MSIX layout for diagnosis.

The publish profiles deliberately produce a multi-file, self-contained application. WPF, KokoroSharp, ONNX Runtime, NAudio, voice embeddings, and other native/runtime files are therefore included explicitly. Trimming and single-file bundling are disabled because both can hide missing native assets until runtime. The roughly 326 MB Kokoro model itself is installed only after an explicit in-app request and is not embedded in the base ZIP/MSIX.

## Signed test package

The repository default publisher (`CN=SafeSpeak`) is a development placeholder. To create a signed package, the manifest publisher must exactly match the subject of the certificate:

```powershell
./installer/Build-Release.ps1 `
  -Architecture x64 `
  -PackageVersion 0.1.0.0 `
  -Format Msix `
  -Publisher "CN=Exact certificate subject" `
  -CertificateThumbprint "CERTIFICATE_THUMBPRINT"
```

The script signs through the current user's certificate store and verifies the signature with `signtool`. It does not create, import, or export certificates. An unsigned package can be structurally tested and submitted to a signing pipeline, but Windows will not accept it for ordinary sideload installation.

## Microsoft Store submission

Before the first Store build:

1. Reserve the SafeSpeak product name in Partner Center.
2. Copy the package Identity Name, Publisher, and Publisher display name from Partner Center. These values are assigned by Microsoft and must not be guessed.
3. Replace the generated placeholder Store artwork in `installer/Assets` with final branded production artwork at all required Store scales.
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

## Continuous packaging verification

`.github/workflows/desktop-build.yml` runs the same release script on pull requests, pushes to `main`, and manual dispatches. GitHub Actions also validates and packages the separate Stream Deck plug-in, then uploads the ZIP, unsigned MSIX, plug-in installer, and release report for inspection. The workflow intentionally does not publish or sign a public release because those actions require protected project credentials and an explicit release decision.

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

- Local speech/AI models belong under SafeSpeak's application-data model directory and are not embedded in the base Store package unless their licenses and package-size impact have been reviewed.
- A Stream Deck plug-in is distributed separately. Installing an MSIX must not silently modify an existing Stream Deck profile.
- A virtual audio cable remains optional; SafeSpeak must continue to launch and provide single-output audio without it.

## Microsoft references

- [Generate an MSIX package with command-line tools](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-manual-conversion)
- [Microsoft Store MSIX package and version requirements](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [Full-trust capability declarations](https://learn.microsoft.com/windows/apps/package-and-deploy/app-capability-declarations)
- [Upload MSIX packages in Partner Center](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/upload-app-packages)
