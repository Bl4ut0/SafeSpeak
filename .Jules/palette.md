## 2024-09-17 - Add ARIA Labels to ComboBox Items

**Learning:** When using DataTemplates for ComboBox items, if the display text is different from what screen readers should read or if additional context like descriptions should be provided to screen readers, `AutomationProperties.Name` and/or `AutomationProperties.HelpText` are crucial.

**Action:** Ensured `AutomationProperties.HelpText` is used where `ToolTip` provides additional contextual information in `ComboBox.ItemTemplate`. This makes sure screen reader users also get the description information.
