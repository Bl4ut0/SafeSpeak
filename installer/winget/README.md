# WinGet manifest output

This directory intentionally contains no release manifest until an MSIX has been published at a stable HTTPS URL.

Generate the three required manifests with:

```powershell
./installer/New-WingetManifests.ps1 `
  -PackageVersion 1.0.0 `
  -InstallerUrl "https://github.com/Bl4ut0/SafeSpeak/releases/download/v1.0.0/SafeSpeak_1.0.0.0_x64.msix" `
  -InstallerSha256 (Get-FileHash ./artifacts/SafeSpeak_1.0.0.0_x64.msix -Algorithm SHA256).Hash
```

Then run `winget validate --manifest installer/winget` before opening a pull request against `microsoft/winget-pkgs`.
