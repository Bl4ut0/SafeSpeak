## 2023-10-27 - Bridge Accessibility and Visual UX
**Learning:** In WPF applications heavily reliant on `AutomationProperties.HelpText` for screen reader guidance, sighted mouse users miss out on this rich contextual information unless it is explicitly duplicated to the visual `ToolTip` property.
**Action:** When adding or updating complex controls with `AutomationProperties.HelpText`, always ensure a corresponding `ToolTip` is added (using the same static string or data binding) so the UX benefits both keyboard/screen reader users and mouse users.
