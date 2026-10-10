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
using WorkTrail.Runtime;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Exercises the production Sentry scrubber against the SDK payload types it mutates.</summary>
public sealed class SentryEventSanitizerTests
{
    /// <summary>Ensures event identity, structured values, secrets, and absolute source paths are removed.</summary>
    [Fact]
    public void Sanitize_RemovesIdentitySecretsStructuredValuesAndAbsolutePaths()
    {
        var stackFrame = new SentryStackFrame
        {
            AbsolutePath = @"C:\Users\private\source\Worker.cs",
            FileName = @"C:\Users\private\source\Worker.cs",
            Package = @"C:\Users\private\WorkTrail.dll",
            ContextLine = "var token = secret;",
        };
        stackFrame.PreContext.Add("password=before");
        stackFrame.PostContext.Add("password=after");
        stackFrame.Vars["token"] = "secret";

        var sentryEvent = new SentryEvent
        {
            User = new SentryUser { Id = "private-user" },
            Request = new SentryRequest { Url = "https://private.example/screenshot" },
            ServerName = "private-machine",
            Logger = @"C:\Users\private\logger",
            TransactionName = "api_key=transaction-secret",
            Fingerprint = ["private-fingerprint"],
            Message = new SentryMessage
            {
                Message = "Failed at C:\\Users\\private\\shot.png",
                Formatted = "dsn=https://public@example.invalid/42",
                Params = ["private-parameter"],
            },
            SentryExceptions =
            [
                new SentryException
                {
                    Value = "Authorization: Bearer exception-secret",
                    Mechanism = new Mechanism
                    {
                        Description = @"C:\Users\private\mechanism",
                        Source = "token=mechanism-secret",
                        HelpLink = "https://user:password@private.example/help",
                        Type = "private-mechanism",
                    },
                    Stacktrace = new SentryStackTrace { Frames = [stackFrame] },
                },
            ],
            SentryThreads =
            [
                new SentryThread
                {
                    Name = "token=thread-secret",
                    Stacktrace = new SentryStackTrace
                    {
                        Frames = [new SentryStackFrame { AbsolutePath = @"C:\Users\private\Thread.cs" }],
                    },
                },
            ],
            DebugImages =
            [
                new DebugImage
                {
                    CodeFile = @"C:\Users\private\WorkTrail.dll",
                    DebugFile = @"C:\Users\private\WorkTrail.pdb",
                },
            ],
        };
        sentryEvent.SetExtra("structured-secret", new { Password = "private" });
        var sentryException = sentryEvent.SentryExceptions!.Single();
        sentryException.Mechanism!.Data["secret"] = "private-data";
        sentryException.Mechanism.Meta["secret"] = "private-meta";
        sentryEvent.SetTag("private-tag", "private-value");
        sentryEvent.Contexts["private-context"] = new { Token = "private" };
        sentryEvent.Contexts.Device.Name = "private-machine";
        sentryEvent.Contexts.Device.DeviceUniqueIdentifier = "private-device";
        sentryEvent.Contexts.Trace.Description = @"C:\Users\private\trace token=trace-secret";
        var breadcrumb = new Breadcrumb(
            "password=breadcrumb-secret",
            "default",
            new Dictionary<string, string> { ["private"] = "secret" },
            "test",
            BreadcrumbLevel.Error);

        var sanitized = SentryEventSanitizer.Sanitize(sentryEvent);
        var sanitizedBreadcrumb = SentryEventSanitizer.SanitizeBreadcrumb(breadcrumb);

        Assert.Same(sentryEvent, sanitized);
        Assert.Null(sanitized!.User.Id);
        Assert.Null(sanitized.User.Username);
        Assert.Null(sanitized.User.Email);
        Assert.Empty(sanitized.User.Other);
        Assert.Null(sanitized.Request.Url);
        Assert.Null(sanitized.Request.Data);
        Assert.Empty(sanitized.Request.Headers);
        Assert.Null(sanitized.ServerName);
        Assert.Empty(sanitized.Extra);
        Assert.Empty(sanitized.Tags);
        Assert.Empty(sanitized.Fingerprint);
        Assert.Empty(sanitized.Message!.Params!);
        Assert.DoesNotContain("private", sanitized.Message.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.invalid", sanitized.Message.Formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception-secret", sanitized.SentryExceptions!.Single().Value, StringComparison.Ordinal);
        Assert.Null(sanitized.SentryExceptions!.Single().Mechanism);
        Assert.DoesNotContain("thread-secret", sanitized.SentryThreads!.Single().Name, StringComparison.Ordinal);
        Assert.Empty(sanitized.Contexts.Keys);
        Assert.Null(sanitized.Contexts.Device.Name);
        Assert.Null(sanitized.Contexts.Device.DeviceUniqueIdentifier);

        var sanitizedFrame = sanitized.SentryExceptions!.Single().Stacktrace!.Frames.Single();
        Assert.Null(sanitizedFrame.AbsolutePath);
        Assert.Equal("Worker.cs", sanitizedFrame.FileName);
        Assert.Equal("[path]", sanitizedFrame.Package);
        Assert.Null(sanitizedFrame.ContextLine);
        Assert.Empty(sanitizedFrame.PreContext);
        Assert.Empty(sanitizedFrame.PostContext);
        Assert.Empty(sanitizedFrame.Vars);
        var debugImages = sanitized.DebugImages!.ToArray();
        Assert.Equal("WorkTrail.dll", debugImages.Single().CodeFile);
        Assert.Equal("WorkTrail.pdb", debugImages.Single().DebugFile);
        Assert.Empty(sanitizedBreadcrumb.Data!);
        Assert.DoesNotContain("breadcrumb-secret", sanitizedBreadcrumb.Message, StringComparison.Ordinal);
    }

    /// <summary>Ensures credential URLs and common secret spellings are redacted from free-form values.</summary>
    [Theory]
    [InlineData("https://public-key@ingest.example/42", "public-key")]
    [InlineData("https://user:password@service.example/path", "password")]
    [InlineData("api_key=top-secret", "top-secret")]
    [InlineData("refresh token: 'top secret'", "top secret")]
    [InlineData("Authorization: Bearer abc.def-123", "abc.def-123")]
    [InlineData(@"C:\Users\private\capture.png", "private")]
    [InlineData("C:/Users/private/capture.png", "private")]
    [InlineData("{\"apiKey\":\"top-secret\"}", "top-secret")]
    [InlineData("{\"authorization\":\"Bearer json-secret\"}", "json-secret")]
    [InlineData("0123456789abcdef0123456789abcdef", "0123456789abcdef0123456789abcdef")]
    public void RedactText_RemovesSensitiveValue(string input, string sensitiveValue)
    {
        var redacted = SentryEventSanitizer.RedactText(input);

        Assert.DoesNotContain(sensitiveValue, redacted, StringComparison.OrdinalIgnoreCase);
    }
}
