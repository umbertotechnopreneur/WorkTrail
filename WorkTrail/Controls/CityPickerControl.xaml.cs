// SPDX-License-Identifier: MIT
using System.Globalization;
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
    private CompareInfo _searchComparer = CultureInfo.InvariantCulture.CompareInfo;
    private bool _inline;
    private CityOption? SelectedCity => (_inline ? CityList.SelectedItem : CityComboBox.SelectedItem) as CityOption;

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
    public string? SelectedCityId => SelectedCity?.Id;

    /// <summary>Gets the chosen display name.</summary>
    public string? SelectedDisplayName => SelectedCity?.DisplayName;

    /// <summary>Displays a persistent result list for the full-size city picker.</summary>
    public void UseInlineList()
    {
        _inline = true;
        CityComboBox.Visibility = Visibility.Collapsed;
        InlinePicker.Visibility = Visibility.Visible;
    }

    /// <summary>Displays the supplied catalog and an optional suggestion that the user can replace.</summary>
    public void ApplyCities(IReadOnlyList<WorldClockCitySummary> cities, LocalizationService strings, string? selectedCityId = null)
    {
        var cityOptions = cities.Select(city => new CityOption(city.Id, $"{city.Name} · {city.CountryCode}"));
        var displayNameComparer = StringComparer.Create(strings.Culture, ignoreCase: true);
        _searchComparer = strings.Culture.CompareInfo;

        _options = cityOptions
            .OrderBy(city => city.DisplayName, displayNameComparer)
            .ToList();
        var searchLabel = strings.Translate("WorldClock.SearchCity");
        if (_inline)
        {
            CitySearchBox.PlaceholderText = searchLabel;
            AutomationProperties.SetName(CitySearchBox, searchLabel);
            AutomationProperties.SetName(CityList, strings.Translate("WorldClock.PickerTitle"));
            FilterCities(selectedCityId);
            return;
        }

        CityComboBox.PlaceholderText = searchLabel;
        AutomationProperties.SetName(CityComboBox, CityComboBox.PlaceholderText);
        CityComboBox.ItemsSource = _options;
        CityComboBox.SelectedItem = _options.FirstOrDefault(city => city.Id == selectedCityId);
    }

    /// <summary>Removes an already-added city while keeping the rest of the catalog available.</summary>
    public void RemoveCity(string cityId)
    {
        _options.RemoveAll(city => city.Id == cityId);
        if (_inline)
        {
            CityList.SelectedItem = null;
            if (CitySearchBox.Text.Length == 0)
            {
                FilterCities(null);
            }
            else
            {
                CitySearchBox.Text = string.Empty;
            }
            return;
        }

        CityComboBox.SelectedItem = null;
        CityComboBox.ItemsSource = null;
        CityComboBox.ItemsSource = _options;
    }

    /// <summary>Moves keyboard focus to the searchable catalog.</summary>
    public void FocusPicker()
    {
        var target = _inline ? (Control)CitySearchBox : CityComboBox;
        target.Focus(FocusState.Programmatic);
    }

    private void CitySearchBox_TextChanged(object sender, TextChangedEventArgs args) => FilterCities(SelectedCityId);

    private void FilterCities(string? selectedCityId)
    {
        var query = CitySearchBox.Text.Trim();
        var results = _options.Where(city => query.Length == 0 || _searchComparer.IndexOf(
            city.DisplayName, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).ToArray();
        CityList.ItemsSource = results;
        CityList.SelectedItem = results.FirstOrDefault(city => city.Id == selectedCityId);
    }

    /// <summary>Publishes the city selected in the picker.</summary>
    private void CityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args) => SelectionChanged?.Invoke(this, args);

    /// <summary>Carries a selectable city's display text and catalog identity.</summary>
    private sealed record CityOption(string Id, string DisplayName);

    #endregion
}
