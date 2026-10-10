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


using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Taskbar;

/// <summary>Owns the dedicated WPF dispatcher that renders the transparent taskbar surface while Core retains taskbar interop ownership.</summary>
public sealed class TaskbarWidgetSurface : IDisposable
{
    private readonly IWorkTrailApplication _application;
    private readonly DashboardRefreshCoordinator _dashboardRefreshCoordinator;
    private readonly TaskbarWidgetHost _host;
    private readonly ILogger<TaskbarWidgetSurface> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TaskCompletionSource _startupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private System.Windows.Application? _wpfApplication;
    private Dispatcher? _dispatcher;
    private DispatcherTimer? _recoveryTimer;
    private TaskbarWidgetWindow? _window;
    private Exception? _startupException;
    private AppSettings? _settings;
    private string _position = TaskbarWidgetPositions.Left;
    private int _disposed;
    private int _hostDisposed;

    /// <summary>Starts the dedicated WPF dispatcher and creates the alpha-capable taskbar surface.</summary>
    public TaskbarWidgetSurface(IWorkTrailApplication application, DashboardRefreshCoordinator dashboardRefreshCoordinator, TaskbarWidgetHost host, ILogger<TaskbarWidgetSurface>? logger = null)
    {
        _application = application;
        _dashboardRefreshCoordinator = dashboardRefreshCoordinator ?? throw new ArgumentNullException(nameof(dashboardRefreshCoordinator));
        _host = host;
        _logger = logger ?? NullLogger<TaskbarWidgetSurface>.Instance;
        _thread = new Thread(DispatcherThreadMain)
        {
            IsBackground = true,
            Name = "WorkTrail taskbar surface"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_startupCompletion.Task.Wait(TimeSpan.FromSeconds(10)))
        {
            Dispose();
            throw new TimeoutException("Timed out while creating the WorkTrail taskbar surface.");
        }

        if (_startupException is not null)
        {
            Dispose();
            throw new InvalidOperationException("Unable to create the WorkTrail taskbar surface.", _startupException);
        }
    }

    /// <summary>Occurs on the WPF dispatcher when the user asks to open the WinUI player.</summary>
    public event EventHandler? FlyoutRequested;

    /// <summary>Applies persisted presentation settings on the WPF dispatcher.</summary>
    public void ApplySettings(AppSettings settings) => Invoke(window =>
    {
        _settings = settings;
        window.ApplySettings(settings);
    });

    /// <summary>Parents the hidden transparent HWND into the taskbar and only then shows its WPF surface.</summary>
    public bool Attach(string position) => Invoke(window =>
    {
        _position = position;
        return TryAttachPreparedWindow(window, startRecoveryTimer: true);
    });

    /// <summary>Updates only the Core host placement while preserving the WPF surface and shared runtime.</summary>
    public void Configure(string position) => Invoke(window =>
    {
        _position = position;
        if (window.IsVisible)
        {
            _host.Configure(position);
        }
        else
        {
            _ = TryAttachPreparedWindow(window, startRecoveryTimer: false);
        }
    });

    /// <summary>Closes the alpha surface and stops its WPF dispatcher after Core removes taskbar parenting.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetimeCancellation.Cancel();
        var dispatcher = _dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
        {
            try
            {
                // Never synchronously invoke a dispatcher whose message loop may still be starting.
                // The queued shutdown is also observed by the startup cancellation checks below.
                _ = dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(ShutdownOnDispatcher));
            }
            catch (InvalidOperationException)
            {
                // Dispatcher shutdown won the race; its thread-level finally block owns cleanup.
            }
        }

        if (!_thread.Join(TimeSpan.FromSeconds(5)))
        {
            // A blocked WPF startup must not retain Core taskbar interop after the public surface is disposed.
            DisposeHost();
            return;
        }

        _lifetimeCancellation.Dispose();
    }

    private void DispatcherThreadMain()
    {
        try
        {
            _wpfApplication = new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            _dispatcher = Dispatcher.CurrentDispatcher;
            if (_lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            PrepareReplacementWindow();
            if (_lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            // Completion runs inside the active message loop, so callers can never Invoke a pre-loop dispatcher.
            _ = _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(SignalDispatcherStarted));
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            _startupException = exception;
        }
        finally
        {
            CleanupAfterDispatcherExit();
            _startupCompletion.TrySetResult();
        }
    }

    private void SignalDispatcherStarted()
    {
        _startupCompletion.TrySetResult();
        if (_lifetimeCancellation.IsCancellationRequested)
        {
            ShutdownOnDispatcher();
        }
    }

    private void ShutdownOnDispatcher()
    {
        _recoveryTimer?.Stop();
        DisposeHost();
        CloseCurrentWindow();
        _wpfApplication?.Shutdown();
    }

    private void CleanupAfterDispatcherExit()
    {
        try
        {
            _recoveryTimer?.Stop();
            CloseCurrentWindow();
        }
        catch (Exception exception)
        {
            // The dispatcher is already exiting; log the failed presentation cleanup and still release Core interop.
            _logger.LogWarning(exception, "Taskbar widget WPF cleanup failed during dispatcher shutdown.");
        }
        finally
        {
            DisposeHost();
        }
    }

    private void DisposeHost()
    {
        if (Interlocked.Exchange(ref _hostDisposed, 1) == 0)
        {
            _host.Dispose();
        }
    }

    private void CloseCurrentWindow()
    {
        if (_window is not { } window)
        {
            return;
        }

        _window = null;
        window.FlyoutRequested -= Window_FlyoutRequested;
        window.Close();
    }

    private void StartRecoveryTimer()
    {
        if (_recoveryTimer is not null)
        {
            return;
        }

        _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _recoveryTimer.Tick += (_, _) => RecoverFromExplorerChanges();
        _recoveryTimer.Start();
    }

    private bool TryAttachPreparedWindow(TaskbarWidgetWindow window, bool startRecoveryTimer)
    {
        _logger.LogInformation("Attaching taskbar widget. Handle={Handle} Position={Position}", window.Handle, _position);
        var attached = _host.Attach(window.Handle, _position);
        if (attached)
        {
            // A layered WPF HWND must be parented before its first visible frame; otherwise the Windows 11
            // taskbar can retain the alpha hit-test surface without compositing any of its pixels.
            window.Show();
            if (startRecoveryTimer)
            {
                StartRecoveryTimer();
            }
        }

        _logger.LogInformation("Taskbar widget attach result={Attached}.", attached);
        return attached;
    }

    private void RecoverFromExplorerChanges()
    {
        try
        {
            if (!_host.HasValidWidgetHandle)
            {
                _logger.LogInformation("Explorer released the taskbar widget HWND; recreating its WPF surface.");
                PrepareReplacementWindow();
            }

            var window = GetWindow();
            if (!window.IsVisible)
            {
                _ = TryAttachPreparedWindow(window, startRecoveryTimer: false);
                return;
            }

            _ = _host.Recover();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Taskbar widget recovery attempt failed.");
        }
    }

    private void PrepareReplacementWindow()
    {
        CloseCurrentWindow();

        // Position the hidden surface on the taskbar monitor before realizing its HWND so WPF adopts the correct DPI.
        var bounds = TaskbarWidgetHost.GetDesiredBounds(_position);
        var replacement = new TaskbarWidgetWindow(_application, _dashboardRefreshCoordinator);
        replacement.FlyoutRequested += Window_FlyoutRequested;
        if (_settings is not null)
        {
            replacement.ApplySettings(_settings);
        }

        replacement.PrepareForTaskbar(bounds);
        _window = replacement;
        _logger.LogInformation("Taskbar widget hidden WPF HWND created. Handle={Handle} Bounds=({X},{Y},{Width},{Height}) Scale={Scale}", replacement.Handle, bounds.ScreenX, bounds.ScreenY, bounds.Width, bounds.Height, bounds.Scale);
    }

    private void Window_FlyoutRequested(object? sender, EventArgs e) => FlyoutRequested?.Invoke(this, EventArgs.Empty);

    private void Invoke(Action<TaskbarWidgetWindow> action)
    {
        var dispatcher = GetDispatcher();
        if (dispatcher.CheckAccess())
        {
            action(GetWindow());
            return;
        }

        dispatcher.Invoke(() => action(GetWindow()));
    }

    private TResult Invoke<TResult>(Func<TaskbarWidgetWindow, TResult> action)
    {
        var dispatcher = GetDispatcher();
        return dispatcher.CheckAccess()
            ? action(GetWindow())
            : dispatcher.Invoke(() => action(GetWindow()));
    }

    private Dispatcher GetDispatcher() => _dispatcher is { HasShutdownStarted: false } dispatcher
        ? dispatcher
        : throw new ObjectDisposedException(nameof(TaskbarWidgetSurface));

    private TaskbarWidgetWindow GetWindow() => _window ?? throw new InvalidOperationException("The taskbar surface has not been initialized.");
}
