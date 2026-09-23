## 2024-05-15 - [MainWindow.xaml ToolTip accessibility fixes]
**Learning:** Interactive elements with an `AutomationProperties.HelpText` should also have a corresponding `ToolTip` property mapped to the same text or binding. This bridges accessibility metadata with visual UX for sighted mouse users.
**Action:** When updating WPF XAML UI elements, I will ensure these attributes remain synchronized.
