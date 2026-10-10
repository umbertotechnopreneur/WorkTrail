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


using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Spectre.Console;
using Xunit;

namespace WorkTrail.Cli.Tests;

/// <summary>Checks that the CLI floppy stays at the terminal's right edge or falls back to text.</summary>
public sealed class VibeWareBrandTests
{
    /// <summary>Checks the visible cell position and narrow-terminal fallback.</summary>
    [Theory]
    [InlineData(60, false)]
    [InlineData(120, true)]
    public void Artwork_UsesRightmostColumnsOnlyWhenItFits(int width, bool showArtwork)
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(output)
        });
        console.Profile.Width = width;
        console.Profile.Capabilities.Unicode = true;
        console.Profile.Capabilities.Links = false;
        console.Write(new VibeWareBrand(console, new Text("WorkTrail details")));

        var rendered = Regex.Replace(output.ToString(), "\u001b\\[[0-9;]*m", "");
        Assert.Contains("WorkTrail details", rendered);
        Assert.Contains(VibeWareBrand.ManifestoUrl, Regex.Replace(rendered, @"\s+", ""));
        var artworkLines = rendered.Split('\n').Where(line => line.Contains('▀')).ToArray();
        Assert.Equal(showArtwork, artworkLines.Length > 0);
        Assert.All(artworkLines, line => Assert.True(line.IndexOf('▀') >= width - 20));
    }
}
