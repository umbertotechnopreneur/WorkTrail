// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Services;
using Windows.UI;

namespace WorkTrail.Controls;

/// <summary>Builds the shared navigation menu for the clocks and astronomy windows.</summary>
internal static class AstronomyWindowMenu
{
    /// <summary>Creates the navigation-only form of the shared astronomy menu.</summary>
    /// <param name="getStrings">Provides the current localization service.</param>
    /// <param name="openWindow">Opens the requested astronomy window.</param>
    /// <param name="closeWindow">Closes only the owning window when supplied.</param>
    /// <returns>The configured shared navigation menu.</returns>
    /// <exception cref="ArgumentNullException">A required callback is null.</exception>
    internal static MenuFlyout Create(
        Func<LocalizationService> getStrings,
        Action<string> openWindow,
        Action? closeWindow = null) =>
        Create(getStrings, openWindow, null, null, null, closeWindow);

    /// <summary>Creates a localized menu for navigating and controlling its owning window.</summary>
    /// <param name="getStrings">Provides the current localization service.</param>
    /// <param name="openWindow">Opens the requested astronomy window.</param>
    /// <param name="showMainWindow">Reveals and activates the main WorkTrail window.</param>
    /// <param name="isShownInTaskbar">Gets whether the owning window is currently shown in the taskbar.</param>
    /// <param name="setShownInTaskbar">Persists a requested taskbar visibility change.</param>
    /// <param name="closeWindow">Closes only the owning window when supplied.</param>
    /// <returns>The configured shared context menu.</returns>
    /// <exception cref="ArgumentNullException">A required callback is null.</exception>
    /// <exception cref="ArgumentException">Only some of the window-control callbacks were supplied.</exception>
    internal static MenuFlyout Create(
        Func<LocalizationService> getStrings,
        Action<string> openWindow,
        Action? showMainWindow,
        Func<bool>? isShownInTaskbar,
        Func<bool, Task<bool>>? setShownInTaskbar,
        Action? closeWindow = null)
    {
        ArgumentNullException.ThrowIfNull(getStrings);
        ArgumentNullException.ThrowIfNull(openWindow);
        var hasWindowControls = showMainWindow is not null;
        if (hasWindowControls != (isShownInTaskbar is not null)
            || hasWindowControls != (setShownInTaskbar is not null))
        {
            throw new ArgumentException("Window controls must provide all taskbar callbacks together.");
        }

        var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 320d));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));
        var menu = new MenuFlyout
        {
            ShouldConstrainToRootBounds = false,
            SystemBackdrop = new DesktopAcrylicBackdrop(),
            MenuFlyoutPresenterStyle = presenterStyle
        };

        if (showMainWindow is not null)
        {
            AddItem("Tray.ShowMainWindow", "\uE80F", showMainWindow);
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        AddItem("Celestial.Sky.Title", "\uE9CE", () => openWindow(WindowStateKeys.LocalSky), Color.FromArgb(255, 173, 124, 245));
        AddItem("Celestial.Agenda.Title", "\uE787", () => openWindow(WindowStateKeys.AstronomyAgenda), Color.FromArgb(255, 125, 159, 248));
        AddItem("Celestial.Map.Title", "\uE774", () => openWindow(WindowStateKeys.CelestialMap), Color.FromArgb(255, 219, 141, 114));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddItem("WorldClock.Map.MenuLabel", "\uE707", () => openWindow(WindowStateKeys.WorldMap), Color.FromArgb(255, 113, 203, 183));
        ToggleMenuFlyoutItem? taskbarItem = null;
        if (isShownInTaskbar is not null && setShownInTaskbar is { } persistTaskbarVisibility)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var taskbarToggle = new ToggleMenuFlyoutItem
            {
                Tag = "Options.Window.ShowInTaskbar.Header",
                MinWidth = 320
            };
            taskbarToggle.Click += async (_, _) =>
            {
                var requestedValue = taskbarToggle.IsChecked;
                taskbarToggle.IsEnabled = false;
                try
                {
                    if (!await persistTaskbarVisibility(requestedValue))
                    {
                        taskbarToggle.IsChecked = !requestedValue;
                    }
                }
                finally
                {
                    taskbarToggle.IsEnabled = true;
                }
            };
            taskbarItem = taskbarToggle;
            menu.Items.Add(taskbarToggle);
        }

        if (closeWindow is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            AddItem("Window.Close", string.Empty, closeWindow);
        }

        ApplyLanguage();
        menu.Opening += (_, _) => ApplyLanguage();
        return menu;

        // textKey identifies the localized item label.
        // glyph supplies the optional Fluent icon shown beside the label.
        // action runs when the user selects the item.
        // color optionally accents the supplied glyph.
        void AddItem(string textKey, string glyph, Action action, Color? color = null)
        {
            var item = new MenuFlyoutItem { Tag = textKey, MinWidth = 320 };
            if (!string.IsNullOrEmpty(glyph))
            {
                var icon = new FontIcon { Glyph = glyph };
                if (color is { } foreground)
                {
                    icon.Foreground = new SolidColorBrush(foreground);
                }

                item.Icon = icon;
            }

            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        void ApplyLanguage()
        {
            var strings = getStrings();
            foreach (var item in menu.Items.OfType<MenuFlyoutItem>())
            {
                item.Text = strings.Translate((string)item.Tag);
                AutomationProperties.SetName(item, item.Text);
            }

            if (taskbarItem is { } taskbarToggle && isShownInTaskbar is { } readTaskbarVisibility)
            {
                taskbarToggle.Text = strings.Translate((string)taskbarToggle.Tag);
                taskbarToggle.IsChecked = readTaskbarVisibility();
                AutomationProperties.SetName(taskbarToggle, taskbarToggle.Text);
            }
        }
    }
}
