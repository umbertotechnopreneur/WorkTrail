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


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sentry;
using Serilog;
using WorkTrail.Application;

namespace WorkTrail.Runtime;

/// <summary>Creates the process-wide logging pipeline used by both packaged and unpackaged launches.</summary>
internal static class LoggingBootstrapper
{
    private const int RetainedFileCount = 15;
    private static readonly TimeSpan RetainedFileTime = TimeSpan.FromDays(15);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
    private static int _shutdownStarted;

    /// <summary>Builds the application service provider and configures console/file sinks.</summary>
    internal static ServiceProvider CreateServiceProvider()
    {
        var resolved = new ObservabilityConfigurationService().Load();
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console();
        var fileLoggingEnabled = false;

        try
        {
            if (resolved.LogDirectory is not null)
            {
                configuration.WriteTo.File(
                    Path.Combine(resolved.LogDirectory, "worktrail-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFileCount,
                    retainedFileTimeLimit: RetainedFileTime,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1));
                fileLoggingEnabled = true;
            }
        }
        catch (Exception exception)
        {
            // A read-only or unavailable profile must not turn diagnostics into a startup failure.
            System.Diagnostics.Debug.WriteLine($"WorkTrail file logging unavailable: {exception.GetType().Name}");
        }

        Log.Logger = configuration.CreateLogger();

        var sentryDsn = resolved.SentryDsn;
        var sentryStatus = resolved.SentryStatus;
        ServiceProvider serviceProvider;
        try
        {
            serviceProvider = BuildServiceProvider(sentryDsn, fileLoggingEnabled, sentryStatus, resolved.SentryEnvironment);
        }
        catch (Exception exception) when (sentryDsn is not null)
        {
            // A Sentry initialization fault must never disable the local console/file pipeline.
            System.Diagnostics.Debug.WriteLine($"WorkTrail Sentry logging unavailable: {exception.GetType().Name}");
            serviceProvider = BuildServiceProvider(sentryDsn: null, fileLoggingEnabled, sentryStatus: "unavailable", resolved.SentryEnvironment);
        }

        if (resolved.SentryStatus == "invalid")
        {
            serviceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(LoggingBootstrapper))
                .LogWarning("Sentry is disabled because its DSN or environment configuration is malformed.");
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown(serviceProvider);
        return serviceProvider;
    }

    /// <summary>Flushes remote events within a fixed budget and releases all logging providers.</summary>
    internal static async Task ShutdownAsync(ServiceProvider serviceProvider)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        try
        {
            if (SentrySdk.IsEnabled)
            {
                // CLI launches are short-lived; give queued remote events a bounded delivery window.
                await SentrySdk.FlushAsync(ShutdownTimeout).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            // Network or SDK shutdown failures are diagnostic-only and must not delay process exit.
            System.Diagnostics.Debug.WriteLine($"WorkTrail Sentry flush unavailable: {exception.GetType().Name}");
        }
        finally
        {
            try
            {
                serviceProvider.Dispose();
            }
            catch (Exception exception)
            {
                // Provider disposal is best-effort during process shutdown; local sinks still need closing.
                System.Diagnostics.Debug.WriteLine($"WorkTrail logging provider disposal unavailable: {exception.GetType().Name}");
            }
            finally
            {
                CloseLocalSinks();
            }
        }
    }

    private static ServiceProvider BuildServiceProvider(string? sentryDsn, bool fileLoggingEnabled, string sentryStatus, string sentryEnvironment)
    {
        var serviceProvider = new ServiceCollection()
            .AddSingleton(new ObservabilityHealth(
                ConsoleLoggingEnabled: true,
                FileLoggingEnabled: fileLoggingEnabled,
                SentryStatus: sentryStatus,
                SendsDefaultPii: false))
            .AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddSerilog(Log.Logger, dispose: false);
                if (!string.IsNullOrWhiteSpace(sentryDsn))
                {
                    builder.AddSentry(options =>
                    {
                        options.Dsn = sentryDsn;
                        options.Environment = sentryEnvironment;
                        options.Release = typeof(App).Assembly.GetName().Version?.ToString(3);
                        options.IsGlobalModeEnabled = true;
                        options.SendDefaultPii = false;
                        options.MinimumBreadcrumbLevel = LogLevel.Information;
                        options.MinimumEventLevel = LogLevel.Error;
                        options.FlushTimeout = ShutdownTimeout;
                        options.ShutdownTimeout = ShutdownTimeout;
                        options.SetBeforeSend(SentryEventSanitizer.Sanitize);
                        options.SetBeforeBreadcrumb(SentryEventSanitizer.SanitizeBreadcrumb);
                    });
                }
            })
            .BuildServiceProvider();

        try
        {
            // Resolve providers now so any optional Sentry configuration fault can use the local-only fallback.
            _ = serviceProvider.GetRequiredService<ILoggerFactory>();
            return serviceProvider;
        }
        catch
        {
            serviceProvider.Dispose();
            throw;
        }
    }

    private static void Shutdown(ServiceProvider serviceProvider)
    {
        try
        {
            ShutdownAsync(serviceProvider).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            // ProcessExit cannot surface failures; always attempt to close the local Serilog sinks.
            System.Diagnostics.Debug.WriteLine($"WorkTrail logging shutdown unavailable: {exception.GetType().Name}");
            CloseLocalSinks();
        }
    }

    private static void CloseLocalSinks()
    {
        try
        {
            Log.CloseAndFlush();
        }
        catch (Exception exception)
        {
            // There is no remaining logger at this point; Debug is the last non-throwing fallback.
            System.Diagnostics.Debug.WriteLine($"WorkTrail local logging flush unavailable: {exception.GetType().Name}");
        }
    }

}
