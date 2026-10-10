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
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Verifies the environment-only, fail-closed Sentry configuration contract.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ObservabilityConfigurationTests
{
    private const string DsnVariable = "WORKTRAIL_SENTRY_DSN";
    private const string EnvironmentVariable = "WORKTRAIL_SENTRY_ENVIRONMENT";
    private const string LogDirectoryVariable = "WORKTRAIL_LOG_DIRECTORY";

    /// <summary>Ensures no remote sink is enabled when an operator provides no DSN.</summary>
    [Fact]
    public void MissingDsn_KeepsRemoteDiagnosticsDisabled()
    {
        WithSentryEnvironment(null, null, () =>
        {
            var result = new ObservabilityConfigurationService().Load();

            Assert.Null(result.SentryDsn);
            Assert.Equal("disabled", result.SentryStatus);
            Assert.Equal("development", result.SentryEnvironment);
        });
    }

    /// <summary>Ensures a valid project DSN and deployment environment are accepted.</summary>
    [Fact]
    public void ValidDsn_EnablesTheConfiguredDeploymentEnvironment()
    {
        const string dsn = "https://public-key@sentry.example.invalid/42";
        WithSentryEnvironment(dsn, "production", () =>
        {
            var result = new ObservabilityConfigurationService().Load();

            Assert.Equal(dsn, result.SentryDsn);
            Assert.Equal("enabled", result.SentryStatus);
            Assert.Equal("production", result.SentryEnvironment);
        });
    }

    /// <summary>Ensures malformed remote configuration does not become an active sink.</summary>
    [Fact]
    public void MalformedDsn_IsRejectedWithoutBlockingLocalDiagnostics()
    {
        WithSentryEnvironment("not-a-sentry-dsn", "invalid environment value", () =>
        {
            var result = new ObservabilityConfigurationService().Load();

            Assert.Null(result.SentryDsn);
            Assert.Equal("invalid", result.SentryStatus);
            Assert.Equal("development", result.SentryEnvironment);
        });
    }

    /// <summary>Ensures a malformed deployment label cannot silently send production events as development.</summary>
    [Fact]
    public void InvalidEnvironment_DisablesOtherwiseValidRemoteDiagnostics()
    {
        const string dsn = "https://public-key@sentry.example.invalid/42";
        WithSentryEnvironment(dsn, "invalid environment value", () =>
        {
            var result = new ObservabilityConfigurationService().Load();

            Assert.Null(result.SentryDsn);
            Assert.Equal("invalid", result.SentryStatus);
            Assert.Equal("development", result.SentryEnvironment);
        });
    }

    private static void WithSentryEnvironment(string? dsn, string? environment, Action assertion)
    {
        var previousDsn = Environment.GetEnvironmentVariable(DsnVariable, EnvironmentVariableTarget.Process);
        var previousEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable, EnvironmentVariableTarget.Process);
        var previousLogDirectory = Environment.GetEnvironmentVariable(LogDirectoryVariable, EnvironmentVariableTarget.Process);
        var logDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail-ObservabilityTests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(DsnVariable, dsn, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(EnvironmentVariable, environment, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(LogDirectoryVariable, logDirectory, EnvironmentVariableTarget.Process);
            assertion();
        }
        finally
        {
            Environment.SetEnvironmentVariable(DsnVariable, previousDsn, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(EnvironmentVariable, previousEnvironment, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(LogDirectoryVariable, previousLogDirectory, EnvironmentVariableTarget.Process);
            if (Directory.Exists(logDirectory))
            {
                Directory.Delete(logDirectory, recursive: true);
            }
        }
    }
}
