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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Guards the dependency direction and passive frontend boundaries of the application.</summary>
public sealed class ArchitectureBoundaryContractTests
{
    private static readonly string[] ForbiddenFrontendInfrastructureTokens =
    [
        "using Microsoft.Win32;",
        "using System.IO;",
        "using System.Net.Http;",
        "using WorkTrail.Infrastructure;",
        "Directory.CreateDirectory(",
        "Directory.Delete(",
        "Environment.GetEnvironmentVariable(",
        "File.Delete(",
        "File.ReadAll",
        "File.WriteAll",
        "new HttpClient(",
        "new LocalStore(",
        "new ScreenCaptureService(",
        "new SqliteActivityStore(",
        "new TrackingDomainService(",
        "Process.Start("
    ];

    /// <summary>Ensures Core cannot acquire a dependency on a frontend or presentation framework.</summary>
    [Fact]
    public void CoreAssembly_DoesNotReferenceFrontendAssemblies()
    {
        var forbiddenPrefixes = new[]
        {
            "Microsoft.UI",
            "Microsoft.WindowsAppSDK",
            "Spectre",
            "WorkTrail.Cli",
            "WorkTrail.Hardware",
            "LibreHardwareMonitorLib",
            "WorkTrail.Presentation",
            "WorkTrail.Taskbar"
        };
        var references = typeof(IWorkTrailApplication).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(references);
    }

    /// <summary>Ensures project references continue to point from frontends toward shared application layers.</summary>
    [Fact]
    public void ProjectReferences_FollowTheApprovedDependencyGraph()
    {
        var expectedReferences = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["WorkTrail"] = ["WorkTrail.Cli", "WorkTrail.Core", "WorkTrail.Hardware", "WorkTrail.Presentation", "WorkTrail.Taskbar"],
            ["WorkTrail.Cli"] = ["WorkTrail.Core"],
            ["WorkTrail.Core"] = ["WorkTrail.Ocr", "WorkTrail.Search"],
            ["WorkTrail.Hardware"] = ["LibreHardwareMonitor", "WorkTrail.Core"],
            ["WorkTrail.Ocr"] = [],
            ["WorkTrail.Presentation"] = ["WorkTrail.Core"],
            ["WorkTrail.Search"] = [],
            ["WorkTrail.Taskbar"] = ["WorkTrail.Core"]
        };

        foreach (var (projectName, expected) in expectedReferences)
        {
            var projectFile = RepositoryFile(projectName, $"{projectName}.csproj");
            var actual = XDocument.Load(projectFile)
                .Descendants("ProjectReference")
                .Select(reference => ProjectNameFromReference((string?)reference.Attribute("Include")))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
        }

        // The application ships the isolated collector but must never load its sensor implementation in-process.
        var appProject = XDocument.Load(RepositoryFile("WorkTrail", "WorkTrail.csproj"));
        var collectorReference = Assert.Single(appProject.Descendants("ProjectReference"), reference =>
            ProjectNameFromReference((string?)reference.Attribute("Include")) == "WorkTrail.Hardware");
        Assert.Equal("false", (string?)collectorReference.Attribute("ReferenceOutputAssembly"));
        Assert.Equal("_HardwareCollectorTarget", (string?)collectorReference.Attribute("OutputItemType"));
        var libraryProject = XDocument.Load(RepositoryFile("WorkTrail.Hardware", "LibreHardwareMonitor", "LibreHardwareMonitor.csproj"));
        Assert.Empty(libraryProject.Descendants("ProjectReference"));
    }

    /// <summary>Ensures WinUI presentation sources delegate I/O and environment work to the application facade.</summary>
    [Fact]
    public void WinUiCodeBehind_DoesNotOwnInfrastructureOperations()
    {
        var frontendDirectory = RepositoryFile("WorkTrail");
        var codeBehindFiles = Directory
            .EnumerateFiles(frontendDirectory, "*.xaml.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith($"{Path.DirectorySeparatorChar}App.xaml.cs", StringComparison.OrdinalIgnoreCase))
            .Concat(Directory.EnumerateFiles(
                Path.Combine(frontendDirectory, "Controls"),
                "*.cs",
                SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(codeBehindFiles);
        Assert.All(codeBehindFiles, AssertPassiveFrontendSource);
    }

    /// <summary>Ensures Spectre command routing and rendering cannot bypass the shared application facade.</summary>
    [Fact]
    public void CliCommandsAndRenderers_DoNotOwnInfrastructureOperations()
    {
        var cliDirectory = RepositoryFile("WorkTrail.Cli");
        var cliSources = Directory
            .EnumerateFiles(cliDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith($"{Path.DirectorySeparatorChar}CliBootstrap.cs", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(cliSources);
        Assert.All(cliSources, AssertPassiveFrontendSource);
    }

    private static void AssertPassiveFrontendSource(string path)
    {
        var source = File.ReadAllText(path);

        foreach (var forbiddenToken in ForbiddenFrontendInfrastructureTokens)
        {
            Assert.DoesNotContain(forbiddenToken, source, StringComparison.Ordinal);
        }
    }

    private static string ProjectNameFromReference(string? include)
    {
        Assert.False(string.IsNullOrWhiteSpace(include));
        var normalized = include!.Replace('\\', '/');
        return Path.GetFileNameWithoutExtension(normalized);
    }

    private static string RepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }
}
