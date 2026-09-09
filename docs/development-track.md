# SafeSpeak development track

SafeSpeak uses `develop` for ordinary implementation and test builds. The
default `main` branch remains the release-integration track.

Android and iOS use separate branch families and workflows so mobile test
builds cannot trigger or publish a Windows package. See
[mobile development tracks](mobile-development-tracks.md).

## Branch and workflow boundaries

| Change | Workflow | Output | Publishing authority |
| --- | --- | --- | --- |
| Push to `develop` | Development build | Tested unsigned x64 portable ZIP and release report, retained for 7 days | None |
| Pull request targeting `develop` | Development build | Same development artifact | None |
| Push to `main` | Main release build | Signed x64 and ARM64 ZIP/MSI/MSIX packages, Stream Deck package, permanent GitHub Release, Store bundle, and committed certification submission | GitHub Releases and `microsoft-store-production` after every required build succeeds |
| Pull request targeting `main` | Main release build | Same release-candidate artifacts | None |
| Push `v<version>` or `v<version>-rc.N` | Main release build | Signed permanent GitHub release or prerelease with verified ZIP/MSI/MSIX packages, Stream Deck package, reports, and SHA-256 checksums | GitHub Releases only; signing secrets required |
| Manual Store workflow | Microsoft Store publisher | Store bundle; protected read-only connection check; optional protected draft/commit stages | Protected `microsoft-store-production` environment only |
| Push/PR to `android/develop` or `android/main` | Android test build | Test APK, retained for 7 or 14 days | None |
| Push/PR to `ios/develop` or `ios/main` | iOS simulator test build | Unsigned Simulator ZIP, retained for 7 or 14 days | None |

The development workflow has read-only repository permissions. It contains no
Store identity, credential, signing, release, deployment, or Partner Center
step. It calls the same authoritative release entry point as the main workflow,
but requests only `x64` and `Zip` and writes into `artifacts/development`.

## Normal development flow

1. Branch from the current `develop` branch.
2. Implement and test locally.
3. Open a pull request targeting `develop`; the Development build must pass.
4. Merge into `develop` and use its seven-day artifact for manual testing.
5. When a release candidate is ready, open a pull request from `develop` to
   `main`. That pull request deliberately runs the full Main release build.
6. Merge only after review and a successful build. The push to `main` signs and
   packages both architectures, creates `v<version>` and its GitHub Release,
   builds the Store bundle, and commits it for Microsoft certification. GitHub
   Actions sends the normal failure notification if any stage fails.
7. If automatic version or release-note inference cannot be used, manually run
   **Main release build** on `main`, select **deploy release**, and answer the
   version and release-details questions. The supplied version must match both
   version properties in `Directory.Build.props`.
8. Push a reviewed `v<version>-rc.N` tag only when a separate prerelease is
   needed. Tagged builds fail closed unless every public package is signed.
9. Use manual Store dispatch when a read-only connection test or draft-only
   upload is needed without a `main` promotion.

The standalone Store publisher has only an explicit manual trigger. Automatic
Store publishing runs inside the Main release build so it uses the bundle made
and retained by that same verified run. Do not add Store secrets or a protected
environment to `.github/workflows/development-build.yml`.
