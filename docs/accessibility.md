# Accessibility contract

SafeSpeak is intended to be independently operable by a fully blind streamer after initial Stream Deck button placement.

## Implemented foundation

- The first launch speaks a yes/no question through the local Windows voice before requiring navigation. Enter or Y enables fully blind spoken guidance, N declines it, and P enables partially sighted guidance.
- SafeSpeak spoken guidance reads only safe status and action summaries. It complements NVDA, JAWS, and Narrator rather than presenting itself as a replacement screen reader.
- Setup and dashboard UI use native WPF radio buttons, buttons, group boxes, and text controls.
- Tab order follows document order and all actions work from the keyboard.
- Controls have visible labels and UI Automation names or help text where extra context is needed.
- Connection, armed state, automatic playback, queue state, and English-only mode are available as text, not colour alone.
- Action and moderation summaries use an Automation live region. Rejected message content is never placed in that announcement.
- The emergency action has an explicit text label in both the app and Stream Deck.

## Manual acceptance checklist

Before a release, repeat the following with current NVDA, JAWS, and Narrator versions:

1. Delete or move `%LOCALAPPDATA%\SafeSpeak\settings.json` and start SafeSpeak.
2. Complete setup using only Tab, arrow keys, Space, and Enter.
3. Confirm every dashboard status and action has a meaningful spoken name.
4. Start and stop the TikFinity simulator and confirm connection changes are announced without moving focus.
5. Operate every action using only the keyboard.
6. Trigger a rejected fixture and confirm its unsafe text and username are not spoken.
7. Trigger emergency stop and confirm focus remains usable and the new state is announced.
8. With a sighted helper, add only the requested SafeSpeak actions to the user's existing Stream Deck profile.

Private status speech, audio-route descriptions, preference editing, high-contrast visual review, and real screen-reader acceptance testing remain open work. Basic accessibility must remain available in every mode; the first-run preference may add guidance but must never hide controls.
