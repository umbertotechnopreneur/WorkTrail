// SPDX-License-Identifier: MIT

using Spectre.Console;
using Spectre.Console.Rendering;

namespace WorkTrail.Cli;

/// <summary>Places the owner-supplied floppy artwork at the terminal's right edge when it fits.</summary>
internal sealed class VibeWareBrand(IAnsiConsole console, IRenderable content) : IRenderable
{
    internal const string ManifestoUrl = "https://umbertogiacobbi.biz/vibeware/manifesto";
    private const int ImageSize = 20;
    private static readonly Lazy<byte[]> Pixels = new(LoadPixels);

    /// <summary>Measures the same layout that will be rendered.</summary>
    public Measurement Measure(RenderOptions options, int maxWidth) => CreateContent(options, maxWidth).Measure(options, maxWidth);

    /// <summary>Renders the artwork only when the product text and floppy fit side by side.</summary>
    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => CreateContent(options, maxWidth).Render(options, maxWidth);

    /// <summary>Reserves the last twenty columns for the floppy on suitable terminals.</summary>
    private IRenderable CreateContent(RenderOptions options, int width)
    {
        var name = new Markup($"[bold teal link={ManifestoUrl}]VibeWare[/]");
        var link = new Markup($"[grey70 link={ManifestoUrl}]{ManifestoUrl}[/]");
        var requiredWidth = Math.Max(88, content.Measure(options, width).Min + ImageSize + 2);
        if (width < requiredWidth || !console.Profile.Capabilities.Ansi || !console.Profile.Capabilities.Unicode
            || !console.Profile.Supports(ColorSystem.Legacy))
            return new Rows(content, Text.Empty, name, link);

        var artwork = new Canvas(ImageSize, ImageSize) { Scale = false };
        var pixels = Pixels.Value;
        for (var row = 0; row < ImageSize; row++)
        {
            for (var column = 0; column < ImageSize; column++)
            {
                var offset = (row * ImageSize + column) * 3;
                artwork.SetPixel(column, row, new Color(pixels[offset], pixels[offset + 1], pixels[offset + 2]));
            }
        }

        var header = new Grid().Expand()
            .AddColumn(new GridColumn { Padding = new Padding(0, 0, 2, 0) })
            .AddColumn(new GridColumn { Width = ImageSize, Padding = new Padding(0) });
        header.AddRow(content, new Rows(artwork, Align.Center(name)));
        return new Rows(header, Text.Empty, Align.Right(link));
    }

    /// <summary>Loads the exact 20 by 20 RGB pixels from the bundled owner-supplied art.</summary>
    private static byte[] LoadPixels()
    {
        using var stream = typeof(VibeWareBrand).Assembly.GetManifestResourceStream("WorkTrail.Cli.Assets.vibeware-symbol.rgb")
            ?? throw new InvalidOperationException("The VibeWare artwork is missing.");
        var pixels = new byte[ImageSize * ImageSize * 3];
        if (stream.Length != pixels.Length) throw new InvalidOperationException("The VibeWare artwork has an invalid size.");
        stream.ReadExactly(pixels);
        return pixels;
    }
}
