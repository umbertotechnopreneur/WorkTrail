// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WorkTrail.Controls;

/// <summary>Shows native language names with vector country flags while retaining canonical setting values.</summary>
public sealed class LanguagePicker : ComboBox
{
    private static readonly (string Code, string Name, string Country)[] Languages =
    [
        ("en-US", "English (United States)", "us"), ("it-IT", "Italiano", "it"),
        ("fr-FR", "Français", "fr"), ("de-DE", "Deutsch", "de"), ("es-ES", "Español", "es"),
        ("zh-Hans", "简体中文", "cn"), ("vi-VN", "Tiếng Việt", "vn"), ("ko-KR", "한국어", "kr"),
        ("pt-PT", "Português (Portugal)", "pt"), ("pt-BR", "Português (Brasil)", "br")
    ];

    /// <summary>Populates translated system choice and native names without changing the persisted language codes.</summary>
    public void Apply(string selectedLanguage, string systemLabel)
    {
        SelectedValuePath = "Tag";
        Items.Clear();
        AddChoice("system", systemLabel, new FontIcon { Glyph = "\uE770", FontSize = 18, Width = 28 });
        foreach (var (code, name, country) in Languages)
            AddChoice(code, name, new Image
            {
                Width = 28,
                Height = 18,
                Source = new SvgImageSource(new Uri($"ms-appx:///Assets/Languages/{country}.svg"))
            });
        SelectedValue = selectedLanguage;
    }

    private void AddChoice(string code, string name, FrameworkElement symbol)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        AutomationProperties.SetAccessibilityView(symbol, AccessibilityView.Raw);
        panel.Children.Add(symbol);
        panel.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var item = new ComboBoxItem { Tag = code, Content = panel };
        AutomationProperties.SetName(item, name);
        Items.Add(item);
    }
}
