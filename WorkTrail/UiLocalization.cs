// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Applies the resolved application language to tagged WinUI presentation elements.</summary>
internal static class UiLocalization
{
    /// <summary>Gives an icon or visual-content command the same localized name and tooltip.</summary>
    /// <param name="element">The command or control exposed through UI Automation.</param>
    /// <param name="label">The localized description of its action.</param>
    public static void SetAccessibleLabel(DependencyObject element, string label)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        AutomationProperties.SetName(element, label);
        ToolTipService.SetToolTip(element, label);
    }

    /// <summary>Localizes one visual subtree without performing persistence or environment access.</summary>
    /// <param name="root">The surface and its declared or realized descendants.</param>
    /// <param name="strings">The resolved application language.</param>
    public static void Apply(DependencyObject root, LocalizationService strings)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(strings);

        Apply(root, strings, new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>Visits each element once, including controls declared in unopened menus.</summary>
    /// <param name="root">The next element to localize.</param>
    /// <param name="strings">The resolved application language.</param>
    /// <param name="visited">Elements already processed in this traversal.</param>
    private static void Apply(
        DependencyObject root,
        LocalizationService strings,
        HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root))
        {
            return;
        }

        if (root is FrameworkElement element)
        {
            element.Language = strings.Language;
            if (element.Tag is string key && !string.IsNullOrWhiteSpace(key))
            {
                ApplyElement(element, key, strings);
            }
        }

        // Automation names have their own map; visible captions keep their existing translation keys.
        var automationId = AutomationProperties.GetAutomationId(root);
        if (!string.IsNullOrWhiteSpace(automationId)
            && strings.TryTranslateAutomationName(automationId, out var accessibleName))
        {
            AutomationProperties.SetName(root, accessibleName);
            if (root is ButtonBase button && button.Content is not string)
            {
                ToolTipService.SetToolTip(button, accessibleName);
            }
        }

        // Declared children remain reachable here even while an options page is collapsed and absent
        // from the realized visual tree. This keeps first-open surfaces in the selected language.
        foreach (var child in DeclaredChildren(root))
        {
            Apply(child, strings, visited);
        }

        // Flyout containers have declared content but are not themselves visuals.
        var visualChildren = root is UIElement ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (var index = 0; index < visualChildren; index++)
        {
            Apply(VisualTreeHelper.GetChild(root, index), strings, visited);
        }
    }

    /// <summary>Includes logical children that may not yet exist in the realized visual tree.</summary>
    /// <param name="root">The control owning content, a flyout, or navigation commands.</param>
    private static IEnumerable<DependencyObject> DeclaredChildren(DependencyObject root)
    {
        if (root is Button button && button.Flyout is { } buttonFlyout)
        {
            yield return buttonFlyout;
        }

        if (root is NavigationView navigation)
        {
            foreach (var item in navigation.MenuItems.Concat(navigation.FooterMenuItems).OfType<DependencyObject>())
            {
                yield return item;
            }
        }

        if (root is CommandBar commandBar)
        {
            foreach (var command in commandBar.PrimaryCommands.Concat(commandBar.SecondaryCommands).OfType<DependencyObject>())
            {
                yield return command;
            }
        }

        switch (root)
        {
            case MenuFlyout menu:
                foreach (var item in menu.Items)
                {
                    yield return item;
                }
                break;
            case MenuFlyoutSubItem subMenu:
                foreach (var item in subMenu.Items)
                {
                    yield return item;
                }
                break;
            case Flyout flyout when flyout.Content is DependencyObject flyoutContent:
                yield return flyoutContent;
                break;
            case Expander expander:
                if (expander.Header is DependencyObject header)
                {
                    yield return header;
                }
                if (expander.Content is DependencyObject expandedContent)
                {
                    yield return expandedContent;
                }
                break;
            case Border border when border.Child is DependencyObject borderChild:
                yield return borderChild;
                break;
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    yield return child;
                }
                break;
            case UserControl userControl when userControl.Content is DependencyObject userContent:
                yield return userContent;
                break;
            case ContentControl contentControl when contentControl.Content is DependencyObject controlContent:
                yield return controlContent;
                break;
            case ContentPresenter presenter when presenter.Content is DependencyObject presenterContent:
                yield return presenterContent;
                break;
            case ItemsControl itemsControl:
                foreach (var child in itemsControl.Items.OfType<DependencyObject>())
                {
                    yield return child;
                }
                break;
        }
    }

    private static void ApplyElement(FrameworkElement element, string key, LocalizationService strings)
    {
        switch (element)
        {
            case Expander expander:
                SetIfTranslated(strings, key, value =>
                {
                    expander.Header = value;
                    AutomationProperties.SetName(expander, value);
                });
                break;
            case Slider slider:
                SetIfTranslated(strings, $"{key}.Header", value =>
                {
                    slider.Header = value;
                    AutomationProperties.SetName(slider, value);
                });
                break;
            case TextBlock textBlock:
                SetIfTranslated(strings, key, value => textBlock.Text = value);
                break;
            case Button button:
                ApplyButtonLabel(button, key, strings);
                break;
            case CheckBox checkBox:
                SetIfTranslated(strings, key, value =>
                {
                    checkBox.Content = value;
                    AutomationProperties.SetName(checkBox, value);
                });
                break;
            case ToggleButton toggleButton:
                ApplyButtonLabel(toggleButton, key, strings);
                break;
            case ToggleSwitch toggle:
                SetIfTranslated(strings, $"{key}.Header", value => toggle.Header = value);
                SetIfTranslated(strings, $"{key}.Off", value => toggle.OffContent = value);
                SetIfTranslated(strings, $"{key}.On", value => toggle.OnContent = value);
                SetIfTranslated(strings, $"{key}.Header", value => AutomationProperties.SetName(toggle, value));
                break;
            case ComboBox comboBox:
                SetIfTranslated(strings, $"{key}.Header", value =>
                {
                    comboBox.Header = value;
                    AutomationProperties.SetName(comboBox, value);
                });
                foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
                {
                    var option = item.Tag?.ToString();
                    if (!string.IsNullOrWhiteSpace(option))
                    {
                        SetIfTranslated(strings, $"{key}.{option}", value => item.Content = value);
                    }
                }
                break;
            case TextBox textBox:
                SetIfTranslated(strings, $"{key}.Header", value =>
                {
                    textBox.Header = value;
                    AutomationProperties.SetName(textBox, value);
                });
                SetIfTranslated(strings, $"{key}.Placeholder", value => textBox.PlaceholderText = value);
                break;
            case AutoSuggestBox autoSuggestBox:
                SetIfTranslated(strings, key, value =>
                {
                    autoSuggestBox.PlaceholderText = value;
                    AutomationProperties.SetName(autoSuggestBox, value);
                });
                break;
            case NumberBox numberBox:
                if (!SetIfTranslated(strings, $"{key}.Header", value =>
                    {
                        numberBox.Header = value;
                        AutomationProperties.SetName(numberBox, value);
                    }))
                {
                    SetIfTranslated(strings, key, value =>
                    {
                        numberBox.Header = value;
                        AutomationProperties.SetName(numberBox, value);
                    });
                }
                break;
            case PasswordBox passwordBox:
                SetIfTranslated(strings, $"{key}.Header", value =>
                {
                    passwordBox.Header = value;
                    AutomationProperties.SetName(passwordBox, value);
                });
                SetIfTranslated(strings, $"{key}.Placeholder", value => passwordBox.PlaceholderText = value);
                break;
            case CalendarDatePicker datePicker:
                SetIfTranslated(strings, $"{key}.Header", value =>
                {
                    datePicker.Header = value;
                    AutomationProperties.SetName(datePicker, value);
                });
                SetIfTranslated(strings, $"{key}.Placeholder", value => datePicker.PlaceholderText = value);
                break;
            case ToggleMenuFlyoutItem toggleMenuItem:
                SetIfTranslated(strings, key, value =>
                {
                    toggleMenuItem.Text = value;
                    AutomationProperties.SetName(toggleMenuItem, value);
                });
                break;
            case MenuFlyoutSubItem menuSubItem:
                SetIfTranslated(strings, key, value =>
                {
                    menuSubItem.Text = value;
                    AutomationProperties.SetName(menuSubItem, value);
                });
                break;
            case MenuFlyoutItem menuItem:
                SetIfTranslated(strings, key, value =>
                {
                    menuItem.Text = value;
                    AutomationProperties.SetName(menuItem, value);
                });
                break;
            case DatePicker datePicker:
                SetIfTranslated(strings, $"{key}.Header", value =>
                {
                    datePicker.Header = value;
                    AutomationProperties.SetName(datePicker, value);
                });
                break;
            case Thumb thumb:
                SetIfTranslated(strings, key, value => SetAccessibleLabel(thumb, value));
                break;
        }
    }

    private static void ApplyButtonLabel(ButtonBase button, string key, LocalizationService strings)
    {
        SetIfTranslated(strings, key, value =>
        {
            if (button.Content is string)
            {
                button.Content = value;
                AutomationProperties.SetName(button, value);
            }
            else
            {
                // Icon-only and visual-content commands retain their content; their label is exposed accessibly.
                SetAccessibleLabel(button, value);
            }
        });
    }

    private static bool SetIfTranslated(LocalizationService strings, string key, Action<string> setter)
    {
        if (strings.TryTranslate(key, out var value))
        {
            setter(value);
            return true;
        }

        return false;
    }
}
