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
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class AiKeyOptionsSurfaceContractTests
{
    [Fact]
    public void AiKeyEditor_IsCollapsedBehindAStateAwareExpander()
    {
        var options = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "OptionsControl.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OptionsControl.xaml.cs"));
        var expander = options.Descendants().Single(element => HasName(element, "ApiKeyExpander"));
        var statusIcon = options.Descendants().Single(element => HasName(element, "ApiKeyStatusIcon"));
        var keyBox = options.Descendants().Single(element => HasName(element, "ApiKeyBox"));
        var actionText = options.Descendants().Single(element => HasName(element, "ApiKeyActionText"));

        Assert.Equal("Expander", expander.Name.LocalName);
        Assert.Contains(expander.Descendants(), element => ReferenceEquals(element, keyBox));
        Assert.Contains(expander.Descendants(), element => element.Attribute("Click")?.Value == "SetApiKeyButton_Click");
        Assert.Equal("Set key", actionText.Attribute("Text")?.Value);
        Assert.Equal("\uE7BA", statusIcon.Attribute("Glyph")?.Value);
        Assert.Equal("{ThemeResource SystemFillColorCautionBrush}", statusIcon.Attribute("Foreground")?.Value);
        Assert.Contains("SystemFillColorSuccessBrush", options.ToString(), StringComparison.Ordinal);
        Assert.Contains("Options.ApiKeyAction.Change", source, StringComparison.Ordinal);
        Assert.Contains("Options.ApiKeyAction.Set", source, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.GoToState(this, visualState, false);", source, StringComparison.Ordinal);
        Assert.Contains("ApiKeyExpander.IsExpanded = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.", source, StringComparison.Ordinal);
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

    private static bool HasName(XElement element, string name) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name);
}
