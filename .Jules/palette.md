## 2026-09-21 - Map ToolTip to HelpText for WPF Accessibility
**Learning:** In this WPF desktop application, interactive elements with an 'AutomationProperties.HelpText' often lack a corresponding visual tooltip, leaving sighted mouse users without the same contextual help that screen reader users receive.
**Action:** When adding or updating 'AutomationProperties.HelpText' on WPF UI elements, ensure that a corresponding 'ToolTip' property is also added (using the same text or binding) to bridge accessibility metadata with visual UX.
