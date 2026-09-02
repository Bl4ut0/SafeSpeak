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
| Push to `main` | Main release build | Tested x64 and ARM64 ZIP/MSI/MSIX packages and Stream Deck package, retained for 14 days | None until the build succeeds |
| Pull request targeting `main` | Main release build | Same release-candidate artifacts | None |
| Successful Main release build after a push to `main` | Microsoft Store publisher | Store-specific x64/ARM64 bundle, Partner Center access check, and committed certification submission | Protected `microsoft-store-production` environment only |
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
6. Merge only after review and a successful build. The resulting successful
   push build on `main` triggers the protected Store workflow for the exact
   commit. Its required reviewer must approve the production deployment before
   the bundle is uploaded and committed for Microsoft certification.
7. Push a reviewed `v<version>-rc.N` tag for a GitHub prerelease or
   `v<version>` for a stable release. Tagged builds fail closed unless the
   trusted Authenticode PFX secrets are configured and every executable,
   MSI, and MSIX signature verifies.
8. Use manual Store dispatch when a read-only connection test or draft-only
   upload is needed without a `main` promotion.

The Store publisher deliberately has no direct `push` or `pull_request` trigger.
It accepts only a successful `Main release build` `workflow_run` for a push to
`main`, or an explicit manual dispatch. Do not add Store secrets or a protected
environment to `.github/workflows/development-build.yml`.
