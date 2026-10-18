// SPDX-License-Identifier: MIT
using System.Text.Json;

namespace WorkTrail.Services;

/// <summary>Validated provider output; the UI receives Markdown independently of machine-readable estimates.</summary>
internal sealed record ScreenshotAnalysisResponse(string Summary, ActivityContinuityAssessment ActivityContinuity)
{
    internal static ScreenshotAnalysisResponse Parse(string response)
    {
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2
            || !root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(summary.GetString())
            || !root.TryGetProperty("activity_continuity", out var continuity) || continuity.ValueKind != JsonValueKind.Object
            || continuity.EnumerateObject().Count() != 2
            || !continuity.TryGetProperty("continues_confidence", out var continues) || continues.ValueKind != JsonValueKind.Number || !continues.TryGetDouble(out var continuesConfidence)
            || !continuity.TryGetProperty("ended_confidence", out var ended) || ended.ValueKind != JsonValueKind.Number || !ended.TryGetDouble(out var endedConfidence))
            throw new JsonException("The AI provider did not return the required screenshot analysis object.");
        var assessment = new ActivityContinuityAssessment(continuesConfidence, endedConfidence);
        if (!assessment.IsValid)
            throw new JsonException("Activity continuity confidences must be finite values from zero to one with a total no greater than one.");
        return new ScreenshotAnalysisResponse(summary.GetString()!, assessment);
    }
}
