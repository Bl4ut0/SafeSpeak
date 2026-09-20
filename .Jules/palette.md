## 2024-05-24 - Bridging a11y help text to visual tooltips
**Learning:** Using screen reader `AutomationProperties.HelpText` directly as a visual `ToolTip` ensures that the same rich contextual guidance provided to keyboard/screen reader users is available to sighted mouse users, unifying the accessibility metadata with the visual UX.
**Action:** Whenever adding `AutomationProperties.HelpText` to interactive WPF elements, always bind or mirror that value to the `ToolTip` property to benefit all users.
