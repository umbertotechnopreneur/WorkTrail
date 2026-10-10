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
using System.Text.RegularExpressions;
using WorkTrail.Runtime;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class RuntimeOperationCatalogTests
{
    /// <summary>Verifies that each typed runtime operation has one unique stable wire name.</summary>
    [Fact]
    public void Catalog_MapsEveryTypedOperationToOneUniqueWireName()
    {
        var definitions = RuntimeOperationCatalog.All;
        var operations = Enum.GetValues<RuntimeOperation>();

        Assert.Equal(operations.Length, definitions.Count);
        Assert.Equal(definitions.Count, definitions.Select(static item => item.Operation).Distinct().Count());
        Assert.Equal(definitions.Count, definitions.Select(static item => item.WireName).Distinct(StringComparer.Ordinal).Count());
        foreach (var definition in definitions)
        {
            Assert.Matches("^[a-z0-9._]+$", definition.WireName);
            Assert.Equal(definition.WireName, RuntimeOperationCatalog.GetWireName(definition.Operation));
            Assert.True(RuntimeOperationCatalog.TryResolve(definition.WireName, out var resolved));
            Assert.Equal(definition.Operation, resolved);
        }

        Assert.Equal(
            "screenshot.analysis.delete.v1",
            RuntimeOperationCatalog.GetWireName(RuntimeOperation.ScreenshotAnalysisDeleteV1));
        Assert.Equal(
            "screenshot.image.get.v1",
            RuntimeOperationCatalog.GetWireName(RuntimeOperation.ScreenshotImageGetV1));
    }

    /// <summary>Verifies that duplicate runtime wire names fail catalog construction.</summary>
    [Fact]
    public void Catalog_FailsFastWhenWireNamesAreDuplicated()
    {
        RuntimeOperationDefinition[] duplicates =
        [
            new(RuntimeOperation.RuntimeHealth, "duplicate.operation"),
            new(RuntimeOperation.TrackingStart, "duplicate.operation")
        ];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeOperationCatalog.BuildWireLookup(duplicates));

        Assert.Equal("Duplicate runtime operation wire name 'duplicate.operation'.", exception.Message);
    }

    /// <summary>Verifies that the host and client use the complete shared typed operation catalog.</summary>
    [Fact]
    public void HostAndClient_ReferenceTheCompleteSharedTypedCatalog()
    {
        var hostAndClientSource = SourceForCurrentConfiguration(File.ReadAllText(RepositoryFile("WorkTrail.Core", "Runtime", "RuntimeHost.cs")));
        var dispatcherSource = SourceForCurrentConfiguration(File.ReadAllText(RepositoryFile("WorkTrail.Core", "Runtime", "RuntimeRequestDispatcher.cs")));
        var dispatchStart = dispatcherSource.IndexOf("return operation switch", StringComparison.Ordinal);
        var dispatchEnd = dispatcherSource.IndexOf("catch (OperationCanceledException)", dispatchStart, StringComparison.Ordinal);
        var clientStart = hostAndClientSource.IndexOf("public sealed class RuntimeClient", StringComparison.Ordinal);
        Assert.True(dispatchStart >= 0);
        Assert.True(dispatchEnd > dispatchStart);
        Assert.True(clientStart >= 0);
        var dispatchSource = dispatcherSource[dispatchStart..dispatchEnd];
        var clientSource = hostAndClientSource[clientStart..];
        var expected = RuntimeOperationCatalog.All
            .Select(static definition => definition.Operation.ToString())
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var hostOperations = Regex.Matches(
                dispatchSource,
                @"RuntimeOperation\.(?<name>[A-Za-z0-9]+)\s*=>")
            .Select(static match => match.Groups["name"].Value)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var clientOperations = Regex.Matches(
                clientSource,
                @"RuntimeOperation\.(?<name>[A-Za-z0-9]+)")
            .Select(static match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, hostOperations);
        Assert.Equal(expected, clientOperations);
        Assert.Contains("RuntimeOperation operation", clientSource, StringComparison.Ordinal);
        Assert.DoesNotContain("string operation", clientSource, StringComparison.Ordinal);
        Assert.All(RuntimeOperationCatalog.All, definition =>
        {
            Assert.DoesNotContain($"\"{definition.WireName}\"", hostAndClientSource, StringComparison.Ordinal);
            Assert.DoesNotContain($"\"{definition.WireName}\"", dispatcherSource, StringComparison.Ordinal);
        });
    }

    /// <summary>Verifies that the host coordinates extracted runtime services instead of owning their implementation.</summary>
    [Fact]
    public void Host_DelegatesPipeServingDispatchAndMutexOwnership()
    {
        var source = File.ReadAllText(RepositoryFile("WorkTrail.Core", "Runtime", "RuntimeHost.cs"));
        var hostEnd = source.IndexOf("public sealed class RuntimeClient", StringComparison.Ordinal);

        Assert.True(hostEnd >= 0);
        var hostSource = source[..hostEnd];
        Assert.Contains("new RuntimeMutexLease", hostSource, StringComparison.Ordinal);
        Assert.Contains("new RuntimeRequestDispatcher", hostSource, StringComparison.Ordinal);
        Assert.Contains("new RuntimePipeServer", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NamedPipeServerStream", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("return operation switch", hostSource, StringComparison.Ordinal);
    }

    /// <summary>Guards the compiled wire catalog and client API against exposing simulation in Release.</summary>
    [Fact]
    public void DebugSimulation_IsAvailableOnlyInDebugBuilds()
    {
        var registered = RuntimeOperationCatalog.TryResolve("debug.features.simulate.v1", out _);
        var clientMethod = typeof(RuntimeClient).GetMethod("SimulateFeatureAccessAsync");
#if DEBUG
        Assert.True(registered);
        Assert.NotNull(clientMethod);
#else
        Assert.False(registered);
        Assert.Null(clientMethod);
#endif
    }

    private static string SourceForCurrentConfiguration(string source)
    {
#if !DEBUG
        // These source contracts contain simple DEBUG-only blocks. Reject more complex
        // directives rather than guessing their meaning or hiding unrelated operations.
        source = Regex.Replace(source, @"(?ms)^#if DEBUG\r?\n(?<body>.*?)^#endif[ \t]*(?:\r?\n|$)", match =>
        {
            Assert.DoesNotMatch(@"(?m)^\s*#(?:if|elif|else|endif)\b", match.Groups["body"].Value);
            return string.Empty;
        });
#endif
        Assert.DoesNotMatch(@"(?m)^\s*#(?:if|elif|else|endif)\b", Regex.Replace(source, @"(?m)^#(?:if DEBUG|endif)[ \t]*\r?$", string.Empty));
        return source;
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
