# SafeSpeak

SafeSpeak is a Windows-first, accessibility-focused TTS safety application for livestreamers. It is being designed primarily for blind streamers who want chat text-to-speech without letting abusive, obfuscated, or multilingual bypass messages reach broadcast audio.

> [!IMPORTANT]
> SafeSpeak is currently in planning and early development. It is not ready for live use.

## Planned workflow

```text
TikTok LIVE -> TikFinity -> local WebSocket -> SafeSpeak moderation -> approved TTS -> Windows audio session
                                                      ^
                                                      |
                                              Stream Deck plug-in
```

SafeSpeak will listen to TikFinity's local event feed. TikFinity's own automatic TTS must be disabled so that only moderated messages are spoken.

## Project principles

- Accessible from first launch with NVDA, JAWS, and Narrator.
- Local-first moderation and speech with no required subscription.
- Fail closed when moderation, chat input, or audio routing fails.
- One Windows application running alongside TikFinity.
- An Elgato plug-in that exposes optional actions without modifying existing Stream Deck profiles.
- Private screen-reader feedback never enters the broadcast audio path.
- Rejected chat text and usernames are not retained by default.

## Repository layout

- `src/SafeSpeak.App` - accessible WPF desktop application
- `src/SafeSpeak.Core` - moderation, policies, queues, and connector contracts
- `tests/SafeSpeak.Core.Tests` - deterministic unit and regression tests
- `streamdeck` - Elgato Stream Deck plug-in
- `installer` - one-time Windows installer packaging
- `docs` - product, architecture, accessibility, and testing documentation
- `tools` - development and diagnostic utilities

See [the product plan](docs/product-plan.md) for the current scope and milestones.

## Safety notice

No automated moderation system can guarantee that every abusive message will be detected. SafeSpeak is intended to reduce the chance that unsafe chat reaches TTS; it does not replace TikTok moderation, trusted human moderators, or account-level safety controls.

## License

SafeSpeak is licensed under the [MIT License](LICENSE).

