# UI automation contracts

The launcher, reusable city selector and weather credential editor have stable semantic selectors registered in `WorkTrail.Core/Data/ui-automation-names.json`. The city selector is shared by Welcome and the add-city window. Each registry value is a localization key available in every shipped language.

Use `AutomationProperties.AutomationId` for selectors. Give icon-only buttons and toggles the same localized accessible name and tooltip. Do not select by translated display text or reuse retired tracking selectors.

`UiAutomationContractTests` compares the complete static-control selector set with the registry and checks name availability across supported languages. It does not prove native UI acceptance, focus order, screen-reader behavior or display scaling; those need a separate Windows UI check.
