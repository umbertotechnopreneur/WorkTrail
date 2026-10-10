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
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ApplicationLogServiceTests
{
    [Fact]
    public void RedactedExport_UsesNewestBoundedTailWithoutChangingRawLog()
    {
        var root = CreateTemporaryDirectory();
        var logDirectory = Path.Combine(root, "logs");
        var exportDirectory = Path.Combine(root, "exports");
        Directory.CreateDirectory(logDirectory);
        try
        {
            var older = Path.Combine(logDirectory, "worktrail-20260808.log");
            var latest = Path.Combine(logDirectory, "worktrail-20260809.log");
            File.WriteAllText(older, "old log");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-2));

            var privateLine = "api_key=sk-test-only-secret-123456 Bearer bearer-token-123456 " +
                              @"C:\Users\private\screenshots\capture.webp \\server\private\capture.webp " +
                              "01234567-89ab-cdef-0123-456789abcdef";
            var raw = new string('x', 1_100_000) + Environment.NewLine +
                      "Attempt=abcdef123456 HttpStatus=429 FailureCategory=http_429.insufficient_quota" + Environment.NewLine +
                      privateLine;
            File.WriteAllText(latest, raw);
            File.SetLastWriteTimeUtc(latest, DateTime.UtcNow);
            Directory.CreateDirectory(exportDirectory);
            for (var index = 0; index < 6; index++)
            {
                var staleExport = Path.Combine(exportDirectory, $"worktrail-support-20260808-00000{index}-000.log");
                File.WriteAllText(staleExport, "stale");
                File.SetLastWriteTimeUtc(staleExport, DateTime.UtcNow.AddDays(index == 0 ? -2 : 0).AddMinutes(-index));
            }

            var service = new ApplicationLogService(logDirectory, exportDirectory);

            Assert.Equal(latest, service.FindLatestLogPath());
            var export = service.CreateRedactedExport();
            var sharedText = File.ReadAllText(export);

            Assert.Contains("Attempt=abcdef123456", sharedText, StringComparison.Ordinal);
            Assert.Contains("HttpStatus=429", sharedText, StringComparison.Ordinal);
            Assert.Contains("http_429.insufficient_quota", sharedText, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-test-only-secret", sharedText, StringComparison.Ordinal);
            Assert.DoesNotContain("bearer-token", sharedText, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\Users\private", sharedText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"\\server\private", sharedText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("01234567-89ab-cdef-0123-456789abcdef", sharedText, StringComparison.OrdinalIgnoreCase);
            Assert.True(new FileInfo(export).Length < 1_100_000);
            Assert.Equal(raw, File.ReadAllText(latest));
            Assert.True(Directory.EnumerateFiles(exportDirectory, "worktrail-support-*.log").Count() <= 4);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Authorization: Bearer token-value", "[secret]")]
    [InlineData("api_key=sk-secret-value", "[secret]")]
    [InlineData("{\"token\":\"sk-json-secret-value\"}", "{[secret]}")]
    [InlineData(@"Path=C:\Users\private\file.log", "Path=[path]")]
    [InlineData(@"Path=\\server\private\file.log", "Path=[path]")]
    public void RedactForSharing_RemovesSensitiveValues(string input, string expectedFragment)
    {
        var result = ApplicationLogService.RedactForSharing(input);

        Assert.Contains(expectedFragment, result, StringComparison.Ordinal);
        Assert.DoesNotContain("private", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-value", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-value", result, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
