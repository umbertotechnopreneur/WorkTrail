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
    /// <summary>Creates a localized menu with an optional action that closes only its owning window.</summary>
    internal static MenuFlyout Create(
        Func<LocalizationService> getStrings,
        Action<string> openWindow,
        Action? closeWindow = null)
    {
        ArgumentNullException.ThrowIfNull(getStrings);
        ArgumentNullException.ThrowIfNull(openWindow);
        var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 320d));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));
        var menu = new MenuFlyout
        {
            ShouldConstrainToRootBounds = false,
            SystemBackdrop = new DesktopAcrylicBackdrop(),
            MenuFlyoutPresenterStyle = presenterStyle
        };

        AddItem("Celestial.Sky.Title", "\uE9CE", () => openWindow(WindowStateKeys.LocalSky), Color.FromArgb(255, 173, 124, 245));
        AddItem("Celestial.Agenda.Title", "\uE787", () => openWindow(WindowStateKeys.AstronomyAgenda), Color.FromArgb(255, 125, 159, 248));
        AddItem("Celestial.Map.Title", "\uE774", () => openWindow(WindowStateKeys.CelestialMap), Color.FromArgb(255, 219, 141, 114));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddItem("WorldClock.Map.MenuLabel", "\uE707", () => openWindow(WindowStateKeys.WorldMap), Color.FromArgb(255, 113, 203, 183));
        if (closeWindow is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            AddItem("Window.Close", "\uE8BB", closeWindow);
        }

        ApplyLanguage();
        menu.Opening += (_, _) => ApplyLanguage();
        return menu;

        void AddItem(string textKey, string glyph, Action action, Color? color = null)
        {
            var icon = new FontIcon { Glyph = glyph };
            if (color is { } foreground)
            {
                icon.Foreground = new SolidColorBrush(foreground);
            }

            var item = new MenuFlyoutItem { Tag = textKey, Icon = icon, MinWidth = 320 };
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
        }
    }
}
