# SafeSpeak Release Candidate Rules & Standards

These rules are mandatory for all developers, automated pipelines, and AI assistants preparing release candidates or pushing releases to `main`.

---

## 1. Opening Page & Narrator Configuration Rule (MANDATORY)

Every release candidate must update the opening setup update prompt dialog so that existing users are informed of what has changed upon first launch of the updated version.

### The Problem This Solves
If a release updates features, UI layout, or accessibility options without updating the setup guide version or release highlights:
- Existing users will never be informed of the new features.
- Screen reader users and users with spoken guidance enabled will receive stale or missing audio announcements.
- Sighted and blind users will have inconsistent experiences.

### Single Source of Truth: `ReleaseUpdateInfo.cs`
All opening dialog content and narrator speech must originate from [`src/SafeSpeak.Core/Models/ReleaseUpdateInfo.cs`](../src/SafeSpeak.Core/Models/ReleaseUpdateInfo.cs):
1. **`CurrentGuideVersion`**: A monotonically increasing integer. When incremented, SafeSpeak's `ShouldPromptSetupUpdate` condition becomes true for all users whose `LastAcknowledgedSetupVersion < CurrentGuideVersion`.
2. **`CurrentHighlights`**: A list of concise, user-facing bullet points describing what is new or changed in this version.
3. **`GetAnnouncementText()`**: Generates the complete spoken announcement for SafeSpeak's built-in narrator.
4. **`GetHelpText()`**: Generates the accessibility HelpText for external screen readers (Windows Narrator, NVDA, JAWS).

> [!CAUTION]
> Never hardcode release highlights or announcement strings in `SetupUpdatePromptDialog.xaml` or `SetupUpdatePromptDialog.xaml.cs`. They must always be derived dynamically from `ReleaseUpdateInfo`.

---

## 2. Release Candidate Preparation Workflow

When preparing a new release candidate (e.g. `1.0.10.0` or `1.1.0.0`):

### Option A: Automated Release Script (Recommended)
Run the release preparation script from the repository root:
```powershell
./installer/Update-ReleaseCandidate.ps1 `
  -PackageVersion 1.0.10.0 `
  -Highlights @(
    "Streaming Platforms: Added Discord voice activity connector.",
    "Navigation and Speech: Optimized narrator speech latency.",
    "Fixes: Fixed audio output device fallback when unplugging headphones."
  )
```

This script automatically:
1. Validates the 4-part version number (ensuring the final digit is `0` for Store compliance).
2. Updates `<SafeSpeakVersion>` and `<SafeSpeakStoreVersion>` in `Directory.Build.props`.
3. Increments `CurrentGuideVersion` in `ReleaseUpdateInfo.cs` (which automatically updates `AppSettings.CurrentSetupGuideVersion`).
4. Updates `CurrentHighlights` in `ReleaseUpdateInfo.cs`.
5. Updates version references in `installer/README.md`.
6. Updates contract test version assertions in `ReleaseEntryPointContractTests.cs`.

### Option B: Manual Synchronization Checklist
If updating manually, all of the following files must be updated in tandem:
1. `Directory.Build.props`: Update `<SafeSpeakVersion>` and `<SafeSpeakStoreVersion>`.
2. `src/SafeSpeak.Core/Models/ReleaseUpdateInfo.cs`: Increment `CurrentGuideVersion` and write new `CurrentHighlights`.
3. `installer/README.md`: Update version flags and release tag examples.
4. `tests/SafeSpeak.App.Contracts.Tests/ReleaseEntryPointContractTests.cs`: Update `<SafeSpeakStoreVersion>` and workflow assertions.

---

## 3. Pre-Release Quality Gates

Before merging a release candidate into `main`:
1. Run the full test suite:
   ```powershell
   dotnet test SafeSpeak.sln
   ```
   Both `SafeSpeak.Core.Tests` and `SafeSpeak.App.Contracts.Tests` must pass with 0 failures.
2. Confirm that `ReleaseCandidate_RequiresAuthoritativeOpeningPageAndNarratorConfiguration` passes.
3. Push changes to `develop` first, verify the GitHub Actions development build passes, and then merge to `main`.
