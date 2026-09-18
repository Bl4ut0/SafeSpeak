## 2024-09-18 - Discovering ToolTips via Automation Properties
**Learning:** Sighted users were missing out on helpful context that was already defined for screen readers in `AutomationProperties.HelpText`. Adding a `ToolTip` to these elements makes the UI significantly more intuitive for mouse users without increasing screen clutter, bridging the gap between accessibility metadata and visual UX.
**Action:** Always check if a button has `AutomationProperties.HelpText`. If it does and doesn't have visible descriptive text, it's a prime candidate for a `ToolTip`.
