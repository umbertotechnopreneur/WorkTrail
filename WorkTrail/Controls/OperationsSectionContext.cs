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


using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;

namespace WorkTrail.Controls;

/// <summary>Coordinates one passive operations subsection with the shared facade and banner host.</summary>
internal sealed class OperationsSectionContext
{
    private readonly TimedInfoBar _status;
    private readonly Action<bool> _setProgressState;
    private readonly UIElement _interactionRoot;
    private readonly Func<string, string?> _tryTranslate;
    private bool _operationInProgress;

    internal OperationsSectionContext(
        IWorkTrailApplication application,
        MicaDialogService dialogs,
        Window ownerWindow,
        TimedInfoBar status,
        Action<bool> setProgressState,
        UIElement interactionRoot,
        Func<string, string?> tryTranslate)
    {
        Application = application ?? throw new ArgumentNullException(nameof(application));
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        OwnerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _setProgressState = setProgressState ?? throw new ArgumentNullException(nameof(setProgressState));
        _interactionRoot = interactionRoot ?? throw new ArgumentNullException(nameof(interactionRoot));
        _tryTranslate = tryTranslate ?? throw new ArgumentNullException(nameof(tryTranslate));
    }

    internal IWorkTrailApplication Application { get; }

    internal MicaDialogService Dialogs { get; }

    internal Window OwnerWindow { get; }

    /// <summary>Runs a quick request with the subsection's existing inline progress feedback.</summary>
    internal Task<OperationResult<T>?> ExecuteAsync<T>(
        Func<IWorkTrailApplication, CancellationToken, Task<OperationResult<T>>> operation,
        bool showSuccess = true) =>
        ExecuteCoreAsync(operation, showSuccess, showInlineProgress: true);

    /// <summary>Runs a potentially long request behind the shared modal progress surface without an inline spinner.</summary>
    /// <param name="operation">Request executed through the shared application facade.</param>
    /// <param name="title">Localized heading of the progress surface.</param>
    /// <param name="description">Localized explanation of the operation.</param>
    /// <param name="showSuccess">Whether successful completion produces a banner.</param>
    /// <param name="archiveOperationId">Archive job whose phase progress is displayed, if any.</param>
    /// <param name="retentionOperationId">Retention job whose phase progress is displayed, if any.</param>
    internal Task<OperationResult<T>?> ExecuteWithProgressAsync<T>(
        Func<IWorkTrailApplication, CancellationToken, Task<OperationResult<T>>> operation,
        string title,
        string description,
        bool showSuccess = true,
        Guid? archiveOperationId = null,
        Guid? retentionOperationId = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return ExecuteCoreAsync(
            (application, _) => Dialogs.RunWithProgressAsync(
                application,
                OwnerWindow,
                OwnerWindow.Content is FrameworkElement ownerContent
                    ? ownerContent.ActualTheme
                    : throw new InvalidOperationException("Progress dialogs require framework-element owner content."),
                title,
                description,
                operation, archiveOperationId, retentionOperationId),
            showSuccess,
            showInlineProgress: false);
    }

    // operation runs one request through the shared application facade.
    // showSuccess controls whether a successful result displays a notification.
    // showInlineProgress controls the subsection's progress indicator.
    private async Task<OperationResult<T>?> ExecuteCoreAsync<T>(
        Func<IWorkTrailApplication, CancellationToken, Task<OperationResult<T>>> operation,
        bool showSuccess,
        bool showInlineProgress)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_operationInProgress)
        {
            ShowStatus(Translate("Operations.Status.InProgress.Title"), Translate("Operations.Status.InProgress.Message"), InfoBarSeverity.Warning);
            return null;
        }

        var ownerClosed = false;
        void OwnerClosed(object sender, WindowEventArgs args) => ownerClosed = true;
        OwnerWindow.Closed += OwnerClosed;
        _operationInProgress = true;
        try
        {
            _interactionRoot.IsHitTestVisible = false;
            _interactionRoot.Opacity = 0.72;
            if (showInlineProgress)
            {
                _setProgressState(true);
            }
            var result = await operation(Application, CancellationToken.None);
            if (ownerClosed)
            {
                // A closed owner cannot render a late result or ask its caller to update detached controls.
                return null;
            }

            if (result.Succeeded)
            {
                if (showSuccess)
                {
                    ShowStatus(
                        Translate("Operations.Status.Completed.Title"),
                        ResultMessage(result.MessageKey, succeeded: true),
                        InfoBarSeverity.Success);
                }
            }
            else if (result.Code == "feature.premium_required")
            {
                await Dialogs.ShowPremiumUpgradeAsync(Application, OwnerWindow, Translate);
            }
            else
            {
                ShowStatus(
                    Translate("Operations.Status.Failed.Title"),
                    ResultMessage(result.MessageKey, succeeded: false),
                    InfoBarSeverity.Error);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            // Cancellation keeps the subsection interactive and does not infer a successful result.
            if (!ownerClosed)
            {
                ShowStatus(Translate("Operations.Status.Cancelled.Title"), Translate("Operations.Status.Cancelled.Message"), InfoBarSeverity.Warning);
            }
            return null;
        }
        catch (Exception)
        {
            // Runtime failures are rendered without exposing implementation or host details.
            if (!ownerClosed)
            {
                ShowStatus(Translate("Operations.Status.RuntimeUnavailable.Title"), Translate("Operations.Status.RuntimeUnavailable.Message"), InfoBarSeverity.Error);
            }
            return null;
        }
        finally
        {
            try
            {
                if (!ownerClosed)
                {
                    if (showInlineProgress)
                    {
                        _setProgressState(false);
                    }
                    _interactionRoot.Opacity = 1;
                    _interactionRoot.IsHitTestVisible = true;
                }
            }
            finally
            {
                OwnerWindow.Closed -= OwnerClosed;
                _operationInProgress = false;
            }
        }
    }

    internal void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        switch (severity)
        {
            case InfoBarSeverity.Success:
                Dialogs.Notifications.ShowSuccess(_status, title, message);
                break;
            case InfoBarSeverity.Error:
                Dialogs.Notifications.ShowError(_status, title, message);
                break;
            case InfoBarSeverity.Warning:
                Dialogs.Notifications.ShowWarning(_status, title, message);
                break;
            default:
                Dialogs.Notifications.ShowInfo(_status, title, message);
                break;
        }
    }

    internal string ResultMessage(string messageKey, bool succeeded)
    {
        return _tryTranslate(messageKey)
            ?? Translate(succeeded ? "Operations.Result.Success" : "Operations.Result.Failure");
    }

    private string Translate(string key) =>
        _tryTranslate(key)
        ?? throw new KeyNotFoundException($"Missing required localization key '{key}'.");
}
