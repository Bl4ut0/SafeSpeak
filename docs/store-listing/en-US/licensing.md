# SafeSpeak App Store licensing

## Price

SafeSpeak is offered as a free application. There is no purchase price for the
official binary, and the current release does not include in-app purchases.

Free pricing does not make SafeSpeak public domain or open source. SafeSpeak's
original source code, user interface, documentation, artwork, and binaries are
proprietary and remain copyrighted by Alex Mammen.

## Apple App Store setting

Use Apple's Standard End User License Agreement for the App Store binary. Do
not paste the repository's source-visible license into App Store Connect as a
Custom EULA without a separate legal review.

Apple's Standard EULA already treats applications as licensed rather than sold
and restricts transfer, redistribution, sublicensing, copying, modification,
reverse engineering, and derivative works, subject to its Usage Rules and
rights that applicable law does not allow a licensor to exclude.

The repository license separately controls the source code, original artwork,
documentation, direct binary downloads, and other SafeSpeak materials made
visible outside the App Store. Store-authorized installation, re-download,
updates, and device or family-sharing mechanisms are not treated as prohibited
redistribution.

## Plain-language statement for project pages

> SafeSpeak is free to download and use from authorized distribution channels.
> It is proprietary source-visible software, not open source. You may inspect
> the published source under the SafeSpeak license, but you may not copy, build,
> modify, repackage, mirror, or redistribute SafeSpeak or its original assets
> without prior written permission. Third-party components remain under their
> own licenses.

## Store checklist

- Price tier: Free
- In-app purchases: None in the current release
- App Store license agreement: Apple Standard EULA
- Copyright: `2026 Alex Mammen`
- Repository license: SafeSpeak Proprietary Source-Visible License 1.1
- Third-party notices: include the packaged `THIRD-PARTY-NOTICES.md` and
  component license files
- Do not describe SafeSpeak as open source, freeware, public domain, or freely
  redistributable

The product price and the software license are separate decisions. Changing the
price later does not automatically change the repository or binary license.

## Why source-visible rather than open source

An open-source license must grant meaningful rights to inspect, modify, and
redistribute the software. SafeSpeak currently does not grant modification or
redistribution rights, so describing it as open source would be inaccurate.

Source visibility still allows public security and privacy review while the
Owner controls official builds, branding, accessibility quality, and release
channels. A future release could use a different model—for example, an
open-source moderation/connector core with proprietary SafeSpeak branding,
artwork, and official applications—but that would be a deliberate new license
grant. It would not cancel the licenses attached to earlier releases.
