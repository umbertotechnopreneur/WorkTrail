// SPDX-License-Identifier: MIT

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WorkTrail.Application;
using WorkTrail.Services;
using Windows.Storage.Pickers;
using Windows.System;

namespace WorkTrail;

/// <summary>Collects export choices and renders facade previews; all data, licensing and file writes stay in Core.</summary>
internal sealed partial class ReportExportWindow : Window
{
    private readonly IWorkTrailApplication _application;
    private readonly LocalizationService _strings;
    private readonly MicaDialogService _messages = new();
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private ReportExportSetup? _setup;
    private string _summary = "";
    private bool _applying = true;
    private bool _busy;
    private bool _closed;
    private bool _loaded;
    private bool _closeAfterCancel;

    // application provides report operations without exposing Core implementation details.
    // theme selects the visual theme applied to the export window.
    // strings provides localized text and formatting for the active language.
    // ownerAppWindow identifies the application window that owns this modal surface.
    // ownerHandle provides the native owner required by Windows pickers and dialogs.
    internal ReportExportWindow(IWorkTrailApplication application, ElementTheme theme, LocalizationService strings,
        AppWindow ownerAppWindow, IntPtr ownerHandle)
    {
        _application = application;
        _strings = strings;
        InitializeComponent();
        TitlePremiumBadge.Text = strings.Translate("Premium.Badge");
        TimesheetPremiumBadge.Text = strings.Translate("Premium.Badge");
        TimesheetRate.ValueChanged += (_, _) => ResetTimesheetSelection();
        Title = T("Export.Title");
        RootGrid.RequestedTheme = theme;
        UiLocalization.Apply(RootGrid, strings);
        GroupingInfoLink.Content = T("Export.MoreInformation");
        UiLocalization.SetAccessibleLabel(GroupingInfoLink, T("Export.MoreInformation"));
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowHandle));
        _titleBar = new CustomTitleBarController(this, appWindow, RootGrid, TitleDragRegion,
            TitleBarLeftInsetColumn, TitleBarRightInsetColumn, static () => Array.Empty<FrameworkElement>(), useTallTitleBar: false, allowAutoHide: false);
        _placement = new WindowPlacementService(application, this, appWindow, WindowStateKeys.ReportExport, 1160, 820, 24, ownerAppWindow.Id);
        WindowInteropService.SetOwner(WindowHandle, ownerHandle);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true; presenter.IsMaximizable = true; presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: true);
        }
        foreach (var item in new[] { ExportTab, ContentsTab, SummaryTab, TimesheetTab })
        {
            item.Content = T((string)item.Tag);
            UiLocalization.SetAccessibleLabel(item, (string)item.Content);
        }
        FromPicker.Header = T("Export.From"); ToPicker.Header = T("Export.To");
        UiLocalization.SetAccessibleLabel(FromPicker, T("Export.From"));
        UiLocalization.SetAccessibleLabel(ToPicker, T("Export.To"));
        UiLocalization.SetAccessibleLabel(TodayButton, T("Export.Today"));
        UiLocalization.SetAccessibleLabel(WeekButton, T("Export.Week"));
        UiLocalization.SetAccessibleLabel(MonthButton, T("Export.Month"));
        FormatCombo.ItemsSource = new[] { "Excel .xlsx", "CSV .zip", "JSON .json" };
        WorkbookThemeCombo.ItemsSource = new[] { T("Export.ThemeWorkTrail"), T("Export.ThemeGreen"), T("Export.ThemeBlue") };
        UiLocalization.SetAccessibleLabel(WorkbookThemeCombo, T("Export.ExcelTheme"));
        DescriptionCombo.ItemsSource = new[] { T("Export.Brief"), T("Export.CompleteText"), T("Export.Both") };
        SeparatorCombo.ItemsSource = new[] { ";", "," };
        GroupingCombo.ItemsSource = new[] { T("Export.ByDay"), T("Export.ByApplication"), T("Export.WholePeriod") };
        GroupingCombo.SelectedIndex = 0;
        TimesheetGroupingCombo.ItemsSource = new[] { T("Timesheet.ByDay"), T("Timesheet.ByWeek") };
        TimesheetGroupingCombo.SelectedIndex = 0;
        UiLocalization.SetAccessibleLabel(TimesheetGroupingCombo, T("Timesheet.Grouping"));
        Navigation.SelectedItem = ExportTab;
        ExportButton.IsEnabled = false;
        appWindow.Closing += (_, args) =>
        {
            if (_busy) { args.Cancel = true; _closeAfterCancel = true; _operation?.Cancel(); }
        };
        Closed += (_, _) =>
        {
            _closed = true; _lifetime.Cancel(); _operation?.Cancel();
            _messages.CloseActive(); _titleBar.Dispose(); _completion.TrySetResult(true);
        };
    }

    internal IntPtr WindowHandle { get; }

    /// <summary>Shows the configured export surface through the shared window queue.</summary>
    internal Task<bool> ShowAsync()
    {
        // The native owner keeps this modal surface above WorkTrail without placing it above other apps.
        Activate();
        return _completion.Task;
    }

    internal void DisposePlacement() => _placement.Dispose();

    private string T(string key) => _strings.Translate(key);

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        _placement.ApplyDefaultBounds(RootGrid);
        try
        {
            await _placement.RestoreOrCenterAsync(RootGrid, _lifetime.Token);
            var result = await _application.GetReportExportSetupAsync(_lifetime.Token);
            if (_closed) return;
            if (!result.Succeeded || result.Value is null) { ShowStatus(result.MessageKey, InfoBarSeverity.Error); return; }
            _setup = result.Value;
            DevicesCombo.ItemsSource = new[] { T("Export.AllDevices") }.Concat(_setup.Installations.Select(item => item.FriendlyName)).ToArray();
            ApplyOptions(_setup.Options with { Language = _strings.Language });
            ExportButton.IsEnabled = true;
            await LoadTimesheetJobsAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!_closed) ShowStatus("Export.Failed", InfoBarSeverity.Error); }
    }

    private void ApplyOptions(ReportExportOptions options)
    {
        _applying = true;
        FromPicker.Date = new DateTimeOffset(options.From.ToDateTime(TimeOnly.MinValue));
        ToPicker.Date = new DateTimeOffset(options.ToInclusive.ToDateTime(TimeOnly.MinValue));
        TimeZoneText.Text = options.TimeZoneId;
        DevicesCombo.SelectedIndex = 0;
        FormatCombo.SelectedIndex = (int)options.Format;
        WorkbookThemeCombo.SelectedIndex = (int)options.Theme;
        DescriptionCombo.SelectedIndex = (int)options.DescriptionMode;
        SeparatorCombo.SelectedIndex = options.CsvSeparator == ";" ? 0 : 1;
        DaysCheck.IsChecked = options.IncludeDays; ApplicationsCheck.IsChecked = options.IncludeApplications;
        CapturesCheck.IsChecked = options.IncludeCaptures; DescriptionsCheck.IsChecked = options.IncludeDescriptions;
        OcrCheck.IsChecked = options.IncludeOcr; TitlesCheck.IsChecked = options.IncludeWindowTitles;
        DevicesCheck.IsChecked = options.IncludeDevices; PathsCheck.IsChecked = options.IncludePaths;
        TelemetryCheck.IsChecked = options.IncludeTelemetry; UsageCheck.IsChecked = options.IncludeAiUsage;
        _applying = false;
        UpdateFormatHelp();
    }

    private ReportExportOptions CollectOptions()
    {
        if (_setup is null || FromPicker.Date is not { } from || ToPicker.Date is not { } to)
            throw new InvalidOperationException("An export range is required.");
        return _setup.Options with
        {
            From = DateOnly.FromDateTime(from.DateTime),
            ToInclusive = DateOnly.FromDateTime(to.DateTime),
            Language = _strings.Language,
            Format = (ReportExportFormat)FormatCombo.SelectedIndex,
            Theme = (ReportWorkbookTheme)WorkbookThemeCombo.SelectedIndex,
            InstallationId = DevicesCombo.SelectedIndex > 0 ? _setup.Installations[DevicesCombo.SelectedIndex - 1].InstallationId : null,
            IncludeDays = DaysCheck.IsChecked == true,
            IncludeApplications = ApplicationsCheck.IsChecked == true,
            IncludeCaptures = CapturesCheck.IsChecked == true,
            IncludeDescriptions = DescriptionsCheck.IsChecked == true,
            DescriptionMode = (ReportDescriptionMode)DescriptionCombo.SelectedIndex,
            IncludeOcr = OcrCheck.IsChecked == true,
            IncludeWindowTitles = TitlesCheck.IsChecked == true,
            IncludeDevices = DevicesCheck.IsChecked == true,
            IncludePaths = PathsCheck.IsChecked == true,
            IncludeTelemetry = TelemetryCheck.IsChecked == true,
            IncludeAiUsage = UsageCheck.IsChecked == true,
            CsvSeparator = SeparatorCombo.SelectedIndex == 0 ? ";" : ","
        };
    }

    // sender identifies a range, device or file-content control.
    // e describes the change; file formatting must not discard a paid AI draft.
    private void OptionsChanged(object sender, RoutedEventArgs e)
    {
        if (_applying || _closed || _setup is null || _busy) return;
        var sourcesChanged = ReferenceEquals(sender, FromPicker) || ReferenceEquals(sender, ToPicker)
            || ReferenceEquals(sender, DevicesCombo) || ReferenceEquals(sender, this);
        if (sourcesChanged)
        {
            ResetSummary();
            ResetTimesheetSelection();
        }
        UpdateFormatHelp();
    }

    private void DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => OptionsChanged(sender, new RoutedEventArgs());

    private void UpdateFormatHelp()
    {
        FormatHelp.Text = T(FormatCombo.SelectedIndex switch
        { 1 => "Export.CsvHelp", 2 => "Export.JsonHelp", _ => "Export.ExcelHelp" });
        SeparatorCombo.Visibility = FormatCombo.SelectedIndex == (int)ReportExportFormat.Csv
            ? Visibility.Visible : Visibility.Collapsed;
        DescriptionCombo.IsEnabled = DescriptionsCheck.IsChecked == true;
    }

    private void SummaryOptionsChanged(object sender, RoutedEventArgs e) { if (!_applying) ResetSummary(); }

    private void ResetSummary()
    {
        _summary = "";
        SummaryTextBox.Text = "";
        IncludeSummaryCheck.IsChecked = false;
    }

    // sender is the editable draft that will be passed to export.
    // args describes a text change; edits invalidate an existing workbook sample without sending AI requests.
    private void SummaryTextBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_closed || sender is not TextBox editor) return;
        _summary = editor.Text;
    }

    // sender is the wizard navigation control.
    // args identifies the destination whose report page must become visible.
    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (ExportPage is null || TimesheetPage is null) return;
        ExportPage.Visibility = ReferenceEquals(args.SelectedItem, ExportTab) ? Visibility.Visible : Visibility.Collapsed;
        ContentsPage.Visibility = ReferenceEquals(args.SelectedItem, ContentsTab) ? Visibility.Visible : Visibility.Collapsed;
        var summary = ReferenceEquals(args.SelectedItem, SummaryTab);
        var timesheet = ReferenceEquals(args.SelectedItem, TimesheetTab);
        if (SummaryEditorPanel is not null)
        {
            ExcelPreviewGenerateButton.Visibility = Visibility.Visible;
            SummaryEditorPanel.Visibility = summary ? Visibility.Visible : Visibility.Collapsed;
            ReportWelcomePanel.Visibility = ReferenceEquals(args.SelectedItem, ExportTab) ? Visibility.Visible : Visibility.Collapsed;
            ReportTabHelpPanel.Visibility = ReportWelcomePanel.Visibility;
            ContentsDetailsPage.Visibility = ReferenceEquals(args.SelectedItem, ContentsTab) ? Visibility.Visible : Visibility.Collapsed;
        }
        TimesheetPage.Visibility = timesheet ? Visibility.Visible : Visibility.Collapsed;
        UpdateTimesheetPanels();
        ExportButton.Visibility = timesheet ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = timesheet ? Visibility.Collapsed : Visibility.Visible;
        if (timesheet) TimesheetRange.Text = $"{FromPicker.Date:d} — {ToPicker.Date:d} · {TimeZoneText.Text}";
        SummaryPage.Visibility = summary ? Visibility.Visible : Visibility.Collapsed;
    }

    // sender is the scrolling report surface beneath the fixed title bar.
    // args provides the visible height so the draft fills the page without expanding to fit all its text.
    private void BodyScrollViewer_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (SummaryEditorPanel is not null)
            SummaryEditorPanel.Height = Math.Max(420, args.NewSize.Height - 40);
    }

    private void SetPeriod(DateOnly from, DateOnly to)
    {
        _applying = true;
        FromPicker.Date = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue)); ToPicker.Date = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue));
        _applying = false; OptionsChanged(this, new RoutedEventArgs());
    }

    // sender is the footer whose actions reflow as the window narrows.
    // e provides the available footer width for the responsive arrangement.
    private void FooterGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 900;
        Grid.SetColumnSpan(PreferenceActions, stacked ? 4 : 1);
        Grid.SetRow(CloseButton, stacked ? 1 : 0);
        Grid.SetRow(ExcelPreviewGenerateButton, stacked ? 1 : 0);
        Grid.SetRow(ExportButton, stacked ? 1 : 0);
    }
    private void Today_Click(object sender, RoutedEventArgs e) { if (_setup is { } setup) SetPeriod(setup.Options.ToInclusive, setup.Options.ToInclusive); }
    private void Week_Click(object sender, RoutedEventArgs e) { if (_setup is { } setup) SetPeriod(setup.Options.ToInclusive.AddDays(-6), setup.Options.ToInclusive); }
    private void Month_Click(object sender, RoutedEventArgs e) { if (_setup is { } setup) SetPeriod(new(setup.Options.ToInclusive.Year, setup.Options.ToInclusive.Month, 1), setup.Options.ToInclusive); }

    private async void Save_Click(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        var result = await _application.SaveReportExportPreferencesAsync(CollectOptions(), token);
        ShowStatus(result.Succeeded ? "Export.PresetSaved" : result.MessageKey, result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    });

    private async void SummaryInfo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _application.OpenProductLinkAsync("report-summary", _lifetime.Token);
            if (!result.Succeeded) ShowStatus(result.MessageKey, InfoBarSeverity.Error);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { ShowStatus("ProductLinkUnavailable", InfoBarSeverity.Error); }
    }

    // sender identifies the summary generation command.
    // e describes the click that starts summary generation.
    private async void Generate_Click(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        var status = await _application.GetAiStatusAsync(token);
        if (!status.Succeeded || status.Value is null) { ShowStatus(status.MessageKey, InfoBarSeverity.Error); return; }
        ProviderText.Text = status.Value.Provider + " · "
            + ReportSummaryModelPolicy.Resolve(status.Value.Provider, status.Value.Model);
        var request = new ReportSummaryRequest(CollectOptions(), (ReportSummaryGrouping)GroupingCombo.SelectedIndex,
            DetailedCheck.IsChecked == true, SummaryExcerptCheck.IsChecked == true, SummaryFullDescriptionCheck.IsChecked == true,
            SummaryOcrCheck.IsChecked == true, SummaryTitlesCheck.IsChecked == true);
        var result = await _application.GenerateReportSummaryAsync(request, token);
        if (_closed) return;
        if (!result.Succeeded || result.Value is null)
        {
            var size = result.Issues.FirstOrDefault(issue => issue.Code == "export.summary_too_large");
            if (size?.ActualLength is { } actualLength && size.Limit is { } limit)
            {
                StatusBar.Message = _strings.Format("Export.SummaryTooLarge", actualLength.ToString("N0", _strings.Culture), limit.ToString("N0", _strings.Culture));
                StatusBar.Severity = InfoBarSeverity.Error;
                StatusBar.IsOpen = true;
            }
            else ShowStatus(result.MessageKey, InfoBarSeverity.Error);
            return;
        }
        _summary = result.Value.Text;
        SummaryTextBox.Text = _summary;
        SummaryTextBox.SelectionStart = 0;
        SummaryTextBox.SelectionLength = 0;
        ProviderText.Text = result.Value.Provider + " · " + result.Value.Model;
        IncludeSummaryCheck.IsChecked = true;
        ShowStatus("Export.SummaryReady", InfoBarSeverity.Success);
    });

    private async void Export_Click(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        var access = await _application.GetFeatureAccessAsync(token);
        if (!access.Succeeded || access.Value is null) { ShowStatus("Premium.Error", InfoBarSeverity.Error); return; }
        if (!FeatureCatalog.IsAllowed(ProductFeature.ReportExport, access.Value)) { await ShowUpgradeAsync(); return; }
        var options = CollectOptions();
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = ReportExportFileNames.SuggestedFileName(options)
        };
        picker.FileTypeChoices.Add(T("Export.Title"), [ReportExportFileNames.Extension(options.Format)]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
        var destination = await picker.PickSaveFileAsync();
        if (destination is null || token.IsCancellationRequested) return;
        var result = await _application.ExportReportAsync(new(options, destination.Path,
            IncludeSummaryCheck.IsChecked == true ? _summary : null, Overwrite: true), token);
        if (result.Code == "feature.premium_required") { await ShowUpgradeAsync(); return; }
        if (!result.Succeeded || result.Value is null) { ShowStatus(result.MessageKey, InfoBarSeverity.Error); return; }
        StatusBar.Message = _strings.Format("Export.SavedTo", result.Value.Path);
        StatusBar.Severity = InfoBarSeverity.Success; StatusBar.IsOpen = true;
    });

    private Task ShowUpgradeAsync() => _messages.ShowPremiumUpgradeAsync(
        _application, this, T, "Export.UpgradeMessage");

    // action performs one cancellable report operation while shared controls are disabled.
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_busy || _closed || _setup is null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = cancellation; _busy = true;
        ExcelPreviewGenerateButton.IsEnabled = false;
        Navigation.IsEnabled = false; ExportButton.IsEnabled = false; SaveButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible; BusyBar.Visibility = Visibility.Visible; StatusBar.IsOpen = false;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { if (!_closed) ShowStatus("Export.Cancelled", InfoBarSeverity.Informational); }
        catch (Exception) { if (!_closed) ShowStatus("Export.Failed", InfoBarSeverity.Error); }
        finally
        {
            _operation = null; _busy = false;
            if (!_closed)
            {
                Navigation.IsEnabled = true; ExportButton.IsEnabled = true; SaveButton.IsEnabled = true;
                ExcelPreviewGenerateButton.IsEnabled = true;
                CancelButton.Visibility = Visibility.Collapsed; BusyBar.Visibility = Visibility.Collapsed;
                if (_closeAfterCancel) Close();
            }
        }
    }

    private void ShowStatus(string key, InfoBarSeverity severity)
    {
        if (_closed) return;
        StatusBar.Message = T(key); StatusBar.Severity = severity; StatusBar.IsOpen = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void Close_Click(object sender, RoutedEventArgs e) { if (_busy) { _closeAfterCancel = true; _operation?.Cancel(); } else Close(); }
    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { e.Handled = true; Close_Click(sender, new RoutedEventArgs()); }
    }
}
