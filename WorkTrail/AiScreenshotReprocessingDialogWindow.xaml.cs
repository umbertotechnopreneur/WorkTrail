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


using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using WorkTrail.Application;
using WorkTrail.Services;
using Windows.System;

namespace WorkTrail;

/// <summary>Shows provider-call-free preflight and deterministic progress for one daily AI screenshot reprocessing job.</summary>
internal sealed partial class AiScreenshotReprocessingDialogWindow : Window
{
    private const int LogicalWidth = 780;
    private const int LogicalHeight = 700;
    private const int LogicalScreenMargin = 24;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IWorkTrailApplication _application;
    private readonly DateOnly _date;
    private readonly LocalizationService _strings;
    private readonly CultureInfo _culture;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly IntPtr _windowHandle;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private AiScreenshotReprocessPlan? _plan;
    private Guid? _jobId;
    private string? _jobStatus;
    private bool _isLoaded;
    private bool _isCompleting;
    private bool _operationPending;

    /// <summary>Creates a passive acrylic surface over the application-level reprocessing use case.</summary>
    internal AiScreenshotReprocessingDialogWindow(
        IWorkTrailApplication application,
        DateOnly date,
        ElementTheme theme,
        LocalizationService strings,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _date = date;
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        _culture = _strings.Culture;
        InitializeComponent();
        Title = T("AiReprocess.WindowTitle");
        RootGrid.RequestedTheme = theme;
        RootGrid.Language = _strings.Language;
        UiLocalization.Apply(RootGrid, _strings);
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_windowHandle));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            static () => Array.Empty<FrameworkElement>());
        _placement = new WindowPlacementService(
            application,
            this,
            _appWindow,
            WindowStateKeys.AiScreenshotReprocessing,
            LogicalWidth,
            LogicalHeight,
            LogicalScreenMargin,
            ownerAppWindow.Id);
        WindowInteropService.SetOwner(_windowHandle, ownerHandle);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        ApplyLocalizedContent();
        Closed += AiScreenshotReprocessingDialogWindow_Closed;
    }

    /// <summary>Activates the queued acrylic surface and completes after the window closes.</summary>
    internal Task ShowAsync()
    {
        WindowInteropService.MakeTopmostWithoutActivation(_windowHandle);
        Activate();
        return _completion.Task;
    }

    internal IntPtr WindowHandle => _windowHandle;

    internal void DisposePlacement()
    {
        _placement.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        _placement.ApplyDefaultBounds(RootGrid);
        await _placement.RestoreOrCenterAsync(RootGrid, CancellationToken.None);
        CloseButton.Focus(FocusState.Programmatic);
        await PreviewAsync();
    }

    private void ApplyLocalizedContent()
    {
        SelectedDateValueText.Text = _date.ToString("D", _culture);
        StartButton.Content = string.Format(_culture, T("AiReprocess.Start"), 0);
        AutomationProperties.SetName(RootGrid, T("AiReprocess.Title"));
        AutomationProperties.SetName(DialogTitleText, T("AiReprocess.Title"));
        AutomationProperties.SetName(DialogSubtitleText, T("AiReprocess.Subtitle"));
        AutomationProperties.SetName(StartButton, StartButton.Content?.ToString() ?? T("AiReprocess.Start"));
        AutomationProperties.SetName(PauseResumeButton, T("AiReprocess.Pause"));
        AutomationProperties.SetName(CloseButton, T("AiReprocess.Close"));
    }

    private async Task PreviewAsync()
    {
        SetOperationPending(true);
        PreflightStateText.Text = T("AiReprocess.Loading");
        PreflightProgressBar.Visibility = Visibility.Visible;
        PreflightProgressBar.IsIndeterminate = true;
        OperationStatusText.Text = string.Empty;

        try
        {
            var result = await _application.PreviewAiScreenshotReprocessingAsync(
                new AiScreenshotReprocessRequest(_date),
                _lifetimeCancellation.Token);
            if (_isCompleting)
            {
                return;
            }

            if (!result.Succeeded || result.Value is null || !IsValidPlan(result.Value))
            {
                ShowOperationFailure(result.Code);
                return;
            }

            _plan = result.Value;
            RenderPlan(result.Value);
            if (result.Value.ActiveJobId is { } activeJobId)
            {
                await LoadActiveJobAsync(activeJobId);
            }
        }
        catch (OperationCanceledException) when (_isCompleting)
        {
            // Closing cancels only this presentation request; a previously started Core job is unaffected.
        }
        catch (Exception)
        {
            if (!_isCompleting)
            {
                ShowOperationFailure("ai.screenshot_reprocess.preview.unavailable");
            }
        }
        finally
        {
            if (!_isCompleting)
            {
                PreflightProgressBar.IsIndeterminate = false;
                PreflightProgressBar.Visibility = Visibility.Collapsed;
                SetOperationPending(false);
            }
        }
    }

    private void RenderPlan(AiScreenshotReprocessPlan plan)
    {
        PreflightStateText.Text = T("AiReprocess.Title");
        ProviderModelValueText.Text = $"{plan.Provider} · {plan.Model}";
        MissingScreenshotsValueText.Text = plan.MissingDescriptionScreenshotCount.ToString("N0", _culture);
        MissingCapturesValueText.Text = plan.MissingDescriptionCaptureCount.ToString("N0", _culture);
        MaximumRequestsValueText.Text = plan.ProcessableTodayCaptureCount.ToString("N0", _culture);
        EligibleBreakdownText.Text = string.Format(
            _culture,
            T("AiReprocess.Eligible"),
            plan.EligibleScreenshotCount,
            plan.EligibleCaptureCount);
        ExcludedBreakdownText.Text = string.Format(
            _culture,
            T("AiReprocess.Excluded"),
            plan.MissingFileCount,
            plan.PrivacyBlockedCaptureCount,
            plan.MissingMetadataCaptureCount);
        QuotaBreakdownText.Text = string.Join(
            " · ",
            string.Format(
                _culture,
                T("AiReprocess.QuotaValue"),
                plan.DailyAnalysisCount,
                plan.DailyAnalysisLimit,
                plan.RemainingDailyAllowance,
                plan.ProcessableTodayCaptureCount,
                plan.ProcessableTodayScreenshotCount),
            string.Format(_culture, T("AiReprocess.EstimatedCost"), plan.EstimatedMaximumCostTodayUsd));
        PreflightCountsGrid.Visibility = Visibility.Visible;
        PreflightBreakdownGrid.Visibility = Visibility.Visible;

        StartButton.Content = string.Format(_culture, T("AiReprocess.Start"), plan.ProcessableTodayScreenshotCount);
        AutomationProperties.SetName(StartButton, StartButton.Content?.ToString() ?? T("AiReprocess.Start"));
        StartButton.IsEnabled = plan.CanStart && !_operationPending;
        BlockingReasonText.Text = plan.CanStart ? string.Empty : ResolveBlockingReason(plan.BlockingReason);
        BlockingReasonText.Visibility = plan.CanStart ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(
            PreflightCountsGrid,
            $"{MissingScreenshotsValueText.Text} {T("AiReprocess.Screenshots")}; " +
            $"{MissingCapturesValueText.Text} {T("AiReprocess.Captures")}; " +
            $"{MaximumRequestsValueText.Text} {T("AiReprocess.Requests")}");
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_plan is not { CanStart: true } plan || _operationPending)
        {
            return;
        }

        SetOperationPending(true);
        OperationStatusText.Text = T("AiReprocess.Loading");
        try
        {
            var result = await _application.StartAiScreenshotReprocessingAsync(plan.PlanId, _lifetimeCancellation.Token);
            if (_isCompleting)
            {
                return;
            }

            if (!result.Succeeded || result.Value is null || !IsValidSnapshot(result.Value))
            {
                if (string.Equals(result.Code, "ai.screenshot_reprocess.plan.stale", StringComparison.Ordinal))
                {
                    await PreviewAsync();
                    OperationStatusText.Text = T("AiReprocess.ScopeChanged");
                    return;
                }

                ShowOperationFailure(result.Code);
                return;
            }

            _jobId = result.Value.JobId;
            ShowProgress(result.Value);
        }
        catch (OperationCanceledException) when (_isCompleting)
        {
            // Window closure cancels the frontend request only; Core owns any job already created.
        }
        catch (Exception)
        {
            if (!_isCompleting)
            {
                ShowOperationFailure("ai.screenshot_reprocess.start.unavailable");
            }
        }
        finally
        {
            if (!_isCompleting)
            {
                SetOperationPending(false);
            }
        }
    }

    private async Task PollJobAsync(Guid jobId)
    {
        try
        {
            while (!_isCompleting && !IsTerminal(_jobStatus))
            {
                await Task.Delay(PollInterval, _lifetimeCancellation.Token);
                var result = await _application.GetAiScreenshotReprocessingJobAsync(jobId, _lifetimeCancellation.Token);
                if (_isCompleting)
                {
                    return;
                }

                if (!result.Succeeded || result.Value is null || !IsValidSnapshot(result.Value) || result.Value.JobId != jobId)
                {
                    ShowOperationFailure(result.Code);
                    continue;
                }

                OperationStatusText.Text = string.Empty;
                RenderJob(result.Value);
            }
        }
        catch (OperationCanceledException) when (_isCompleting)
        {
            // Polling is presentation-owned and stops without pausing or cancelling the Core job.
        }
        catch (Exception)
        {
            if (!_isCompleting)
            {
                ShowOperationFailure("ai.screenshot_reprocess.status.unavailable");
            }
        }
    }

    private async Task LoadActiveJobAsync(Guid jobId)
    {
        var result = await _application.GetAiScreenshotReprocessingJobAsync(jobId, _lifetimeCancellation.Token);
        if (_isCompleting)
        {
            return;
        }

        if (!result.Succeeded || result.Value is null || !IsValidSnapshot(result.Value) || result.Value.JobId != jobId)
        {
            ShowOperationFailure(result.Code);
            return;
        }

        _jobId = jobId;
        ShowProgress(result.Value);
    }

    private void ShowProgress(AiScreenshotReprocessJobSnapshot snapshot)
    {
        SelectedDateValueText.Text = snapshot.Date.ToString("D", _culture);
        PreflightPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        StartButton.Visibility = Visibility.Collapsed;
        PauseResumeButton.Visibility = Visibility.Visible;
        RenderJob(snapshot);
        if (!IsTerminal(snapshot.Status))
        {
            _ = PollJobAsync(snapshot.JobId);
        }
    }

    private async void PauseResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_jobId is not { } jobId || _operationPending)
        {
            return;
        }

        var resume = _jobStatus is AiScreenshotReprocessJobStatuses.PausedByUser or AiScreenshotReprocessJobStatuses.PausedDailyQuota;
        if (!resume && _jobStatus != AiScreenshotReprocessJobStatuses.Running)
        {
            return;
        }

        SetOperationPending(true);
        try
        {
            var result = resume
                ? await _application.ResumeAiScreenshotReprocessingAsync(jobId, _lifetimeCancellation.Token)
                : await _application.PauseAiScreenshotReprocessingAsync(jobId, _lifetimeCancellation.Token);
            if (_isCompleting)
            {
                return;
            }

            if (!result.Succeeded || result.Value is null || !IsValidSnapshot(result.Value) || result.Value.JobId != jobId)
            {
                ShowOperationFailure(result.Code);
                return;
            }

            OperationStatusText.Text = string.Empty;
            RenderJob(result.Value);
        }
        catch (OperationCanceledException) when (_isCompleting)
        {
            // The background job retains its last Core-owned state when this presentation closes.
        }
        catch (Exception)
        {
            if (!_isCompleting)
            {
                ShowOperationFailure("ai.screenshot_reprocess.command.unavailable");
            }
        }
        finally
        {
            if (!_isCompleting)
            {
                SetOperationPending(false);
            }
        }
    }

    private void RenderJob(AiScreenshotReprocessJobSnapshot snapshot)
    {
        _jobStatus = snapshot.Status;
        JobStateText.Text = ResolveJobStatus(snapshot.Status);
        JobDateText.Text = snapshot.Date.ToString("D", _culture);
        JobProgressBar.Maximum = Math.Max(1, snapshot.TotalCaptures);
        JobProgressBar.Value = Math.Min(snapshot.CompletedCaptures, snapshot.TotalCaptures);
        CompletedCapturesText.Text = FormatCount("AiReprocess.CompletedCount", snapshot.CompletedCaptures, snapshot.TotalCaptures);
        RemainingCapturesText.Text = FormatCount("AiReprocess.RemainingCount", snapshot.RemainingCaptures);
        SucceededCapturesText.Text = FormatCount("AiReprocess.SucceededCount", snapshot.SucceededCaptures);
        SkippedCapturesText.Text = FormatCount("AiReprocess.SkippedCount", snapshot.SkippedCaptures);
        FailedCapturesText.Text = FormatCount("AiReprocess.FailedCount", snapshot.FailedCaptures);
        CompletedScreenshotsText.Text = FormatCount("AiReprocess.CompletedCount", snapshot.CompletedScreenshots, snapshot.TotalScreenshots);
        RemainingScreenshotsText.Text = FormatCount("AiReprocess.RemainingCount", snapshot.RemainingScreenshots);
        SucceededScreenshotsText.Text = FormatCount("AiReprocess.SucceededCount", snapshot.SucceededScreenshots);
        SkippedScreenshotsText.Text = FormatCount("AiReprocess.SkippedCount", snapshot.SkippedScreenshots);
        FailedScreenshotsText.Text = FormatCount("AiReprocess.FailedCount", snapshot.FailedScreenshots);
        CurrentItemText.Text = FormatCurrentItem(snapshot.CurrentItem, snapshot.TotalCaptures);
        AutomationProperties.SetName(
            JobProgressBar,
            string.Format(
                _culture,
                T("AiReprocess.ProgressAccessible"),
                snapshot.CompletedCaptures,
                snapshot.TotalCaptures,
                snapshot.RemainingCaptures,
                snapshot.CompletedScreenshots,
                snapshot.TotalScreenshots,
                snapshot.RemainingScreenshots));

        var paused = snapshot.Status is AiScreenshotReprocessJobStatuses.PausedByUser or AiScreenshotReprocessJobStatuses.PausedDailyQuota;
        PauseResumeButton.Content = T(paused ? "AiReprocess.Resume" : "AiReprocess.Pause");
        AutomationProperties.SetName(PauseResumeButton, PauseResumeButton.Content?.ToString() ?? string.Empty);
        PauseResumeButton.IsEnabled = !_operationPending &&
            snapshot.Status is AiScreenshotReprocessJobStatuses.Running
                or AiScreenshotReprocessJobStatuses.PausedByUser
                or AiScreenshotReprocessJobStatuses.PausedDailyQuota;
        PauseResumeButton.Visibility = IsTerminal(snapshot.Status) ? Visibility.Collapsed : Visibility.Visible;
    }

    private string FormatCurrentItem(AiScreenshotReprocessCurrentItem? item, int totalCaptures)
    {
        if (item is null)
        {
            return T("AiReprocess.NoCurrentItem");
        }

        var origin = item.CaptureOrigin.Trim().ToLowerInvariant() switch
        {
            ScreenshotCaptureOrigins.Manual => T("Screenshots.Origin.Manual"),
            ScreenshotCaptureOrigins.Scheduled => T("Screenshots.Origin.Scheduled"),
            _ => T("AiReprocess.Unavailable")
        };
        var application = string.IsNullOrWhiteSpace(item.Application) ? null : item.Application.Trim();
        var summary = $"{item.Ordinal.ToString("N0", _culture)}/{totalCaptures.ToString("N0", _culture)} · " +
            $"{item.CapturedAt.ToLocalTime().ToString("g", _culture)} · {origin} · " +
            $"{item.ScreenshotCount.ToString("N0", _culture)} {T("AiReprocess.Screenshots")}";
        return application is null ? summary : $"{summary} · {application}";
    }

    private string ResolveJobStatus(string status) => status switch
    {
        AiScreenshotReprocessJobStatuses.Running => T("AiReprocess.Running"),
        AiScreenshotReprocessJobStatuses.PauseRequested => T("AiReprocess.Pausing"),
        AiScreenshotReprocessJobStatuses.PausedByUser => T("AiReprocess.PausedByUser"),
        AiScreenshotReprocessJobStatuses.PausedDailyQuota => T("AiReprocess.PausedQuota"),
        AiScreenshotReprocessJobStatuses.Completed => T("AiReprocess.Completed"),
        AiScreenshotReprocessJobStatuses.CompletedWithErrors => T("AiReprocess.CompletedWithErrors"),
        AiScreenshotReprocessJobStatuses.Failed => T("AiReprocess.Failed"),
        _ => T("AiReprocess.Unavailable")
    };

    private string ResolveBlockingReason(string? reason) => reason switch
    {
        "job_active" => T("AiReprocess.BlockingReason.JobActive"),
        "ai_disabled" => T("AiReprocess.BlockingReason.AiDisabled"),
        "ai_configuration_invalid" => T("AiReprocess.BlockingReason.AiConfigurationInvalid"),
        "no_eligible_captures" => T("AiReprocess.BlockingReason.NoEligibleCaptures"),
        "daily_quota" => T("AiReprocess.BlockingReason.DailyQuota"),
        _ => T("AiReprocess.BlockingReason.Unavailable")
    };

    private string FormatCount(string key, params object[] values) => string.Format(_culture, T(key), values);

    private void SetOperationPending(bool pending)
    {
        _operationPending = pending;
        OperationProgressRing.IsActive = pending;
        OperationProgressRing.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        StartButton.IsEnabled = !pending && _plan is { CanStart: true };
        if (_jobId is not null)
        {
            PauseResumeButton.IsEnabled = !pending &&
                _jobStatus is AiScreenshotReprocessJobStatuses.Running
                    or AiScreenshotReprocessJobStatuses.PausedByUser
                    or AiScreenshotReprocessJobStatuses.PausedDailyQuota;
        }
    }

    private void ShowOperationFailure(string code)
    {
        OperationStatusText.Text = string.Format(_culture, T("AiReprocess.Unavailable"), code);
        StartButton.IsEnabled = false;
    }

    private bool IsValidPlan(AiScreenshotReprocessPlan plan) =>
        plan.PlanId != Guid.Empty &&
        (plan.ActiveJobId is null || plan.ActiveJobId != Guid.Empty) &&
        plan.Date == _date &&
        plan.ExpiresAt > DateTimeOffset.UtcNow &&
        plan.MissingDescriptionScreenshotCount >= 0 &&
        plan.MissingDescriptionCaptureCount >= 0 &&
        plan.EligibleScreenshotCount >= 0 &&
        plan.EligibleCaptureCount >= 0 &&
        plan.EligibleScreenshotCount <= plan.MissingDescriptionScreenshotCount &&
        plan.EligibleCaptureCount <= plan.MissingDescriptionCaptureCount &&
        plan.MissingFileCount >= 0 &&
        plan.PrivacyBlockedCaptureCount >= 0 &&
        plan.MissingMetadataCaptureCount >= 0 &&
        plan.DailyAnalysisCount >= 0 &&
        plan.DailyAnalysisLimit >= 0 &&
        plan.RemainingDailyAllowance >= 0 &&
        plan.RemainingDailyAllowance == Math.Max(0, plan.DailyAnalysisLimit - plan.DailyAnalysisCount) &&
        plan.ProcessableTodayCaptureCount >= 0 &&
        plan.ProcessableTodayCaptureCount <= plan.EligibleCaptureCount &&
        plan.ProcessableTodayCaptureCount <= plan.RemainingDailyAllowance &&
        plan.ProcessableTodayScreenshotCount >= 0 &&
        plan.ProcessableTodayScreenshotCount <= plan.EligibleScreenshotCount &&
        plan.EstimatedMaximumCostTodayUsd >= 0m &&
        !string.IsNullOrWhiteSpace(plan.Provider) &&
        !string.IsNullOrWhiteSpace(plan.Model);

    private bool IsValidSnapshot(AiScreenshotReprocessJobSnapshot snapshot) =>
        snapshot.JobId != Guid.Empty &&
        snapshot.TotalCaptures >= 0 &&
        snapshot.TotalScreenshots >= 0 &&
        snapshot.CompletedCaptures == snapshot.SucceededCaptures + snapshot.SkippedCaptures + snapshot.FailedCaptures &&
        snapshot.CompletedScreenshots == snapshot.SucceededScreenshots + snapshot.SkippedScreenshots + snapshot.FailedScreenshots &&
        snapshot.RemainingCaptures == snapshot.TotalCaptures - snapshot.CompletedCaptures &&
        snapshot.RemainingScreenshots == snapshot.TotalScreenshots - snapshot.CompletedScreenshots &&
        snapshot.CompletedCaptures >= 0 &&
        snapshot.CompletedScreenshots >= 0 &&
        snapshot.RemainingCaptures >= 0 &&
        snapshot.RemainingScreenshots >= 0 &&
        snapshot.SucceededCaptures >= 0 &&
        snapshot.SucceededScreenshots >= 0 &&
        snapshot.SkippedCaptures >= 0 &&
        snapshot.SkippedScreenshots >= 0 &&
        snapshot.FailedCaptures >= 0 &&
        snapshot.FailedScreenshots >= 0 &&
        IsKnownStatus(snapshot.Status) &&
        (snapshot.CurrentItem is null ||
            (snapshot.CurrentItem.Ordinal > 0 &&
             snapshot.CurrentItem.Ordinal <= Math.Max(1, snapshot.TotalCaptures) &&
             snapshot.CurrentItem.ScreenshotCount > 0));

    private static bool IsKnownStatus(string? status) => status is
        AiScreenshotReprocessJobStatuses.Running or
        AiScreenshotReprocessJobStatuses.PauseRequested or
        AiScreenshotReprocessJobStatuses.PausedByUser or
        AiScreenshotReprocessJobStatuses.PausedDailyQuota or
        AiScreenshotReprocessJobStatuses.Completed or
        AiScreenshotReprocessJobStatuses.CompletedWithErrors or
        AiScreenshotReprocessJobStatuses.Failed;

    private static bool IsTerminal(string? status) => status is
        AiScreenshotReprocessJobStatuses.Completed or
        AiScreenshotReprocessJobStatuses.CompletedWithErrors or
        AiScreenshotReprocessJobStatuses.Failed;

    private string T(string key) => _strings.Translate(key);

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await CompleteAsync();

    private async void RootGrid_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        await CompleteAsync();
    }

    private async Task CompleteAsync()
    {
        if (_isCompleting)
        {
            return;
        }

        _isCompleting = true;
        StartButton.IsEnabled = false;
        PauseResumeButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        _lifetimeCancellation.Cancel();
        _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        Close();
    }

    private void AiScreenshotReprocessingDialogWindow_Closed(object sender, WindowEventArgs args)
    {
        _isCompleting = true;
        _lifetimeCancellation.Cancel();
        _titleBar.Dispose();
        _completion.TrySetResult();
    }

}
