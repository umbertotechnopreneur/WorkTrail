# UI automation conversion register

Stable selectors are declared with `AutomationProperties.AutomationId`. The dedicated
[name dictionary](../WorkTrail.Core/Data/ui-automation-names.json) maps each selector to
an existing localization key. `UiLocalization.Apply` sets `AutomationProperties.Name`
in the selected application language and gives icon commands the same localized tooltip.

This register describes source conversion. It does not claim successful FlaUI runs or
live inspection of the installed app's automation tree.

| UI module | Converted source | Coverage | Runtime verification |
| --- | --- | --- | --- |
| Main window | `MainWindow.xaml` | 33 commands, including unopened menus; tracking and visibility names retain their state updates | Pending |
| Report export | `ReportExportWindow.xaml` | 60 selectors for navigation, dates, content, editable AI summaries, batch reports, a collapsible Excel preview, generation, opening and copying the file path | Pending |
| Main settings | `Controls/OptionsControl.xaml` | 40 controls, including screenshot notifications; labels follow the selected language | Pending |
| Data retention | `Controls/RetentionOperationsControl.xaml` | 18 controls; premium labels retain the existing premium suffix | Pending |
| Screenshot gallery | Header, details and timeline controls | 17 static commands, expanders and date/list selectors; existing per-image names remain dynamic | Pending |
| Sensors | `SensorsWindow.xaml`, `Controls/SensorOptionsControl.xaml` | 8 controls for navigation and sensor options | Pending |
| Maintenance and diagnostics | `Controls/OperationsControl.xaml` | 6 navigation and reset commands | Pending |
| Provider key setup | `Controls/ProviderKeySetupControl.xaml` | 10 controls; the password field keeps its provider-specific accessible name | Pending |
| Weekly hours editor | `Controls/WeeklyHoursEditor.xaml` and code-behind | 2 view commands; generated slots use `Schedule.Weekly.Slot.<day>.<index>` with localized day/time names | Pending |
| Operation progress | `OperationProgressDialogWindow.xaml` and code-behind | `OperationProgress.Cancel` has a directly localized name; native Close requests the same cancellation | Pending |

Other windows and operation sections have not yet been converted to this selector
catalog. Existing accessible names on those surfaces remain managed by their current
localization code. Device expanders and virtualized gallery items need live inspection
before their automation behavior can be marked verified.

## Selector and name rules

- Keep IDs in English and stable across language, theme, layout and control state changes.
- Do not include user names, file paths, API keys or localized captions in IDs.
- Register new static selectors in the name dictionary. Reuse an existing translation
  when it describes the same action; add a new translated key when it does not.
- State-dependent names continue to describe the current action. The dictionary supplies
  the initial name; tracking, key setup, premium retention and visibility controls update it.
- Resolve repeated reusable controls inside their owning window or component. Never select
  a virtualized item by its current row position.
- Inspect the running automation tree before writing an end-to-end assertion. An ID on a
  layout panel alone does not provide an interactive automation peer.

## Current validation

`UiAutomationContractTests` checks selector uniqueness on the converted static controls,
their dictionary coverage, resolution in every shipped language and selected-language
behavior. Existing localization tests and a Debug x64 UI build are also required.
These checks do not replace interaction tests on a Windows desktop.

On October 2, 2026, all 52 targeted Core checks and 12 export-localization checks passed.
The Debug x64 UI build completed with no warnings or errors. Formatting verification
also passed. The runtime-verification column remains pending until the relevant controls
have been inspected and exercised through Windows UI Automation.

The screenshot-notification setting and protocol actions were added after that validation.
The Debug x64 MSIX 1.0.2 compiled, passed package validation and was installed on
October 2, 2026. Installed UI and Core assemblies matched the signed package hashes;
formatting verification passed. The new tests and installed-app interaction checks
have not been run, including progress cancellation interactions. The runtime-verification
column therefore remains pending.
