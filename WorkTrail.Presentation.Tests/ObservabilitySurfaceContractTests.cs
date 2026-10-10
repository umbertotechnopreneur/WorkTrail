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
using System.IO;
using System.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Guards privacy and shutdown guarantees in the Windows composition root.</summary>
public sealed class ObservabilitySurfaceContractTests
{
    /// <summary>Ensures launch telemetry cannot regress to logging raw paths, arguments, or installation IDs.</summary>
    [Fact]
    public void AppLogging_DoesNotIncludeSensitiveLaunchValues()
    {
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "App.xaml.cs"));

        Assert.Contains("Launch requested. Mode={Mode}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BaseDirectory={BaseDirectory}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Arguments={Arguments}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallationId={InstallationId}", source, StringComparison.Ordinal);
    }

    /// <summary>Ensures Sentry remains optional, private by default, and bounded during shutdown.</summary>
    [Fact]
    public void LoggingBootstrapper_SentryIsOptionalPrivateAndBounded()
    {
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "Runtime", "LoggingBootstrapper.cs"));
        var configurationSource = File.ReadAllText(RepositoryFile("WorkTrail.Core", "Application", "ObservabilityConfiguration.cs"));

        Assert.Contains("ReadEnvironmentVariable(\"WORKTRAIL_SENTRY_DSN\")", configurationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.GetEnvironmentVariable", source, StringComparison.Ordinal);
        Assert.Contains("options.SendDefaultPii = false", source, StringComparison.Ordinal);
        Assert.Contains("BuildServiceProvider(sentryDsn: null", source, StringComparison.Ordinal);
        Assert.Contains("new ObservabilityHealth(", source, StringComparison.Ordinal);
        Assert.Contains("SentrySdk.FlushAsync(ShutdownTimeout)", source, StringComparison.Ordinal);
        Assert.Contains("options.ShutdownTimeout = ShutdownTimeout", source, StringComparison.Ordinal);
        Assert.Contains("options.SetBeforeSend(SentryEventSanitizer.Sanitize)", source, StringComparison.Ordinal);
        Assert.Contains("options.SetBeforeBreadcrumb(SentryEventSanitizer.SanitizeBreadcrumb)", source, StringComparison.Ordinal);
        Assert.Contains("private const int RetainedFileCount = 15;", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromDays(15)", source, StringComparison.Ordinal);
        Assert.Contains("rollingInterval: RollingInterval.Day", source, StringComparison.Ordinal);
        Assert.Contains("retainedFileCountLimit: RetainedFileCount", source, StringComparison.Ordinal);
        Assert.Contains("retainedFileTimeLimit: RetainedFileTime", source, StringComparison.Ordinal);
    }

    private static string RepositoryFile(params string[] pathSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            }
        }

        throw new DirectoryNotFoundException("Could not locate the WorkTrail repository root.");
    }
}
