# SafeSpeak mobile development tracks

SafeSpeak mobile uses one shared .NET MAUI project with independent Android and
iOS branch and CI boundaries. These builds are test foundations, not mobile
store release claims.

## Long-lived branches

| Platform | Development branch | Release-candidate branch | CI output |
| --- | --- | --- | --- |
| Android | `android/develop` | `android/main` | Installable test APK; protected manual signed AAB candidate |
| iOS | `ios/develop` | `ios/main` | Unsigned iOS Simulator `.app` ZIP; protected manual signed IPA candidate |

The desktop branches remain `develop` and `main`. Mobile workflows have no
Microsoft Partner Center, Google Play, App Store Connect, release, signing, or
deployment permissions. Likewise, the existing Windows workflows do not watch
mobile branches.

## Change flow

1. Shared moderation, normalized-event, connector-contract, and speech-contract
   changes land on desktop `develop` first.
2. Merge `develop` into `android/develop` and `ios/develop`.
3. Platform work stays on its platform development branch until its test build
   and accessibility checks pass.
4. Promote Android with a pull request from `android/develop` to `android/main`.
5. Promote iOS with a pull request from `ios/develop` to `ios/main`.
6. Never merge either mobile `main` branch directly into desktop `main`.

`android/main` and `ios/main` are release-candidate gates. They do not publish.
Signed device workflows are defined but cannot run until the corresponding store
account, protected environment, signing material, and human release approval
exist. They are manual-only, reject the wrong platform branch, and never publish
to a public production track. See `docs/store-submission-readiness.md`.

## Current test foundation

- Shared portable moderation and normalized `LivestreamEvent` contracts
- Provider identity preserved through normalization
- A platform-neutral `ISpeechOutput` contract
- Android and iOS system text-to-speech through .NET MAUI
- Disarmed-by-default test screen with explicit moderation before speech
- Offline simulator available without TikFinity
- Truthful roadmap entries for desktop relay, YouTube Live, Twitch, and direct
  TikTok LIVE access

The direct TikTok entry is intentionally marked **official platform access
required**. No connector may scrape, automate, or bypass a provider's approved
authentication and API terms.

## Local commands

The mobile SDK is pinned separately in `mobile/global.json`, so it does not
change the Windows application's .NET 8 pin.

```powershell
Set-Location mobile
dotnet workload install maui-android
$androidSdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$javaSdk = Join-Path $env:LOCALAPPDATA 'Android\jdk-17'
dotnet build ../src/SafeSpeak.Mobile/SafeSpeak.Mobile.csproj -t:InstallAndroidDependencies -f net10.0-android -p:SafeSpeakMobileTargetFrameworks=net10.0-android -p:AndroidSdkDirectory="$androidSdk" -p:JavaSdkDirectory="$javaSdk" -p:AcceptAndroidSdkLicenses=true
dotnet test ../tests/SafeSpeak.Mobile.Foundation.Tests/SafeSpeak.Mobile.Foundation.Tests.csproj -c Release
dotnet build ../src/SafeSpeak.Mobile/SafeSpeak.Mobile.csproj -c Release -f net10.0-android -p:SafeSpeakMobileTargetFrameworks=net10.0-android -p:AndroidPackageFormats=apk -p:AndroidSdkDirectory="$androidSdk" -p:JavaSdkDirectory="$javaSdk"
```

An iOS build requires macOS and Xcode. CI currently produces an unsigned
simulator build; a simulator ZIP cannot be installed on a physical iPhone.

## Connector graduation gate

A planned connector becomes available only after it implements
`ISourceConnector`, uses official authentication/API access, normalizes events,
passes payload/rate/cancellation/reconnection tests, sends every chat message
through shared moderation, never arms speech, and passes mobile accessibility
review. Tokens belong in the platform credential store, never source control.
