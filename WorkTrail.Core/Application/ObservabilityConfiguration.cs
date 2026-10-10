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


namespace WorkTrail.Application;

/// <summary>Contains process-local observability configuration; it is never persisted or exposed over IPC.</summary>
public sealed record ObservabilityConfiguration(
    string? LogDirectory,
    string? SentryDsn,
    string SentryEnvironment,
    string SentryStatus);

/// <summary>Resolves environment and writable-directory concerns for the Windows logging composition root.</summary>
public sealed class ObservabilityConfigurationService
{
    /// <summary>Loads and validates optional observability inputs without allowing failures to block startup.</summary>
    public ObservabilityConfiguration Load()
    {
        var logDirectory = PrepareLogDirectory();
        var sentryDsn = ResolveSentryDsn(ReadEnvironmentVariable("WORKTRAIL_SENTRY_DSN"), out var invalidDsn);
        var sentryEnvironment = ResolveSentryEnvironment(ReadEnvironmentVariable("WORKTRAIL_SENTRY_ENVIRONMENT"), out var invalidEnvironment);
        var invalidConfiguration = invalidDsn || invalidEnvironment;
        if (invalidConfiguration)
        {
            sentryDsn = null;
        }

        var sentryStatus = invalidConfiguration ? "invalid" : sentryDsn is null ? "disabled" : "enabled";
        return new ObservabilityConfiguration(logDirectory, sentryDsn, sentryEnvironment, sentryStatus);
    }

    private static string? PrepareLogDirectory()
    {
        try
        {
            var overrideDirectory = ReadEnvironmentVariable("WORKTRAIL_LOG_DIRECTORY");
            var directory = string.IsNullOrWhiteSpace(overrideDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkTrail", "logs")
                : Path.GetFullPath(overrideDirectory);
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ReadEnvironmentVariable(string name)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name);
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? ResolveSentryDsn(string? value, out bool invalid)
    {
        invalid = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            invalid = !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || string.IsNullOrWhiteSpace(uri.Host)
                || string.IsNullOrWhiteSpace(uri.UserInfo.Split(':', 2)[0])
                || string.IsNullOrWhiteSpace(uri.Segments.LastOrDefault()?.Trim('/'));
        }
        catch (Exception exception) when (exception is UriFormatException or InvalidOperationException)
        {
            invalid = true;
        }

        return invalid ? null : value.Trim();
    }

    private static string ResolveSentryEnvironment(string? value, out bool invalid)
    {
        invalid = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "development";
        }

        var candidate = value.Trim();
        invalid = candidate.Length > 64
            || !candidate.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
        return invalid ? "development" : candidate;
    }
}
