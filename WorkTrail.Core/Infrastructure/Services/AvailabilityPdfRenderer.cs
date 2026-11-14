// SPDX-License-Identifier: MIT
using SkiaSharp;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Draws selectable text and vector timetable geometry into PDF pages.</summary>
internal static class AvailabilityPdfRenderer
{
    private const float Gutter = 46;
    private const float FullTableTop = 240;
    private const float CompactTableTop = 156;
    private const float FooterSpace = 66;
    private const float RowHeight = 16;


    /// <summary>Renders the visible logical interval, repeating headers across time and city page groups.</summary>
    internal static byte[] Render(AvailabilityPdfRequest request, string assetRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = request.Snapshot ?? throw new ArgumentException("A planner snapshot is required.");
        if (request.Hours is not (16 or 24) || snapshot.Cities.Count is < 1 or > 12 || snapshot.Rows.Count == 0)
            throw new ArgumentException("Unsupported planner export dimensions.");
        var rows = snapshot.Rows.Where(r => r.StartUtc >= request.StartUtc && r.StartUtc < request.StartUtc.AddHours(request.Hours)).ToArray();
        if (rows.Length == 0 || rows.Any(r => r.Cells.Count != snapshot.Cities.Count))
            throw new ArgumentException("The selected interval is outside the planner data.");
        var groups = snapshot.DisplayGroups ?? Enumerable.Range(0, snapshot.Cities.Count)
            .Select(index => new AvailabilityPlannerGroup(index, [index])).ToArray();
        if (groups.Count == 0 || groups.Any(group => group.CityIndices.Count == 0 ||
            !group.CityIndices.Contains(group.PrimaryIndex) ||
            group.CityIndices.Any(index => (uint)index >= (uint)snapshot.Cities.Count)) ||
            (snapshot.DisplayGroups is not null && rows.Any(row => row.GroupedCells?.Count != groups.Count)))
        {
            throw new ArgumentException("The displayed groups do not match the planner data.");
        }
        var (shortSide, longSide) = request.PageSize switch
        {
            AvailabilityPdfPageSize.A4 => (595.28f, 841.89f),
            AvailabilityPdfPageSize.A3 => (841.89f, 1190.55f),
            AvailabilityPdfPageSize.Letter => (612f, 792f),
            AvailabilityPdfPageSize.Legal => (612f, 1008f),
            _ => throw new ArgumentException("Unsupported PDF paper size.")
        };
        if (!Enum.IsDefined(request.Orientation) || !Enum.IsDefined(request.Margins))
            throw new ArgumentException("Unsupported PDF page options.");
        var outerMargin = request.Margins == AvailabilityPdfMargins.None ? 0f : 14.17f;
        var physicalWidth = request.Orientation == AvailabilityPdfOrientation.Landscape ? longSide : shortSide;
        var physicalHeight = request.Orientation == AvailabilityPdfOrientation.Landscape ? shortSide : longSide;
        var PageWidth = physicalWidth - 2 * outerMargin;
        var PageHeight = physicalHeight - 2 * outerMargin;
        const float Margin = 0;
        var citiesPerPage = Math.Clamp((int)((PageWidth - Gutter) / 105), 1, 7);
        var tableTop = request.CompactHeaders ? CompactTableTop : FullTableTop;
        var rowsPerPage = (int)((PageHeight - FooterSpace - tableTop) / RowHeight);
        var selectedInstant = request.SelectedStartUtc ?? snapshot.InstantUtc;
        var strings = new LocalizationService(request.Language);
        var family = request.Language.StartsWith("zh", StringComparison.Ordinal) ? "Microsoft YaHei UI"
            : request.Language.StartsWith("ko", StringComparison.Ordinal) ? "Malgun Gothic" : "Segoe UI";
        using var regular = new PdfFont(family, SKFontStyle.Normal);
        using var bold = new PdfFont(family, SKFontStyle.Bold);
        using var output = new MemoryStream();
        using var document = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata { Title = strings.Translate("Planner.Title"), Creator = "WorkTrail" })
            ?? throw new InvalidOperationException("Could not create a PDF document.");
        var images = new Dictionary<string, SKImage>(StringComparer.Ordinal);
        try
        {
            if (!request.CompactHeaders)
            {
                var approved = Path.GetFullPath(Path.Combine(assetRoot, "Assets", "WorldClocks", "Skylines")) + Path.DirectorySeparatorChar;
                foreach (var index in groups.SelectMany(group => group.CityIndices).Distinct())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var city = snapshot.Cities[index];
                    var path = Path.GetFullPath(Path.Combine(assetRoot, city.Clock.SkylineAssetPath));
                    if (!path.StartsWith(approved, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unapproved skyline path.");
                    // The skyline is raster artwork. The text and grid remain vector content.
                    images[city.Clock.CityId] = SKImage.FromEncodedData(path) ?? throw new InvalidDataException("A skyline could not be decoded.");
                }
            }
            var cityPages = (groups.Count + citiesPerPage - 1) / citiesPerPage;
            var timePages = (rows.Length + rowsPerPage - 1) / rowsPerPage;
            var totalPages = cityPages * timePages;
            var page = 0;
            for (var timePage = 0; timePage < timePages; timePage++)
            {
                for (var cityPage = 0; cityPage < cityPages; cityPage++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var firstGroup = cityPage * citiesPerPage;
                    var count = Math.Min(citiesPerPage, groups.Count - firstGroup);
                    var width = (PageWidth - 2 * Margin - Gutter) / count;
                    var pageRows = rows.Skip(timePage * rowsPerPage).Take(rowsPerPage).ToArray();
                    var canvas = document.BeginPage(physicalWidth, physicalHeight);
                    canvas.Translate(outerMargin, outerMargin);
                    canvas.Clear(SKColors.White);
                    Text(canvas, "WorkTrail", Margin, 35, 15, bold, SKColor.Parse("#E34A2C"));
                    Text(canvas, strings.Translate("Planner.Title"), Margin + 90, 35, 15, bold);
                    var referenceName = snapshot.Cities.First(c => c.Clock.CityId == snapshot.ReferenceCityId).Clock.CityName;
                    Text(canvas, $"{referenceName} · {snapshot.ReferenceLocalTime.ToString("ddd d MMM yyyy HH:mm", strings.Culture)}", Margin, 57, 10, regular);
                    Text(canvas, $"{pageRows[0].StartUtc:dd/MM HH:mm} – {pageRows[^1].EndUtc:dd/MM HH:mm} UTC", Margin, 74, 9, regular);
                    var phaseKeys = new[] { "New", "WaxingCrescent", "FirstQuarter", "WaxingGibbous", "Full", "WaningGibbous", "LastQuarter", "WaningCrescent" };
                    var phaseIndex = (int)Math.Floor((snapshot.MoonPhaseAngleDegrees + 22.5) / 45) % 8;
                    DrawMoon(canvas, PageWidth - 235, 44, 17, snapshot.MoonPhaseAngleDegrees);
                    Text(canvas, strings.Translate("WorldClock.MoonPhase." + phaseKeys[phaseIndex]), PageWidth - 208, 43, 9, bold);
                    var nextPhase = new[] { "New", "FirstQuarter", "Full", "LastQuarter" }[snapshot.NextMoonQuarter];
                    Wrap(canvas, strings.Format("Planner.NextMoon", strings.Translate("WorldClock.MoonPhase." + nextPhase),
                        snapshot.NextMoonPhaseUtc.ToString("dd/MM HH:mm", strings.Culture) + " UTC"), PageWidth - 208, 57, 180, 8, regular, 2);
                    var legendX = Margin;
                    foreach (var state in Enum.GetValues<AvailabilityState>())
                    {
                        using var fill = new SKPaint { Color = StateColor(state, request.Palette) };
                        canvas.DrawRect(legendX, 86, 9, 9, fill);
                        Text(canvas, strings.Translate("Planner.State." + state), legendX + 14, 94, 8, regular);
                        legendX += (PageWidth - Margin * 2) / 4;
                    }
                    Text(canvas, "UTC", Margin + 8, tableTop - 10, 10, bold);
                    for (var i = 0; i < count; i++)
                    {
                        var group = groups[firstGroup + i];
                        var city = snapshot.Cities[group.PrimaryIndex];
                        var grouped = group.CityIndices.Count > 1;
                        var names = string.Join(" · ", group.CityIndices.Select(index => snapshot.Cities[index].Clock.CityName));
                        var x = Margin + Gutter + i * width;
                        if (request.CompactHeaders)
                        {
                            Wrap(canvas, names, x + 5, 111, width - 10, 10, bold, 2);
                            Text(canvas, city.Clock.LocalTime.ToString("HH:mm", strings.Culture), x + 5, 135, 10, bold);
                            Wrap(canvas, city.Clock.LocalTime.ToString("ddd d MMM", strings.Culture), x + 5, 147, width - 10, 8, regular, 1);
                            continue;
                        }
                        var memberWidth = (width - 8) / group.CityIndices.Count;
                        for (var memberIndex = 0; memberIndex < group.CityIndices.Count; memberIndex++)
                        {
                            var member = snapshot.Cities[group.CityIndices[memberIndex]];
                            var image = images[member.Clock.CityId];
                            var imageScale = Math.Min(64f / image.Height, memberWidth / image.Width);
                            var imageWidth = image.Width * imageScale;
                            var imageHeight = image.Height * imageScale;
                            var imageLeft = x + 4 + memberIndex * memberWidth + (memberWidth - imageWidth) / 2;
                            canvas.DrawImage(image, new SKRect(imageLeft, 169 - imageHeight, imageLeft + imageWidth, 169), new SKSamplingOptions(SKFilterMode.Linear));
                        }
                        Wrap(canvas, names, x + 5, 181, width - 10, 10, bold, 2);
                        Text(canvas, $"{city.Clock.LocalTime:HH:mm}  UTC{city.Clock.LocalTime:zzz}", x + 5, 205, 8.5f, regular);
                        var weather = city.Clock.Weather;
                        var weatherText = weather is { IsFresh: true }
                            ? $"{weather.TemperatureCelsius.ToString("0", strings.Culture)}°C · {strings.Translate("WorldClock.WeatherCondition." + weather.ConditionKey)}"
                            : strings.Translate("WorldClock.WeatherNoData");
                        if (grouped)
                        {
                            var details = group.CityIndices.Select(index =>
                            {
                                var member = snapshot.Cities[index];
                                var observation = member.Clock.Weather;
                                var condition = observation is { IsFresh: true }
                                    ? $"{observation.TemperatureCelsius.ToString("0", strings.Culture)}°C {strings.Translate("WorldClock.WeatherCondition." + observation.ConditionKey)}"
                                    : strings.Translate("WorldClock.WeatherNoData");
                                return $"{member.Clock.CityName}: {condition} {member.Schedule.Start:HH:mm}–{member.Schedule.End:HH:mm}";
                            });
                            Wrap(canvas, string.Join(" · ", details), x + 5, 216, width - 10, 8, regular, 2);
                        }
                        else
                        {
                            Wrap(canvas, weatherText, x + 5, 216, width - 10, 8, regular, 2);
                            Text(canvas, $"{city.Schedule.Start:HH:mm} – {city.Schedule.End:HH:mm}", x + 5, 235, 8, regular);
                        }
                    }
                    using var line = new SKPaint { Color = SKColor.Parse("#C7CDD0"), StrokeWidth = .35f, IsStroke = true };
                    using var selection = new SKPaint { Color = SKColor.Parse("#373737"), StrokeWidth = 2, IsStroke = true };
                    using var current = new SKPaint { Color = SKColor.Parse("#A5535C"), StrokeWidth = 1, IsStroke = true };
                    for (var r = 0; r < pageRows.Length; r++)
                    {
                        var row = pageRows[r];
                        var y = tableTop + r * RowHeight;
                        var isCurrent = row.StartUtc <= snapshot.NowUtc && snapshot.NowUtc < row.EndUtc;
                        var isSelected = row.StartUtc <= selectedInstant && selectedInstant < row.EndUtc;
                        var utcLabel = (isCurrent ? "● " : "") + row.StartUtc.ToString("HH:mm", strings.Culture);
                        Text(canvas, utcLabel, Margin + 6, y + 11, 9, regular, isCurrent ? current.Color : null);
                        for (var i = 0; i < count; i++)
                        {
                            var cell = snapshot.DisplayGroups is null
                                ? row.Cells[groups[firstGroup + i].PrimaryIndex]
                                : row.GroupedCells![firstGroup + i];
                            var x = Margin + Gutter + i * width;
                            using var fill = new SKPaint { Color = StateColor(cell.State, request.Palette) };
                            canvas.DrawRect(x, y, width, RowHeight, fill);
                            var isHome = groups[firstGroup + i].CityIndices.Any(index => snapshot.Cities[index].Clock.CityId == snapshot.HomeCityId);
                            if (!request.EmptyCells || isSelected || isHome)
                            {
                                var prefix = isSelected ? "" : cell.State == AvailabilityState.Unknown ? "? " : cell.State == AvailabilityState.Reserve ? "~ " : "";
                                Text(canvas, prefix + cell.LocalStart.ToString("ddd d MMM HH:mm", strings.Culture), x + 5, y + 11, 8.5f, regular,
                                    isSelected ? SKColor.Parse("#505050") : cell.IsNonWorkingDay ? SKColor.Parse("#713544") : null);
                            }
                            canvas.DrawRect(x, y, width, RowHeight, line);
                        }
                        if (isSelected)
                        {
                            canvas.DrawLine(Margin, y + 1, PageWidth - Margin, y + 1, selection);
                            canvas.DrawLine(Margin, y + RowHeight - 1, PageWidth - Margin, y + RowHeight - 1, selection);
                        }
                        if (isCurrent)
                        {
                            canvas.DrawLine(Margin, y + 1, PageWidth - Margin, y + 1, current);
                            canvas.DrawLine(Margin, y + RowHeight - 1, PageWidth - Margin, y + RowHeight - 1, current);
                        }
                    }
                    // Day separators remain PDF strokes and use the Core's reference-zone boundary.
                    // Draw them after all row fills so midnight lines also cover the UTC gutter.
                    using var dayLine = new SKPaint { Color = SKColor.Parse("#607D8B"), StrokeWidth = 1.25f, IsStroke = true };
                    for (var r = 0; r < pageRows.Length; r++)
                    {
                        var row = pageRows[r];
                        if (row.ReferenceDayBoundary is not { } boundary) continue;
                        var fraction = (boundary.InstantUtc - row.StartUtc).Ticks / (double)(row.EndUtc - row.StartUtc).Ticks;
                        var y = tableTop + (r + (float)fraction) * RowHeight;
                        canvas.DrawLine(Margin, y, PageWidth - Margin, y, dayLine);
                    }
                    if (!request.CompactHeaders)
                    {
                        var observations = snapshot.Cities.Where(c => c.Clock.Weather is { IsFresh: true }).Select(c => c.Clock.Weather!.ObservedAtUtc).ToArray();
                        var observationText = observations.Length == 0 ? strings.Translate("WorldClock.WeatherNoData")
                            : strings.Format("Planner.WeatherObserved", observations.Min().ToString("dd/MM HH:mm", strings.Culture) + " UTC");
                        Text(canvas, strings.Translate("Planner.CurrentWeather") + " · OpenWeather · " + observationText, Margin, PageHeight - 38, 8, regular);
                    }
                    if (!request.EmptyCells)
                    {
                        Text(canvas, strings.Translate("Planner.PdfNote"), Margin, PageHeight - 24, 8, regular);
                    }
                    Text(canvas, $"{++page} / {totalPages}", PageWidth - 55, PageHeight - 24, 9, regular);
                    document.EndPage();
                }
            }
            document.Close();
            return output.ToArray();
        }
        finally { foreach (var image in images.Values) image.Dispose(); }
    }

    private static SKColor StateColor(AvailabilityState state, int palette)
    {
        var (red, green, blue) = AvailabilityPalette.GetFill(state, palette);
        return new SKColor(red, green, blue);
    }

    private static void Text(SKCanvas canvas, string text, float x, float baseline, float size, PdfFont face, SKColor? color = null)
    {
        using var paint = new SKPaint { Color = color ?? SKColor.Parse("#203544"), IsAntialias = true };
        face.Draw(canvas, text, x, baseline, size, paint);
    }

    private static void Wrap(SKCanvas canvas, string text, float x, float y, float width, float size, PdfFont face, int maxLines)
    {
        // Fit complete Unicode text, including city names and CJK labels without word separators.
        // Never silently discard a weather condition or a part of the next-phase date.
        List<string> lines;
        while (true)
        {
            lines = new List<string>();
            var line = "";
            foreach (System.Text.RegularExpressions.Match word in System.Text.RegularExpressions.Regex.Matches(text, @"\S+\s*"))
            {
                if (face.Measure(line + word.Value.TrimEnd(), size) > width && line.Length > 0)
                {
                    lines.Add(line.TrimEnd());
                    line = "";
                }
                var elements = System.Globalization.StringInfo.GetTextElementEnumerator(word.Value);
                while (elements.MoveNext())
                {
                    var element = elements.GetTextElement();
                    if (face.Measure(line + element, size) > width && line.Length > 0)
                    {
                        lines.Add(line.TrimEnd());
                        line = "";
                    }
                    line += element;
                }
            }
            if (line.Length > 0) lines.Add(line.TrimEnd());
            if (lines.Count <= maxLines) break;
            size *= .95f;
        }
        for (var index = 0; index < lines.Count; index++)
            Text(canvas, lines[index], x, y + index * (size + 2), size, face);
    }

    /// <summary>Embeds font runs with per-character coverage instead of rendering missing-glyph squares.</summary>
    private sealed class PdfFont : IDisposable
    {
        private readonly SKTypeface _primary;
        private readonly SKFont _coverage;
        private readonly Dictionary<int, SKTypeface> _fallbacks = new();

        internal PdfFont(string family, SKFontStyle style)
        {
            _primary = SKTypeface.FromFamilyName(family, style);
            _coverage = new SKFont(_primary);
        }

        internal float Measure(string text, float size)
        {
            var width = 0f;
            foreach (var (value, face) in Runs(text))
            {
                using var font = new SKFont(face, size);
                width += font.MeasureText(value);
            }
            return width;
        }

        internal void Draw(SKCanvas canvas, string text, float x, float baseline, float size, SKPaint paint)
        {
            foreach (var (value, face) in Runs(text))
            {
                using var font = new SKFont(face, size);
                canvas.DrawText(value, x, baseline, SKTextAlign.Left, font, paint);
                x += font.MeasureText(value);
            }
        }

        private IEnumerable<(string Text, SKTypeface Face)> Runs(string text)
        {
            SKTypeface? current = null;
            var run = new System.Text.StringBuilder();
            foreach (var rune in text.EnumerateRunes())
            {
                var face = _primary;
                if (!_coverage.ContainsGlyph(rune.Value) && !_fallbacks.TryGetValue(rune.Value, out face))
                {
                    face = SKFontManager.Default.MatchCharacter(_primary.FamilyName, _primary.FontStyle, null, rune.Value)
                        ?? throw new InvalidOperationException($"No installed font covers U+{rune.Value:X}.");
                    _fallbacks.Add(rune.Value, face);
                }
                if (current is not null && !ReferenceEquals(current, face))
                {
                    yield return (run.ToString(), current);
                    run.Clear();
                }
                current = face;
                run.Append(rune.ToString());
            }
            if (current is not null) yield return (run.ToString(), current);
        }

        /// <summary>Releases only fonts acquired for this export.</summary>
        public void Dispose()
        {
            _coverage.Dispose();
            foreach (var face in _fallbacks.Values.Distinct()) face.Dispose();
            _primary.Dispose();
        }
    }

    private static void DrawMoon(SKCanvas canvas, float x, float y, float radius, double degrees)
    {
        using var dark = new SKPaint { Color = SKColor.Parse("#344452"), IsAntialias = true };
        using var light = new SKPaint { Color = SKColor.Parse("#E7DDC7"), IsAntialias = true };
        canvas.DrawCircle(x, y, radius, dark);
        using var builder = new SKPathBuilder();
        var angle = degrees * Math.PI / 180;
        var direction = Math.Sin(angle) >= 0 ? 1 : -1;
        for (var i = 0; i <= 64; i++)
        {
            var v = -1d + i / 32d;
            var px = x + direction * radius * Math.Sqrt(Math.Max(0, 1 - v * v));
            var py = y + radius * v;
            if (i == 0) builder.MoveTo((float)px, (float)py); else builder.LineTo((float)px, (float)py);
        }
        for (var i = 64; i >= 0; i--)
        {
            var v = -1d + i / 32d;
            builder.LineTo((float)(x + direction * radius * Math.Cos(angle) * Math.Sqrt(Math.Max(0, 1 - v * v))), (float)(y + radius * v));
        }
        builder.Close();
        using var path = builder.Detach();
        canvas.DrawPath(path, light);
    }
}
