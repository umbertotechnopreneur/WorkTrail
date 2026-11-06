// SPDX-License-Identifier: MIT
namespace WorkTrail.Presentation;

/// <summary>Projects one equal column width for every city without depending on a UI framework.</summary>
public sealed class TimelineColumnLayout
{
    /// <summary>Gets the smallest readable width of a city column in logical pixels.</summary>
    public const double MinimumColumnWidth = 140;

    private double _viewportWidth;
    private int _columnCount;
    private double _preferredWidth;
    private double _columnWidth = MinimumColumnWidth;

    /// <summary>Occurs when a preference or projected geometry changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the effective width shared by all headers and cells.</summary>
    public double ColumnWidth => _columnWidth;

    /// <summary>Gets the manually selected width, or zero when columns fit the viewport automatically.</summary>
    public double PreferredWidth => _preferredWidth;

    /// <summary>Gets the complete width of the equal-column timeline.</summary>
    public double TotalWidth => _columnWidth * _columnCount;

    /// <summary>Updates available space and city count while preserving the user's width preference.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative or non-finite.</exception>
    public void UpdateViewport(double width, int count)
    {
        if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (width == _viewportWidth && count == _columnCount) return;
        _viewportWidth = width;
        _columnCount = count;
        Project();
    }

    /// <summary>Selects a manual width bounded by readable and available space, or zero for automatic fitting.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The preference is negative or non-finite.</exception>
    public void SetPreferredWidth(double width)
    {
        if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        var preferred = width == 0 ? 0 : Math.Max(MinimumColumnWidth, width);
        // Preserve the saved choice through temporary viewport constraints; only Project limits its display.
        if (preferred == _preferredWidth) return;
        _preferredWidth = preferred;
        Project();
    }

    /// <summary>Resizes every column while keeping the dragged column's right edge under the pointer.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The delta is non-finite or the column index is outside the timeline.</exception>
    public void ResizeColumn(double delta, int columnIndex)
    {
        if (!double.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
        if (columnIndex < 0 || columnIndex >= _columnCount) throw new ArgumentOutOfRangeException(nameof(columnIndex));
        if (delta == 0 || MaximumColumnWidth <= MinimumColumnWidth) return;
        var requested = _columnWidth + delta / (columnIndex + 1d);
        if (requested >= MaximumColumnWidth) FitColumns();
        else SetPreferredWidth(Math.Max(MinimumColumnWidth, requested));
    }

    /// <summary>Restores automatic fitting of the shared column width to the viewport.</summary>
    public void FitColumns() => SetPreferredWidth(0);

    private double MaximumColumnWidth => _columnCount > 0
        ? Math.Max(MinimumColumnWidth, _viewportWidth / _columnCount) : MinimumColumnWidth;

    private void Project()
    {
        _columnWidth = _preferredWidth == 0 ? MaximumColumnWidth
            : Math.Clamp(_preferredWidth, MinimumColumnWidth, MaximumColumnWidth);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
