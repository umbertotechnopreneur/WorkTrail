// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Collects a city from a supplied offline catalog without owning persistence or location detection.</summary>
public sealed partial class CityPickerControl : UserControl
{
    #region Fields

    private List<CityOption> _options = [];

    #endregion

    #region Implementation

    /// <summary>Publishes the selected catalog city.</summary>
    public event SelectionChangedEventHandler? SelectionChanged;

    #endregion

    #region Initialization

    /// <summary>Creates an empty reusable city selector.</summary>
    public CityPickerControl() => InitializeComponent();

    #endregion

    #region Implementation

    /// <summary>Gets the chosen identifier, or null until a catalog item is selected.</summary>
    public string? SelectedCityId => (CityComboBox.SelectedItem as CityOption)?.Id;

    /// <summary>Gets the chosen display name.</summary>
    public string? SelectedDisplayName => (CityComboBox.SelectedItem as CityOption)?.DisplayName;

    /// <summary>Displays the supplied catalog and an optional suggestion that the user can replace.</summary>
    public void ApplyCities(IReadOnlyList<WorldClockCitySummary> cities, LocalizationService strings, string? selectedCityId = null)
    {
        var cityOptions = cities.Select(city => new CityOption(city.Id, $"{city.Name} · {city.CountryCode}"));
        var displayNameComparer = StringComparer.Create(strings.Culture, ignoreCase: true);

        _options = cityOptions
            .OrderBy(city => city.DisplayName, displayNameComparer)
            .ToList();
        CityComboBox.PlaceholderText = strings.Translate("WorldClock.SearchCity");
        AutomationProperties.SetName(CityComboBox, CityComboBox.PlaceholderText);
        CityComboBox.ItemsSource = _options;
        CityComboBox.SelectedItem = _options.FirstOrDefault(city => city.Id == selectedCityId);
    }

    /// <summary>Removes an already-added city while keeping the rest of the catalog available.</summary>
    public void RemoveCity(string cityId)
    {
        _options.RemoveAll(city => city.Id == cityId);
        CityComboBox.SelectedItem = null;
        CityComboBox.ItemsSource = null;
        CityComboBox.ItemsSource = _options;
    }

    /// <summary>Moves keyboard focus to the searchable catalog.</summary>
    public void FocusPicker() => CityComboBox.Focus(FocusState.Programmatic);

    /// <summary>Publishes the city selected in the picker.</summary>
    private void CityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args) => SelectionChanged?.Invoke(this, args);

    /// <summary>Carries a selectable city's display text and catalog identity.</summary>
    private sealed record CityOption(string Id, string DisplayName);

    #endregion
}
