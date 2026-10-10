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


using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using WorkTrail.Application;
using WorkTrail.Controls;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Hosts three independently restorable celestial surfaces with the shared disappearing title bar.</summary>
internal sealed partial class CelestialWindow : Window
{
    private readonly IWorkTrailApplication _application;
    private readonly MicaDialogService _dialogs;
    private readonly AstronomyWindowController _controller;
    private readonly string _windowKey;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly DispatcherQueueTimer _resizeTimer;
    private CancellationTokenSource? _projectionCancellation;
    private LocalizationService _strings = new("system");
    private AppSettings _agendaSettings = new();
    private WorldClockSnapshot? _reference;
    private bool _changingCities;
    private bool _closed;
    private RenderRequestKey? _pendingRender;
    private RenderRequestKey? _displayedRender;
    private CelestialSnapshot? _skySnapshot;

    /// <summary>Creates a sky, agenda, or Earth globe window over the application facade.</summary>
    internal CelestialWindow(IWorkTrailApplication application, MicaDialogService dialogs, AppSettings settings, string windowKey)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _windowKey = windowKey;
        if (windowKey is not (WindowStateKeys.LocalSky or WindowStateKeys.AstronomyAgenda or WindowStateKeys.CelestialMap))
        {
            throw new ArgumentException("Unsupported celestial window.", nameof(windowKey));
        }

        InitializeComponent();
        if (windowKey is WindowStateKeys.CelestialMap or WindowStateKeys.LocalSky)
        {
            // Sky and globe reach the window edges; only their lower information and controls use insets.
            ContentGrid.Margin = new Thickness(0);
            Toolbar.Margin = new Thickness(16, 0, 16, 12);
            Footer.Margin = new Thickness(16, 0, 16, 0);
        }

        _resizeTimer = DispatcherQueue.CreateTimer();
        _resizeTimer.IsRepeating = false;
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(250);
        _resizeTimer.Tick += ResizeTimer_Tick;
        _controller = new AstronomyWindowController(
            this, RootGrid, TitleBarDragRegion, TitleBarLeftInsetColumn, TitleBarRightInsetColumn,
            LoadingIndicator, NotificationBanner, application, dialogs, windowKey,
            windowKey == WindowStateKeys.AstronomyAgenda ? 440 : 800,
            windowKey == WindowStateKeys.CelestialMap ? 580 : 740,
            RenderSnapshot, celestialReferenceOnly: true);
        Closed += CelestialWindow_Closed;
        ApplySettings(settings);
    }

    /// <summary>Updates the acrylic theme, localized controls, and current projection.</summary>
    internal void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _strings = new LocalizationService(settings.UiLanguage);
        _agendaSettings = settings;
        _pendingRender = null;
        _displayedRender = null;
        Title = T(_windowKey switch
        {
            WindowStateKeys.LocalSky => "Celestial.Sky.Title",
            WindowStateKeys.AstronomyAgenda => "Celestial.Agenda.Title",
            _ => "Celestial.Map.Title"
        });
        TitleBarText.Text = Title.ToUpper(_strings.Culture);
        CitySelector.PlaceholderText = T("Celestial.SelectCity");
        UiLocalization.SetAccessibleLabel(CitySelector, T(_windowKey == WindowStateKeys.CelestialMap ? "Celestial.Map.CenterCity" : "Celestial.Observer"));
        BodiesScroll.Visibility = _windowKey == WindowStateKeys.LocalSky ? Visibility.Visible : Visibility.Collapsed;
        BodiesFrame.Visibility = BodiesScroll.Visibility;
        AboveHorizonText.Text = T("Celestial.Sky.AboveHorizon");
        UiLocalization.SetAccessibleLabel(PreviousBodiesButton, T("Celestial.Sky.PreviousBodies"));
        UiLocalization.SetAccessibleLabel(NextBodiesButton, T("Celestial.Sky.NextBodies"));
        var isLocalSky = _windowKey == WindowStateKeys.LocalSky;
        PlanetsVisibilitySwitch.Visibility = isLocalSky ? Visibility.Visible : Visibility.Collapsed;
        ConstellationsVisibilitySwitch.Visibility = isLocalSky ? Visibility.Visible : Visibility.Collapsed;
        PlanetsVisibilitySwitch.Header = T("Celestial.Sky.ShowPlanets");
        ConstellationsVisibilitySwitch.Header = T("Celestial.Sky.ShowConstellations");
        UiLocalization.SetAccessibleLabel(PlanetsVisibilitySwitch, T("Celestial.Sky.ShowPlanets"));
        UiLocalization.SetAccessibleLabel(ConstellationsVisibilitySwitch, T("Celestial.Sky.ShowConstellations"));
        ToolTipService.SetToolTip(PlanetsVisibilitySwitch, T("Celestial.Sky.ShowPlanets"));
        ToolTipService.SetToolTip(ConstellationsVisibilitySwitch, T("Celestial.Sky.ShowConstellations"));
        AgendaOptionsButton.Visibility = _windowKey == WindowStateKeys.AstronomyAgenda
            ? Visibility.Visible : Visibility.Collapsed;
        UiLocalization.SetAccessibleLabel(AgendaOptionsButton, T("Celestial.Agenda.Options"));
        ToolTipService.SetToolTip(AgendaOptionsButton, T("Celestial.Agenda.Options"));
        ZodiacNoteText.Text = T("Celestial.Zodiac.Note");
        _controller.ApplySettings(settings);
    }

    /// <summary>Mirrors the world's selected live or converted reference instant.</summary>
    internal void ApplySnapshot(WorldClockSnapshot snapshot, bool isLive) => _controller.ApplySnapshot(snapshot, isLive);

    /// <summary>Closes the window after the composition root saved its workspace.</summary>
    internal void CloseForShutdown() => _controller.CloseForShutdown();

    /// <summary>Discards a failed opening without changing persisted placement.</summary>
    internal void CloseAfterFailedOpening() => _controller.CloseAfterFailedOpening();

    private void RenderSnapshot(WorldClockSnapshot snapshot)
    {
        _reference = snapshot;
        var selectedId = CitySelector.SelectedValue as string;
        _changingCities = true;
        try
        {
            // Minute updates must not rebuild an open city picker or reset its selection.
            if (CitySelector.ItemsSource is not IEnumerable<WorldClockItem> currentCities
                || !currentCities.Select(city => (city.CityId, city.CityName))
                    .SequenceEqual(snapshot.Clocks.Select(city => (city.CityId, city.CityName))))
            {
                CitySelector.ItemsSource = snapshot.Clocks;
                var preferred = _windowKey == WindowStateKeys.AstronomyAgenda
                    ? _agendaSettings.AstronomyAgendaCityId : selectedId;
                CitySelector.SelectedValue = snapshot.Clocks.Any(city => city.CityId == preferred)
                    ? preferred : snapshot.Clocks.FirstOrDefault()?.CityId;
            }
        }
        finally
        {
            _changingCities = false;
        }

        RefreshProjection();
    }

    private void CitySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_changingCities)
        {
            RefreshProjection();
        }
    }

    private async void AgendaOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_windowKey != WindowStateKeys.AstronomyAgenda || _reference is null) return;
        var city = new ComboBox
        {
            Header = T("Celestial.Agenda.Options.City"),
            ItemsSource = _reference.Clocks,
            DisplayMemberPath = "CityName",
            SelectedValuePath = "CityId",
            SelectedValue = CitySelector.SelectedValue,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var saints = new CheckBox
        {
            Content = T("Celestial.Agenda.Options.Saints"),
            IsChecked = _agendaSettings.AstronomyAgendaShowSaints
        };
        var countryBoxes = CelestialCalendarCountries.All.Select(country => new CheckBox
        {
            Content = country.Name,
            Tag = country.Code,
            IsChecked = (_agendaSettings.AstronomyAgendaCountryCodes ?? []).Contains(country.Code, StringComparer.Ordinal)
        }).ToArray();
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(city);
        content.Children.Add(saints);
        content.Children.Add(new TextBlock { Text = T("Celestial.Agenda.Options.Countries"), TextWrapping = TextWrapping.Wrap });
        foreach (var box in countryBoxes) content.Children.Add(box);
        content.Children.Add(new TextBlock
        {
            Text = T("Celestial.Agenda.Options.Coverage"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush")
        });
        var dialogContent = new ScrollViewer { Content = content, MaxHeight = 450, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (!await _dialogs.ConfirmAsync(this, new DialogRequest(
                T("Celestial.Agenda.Options"), T("Celestial.Agenda.Options.Coverage"),
                T("Labels.Save"), T("Dialog.Cancel")), dialogContent)) return;
        if (city.SelectedValue is not string cityId) return;
        try
        {
            var selectedCodes = countryBoxes.Where(box => box.IsChecked == true)
                .Select(box => (string)box.Tag).ToArray();
            var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
            {
                ["astronomy.agenda.city_id"] = cityId,
                ["astronomy.agenda.country_codes"] = string.Join(',', selectedCodes),
                ["astronomy.agenda.show_saints"] = saints.IsChecked == true ? "true" : "false"
            }), _lifetimeCancellation.Token);
            if (!result.Succeeded || result.Value is null)
            {
                ShowFailure(result.MessageKey);
                return;
            }

            _changingCities = true;
            try { CitySelector.SelectedValue = cityId; }
            finally { _changingCities = false; }
            ApplySettings(result.Value);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { if (!_closed) ShowFailure("Celestial.Unavailable", exception); }
    }

    private void SkyVisibilitySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_windowKey != WindowStateKeys.LocalSky || _skySnapshot is null)
        {
            return;
        }

        SkyControl.ShowPlanets = PlanetsVisibilitySwitch.IsOn;
        SkyControl.ShowConstellations = ConstellationsVisibilitySwitch.IsOn;
        RenderBodies(_skySnapshot);
    }

    private async void RefreshProjection()
    {
        if (_closed || _reference is null)
        {
            return;
        }

        var snapshot = _reference;
        var selectedCity = CitySelector.SelectedItem as WorldClockItem;
        var isMap = _windowKey == WindowStateKeys.CelestialMap;
        var pixelWidth = isMap ? Math.Clamp((int)Math.Round(Math.Max(256d, SurfaceGrid.ActualWidth)), 256, 1440) : 0;
        var pixelHeight = isMap ? Math.Clamp((int)Math.Round(Math.Max(160d, SurfaceGrid.ActualHeight)), 160, 960) : 0;
        // Both the independent timer and World Clocks can publish the same live minute. This is rendering
        // coalescing only: explicit reference instants retain their exact ticks and Core receives the original time.
        var isLive = _controller.IsLive;
        var timeSlot = isLive ? snapshot.InstantUtc.UtcTicks / TimeSpan.TicksPerMinute : snapshot.InstantUtc.UtcTicks;
        var requestKey = new RenderRequestKey(selectedCity?.CityId, timeSlot, isLive, pixelWidth, pixelHeight);
        if (requestKey == _pendingRender)
        {
            return;
        }

        if (requestKey == _displayedRender)
        {
            // A resize can return to the displayed geometry while another size is still rendering.
            _projectionCancellation?.Cancel();
            _pendingRender = null;
            return;
        }

        _projectionCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _projectionCancellation = cancellation;
        _pendingRender = requestKey;
        var retainContent = _displayedRender is { } displayed && displayed.CityId == requestKey.CityId
            && displayed.IsLive == isLive && (isLive || displayed.TimeSlot == timeSlot);
        EmptyState.Visibility = Visibility.Collapsed;
        if (!retainContent)
        {
            ClearRenderedContent();
        }

        if (!isMap && selectedCity is null)
        {
            EmptyState.Text = T("Celestial.NoCity");
            EmptyState.Visibility = Visibility.Visible;
            LoadingIndicator.IsActive = false;
            LoadingIndicator.Visibility = Visibility.Collapsed;
            _projectionCancellation = null;
            _pendingRender = null;
            return;
        }

        LoadingIndicator.IsActive = !retainContent;
        LoadingIndicator.Visibility = retainContent ? Visibility.Collapsed : Visibility.Visible;
        try
        {
            if (isMap)
            {
                var center = snapshot.Map.Cities.FirstOrDefault(city => city.CityId == selectedCity?.CityId);
                var request = new CelestialMapRequest(snapshot.InstantUtc,
                    CelestialMapProjection.Globe,
                    pixelWidth, pixelHeight,
                    center?.Latitude ?? 20d, center?.Longitude ?? 15d);
                // The application owns textures, solar calculations, and image generation; this window only presents its DTO.
                var result = await _application.GetCelestialMapAsync(request, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!result.Succeeded || result.Value is null)
                {
                    ShowFailure(result.MessageKey);
                    return;
                }

                await EarthControl.ApplyAsync(result.Value, _strings, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                EarthControl.Visibility = Visibility.Visible;
                SetStatus("Celestial.Map.Note");
            }
            else
            {
                // Core returns observer positions and city-local event times. No UI astronomy or time-zone I/O is required.
                var result = await _application.GetCelestialAsync(
                    new CelestialRequest(selectedCity!.CityId, snapshot.InstantUtc,
                        IncludeSatellites: _windowKey == WindowStateKeys.LocalSky), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!result.Succeeded || result.Value is null)
                {
                    ShowFailure(result.MessageKey);
                    return;
                }

                if (_windowKey == WindowStateKeys.LocalSky)
                {
                    _skySnapshot = result.Value;
                    SkyControl.ShowPlanets = PlanetsVisibilitySwitch.IsOn;
                    SkyControl.ShowConstellations = ConstellationsVisibilitySwitch.IsOn;
                    SkyControl.Apply(result.Value, _strings);
                    SkyControl.Visibility = Visibility.Visible;
                    RenderBodies(result.Value);
                    SetStatus(result.Value.SunAltitudeDegrees >= 0d ? "Celestial.Sky.DaylightNote" : "Celestial.Sky.NightNote");
                }
                else
                {
                    RenderAgenda(result.Value);
                    RenderZodiac(result.Value.Zodiac);
                    AgendaScroll.Visibility = Visibility.Visible;
                    SetStatus("Celestial.Agenda.Note");
                }
            }

            _displayedRender = requestKey;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer city, instant, projection, or close supersedes the pending rendering request.
        }
        catch (Exception exception)
        {
            if (!_closed && !cancellation.IsCancellationRequested)
            {
                ShowFailure("Celestial.Unavailable", exception);
            }
        }
        finally
        {
            if (ReferenceEquals(_projectionCancellation, cancellation))
            {
                _projectionCancellation = null;
                _pendingRender = null;
                // Cancellation may finish after Closed. The detached XAML tree must not be touched.
                if (!_closed)
                {
                    LoadingIndicator.IsActive = false;
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private void PreviousBodiesButton_Click(object sender, RoutedEventArgs e) => ScrollBodies(-1);

    private void NextBodiesButton_Click(object sender, RoutedEventArgs e) => ScrollBodies(1);

    private void ScrollBodies(int direction)
    {
        var step = Math.Max(80d, BodiesScroll.ViewportWidth * 0.75d);
        var offset = Math.Clamp(BodiesScroll.HorizontalOffset + direction * step, 0d, BodiesScroll.ScrollableWidth);
        BodiesScroll.ChangeView(offset, null, null);
    }

    private void BodiesScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateBodiesNavigation();

    private void BodiesScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateBodiesNavigation();

    private void UpdateBodiesNavigation()
    {
        if (PreviousBodiesButton is null || NextBodiesButton is null || BodiesScroll is null)
        {
            return;
        }

        PreviousBodiesButton.IsEnabled = BodiesScroll.HorizontalOffset > 1d;
        NextBodiesButton.IsEnabled = BodiesScroll.HorizontalOffset < BodiesScroll.ScrollableWidth - 1d;
    }

    private void RenderBodies(CelestialSnapshot snapshot)
    {
        BodyItems.Children.Clear();
        foreach (var body in snapshot.Bodies.Where(body => body.IsAboveHorizon
            && (PlanetsVisibilitySwitch.IsOn || !IsPlanet(body.Kind))))
        {
            var name = T($"CelestialBody{body.Kind}");
            var label = new TextBlock
            {
                Text = name,
                FontSize = 15,
                FontWeight = FontWeights.Light,
                Foreground = ThemeBrush("TextFillColorPrimaryBrush")
            };
            UiLocalization.SetAccessibleLabel(label, _strings.Format("Celestial.Sky.BodyPosition", name, body.AltitudeDegrees, body.AzimuthDegrees));
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (body.Kind is CelestialBodyKind.Sun or CelestialBodyKind.Moon)
            {
                chip.Children.Add(new CelestialPhaseControl
                {
                    Width = 44,
                    Height = 44,
                    IsDaylight = body.Kind == CelestialBodyKind.Sun,
                    MoonPhaseAngleDegrees = snapshot.MoonPhaseAngleDegrees
                });
            }
            else
            {
                chip.Children.Add(new CelestialArtworkControl
                {
                    Width = 44,
                    Height = 44,
                    Kind = CelestialArtworkControl.ForPlanet(body.Kind)
                });
            }

            label.VerticalAlignment = VerticalAlignment.Center;
            var caption = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            caption.Children.Add(label);
            caption.Children.Add(new TextBlock
            {
                Text = body.AltitudeDegrees.ToString("0°", _strings.Culture),
                FontWeight = FontWeights.Light,
                FontSize = 12,
                Foreground = ThemeBrush("AccentTextFillColorPrimaryBrush")
            });
            chip.Children.Add(caption);
            BodyItems.Children.Add(chip);
        }

        foreach (var satellite in snapshot.Satellites.Where(item => item.AltitudeDegrees > 0d))
        {
            var name = T($"CelestialSatellite{satellite.Id}");
            var position = _strings.Format("Celestial.Sky.SatellitePosition", name,
                satellite.AltitudeDegrees, satellite.AzimuthDegrees);
            var marker = new Border
            {
                Width = 44,
                Height = 44,
                Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
                BorderBrush = ThemeBrush("AccentTextFillColorPrimaryBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22),
                Child = new TextBlock
                {
                    Text = "✦",
                    FontSize = 25,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = ThemeBrush("AccentTextFillColorPrimaryBrush")
                }
            };
            UiLocalization.SetAccessibleLabel(marker, position);
            ToolTipService.SetToolTip(marker, position);
            var caption = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            caption.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 15,
                FontWeight = FontWeights.Light,
                Foreground = ThemeBrush("TextFillColorPrimaryBrush")
            });
            caption.Children.Add(new TextBlock
            {
                Text = satellite.AltitudeDegrees.ToString("0°", _strings.Culture),
                FontSize = 12,
                FontWeight = FontWeights.Light,
                Foreground = ThemeBrush("AccentTextFillColorPrimaryBrush")
            });
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            chip.Children.Add(marker);
            chip.Children.Add(caption);
            BodyItems.Children.Add(chip);
        }
    }

    private void RenderAgenda(CelestialSnapshot snapshot)
    {
        AgendaRows.Children.Clear();
        DateOnly? previousDate = null;
        var referenceIndex = snapshot.AgendaReferenceIndex;
        var rowIndex = 0;
        var referenceLabel = T(_controller.IsLive ? "WorldClock.Now" : "WorldClock.ReferenceInstant");
        foreach (var item in snapshot.Agenda)
        {
            if (rowIndex++ == referenceIndex)
                AgendaRows.Children.Add(CreateAgendaReferenceMarker(snapshot, referenceLabel));
            var date = DateOnly.FromDateTime(item.StartLocal.DateTime);
            if (date != previousDate)
            {
                AgendaRows.Children.Add(new TextBlock
                {
                    Text = item.StartLocal.ToString("dddd d MMMM yyyy", _strings.Culture),
                    FontSize = 13,
                    FontWeight = FontWeights.Light,
                    Margin = new Thickness(30, 12, 0, 2),
                    Foreground = ThemeBrush("TextFillColorSecondaryBrush")
                });
                previousDate = date;
            }

            var row = new Grid { ColumnSpacing = 14, MinHeight = 94 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = ThemeBrush("DividerStrokeColorDefaultBrush")
            });
            row.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = ThemeBrush("AccentTextFillColorPrimaryBrush")
            });
            var surface = new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
                BorderBrush = ThemeBrush(item.IsAtReferenceInstant ? "AccentTextFillColorPrimaryBrush" : "DividerStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(item.IsAtReferenceInstant ? 2 : 1),
                Opacity = item.IsAtReferenceInstant ? 1d : 0.45d,
                IsHitTestVisible = false
            };
            Grid.SetColumn(surface, 1);
            Grid.SetColumnSpan(surface, 2);
            row.Children.Add(surface);
            FrameworkElement illustration;
            if (item.Kind is CelestialEventKind.NewMoon or CelestialEventKind.FirstQuarter or CelestialEventKind.FullMoon or CelestialEventKind.LastQuarter)
            {
                illustration = new CelestialPhaseControl
                {
                    Width = 60,
                    Height = 60,
                    IsDaylight = false,
                    MoonPhaseAngleDegrees = item.Kind switch
                    {
                        CelestialEventKind.NewMoon => 0d,
                        CelestialEventKind.FirstQuarter => 90d,
                        CelestialEventKind.FullMoon => 180d,
                        _ => 270d
                    }
                };
            }
            else if (item.Kind == CelestialEventKind.MoonPlanetConjunction)
            {
                var relatedBody = item.RelatedBody ?? throw new InvalidDataException("A conjunction must identify its related planet.");
                var pair = new Grid { Width = 64, Height = 60 };
                pair.Children.Add(new CelestialPhaseControl
                {
                    Width = 38,
                    Height = 38,
                    IsDaylight = false,
                    MoonPhaseAngleDegrees = item.MoonPhaseAngleDegrees ?? throw new InvalidDataException("A conjunction must contain its lunar phase at the event instant."),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom
                });
                pair.Children.Add(new CelestialArtworkControl
                {
                    Width = 32,
                    Height = 32,
                    Kind = CelestialArtworkControl.ForPlanet(relatedBody),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top
                });
                illustration = pair;
            }
            else if (item.Kind == CelestialEventKind.MeteorShower)
            {
                illustration = new Image
                {
                    Width = 60,
                    Height = 60,
                    Stretch = Stretch.Uniform,
                    Source = new BitmapImage(new Uri("ms-appx:///Assets/Celestial/Artwork/meteor-shower-v1.png"))
                };
            }
            else if (item.Kind is CelestialEventKind.Moonrise or CelestialEventKind.Moonset)
            {
                illustration = new Image
                {
                    Width = 60,
                    Height = 60,
                    Stretch = Stretch.Uniform,
                    Source = new BitmapImage(new Uri(item.Kind == CelestialEventKind.Moonset
                        ? "ms-appx:///Assets/Celestial/Artwork/moonset-v1.png"
                        : "ms-appx:///Assets/Celestial/Artwork/moon-horizon-v1.png"))
                };
            }
            else if (item.Kind == CelestialEventKind.ImportantDate)
            {
                illustration = new Image
                {
                    Width = 60,
                    Height = 60,
                    Stretch = Stretch.Uniform,
                    Source = ImportantDateArtwork(item.ImportantDateId ?? throw new InvalidDataException("An important date must identify its catalog entry."))
                };
            }
            else if (item.Kind is CelestialEventKind.Holiday or CelestialEventKind.Saint)
            {
                illustration = new Image
                {
                    Width = 60,
                    Height = 60,
                    Stretch = Stretch.Uniform,
                    Source = CalendarArtwork(item)
                };
            }
            else if (item.Kind == CelestialEventKind.SpaceWeather)
            {
                illustration = new Image
                {
                    Width = 60,
                    Height = 60,
                    Stretch = Stretch.Uniform,
                    Source = SpaceWeatherArtwork(item.SpaceWeatherKind ?? throw new InvalidDataException("A space-weather event must identify its condition."))
                };
            }
            else
            {
                illustration = new CelestialArtworkControl
                {
                    Width = 60,
                    Height = 60,
                    Kind = item.Kind switch
                    {
                        CelestialEventKind.Sunrise => CelestialArtworkKind.Sunrise,
                        CelestialEventKind.Sunset => CelestialArtworkKind.Sunset,
                        CelestialEventKind.MorningBlueHour or CelestialEventKind.EveningBlueHour => CelestialArtworkKind.BlueHour,
                        CelestialEventKind.CivilDawn or CelestialEventKind.CivilDusk => CelestialArtworkKind.Twilight,
                        _ => CelestialArtworkKind.Seasons
                    }
                };
            }

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(illustration, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Grid.SetColumn(illustration, 1);
            row.Children.Add(illustration);
            var details = new StackPanel { Spacing = 7, Margin = new Thickness(0, 12, 12, 12), VerticalAlignment = VerticalAlignment.Center };
            if (item.IsAtReferenceInstant)
                details.Children.Add(new TextBlock
                {
                    Text = referenceLabel,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeBrush("AccentTextFillColorPrimaryBrush")
                });
            details.Children.Add(new TextBlock
            {
                Text = EventTitle(item),
                FontWeight = FontWeights.Light,
                FontSize = 19,
                TextWrapping = TextWrapping.Wrap
            });
            details.Children.Add(new TextBlock
            {
                Text = item.Kind == CelestialEventKind.Holiday
                    ? item.StartLocal.ToString("t", _strings.Culture)
                    : item.Kind == CelestialEventKind.Saint
                    ? T("Celestial.Agenda.AllDay")
                    : item.IsApproximate ? T("Celestial.Agenda.ApproximatePeak") : item.EndLocal is { } end
                    ? $"{item.StartLocal.ToString("t", _strings.Culture)} – {end.ToString("t", _strings.Culture)}"
                    : item.StartLocal.ToString("t", _strings.Culture),
                FontWeight = FontWeights.Light,
                FontSize = 13,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush")
            });
            if (item.Kind == CelestialEventKind.MoonPlanetConjunction)
            {
                details.Children.Add(new TextBlock
                {
                    Text = _strings.Format("Celestial.Agenda.ConjunctionDetail",
                        item.SeparationDegrees ?? throw new InvalidDataException("A conjunction must contain its angular separation.")),
                    FontWeight = FontWeights.Light,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeBrush("TextFillColorSecondaryBrush")
                });
            }
            else if (item.Kind == CelestialEventKind.MeteorShower)
            {
                details.Children.Add(new TextBlock
                {
                    Text = _strings.Format("Celestial.Agenda.MeteorActivity",
                        (item.ActivityStartDate ?? throw new InvalidDataException("A meteor shower must contain its activity start.")).ToString("d MMM yyyy", _strings.Culture),
                        (item.ActivityEndDate ?? throw new InvalidDataException("A meteor shower must contain its activity end.")).ToString("d MMM yyyy", _strings.Culture)),
                    FontWeight = FontWeights.Light,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeBrush("TextFillColorSecondaryBrush")
                });
            }
            else if (item.Kind == CelestialEventKind.SpaceWeather)
            {
                details.Children.Add(new TextBlock
                {
                    Text = SpaceWeatherDetail(item),
                    FontWeight = FontWeights.Light,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeBrush("TextFillColorSecondaryBrush")
                });
            }
            else if (item.Kind is CelestialEventKind.Holiday or CelestialEventKind.Saint)
            {
                details.Children.Add(new TextBlock
                {
                    Text = item.Kind == CelestialEventKind.Holiday
                        ? CalendarHolidayDetail(item) : T("Celestial.Agenda.SaintSource"),
                    FontWeight = FontWeights.Light,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeBrush("TextFillColorSecondaryBrush")
                });
            }
            Grid.SetColumn(details, 2);
            row.Children.Add(details);
            AgendaRows.Children.Add(row);
        }

        if (referenceIndex == snapshot.Agenda.Count)
            AgendaRows.Children.Add(CreateAgendaReferenceMarker(snapshot, referenceLabel));

        if (snapshot.Agenda.Count == 0)
        {
            EmptyState.Text = T("Celestial.Agenda.NoEvents");
            EmptyState.Visibility = Visibility.Visible;
        }
    }

    private FrameworkElement CreateAgendaReferenceMarker(CelestialSnapshot snapshot, string label)
    {
        var marker = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 6, 0, 6) };
        marker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        marker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        marker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        marker.Children.Add(new Ellipse
        {
            Width = 12,
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = ThemeBrush("AccentTextFillColorPrimaryBrush")
        });
        var text = new TextBlock
        {
            Text = $"{label} · {snapshot.LocalTime.ToString("g", _strings.Culture)}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("AccentTextFillColorPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(text, 1);
        marker.Children.Add(text);
        var line = new Border
        {
            Height = 2,
            MinWidth = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Background = ThemeBrush("AccentTextFillColorPrimaryBrush")
        };
        Grid.SetColumn(line, 2);
        marker.Children.Add(line);
        return marker;
    }

    private string EventTitle(CelestialAgendaEvent item) => item.Kind switch
    {
        CelestialEventKind.MoonPlanetConjunction => _strings.Format("Celestial.Agenda.ConjunctionTitle",
            T($"CelestialBody{item.RelatedBody ?? throw new InvalidDataException("A conjunction must identify its related planet.")}")),
        CelestialEventKind.MeteorShower => T($"CelestialMeteor{item.MeteorShowerId ?? throw new InvalidDataException("A meteor shower must identify its catalog entry.")}"),
        CelestialEventKind.ImportantDate => T($"CelestialImportantDate{item.ImportantDateId ?? throw new InvalidDataException("An important date must identify its catalog entry.")}"),
        CelestialEventKind.SpaceWeather => T($"CelestialSpaceWeather{item.SpaceWeatherKind ?? throw new InvalidDataException("A space-weather event must identify its condition.")}"),
        CelestialEventKind.Holiday or CelestialEventKind.Saint => item.CalendarLabel
            ?? throw new InvalidDataException("A calendar event must identify its name."),
        _ => T($"CelestialEvent{item.Kind}")
    };

    private string CalendarHolidayDetail(CelestialAgendaEvent item)
    {
        var code = item.CalendarCountryCode ?? throw new InvalidDataException("A holiday must identify its country.");
        var name = CelestialCalendarCountries.All.Single(country => country.Code == code).Name;
        var quality = item.CalendarQuality is "estimated" or "provisional"
            ? $" · {T($"Celestial.Agenda.Quality.{item.CalendarQuality}")}" : string.Empty;
        return $"{name} · {T("Celestial.Agenda.HolidaySource")}{quality}";
    }

    private static BitmapImage CalendarArtwork(CelestialAgendaEvent item)
    {
        var name = item.Kind switch
        {
            CelestialEventKind.Holiday when item.CalendarArtworkFileName is { } artwork
                && artwork.EndsWith(".png", StringComparison.Ordinal)
                && artwork.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                => artwork,
            CelestialEventKind.Saint when item.CalendarEntryKey is { } key
                && key.All(char.IsLetterOrDigit)
                => $"saint-{key}-v1.png",
            _ => throw new InvalidDataException("A calendar event has no approved artwork identifier.")
        };
        return new BitmapImage(new Uri($"ms-appx:///Assets/Celestial/Artwork/{name}"));
    }

    private string SpaceWeatherDetail(CelestialAgendaEvent item)
    {
        var source = T("Celestial.SpaceWeather.Source");
        var intensity = item.NoaaScale is { } scale ? $"G{scale}" : item.KpIndex is { } kp ? $"Kp {kp:0.0}" : null;
        var description = T($"Celestial.SpaceWeather.{item.SpaceWeatherKind ?? throw new InvalidDataException("A space-weather event must identify its condition.")}");
        return intensity is null ? $"{description} · {source}" : $"{description} · {intensity} · {source}";
    }

    private static BitmapImage SpaceWeatherArtwork(SpaceWeatherEventKind kind) => new(new Uri(kind switch
    {
        SpaceWeatherEventKind.GeomagneticStorm => "ms-appx:///Assets/Celestial/Artwork/geomagnetic-storm-v1.png",
        SpaceWeatherEventKind.SolarRadiationStorm or SpaceWeatherEventKind.HighEnergyElectronFlux => "ms-appx:///Assets/Celestial/Artwork/solar-radiation-storm-v1.png",
        SpaceWeatherEventKind.RadioBlackout => "ms-appx:///Assets/Celestial/Artwork/solar-flare-radio-blackout-v1.png",
        _ => throw new InvalidDataException("The space-weather condition has no approved artwork.")
    }));

    private static BitmapImage ImportantDateArtwork(string id) => new(new Uri(id switch
    {
        "birthday" => "ms-appx:///Assets/Celestial/Artwork/birthday-v1.png",
        "christmas" => "ms-appx:///Assets/Celestial/Artwork/christmas-v1.png",
        "new-year" => "ms-appx:///Assets/Celestial/Artwork/new-year-v1.png",
        _ => throw new InvalidDataException("The important date has no approved artwork.")
    }));

    private static bool IsPlanet(CelestialBodyKind kind) => kind is CelestialBodyKind.Mercury or CelestialBodyKind.Venus
        or CelestialBodyKind.Mars or CelestialBodyKind.Jupiter or CelestialBodyKind.Saturn
        or CelestialBodyKind.Uranus or CelestialBodyKind.Neptune;

    private void RenderZodiac(CelestialZodiacSnapshot zodiac)
    {
        ZodiacExpander.Visibility = Visibility.Visible;
        ZodiacSummaryText.Text = _strings.Format("Celestial.Zodiac.Current", T($"CelestialZodiac{zodiac.CurrentSign}"));
        CurrentZodiacImage.Source = ZodiacImage(zodiac.CurrentSign);
        UiLocalization.SetAccessibleLabel(ZodiacExpander, ZodiacSummaryText.Text);
        ZodiacItems.Children.Clear();
        foreach (var sector in zodiac.Signs)
        {
            var current = sector.Sign == zodiac.CurrentSign;
            var name = T($"CelestialZodiac{sector.Sign}");
            var content = new StackPanel { Spacing = 6 };
            var image = new Image { Source = ZodiacImage(sector.Sign), Width = 44, Height = 44 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(image, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            content.Children.Add(image);
            content.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                FontWeight = FontWeights.Light,
                TextAlignment = TextAlignment.Center,
                Foreground = ThemeBrush(current ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush")
            });
            content.Children.Add(new TextBlock
            {
                Text = $"{sector.StartLongitudeDegrees:0}°–{sector.EndLongitudeDegrees:0}°",
                FontSize = 10,
                FontWeight = FontWeights.Light,
                TextAlignment = TextAlignment.Center,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush")
            });
            var tile = new Border
            {
                Child = content,
                MinWidth = 78,
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(current ? 1.5d : 1d),
                BorderBrush = ThemeBrush(current ? "AccentTextFillColorPrimaryBrush" : "DividerStrokeColorDefaultBrush")
            };
            UiLocalization.SetAccessibleLabel(tile, current ? ZodiacSummaryText.Text : name);
            ZodiacItems.Children.Add(tile);
        }
    }

    private static BitmapImage ZodiacImage(TropicalZodiacSign sign) =>
        new(new Uri($"ms-appx:///Assets/Celestial/Zodiac/{sign.ToString().ToLowerInvariant()}.png"));

    private void SetStatus(string key)
    {
        StatusText.Text = T(key);
        UiLocalization.SetAccessibleLabel(StatusText, StatusText.Text);
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 400d || e.NewSize.Height < 320d;
        if (ContentGrid is not null)
        {
            var edgeToEdge = _windowKey is WindowStateKeys.CelestialMap or WindowStateKeys.LocalSky;
            ContentGrid.Margin = edgeToEdge ? new Thickness(0)
                : compact ? new Thickness(8, 4, 8, 8) : new Thickness(20, 8, 20, 16);
            if (edgeToEdge)
            {
                var inset = compact ? 8d : 16d;
                Toolbar.Margin = new Thickness(inset, 0, inset, compact ? 8d : 12d);
                Footer.Margin = new Thickness(inset, 0, inset, 0);
            }

            ContentGrid.RowSpacing = compact ? 6d : 12d;
            TitleBarLogo.Margin = compact ? new Thickness(8, 0, 8, 0) : new Thickness(16, 0, 10, 0);
            TitleBarText.Visibility = e.NewSize.Width < 280d ? Visibility.Collapsed : Visibility.Visible;
            ZodiacSummaryText.MaxWidth = Math.Max(60d, e.NewSize.Width - 160d);
            ZodiacContentScroll.MaxHeight = Math.Clamp(e.NewSize.Height * 0.3d, 80d, 220d);
            Footer.Visibility = e.NewSize.Height < 300d ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_windowKey == WindowStateKeys.CelestialMap && _resizeTimer is not null)
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }
    }

    private void ResizeTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        RefreshProjection();
    }

    private void ShowFailure(string key, Exception? exception = null)
    {
        if (_closed)
        {
            return;
        }

        // Hide obsolete content on failure so a new observer or reference label cannot describe an older projection.
        ClearRenderedContent();
        EmptyState.Text = T("Celestial.Unavailable");
        EmptyState.Visibility = Visibility.Visible;
        var message = exception is null ? T(key) : $"{T(key)} ({exception.GetType().Name})";
        _dialogs.Notifications.ShowError(NotificationBanner, Title, message);
    }

    private void ClearRenderedContent()
    {
        _displayedRender = null;
        _skySnapshot = null;
        SkyControl.Visibility = Visibility.Collapsed;
        AgendaScroll.Visibility = Visibility.Collapsed;
        EarthControl.Visibility = Visibility.Collapsed;
        BodyItems.Children.Clear();
        StatusText.Text = string.Empty;
        ZodiacExpander.Visibility = Visibility.Collapsed;
    }

    private void CelestialWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        Closed -= CelestialWindow_Closed;
        _resizeTimer.Stop();
        _resizeTimer.Tick -= ResizeTimer_Tick;
        // Invalidate the render owner before cancellation can resume an awaiting UI continuation.
        _projectionCancellation = null;
        _pendingRender = null;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private Brush ThemeBrush(string key) => key switch
    {
        "TextFillColorPrimaryBrush" => PrimaryPalette.Background,
        "TextFillColorSecondaryBrush" => SecondaryPalette.Background,
        "AccentTextFillColorPrimaryBrush" => AccentPalette.Background,
        "DividerStrokeColorDefaultBrush" => DividerPalette.Background,
        "CardBackgroundFillColorDefaultBrush" => CardPalette.Background,
        _ => throw new ArgumentException("Unsupported celestial theme brush.", nameof(key))
    };

    private string T(string key) => _strings.Translate(key);

    private sealed record RenderRequestKey(string? CityId, long TimeSlot, bool IsLive, int PixelWidth, int PixelHeight);
}
