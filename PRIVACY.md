# SafeSpeak Privacy Policy

Last updated: September 1, 2026

SafeSpeak is a local-first Windows accessibility application published by The Project Hub. It moderates livestream chat and converts approved content to speech.

## Data SafeSpeak processes

When you connect a supported local livestream tool, SafeSpeak may process livestream events such as viewer display names, usernames, chat messages, and interaction types. It also reads the local settings needed to operate, including moderation preferences, voice choices, and audio-device selections.

## Where processing occurs

Moderation and supported speech processing occur on your Windows device. SafeSpeak does not require a SafeSpeak account and does not send livestream messages, viewer identities, moderation decisions, settings, or audio to The Project Hub. SafeSpeak does not include advertising or developer-operated analytics or telemetry.

SafeSpeak connects to supported integrations on your own computer, including TikFinity through a loopback connection. Those integrations are separate products and are governed by their own privacy practices. If you choose to install an optional local neural-voice model, SafeSpeak downloads that model from the project’s published GitHub release source.

## Local storage and logs

SafeSpeak stores settings locally under `%LOCALAPPDATA%\SafeSpeak`.

Stream audit logging is disabled by default. If you explicitly enable it, SafeSpeak writes logs under `%LOCALAPPDATA%\SafeSpeak\Logs`. Those logs can contain livestream messages, viewer names, normalized or spoken text, moderation results, and related event details. The logs remain on your device unless you choose to copy or share them. You can stop future logging in SafeSpeak and delete existing logs from that local folder.

## Data retention and control

The Project Hub does not receive or retain SafeSpeak’s local settings, livestream content, or audit logs. You control local retention by changing SafeSpeak’s settings or deleting the SafeSpeak folder under `%LOCALAPPDATA%`.

## Children’s privacy

SafeSpeak is a general-purpose accessibility and moderation utility. It is not designed to collect personal information from children, and The Project Hub does not knowingly collect personal information through SafeSpeak.

## Security

SafeSpeak limits its local integration endpoints to the Windows loopback interface where supported. No software can guarantee that all harmful livestream content will be detected, so SafeSpeak should be used alongside platform moderation and human judgment.

## Changes to this policy

Material changes will be published in this repository with an updated revision date.

## Contact

For privacy questions or support, open an issue at <https://github.com/Bl4ut0/SafeSpeak/issues>.
