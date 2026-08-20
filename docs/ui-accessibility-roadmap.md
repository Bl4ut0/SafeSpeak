# UI and accessibility roadmap

## Design direction

SafeSpeak should feel calm, legible, and operational under pressure. The visual hierarchy is: global safety controls first, live status second, task-specific settings third. Accessibility information must be the same information sighted users receive, expressed through text and UI Automation rather than through a separate reduced interface.

## Completed in this pass

- Responsive four-tab dashboard with non-overlapping cards and scrollable settings.
- Large controls and typography, clear section headings, reduced decorative noise, and readable text status.
- Persistent arm, emergency stop, skip, and private-status controls.
- Explicit tab order, access keys, visible three-pixel keyboard focus, Automation names/help, and live regions.
- Dynamic Windows system brushes for High Contrast compatibility.
- Spoken and visible two-answer reader confirmation with Y/N shortcuts and mismatch restart.
- Safe feed properties that hide rejected text, unsafe names, and exact hostile rule matches from Narrator.

## Next design increments

### P0 — release accessibility

1. Run task-based sessions with a fully blind streamer: connect, arm, review, play, skip, emergency stop, change voice, and change reader setting.
2. Test Narrator, NVDA, and JAWS at 100%, 200%, and 400% text/display scaling.
3. Add automated UIA checks for focus order, unique names, enabled state, tab selection, and live-region output.
4. Add keyboard-accessible queue item actions and a deliberately redacted detail/preview view.
5. Verify focus recovery after dialogs, connection failures, downloads, and emergency stop.

### P1 — visual quality and confidence

1. Add polished empty, loading, connected, reconnecting, offline, queue-full, and download states.
2. Add an audio route test, endpoint diagnostics, and a meter with an equivalent written value.
3. Add consistent icon assets with adjacent text labels; never use icons or colour as the only meaning.
4. Introduce compact/comfortable density options while preserving minimum target sizes.
5. Produce final Store iconography and screenshot layouts after the interaction model stabilizes.

### P2 — advanced usability

1. Add a guided first-stream checklist that can be skipped and reopened.
2. Add searchable settings and command discovery for large Stream Deck action sets.
3. Localize visible text, access keys, spoken prompts, and Store metadata.
4. Add a privacy-safe diagnostics page with copy/export actions and no hostile chat content.

## Acceptance rule

A UI change is complete only when it works with mouse, keyboard alone, Windows High Contrast, integrated SafeSpeak speech on/off, and at least Narrator. Visual polish cannot replace accessible names, focus, written state, or predictable navigation.
