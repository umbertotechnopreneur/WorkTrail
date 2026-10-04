// SPDX-License-Identifier: MIT

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WorkTrail.Runtime;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Starts WinUI only after enforcing one long-lived WorkTrail process per user.</summary>
public static class Program
{
    private const string MainInstanceKey = "WorkTrail.Main";
    private static readonly object ActivationGate = new();
    private static readonly Queue<RedirectedActivationRequest> PendingActivations = new();
    private static App? _application;

    /// <summary>Redirects long-lived activations before XAML, services, or windows are initialized.</summary>
    /// <param name="arguments">The bootstrap arguments supplied by Windows or the command line.</param>
    [STAThread]
    public static void Main(string[] arguments)
    {
        // Register managed failure boundaries before WinRT, services or the XAML application can fail.
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        try
        {
            Run(arguments);
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Report(exception, "ProcessStartup");
            Environment.ExitCode = 1;
        }
    }

    /// <summary>Logs and displays a terminal managed failure before the runtime ends the process.</summary>
    /// <param name="sender">The application domain that raised the failure.</param>
    /// <param name="eventArgs">The unhandled exception object supplied by the runtime.</param>
    private static void CurrentDomain_UnhandledException(object? sender, System.UnhandledExceptionEventArgs eventArgs)
    {
        var exception = eventArgs.ExceptionObject as Exception
            ?? new InvalidOperationException("The runtime reported an unknown unhandled failure.");
        ApplicationErrorService.Report(exception, "AppDomain");
    }

    /// <summary>Reports unobserved task failures without turning error reporting into another process failure.</summary>
    /// <param name="sender">The task scheduler that raised the notification.</param>
    /// <param name="eventArgs">The aggregate exception and observed state.</param>
    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        eventArgs.SetObserved();
        ApplicationErrorService.Report(eventArgs.Exception, "TaskScheduler", deferDialog: true);
    }

    /// <summary>Initializes WinRT and launches or redirects the application.</summary>
    /// <param name="arguments">The validated-by-routing bootstrap arguments.</param>
    /// <exception cref="Exception">Activation, service initialization or WinUI startup failed.</exception>
    private static void Run(string[] arguments)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (RequiresSingleInstance(arguments))
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            var mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);
            if (!mainInstance.IsCurrent)
            {
                // Redirection is terminal for this process: the registered instance receives the activation and this process creates no runtime or window.
                StartupThreadService.CompleteActivationRedirection(
                    () => mainInstance.RedirectActivationToAsync(activation).AsTask());
                return;
            }

            mainInstance.Activated += MainInstance_Activated;
        }

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            var application = new App();
            lock (ActivationGate)
            {
                // Publication and draining share the event handler's lock so no activation can be stranded after startup.
                _application = application;
                while (PendingActivations.TryDequeue(out var activation))
                {
                    application.HandleRedirectedActivation(activation);
                }
            }
        });
    }

    private static bool RequiresSingleInstance(IReadOnlyList<string> arguments)
    {
        // Invalid bootstrap arguments fail before redirection, including when another instance is already running.
        return LaunchOptions.Parse(arguments).Mode is not (LaunchMode.Cli or LaunchMode.Help or LaunchMode.Version);
    }

    /// <summary>Routes repeated Windows activations and reports callback failures centrally.</summary>
    /// <param name="sender">The registered Windows application instance.</param>
    /// <param name="activation">The activation payload owned by this callback.</param>
    private static void MainInstance_Activated(object? sender, AppActivationArguments activation)
    {
        try
        {
            // Consume the WinRT payload before returning from its callback; queues must retain only managed values.
            var request = CaptureRedirectedActivation(activation);
            lock (ActivationGate)
            {
                if (_application is null)
                {
                    PendingActivations.Enqueue(request);
                    return;
                }

                _application.HandleRedirectedActivation(request);
            }
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Report(exception, "RedirectedActivation");
        }
    }

    // activation owns the Windows payload only for the duration of this callback.
    private static RedirectedActivationRequest CaptureRedirectedActivation(AppActivationArguments activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        var kind = activation.Kind;
        if (kind == ExtendedActivationKind.Protocol
            && activation.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol)
        {
            // Copy the URI while the WinRT callback owns its payload; the app validates fixed actions.
            return new RedirectedActivationRequest(LaunchOptions.Parse([]), kind, protocol.Uri.AbsoluteUri);
        }
        var options = kind switch
        {
            ExtendedActivationKind.Launch when activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch =>
                WindowsLaunchArguments.Parse(launch.Arguments, "WorkTrail.exe"),
            ExtendedActivationKind.StartupTask => StartupActivationPolicy.Apply(LaunchOptions.Parse([]), kind),
            // Invalid payloads must fail on the receiving callback instead of entering the UI queue with tracking defaults.
            _ => throw new ArgumentException("Unsupported redirected WorkTrail activation.", nameof(activation))
        };

        if (options.Mode is not (LaunchMode.Ui or LaunchMode.Background))
        {
            throw new ArgumentException("Unsupported redirected WorkTrail launch mode.", nameof(activation));
        }

        // Copy the only collection so the immutable request owns every value needed after the sender has exited.
        return new RedirectedActivationRequest(
            options with { RemainingArguments = Array.AsReadOnly(options.RemainingArguments.ToArray()) },
            kind);
    }
}

/// <summary>Contains the managed snapshot consumed after a redirected WinRT activation callback returns.</summary>
internal sealed record RedirectedActivationRequest(LaunchOptions Options, ExtendedActivationKind Kind, string? ProtocolUri = null);
