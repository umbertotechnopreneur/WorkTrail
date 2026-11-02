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
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Builds consistent navigation and per-window commands for desktop widgets.</summary>
internal static class AstronomyWindowMenu
{
    /// <summary>Creates a navigation menu from the canonical desktop-view catalog.</summary>
    /// <param name="getStrings">Provides the current localization service.</param>
    /// <param name="openWindow">Opens or activates the requested desktop view.</param>
    /// <param name="currentWindowKey">Identifies the view omitted from navigation.</param>
    /// <returns>The localized navigation menu.</returns>
    internal static MenuFlyout Create(
        Func<LocalizationService> getStrings,
        Action<string> openWindow,
        string? currentWindowKey = null)
    {
        ArgumentNullException.ThrowIfNull(getStrings);
        ArgumentNullException.ThrowIfNull(openWindow);
        var menu = CreateMenu();
        AddViews(menu.Items, openWindow, currentWindowKey);
        ApplyLabels(menu.Items, getStrings());
        menu.Opening += (_, _) => ApplyLabels(menu.Items, getStrings());
        return menu;
    }

    /// <summary>Creates the shared widget menu with navigation and preferences for its owning window only.</summary>
    /// <param name="getStrings">Provides the current localization service.</param>
    /// <param name="openWindow">Opens or activates the requested desktop view.</param>
    /// <param name="currentWindowKey">Identifies the owning view and omits it from navigation.</param>
    /// <param name="showMainWindow">Reveals and activates the main WorkTrail window.</param>
    /// <param name="getAppearance">Reads the owning window's committed preferences.</param>
    /// <param name="setAppearance">Persists and publishes requested preferences through the application.</param>
    /// <param name="showAppSettings">Reveals shared application settings.</param>
    /// <param name="closeWindow">Closes only the owning window when supplied.</param>
    /// <returns>The localized widget menu, which can also host view-specific commands.</returns>
    internal static MenuFlyout Create(
        Func<LocalizationService> getStrings,
        Action<string> openWindow,
        string currentWindowKey,
        Action showMainWindow,
        Func<WindowAppearance> getAppearance,
        Func<WindowAppearance, Task<bool>> setAppearance,
        Action showAppSettings,
        Action? closeWindow = null)
    {
        ArgumentNullException.ThrowIfNull(getStrings);
        ArgumentNullException.ThrowIfNull(openWindow);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentWindowKey);
        ArgumentNullException.ThrowIfNull(showMainWindow);
        ArgumentNullException.ThrowIfNull(getAppearance);
        ArgumentNullException.ThrowIfNull(setAppearance);
        ArgumentNullException.ThrowIfNull(showAppSettings);

        var menu = CreateMenu();
        menu.Items.Add(Command("Tray.ShowMainWindow", "\uE80F", showMainWindow));
        var views = Submenu("Window.OpenView", "\uE8A7");
        AddViews(views.Items, openWindow, currentWindowKey);
        menu.Items.Add(views);
        menu.Items.Add(new MenuFlyoutSeparator());

        var options = Submenu("Window.Options", "\uE713");
        var taskbar = new ToggleMenuFlyoutItem { Tag = "Window.ShowInTaskbar", Icon = Icon("\uE737") };
        var topmost = new ToggleMenuFlyoutItem { Tag = "Window.AlwaysOnTop", Icon = Icon("\uE718") };
        var opacity = Submenu("Window.Opacity", "\uE790");
        var presets = new Dictionary<int, ToggleMenuFlyoutItem>();
        var saving = false;
        taskbar.Click += async (_, _) => await CommitAsync(value => value with { ShowInTaskbar = taskbar.IsChecked });
        topmost.Click += async (_, _) => await CommitAsync(value => value with { AlwaysOnTop = topmost.IsChecked });
        foreach (var percent in new[] { 25, 50, 75, 100 })
        {
            var item = new ToggleMenuFlyoutItem { Text = $"{percent}%" };
            UiLocalization.SetAccessibleLabel(item, item.Text);
            item.Click += async (_, _) => await CommitAsync(value => value with { OpacityPercent = percent });
            presets.Add(percent, item);
            opacity.Items.Add(item);
        }
        options.Items.Add(taskbar);
        options.Items.Add(topmost);
        options.Items.Add(opacity);
        menu.Items.Add(options);
        menu.Items.Add(Command("Window.AppSettings", "\uE713", showAppSettings));

        if (closeWindow is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Command("Window.Close", "\uE8BB", closeWindow));
        }

        Refresh();
        menu.Opening += (_, _) => Refresh();
        return menu;

        // Preferences are committed by the host; failed mutations restore the last committed checks.
        async Task CommitAsync(Func<WindowAppearance, WindowAppearance> change)
        {
            if (saving)
            {
                Refresh();
                return;
            }
            saving = true;
            options.IsEnabled = false;
            try
            {
                _ = await setAppearance(change(getAppearance()));
            }
            catch (Exception exception)
            {
                ApplicationErrorService.Report(exception, "WindowMenuAppearance", getStrings().Language);
            }
            finally
            {
                saving = false;
                Refresh();
            }
        }

        void Refresh()
        {
            ApplyLabels(menu.Items, getStrings());
            var appearance = getAppearance();
            taskbar.IsChecked = appearance.ShowInTaskbar;
            topmost.IsChecked = appearance.AlwaysOnTop;
            foreach (var preset in presets)
            {
                preset.Value.IsChecked = preset.Key == appearance.OpacityPercent;
            }
            options.IsEnabled = !saving;
        }
    }

    private static MenuFlyout CreateMenu()
    {
        var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 260d));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
        return new MenuFlyout
        {
            ShouldConstrainToRootBounds = false,
            SystemBackdrop = new DesktopAcrylicBackdrop(),
            MenuFlyoutPresenterStyle = presenterStyle
        };
    }

    private static void AddViews(IList<MenuFlyoutItemBase> items, Action<string> openWindow, string? currentWindowKey)
    {
        foreach (var view in WindowViewCatalog.All.Where(view => view.WindowKey != currentWindowKey))
        {
            items.Add(Command(view.LabelKey, view.Glyph, () => openWindow(view.WindowKey)));
        }
    }

    private static MenuFlyoutItem Command(string labelKey, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Tag = labelKey, Icon = Icon(glyph) };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuFlyoutSubItem Submenu(string labelKey, string glyph) => new() { Tag = labelKey, Icon = Icon(glyph) };

    private static FontIcon Icon(string glyph) => new() { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons") };

    private static void ApplyLabels(IEnumerable<MenuFlyoutItemBase> items, LocalizationService strings)
    {
        foreach (var item in items)
        {
            if (item.Tag is string labelKey)
            {
                var label = strings.Translate(labelKey);
                switch (item)
                {
                    case ToggleMenuFlyoutItem toggle: toggle.Text = label; break;
                    case MenuFlyoutItem command: command.Text = label; break;
                    case MenuFlyoutSubItem submenu: submenu.Text = label; break;
                }
                UiLocalization.SetAccessibleLabel(item, label);
            }
            if (item is MenuFlyoutSubItem child)
            {
                ApplyLabels(child.Items, strings);
            }
        }
    }
}
