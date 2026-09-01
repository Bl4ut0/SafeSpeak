# SafeSpeak development track

SafeSpeak uses `develop` for ordinary implementation and test builds. The
default `main` branch remains the release-integration track.

## Branch and workflow boundaries

| Change | Workflow | Output | Publishing authority |
| --- | --- | --- | --- |
| Push to `develop` | Development build | Tested unsigned x64 portable ZIP and release report, retained for 7 days | None |
| Pull request targeting `develop` | Development build | Same development artifact | None |
| Push to `main` | Main release build | Tested x64 and ARM64 ZIP/MSIX packages and Stream Deck package, retained for 14 days | None |
| Pull request targeting `main` | Main release build | Same release-candidate artifacts | None |
| Manual Store workflow | Microsoft Store publisher | Store bundle; optional protected draft/commit stages | Protected `microsoft-store-production` environment only |

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
6. Store work remains a separate manual decision after the `main` candidate and
   human accessibility gates pass.

Do not add `push` or `pull_request` triggers to
`.github/workflows/store-publisher.yml`. Do not add Store secrets or a protected
environment to `.github/workflows/development-build.yml`.
