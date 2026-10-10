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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Cli.Tests;

public sealed class StoreGlobalizationContractTests
{
    private static readonly string[] RequiredLocales =
    [
        "en-US", "it-IT", "fr-FR", "de-DE", "es-ES", "zh-Hans", "vi-VN", "ko-KR", "pt-PT", "pt-BR"
    ];

    [Fact]
    public void UiLanguageCatalog_MatchesShippedLocalizationFiles()
    {
        Assert.Equal(RequiredLocales, ProductLanguageCatalog.UiLocales);

        var localizationDirectory = Path.Combine(RepositoryRoot(), "WorkTrail.Core", "Localization");
        var locales = Directory.GetFiles(localizationDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(locale => locale, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(RequiredLocales.OrderBy(locale => locale, StringComparer.Ordinal), locales);
    }

    [Fact]
    public void PackageManifest_DeclaresTheSameExplicitCanonicalLocales()
    {
        var manifest = XDocument.Load(Path.Combine(RepositoryRoot(), "WorkTrail", "Package.appxmanifest"));
        var ns = manifest.Root!.Name.Namespace;
        var locales = manifest.Root
            .Element(ns + "Resources")!
            .Elements(ns + "Resource")
            .Select(resource => resource.Attribute("Language")?.Value)
            .ToArray();

        Assert.Equal(RequiredLocales, locales);
        Assert.DoesNotContain("x-generate", locales);
    }

    [Fact]
    public void StoreValidator_RejectsDuplicateJsonPropertiesBeforePowerShellDeserialization()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "WorkTrail.StoreValidator.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var listingPath = Path.Combine(temporaryDirectory, "listing.json");
        File.WriteAllText(
            listingPath,
            """
            {
              "schemaVersion": 1,
              "locales": {
                "en-US": {},
                "en-US": {}
              }
            }
            """);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot(), "scripts", "WorkTrail.ps1"));
            startInfo.ArgumentList.Add("-Action");
            startInfo.ArgumentList.Add("ValidateStoreListing");
            startInfo.ArgumentList.Add("-ListingPath");
            startInfo.ArgumentList.Add(listingPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("PowerShell 7 could not be started for Store validation.");
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains(
                "Duplicate JSON property '$.locales.en-US'.",
                standardOutput + standardError,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("WorkTrail repository root was not found.");
    }
}
