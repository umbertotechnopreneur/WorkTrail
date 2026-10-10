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


using Sentry;
using Sentry.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WorkTrail.Runtime;

/// <summary>Removes local identity and secret-bearing values before a Sentry event leaves the process.</summary>
internal static class SentryEventSanitizer
{
    private static readonly Regex WindowsPath = new(
        @"(?:[a-z]:[\\/]|\\\\)[^\r\n\""']*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex SecretAssignment = new(
        @"\b(?:api[_ -]?key|(?:access[_ -]?|refresh[_ -]?)?token|auth(?:orization)?|password|passwd|secret|dsn)\b[\""']?\s*[:=]\s*(?:(?:bearer|basic)\s+\S+|\""[^\""\r\n]*\""|'[^'\r\n]*'|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex AuthorizationCredential = new(
        @"\b(?:bearer|basic)\s+[a-z0-9._~+/=-]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex CredentialUri = new(
        @"\bhttps?://[^\s/@]+(?::[^\s/@]*)?@[^\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex RawIdentifier = new(
        @"\b[0-9a-f]{32}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>Sanitizes an event, or drops it when the SDK payload cannot be made private safely.</summary>
    internal static SentryEvent? Sanitize(SentryEvent sentryEvent)
    {
        ArgumentNullException.ThrowIfNull(sentryEvent);

        try
        {
            // Remote telemetry is optional. Any unexpected SDK shape fails closed instead of risking disclosure.
            return SanitizeCore(sentryEvent) ? sentryEvent : null;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"WorkTrail Sentry event redaction unavailable: {exception.GetType().Name}");
            return null;
        }
    }

    /// <summary>Redacts secret assignments, credential-bearing URLs, Windows paths, and installation identifiers.</summary>
    internal static string? RedactText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = CredentialUri.Replace(value, "[credential-url]");
        redacted = WindowsPath.Replace(redacted, "[path]");
        redacted = SecretAssignment.Replace(redacted, "[secret]");
        redacted = AuthorizationCredential.Replace(redacted, "[secret]");
        return RawIdentifier.Replace(redacted, "[installation]");
    }

    /// <summary>Returns a breadcrumb without structured data and with its display text redacted.</summary>
    internal static Breadcrumb SanitizeBreadcrumb(Breadcrumb breadcrumb)
    {
        ArgumentNullException.ThrowIfNull(breadcrumb);

        return new Breadcrumb(
            RedactText(breadcrumb.Message) ?? string.Empty,
            breadcrumb.Type ?? string.Empty,
            new Dictionary<string, string>(),
            breadcrumb.Category ?? string.Empty,
            breadcrumb.Level);
    }

    private static bool SanitizeCore(SentryEvent sentryEvent)
    {
        sentryEvent.User = null!;
        sentryEvent.Request = null!;
        sentryEvent.ServerName = null!;
        sentryEvent.TransactionName = RedactText(sentryEvent.TransactionName)!;
        sentryEvent.Logger = RedactText(sentryEvent.Logger)!;
        sentryEvent.Fingerprint = Array.Empty<string>();

        if (sentryEvent.Message is { } message)
        {
            message.Message = RedactText(message.Message)!;
            message.Formatted = RedactText(message.Formatted)!;
            message.Params = Array.Empty<object>();
        }

        foreach (var sentryException in sentryEvent.SentryExceptions ?? [])
        {
            sentryException.Value = RedactText(sentryException.Value)!;
            // Mechanism data/meta are free-form dictionaries and future SDK versions can add
            // more string-bearing fields. Removing the object is the only fail-closed contract.
            sentryException.Mechanism = null!;
            SanitizeStackTrace(sentryException.Stacktrace);
        }

        foreach (var thread in sentryEvent.SentryThreads ?? [])
        {
            thread.Name = RedactText(thread.Name)!;
            SanitizeStackTrace(thread.Stacktrace);
        }

        foreach (var debugImage in sentryEvent.DebugImages ?? [])
        {
            debugImage.CodeFile = FileNameOnly(debugImage.CodeFile)!;
            debugImage.DebugFile = FileNameOnly(debugImage.DebugFile)!;
        }

        foreach (var tag in sentryEvent.Tags.Keys.ToArray())
        {
            sentryEvent.UnsetTag(tag);
        }

        // SDK context objects can gain new free-form string fields between versions. Drop every
        // context rather than carrying an incomplete field whitelist that could leak a path or secret.
        foreach (var contextKey in sentryEvent.Contexts.Keys.ToArray())
        {
            sentryEvent.Contexts.Remove(contextKey);
        }

        if (sentryEvent.Extra.Count == 0)
        {
            return true;
        }

        if (sentryEvent.Extra is not IDictionary<string, object> mutableExtra)
        {
            return false;
        }

        mutableExtra.Clear();
        return mutableExtra.Count == 0;
    }

    private static void SanitizeStackTrace(SentryStackTrace? stackTrace)
    {
        if (stackTrace?.Frames is null)
        {
            return;
        }

        foreach (var frame in stackTrace.Frames)
        {
            frame.AbsolutePath = null!;
            frame.FileName = FileNameOnly(frame.FileName)!;
            frame.Package = RedactText(frame.Package)!;
            frame.ContextLine = null!;
            frame.PreContext.Clear();
            frame.PostContext.Clear();
            frame.Vars.Clear();
        }
    }

    private static string? FileNameOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var separator = Math.Max(value.LastIndexOf('\\'), value.LastIndexOf('/'));
        return RedactText(separator >= 0 ? value[(separator + 1)..] : value);
    }
}
