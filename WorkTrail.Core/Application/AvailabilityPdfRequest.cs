// SPDX-License-Identifier: MIT
namespace WorkTrail.Application;

/// <summary>Supported PDF paper sizes.</summary>
public enum AvailabilityPdfPageSize { A4, A3, Letter, Legal }

/// <summary>Supported PDF page orientations.</summary>
public enum AvailabilityPdfOrientation { Landscape, Portrait }

/// <summary>Supported PDF page margins.</summary>
public enum AvailabilityPdfMargins { Narrow, None }

/// <summary>Freezes the displayed planner data, interval and display options for a vector PDF export.</summary>
public sealed record AvailabilityPdfRequest(AvailabilityPlannerSnapshot Snapshot, DateTimeOffset StartUtc,
    int Hours, string Language, bool EmptyCells = false, bool CompactHeaders = false, DateTimeOffset? SelectedStartUtc = null, int Palette = 0)
{
    /// <summary>Gets the selected paper size.</summary>
    public AvailabilityPdfPageSize PageSize { get; init; } = AvailabilityPdfPageSize.A4;
    /// <summary>Gets the selected page orientation.</summary>
    public AvailabilityPdfOrientation Orientation { get; init; } = AvailabilityPdfOrientation.Landscape;
    /// <summary>Gets the selected margin preset.</summary>
    public AvailabilityPdfMargins Margins { get; init; } = AvailabilityPdfMargins.Narrow;
    /// <summary>Gets the proposed PDF file name.</summary>
    public string FileName { get; init; } = string.Empty;
}
