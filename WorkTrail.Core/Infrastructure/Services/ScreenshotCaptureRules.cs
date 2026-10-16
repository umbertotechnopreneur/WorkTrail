// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkTrail.Services;

/// <summary>Ordered internal capture policy, shipped as data without a user-facing editor.</summary>
internal sealed record ScreenshotCaptureRules(IReadOnlyList<ScreenshotCaptureRule> Rules,
    IReadOnlyList<ScreenshotApplicationProfile>? ApplicationProfiles = null,
    IReadOnlyList<string>? AnalysisBlacklist = null,
    IReadOnlyList<ScreenshotContextRule>? ContextRules = null,
    int DefaultIdleTimeoutSeconds = 60,
    IReadOnlyDictionary<string, string>? MicroPrompts = null,
    ScreenshotSimilarityOptions? Similarity = null)
{
    internal static ScreenshotCaptureRules Load()
    {
        using var stream = typeof(ScreenshotCaptureRules).Assembly.GetManifestResourceStream("WorkTrail.Data.screenshot-capture-rules.json")
            ?? throw new InvalidOperationException("Screenshot capture rules are missing.");
        var policy = JsonSerializer.Deserialize<ScreenshotCaptureRules>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidOperationException("Screenshot capture rules are invalid.");
        policy.Validate();
        return policy;
    }

    internal void Validate()
    {
        Similarity?.Validate();
        if (DefaultIdleTimeoutSeconds is < 1 or > 7200)
            throw new InvalidDataException("Default capture inactivity timeout must be between 1 and 7200 seconds.");
        if (MicroPrompts?.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value)) == true)
            throw new InvalidDataException("Micro-prompts require nonempty identifiers and instructions.");
        if (Rules is null || Rules.Count == 0) throw new InvalidDataException("Screenshot capture rules must not be empty.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Rules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id))
                throw new InvalidDataException("Screenshot capture rules require unique nonempty identifiers.");
            var windowSubject = rule.Subject is "active-window" or "secondary-windows";
            var monitorSubject = rule.Subject is "active-monitor" or "secondary-monitors";
            if ((!windowSubject && !monitorSubject) || rule.Capture != (windowSubject ? "window" : "monitor")
                || rule.Priority is not ("primary" or "secondary" or "context")
                || rule.Destination is not ("analysis" or "gallery" or "both")
                || !double.IsFinite(rule.MinimumVisibleFraction) || rule.MinimumVisibleFraction is < 0 or > 1
                || (rule.RequireSecondaryMatch && rule.Subject != "secondary-windows")
                || (rule.ProcessNames is not null && (!windowSubject || rule.ProcessNames.Any(string.IsNullOrWhiteSpace))))
                throw new InvalidDataException($"Screenshot capture rule '{rule.Id}' is invalid.");
        }
        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in ApplicationProfiles ?? [])
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Id) || !profileIds.Add(profile.Id)
                || string.IsNullOrWhiteSpace(profile.Label) || profile.ProcessNames is not { Count: > 0 }
                || profile.ActiveAttention is not ("high" or "normal" or "low")
                || profile.BackgroundAttention is not ("high" or "normal" or "low")
                || profile.IdleTimeoutMinutes is < 1 or > 120
                || profile.ProcessNames.Any(process => string.IsNullOrWhiteSpace(process) || !processes.Add(process)))
                throw new InvalidDataException("Screenshot application profiles require unique process aliases and valid attention values.");
            ValidateMicroPrompts(profile.MicroPromptIds);
            profile.Similarity?.Validate();
        }
        if (AnalysisBlacklist?.Any(string.IsNullOrWhiteSpace) == true)
            throw new InvalidDataException("Screenshot analysis blacklist contains an empty process name.");
        var contextIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in ContextRules ?? [])
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || !contextIds.Add(rule.Id)
                || rule.ProcessNames is not { Count: > 0 } || rule.ProcessNames.Any(string.IsNullOrWhiteSpace)
                || rule.TitleContainsAny is not { Count: > 0 } || rule.TitleContainsAny.Any(string.IsNullOrWhiteSpace)
                || rule.ActiveAttention is not (null or "high" or "normal" or "low")
                || rule.BackgroundAttention is not (null or "high" or "normal" or "low")
                || rule.IdleTimeoutMinutes is < 1 or > 120)
                throw new InvalidDataException("Context rules require unique IDs, process-scoped title hints and valid policy values.");
            ValidateMicroPrompts(rule.MicroPromptIds);
            rule.Similarity?.Validate();
        }
    }

    private void ValidateMicroPrompts(IReadOnlyList<string>? ids)
    {
        if (ids?.Any(id => string.IsNullOrWhiteSpace(id) || MicroPrompts?.ContainsKey(id) != true) == true)
            throw new InvalidDataException("A screenshot profile references an unknown micro-prompt.");
    }

    /// <summary>Resolves selection hints, never inferred user activity, from one visible window.</summary>
    internal ScreenshotWindowPolicy ResolveWindowPolicy(string processName, string windowTitle)
    {
        var profile = ApplicationProfiles?.FirstOrDefault(item =>
            item.ProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase));
        // Explicit precedence, then stable ID order: JSON reordering cannot change a tie.
        var rule = ContextRules?.Where(item => item.Enabled
            && item.ProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase)
            && item.TitleContainsAny.Any(hint => windowTitle.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.Priority).ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
        return new ScreenshotWindowPolicy(
            rule?.ActiveAttention ?? profile?.ActiveAttention ?? "normal",
            rule?.BackgroundAttention ?? profile?.BackgroundAttention ?? "normal",
            rule?.CaptureSecondary ?? profile?.CaptureSecondary ?? (profile is not null && profile.BackgroundAttention != "low"),
            (rule?.IdleTimeoutMinutes ?? profile?.IdleTimeoutMinutes) is { } minutes ? minutes * 60 : DefaultIdleTimeoutSeconds,
            rule is not null ? $"title-hint:{rule.Id}" : profile is not null ? $"app-profile:{profile.Id}" : "default",
            rule?.MicroPromptIds ?? profile?.MicroPromptIds ?? [],
            rule?.Similarity ?? profile?.Similarity ?? Similarity ?? new ScreenshotSimilarityOptions());
    }

    /// <summary>Total allowed input-idle time, not additional active work or evidence of a meeting.</summary>
    internal int GetIdleTimeoutSeconds(string processName, string windowTitle) =>
        ResolveWindowPolicy(processName, windowTitle).IdleTimeoutSeconds;

    internal IReadOnlyList<CaptureImageTarget> Plan(IReadOnlyList<CaptureDisplay> displays,
        ForegroundCaptureTarget foreground, IReadOnlyList<CaptureWindow> windows, bool activeWindowOnly)
    {
        Validate();
        if (displays.Count == 0) throw new InvalidOperationException("No displays are available for screenshot capture.");
        if (!displays.Any(display => HasArea(Intersect(display.Bounds, foreground.WindowBounds))))
            throw new InvalidOperationException("The active window is outside the visible desktop.");
        var focused = displays[ScreenCaptureService.SelectFocusedDisplayIndex(displays.Select(display => display.Bounds).ToArray(), foreground.WindowBounds)];
        if (!windows.Any(window => window.WindowHandle == foreground.WindowHandle))
            throw new InvalidOperationException("The active window is missing from screenshot visibility information.");
        var result = new List<CaptureImageTarget>();
        foreach (var rule in Rules.Where(rule => rule.Enabled && (!activeWindowOnly || rule.Subject == "active-window")))
        {
            if (rule.Capture == "monitor")
            {
                foreach (var display in displays.Where(display => rule.Subject == "active-monitor"
                    ? display.Index == focused.Index : display.Index != focused.Index))
                    AddTarget(result, new CaptureImageTarget(display.Stem, "monitor-context", display, display.Bounds,
                        [display.Bounds], [display.Index], IntPtr.Zero, string.Empty, string.Empty, rule.Priority,
                        rule.Destination != "gallery", rule.Destination != "analysis"));
                continue;
            }

            for (var index = 0; index < windows.Count; index++)
            {
                var window = windows[index];
                var active = window.WindowHandle == foreground.WindowHandle;
                if (!active && !window.IsApplicationWindow) continue;
                if ((rule.Subject == "active-window") != active ||
                    (rule.ProcessNames is { Count: > 0 } && !rule.ProcessNames.Contains(window.ProcessName, StringComparer.OrdinalIgnoreCase))) continue;
                var windowPolicy = ResolveWindowPolicy(window.ProcessName, window.WindowTitle);
                if (!active && rule.RequireSecondaryMatch && !windowPolicy.CaptureSecondary) continue;
                var regions = VisibleRegions(displays, windows, index);
                if (regions.Count == 0) continue;
                var onScreenArea = displays.Sum(display => Area(Intersect(display.Bounds, window.Bounds)));
                if (onScreenArea == 0 || regions.Sum(Area) / (double)onScreenArea < rule.MinimumVisibleFraction) continue;
                var bounds = new NativeMethods.Rect
                {
                    Left = regions.Min(region => region.Left),
                    Top = regions.Min(region => region.Top),
                    Right = regions.Max(region => region.Right),
                    Bottom = regions.Max(region => region.Bottom)
                };
                var display = displays.OrderByDescending(item => regions.Sum(region => Area(Intersect(item.Bounds, region)))).First();
                var related = displays.Where(item => regions.Any(region => HasArea(Intersect(item.Bounds, region))))
                    .Select(item => item.Index).ToArray();
                AddTarget(result, new CaptureImageTarget(active ? "active-window" : $"secondary-window-{index + 1}",
                    active ? "active-window" : "secondary-window", display, bounds, regions.ToArray(), related,
                    window.WindowHandle, window.ApplicationName, window.WindowTitle, rule.Priority,
                    rule.Destination != "gallery", rule.Destination != "analysis",
                    RuleReason: windowPolicy.Reason, MicroPromptIds: windowPolicy.MicroPromptIds));
            }
        }
        // Apply vetoes after destination union: another matching rule must never restore AI access.
        var blockedRegions = windows.Select((window, index) => (window, index))
            .Where(item => IsBlacklisted(item.window.ProcessName))
            .SelectMany(item => VisibleRegions(displays, windows, item.index)).ToArray();
        result = result.Select(target =>
        {
            var window = windows.FirstOrDefault(item => item.WindowHandle == target.WindowHandle);
            var windowPolicy = window is null ? null : ResolveWindowPolicy(window.ProcessName, window.WindowTitle);
            var attention = windowPolicy is null ? "normal" : target.WindowHandle == foreground.WindowHandle
                ? windowPolicy.ActiveAttention : windowPolicy.BackgroundAttention;
            var blocked = (window is not null && IsBlacklisted(window.ProcessName))
                || target.VisibleRegions.Any(region => blockedRegions.Any(blockedRegion => HasArea(Intersect(region, blockedRegion))));
            return target with { Attention = attention, ForAnalysis = target.ForAnalysis && !blocked };
        }).Where(target => target.ForAnalysis || target.ForGallery).ToList();
        if (result.Count == 0) throw new InvalidOperationException("Screenshot capture rules select no visible images with an allowed destination.");
        // Stable sort keeps configured rule and native window order for equal priorities.
        return result.OrderBy(target => PriorityRank(target.Priority)).ToArray();
    }

    private bool IsBlacklisted(string processName) => AnalysisBlacklist?.Contains(processName, StringComparer.OrdinalIgnoreCase) == true;

    private static List<NativeMethods.Rect> VisibleRegions(IReadOnlyList<CaptureDisplay> displays,
        IReadOnlyList<CaptureWindow> windows, int index)
    {
        var regions = displays.Select(display => Intersect(display.Bounds, windows[index].Bounds)).Where(HasArea).ToList();
        // Unmatched and blacklisted windows still occlude lower targets.
        foreach (var above in windows.Take(index))
            regions = regions.SelectMany(region => Subtract(region, above.Bounds)).ToList();
        return regions;
    }

    private static void AddTarget(List<CaptureImageTarget> targets, CaptureImageTarget candidate)
    {
        var index = targets.FindIndex(target =>
            (target.WindowHandle == candidate.WindowHandle || target.WindowHandle == IntPtr.Zero || candidate.WindowHandle == IntPtr.Zero)
            && SameBounds(target.Bounds, candidate.Bounds) && SameRegions(target.VisibleRegions, candidate.VisibleRegions));
        if (index < 0) { targets.Add(candidate); return; }
        var existing = targets[index];
        var identity = existing.WindowHandle == IntPtr.Zero && candidate.WindowHandle != IntPtr.Zero ? candidate : existing;
        targets[index] = identity with
        {
            Priority = PriorityRank(existing.Priority) <= PriorityRank(candidate.Priority) ? existing.Priority : candidate.Priority,
            ForAnalysis = existing.ForAnalysis || candidate.ForAnalysis,
            ForGallery = existing.ForGallery || candidate.ForGallery
        };
    }

    private static int PriorityRank(string priority) => priority switch { "primary" => 0, "secondary" => 1, _ => 2 };
    private static bool SameRegions(IReadOnlyList<NativeMethods.Rect> first, IReadOnlyList<NativeMethods.Rect> second) =>
        first.Count == second.Count && first.All(region => second.Any(other => SameBounds(region, other)));

    private static IEnumerable<NativeMethods.Rect> Subtract(NativeMethods.Rect source, NativeMethods.Rect occluder)
    {
        var overlap = Intersect(source, occluder);
        if (!HasArea(overlap)) { yield return source; yield break; }
        if (source.Top < overlap.Top) yield return new() { Left = source.Left, Top = source.Top, Right = source.Right, Bottom = overlap.Top };
        if (overlap.Bottom < source.Bottom) yield return new() { Left = source.Left, Top = overlap.Bottom, Right = source.Right, Bottom = source.Bottom };
        if (source.Left < overlap.Left) yield return new() { Left = source.Left, Top = overlap.Top, Right = overlap.Left, Bottom = overlap.Bottom };
        if (overlap.Right < source.Right) yield return new() { Left = overlap.Right, Top = overlap.Top, Right = source.Right, Bottom = overlap.Bottom };
    }

    private static NativeMethods.Rect Intersect(NativeMethods.Rect first, NativeMethods.Rect second) => new()
    {
        Left = Math.Max(first.Left, second.Left),
        Top = Math.Max(first.Top, second.Top),
        Right = Math.Min(first.Right, second.Right),
        Bottom = Math.Min(first.Bottom, second.Bottom)
    };
    private static bool HasArea(NativeMethods.Rect value) => value.Right > value.Left && value.Bottom > value.Top;
    private static long Area(NativeMethods.Rect value) => HasArea(value) ? (long)(value.Right - value.Left) * (value.Bottom - value.Top) : 0;
    private static bool SameBounds(NativeMethods.Rect first, NativeMethods.Rect second) =>
        first.Left == second.Left && first.Top == second.Top && first.Right == second.Right && first.Bottom == second.Bottom;
}

internal sealed record ScreenshotCaptureRule(string Id, bool Enabled, string Subject, string Capture,
    string Priority, string Destination, IReadOnlyList<string>? ProcessNames = null,
    bool RequireSecondaryMatch = false, double MinimumVisibleFraction = 0);

internal sealed record ScreenshotApplicationProfile(string Id, string Label, IReadOnlyList<string> ProcessNames,
    string ActiveAttention, string BackgroundAttention, int? IdleTimeoutMinutes = null, bool? CaptureSecondary = null,
    IReadOnlyList<string>? MicroPromptIds = null, ScreenshotSimilarityOptions? Similarity = null);

internal sealed record ScreenshotContextRule(string Id, bool Enabled, int Priority,
    IReadOnlyList<string> ProcessNames, IReadOnlyList<string> TitleContainsAny,
    string? ActiveAttention = null, string? BackgroundAttention = null,
    bool? CaptureSecondary = null, int? IdleTimeoutMinutes = null, IReadOnlyList<string>? MicroPromptIds = null,
    ScreenshotSimilarityOptions? Similarity = null);

internal sealed record ScreenshotWindowPolicy(string ActiveAttention, string BackgroundAttention,
    bool CaptureSecondary, int IdleTimeoutSeconds, string Reason, IReadOnlyList<string> MicroPromptIds,
    ScreenshotSimilarityOptions Similarity);

/// <summary>Visible top-level window facts in desktop z order, before selecting policy subjects.</summary>
internal sealed record CaptureWindow(IntPtr WindowHandle, NativeMethods.Rect Bounds, string ProcessName,
    string ApplicationName, string WindowTitle, bool IsApplicationWindow = true);

internal sealed record CaptureImageTarget(string Stem, string Role, CaptureDisplay Display,
    NativeMethods.Rect Bounds, IReadOnlyList<NativeMethods.Rect> VisibleRegions, IReadOnlyList<int> RelatedScreenIndices,
    IntPtr WindowHandle, string ApplicationName, string WindowTitle, string Priority, bool ForAnalysis, bool ForGallery,
    string Attention = "normal", string RuleReason = "capture-rule", IReadOnlyList<string>? MicroPromptIds = null);

/// <summary>Associates one image with its physical screen, visible desktop region and rule destinations.</summary>
public sealed record ScreenshotImageMetadata(
    string Path,
    string Role,
    int ScreenIndex,
    string ScreenName,
    int ScreenLeft,
    int ScreenTop,
    int ScreenWidth,
    int ScreenHeight,
    int ImageLeft,
    int ImageTop,
    int ImageWidth,
    int ImageHeight,
    bool IsActiveScreen,
    string ApplicationName,
    string WindowTitle,
    IReadOnlyList<int> RelatedScreenIndices,
    string Priority = "context",
    bool ForAnalysis = true,
    bool ForGallery = true,
    string Attention = "normal", string RuleReason = "capture-rule", IReadOnlyList<string>? MicroPromptIds = null);
