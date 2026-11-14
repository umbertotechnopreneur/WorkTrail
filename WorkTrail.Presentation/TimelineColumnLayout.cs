// SPDX-License-Identifier: MIT
namespace WorkTrail.Presentation;

/// <summary>Projects an independently sized home column and shared widths for the remaining cities.</summary>
public sealed class TimelineColumnLayout
{
    /// <summary>Gets the smallest readable width of a city column in logical pixels.</summary>
    public const double MinimumColumnWidth = 140;
    private const double MaximumColumnWidth = 16384;
    private double _viewportWidth;
    private int _columnCount;
    private bool _hasHomeColumn;
    private double _preferredWidth;
    private double _preferredHomeWidth;
    private double _columnWidth = MinimumColumnWidth;
    private double _homeWidth = MinimumColumnWidth;

    /// <summary>Occurs when a preference or projected geometry changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the effective width of ordinary columns.</summary>
    public double ColumnWidth => _columnWidth;
    /// <summary>Gets the preferred ordinary width, or zero for automatic fitting.</summary>
    public double PreferredWidth => _preferredWidth;
    /// <summary>Gets the preferred home width, or zero for automatic fitting.</summary>
    public double PreferredHomeWidth => _preferredHomeWidth;
    /// <summary>Gets the full width of the timeline.</summary>
    public double TotalWidth => _columnCount == 0 ? 0
        : _hasHomeColumn ? _homeWidth + _columnWidth * (_columnCount - 1) : _columnWidth * _columnCount;

    /// <summary>Gets the width of one displayed column.</summary>
    public double GetColumnWidth(int index) => _hasHomeColumn && index == 0 ? _homeWidth : _columnWidth;

    /// <summary>Specifies whether the first displayed column contains the home city.</summary>
    public void SetHomeColumnPresent(bool present)
    {
        if (_hasHomeColumn == present) return;
        _hasHomeColumn = present;
        Project();
    }

    /// <summary>Updates the viewport and column count.</summary>
    public void UpdateViewport(double width, int count)
    {
        if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (width == _viewportWidth && count == _columnCount) return;
        _viewportWidth = width;
        _columnCount = count;
        Project();
    }

    /// <summary>Applies saved widths, using zero for automatic fitting.</summary>
    public void SetPreferredWidths(double homeWidth, double width)
    {
        if (!double.IsFinite(homeWidth) || homeWidth < 0 || !double.IsFinite(width) || width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        var home = homeWidth == 0 ? 0 : Math.Clamp(homeWidth, MinimumColumnWidth, MaximumColumnWidth);
        var other = width == 0 ? 0 : Math.Clamp(width, MinimumColumnWidth, MaximumColumnWidth);
        if (home == _preferredHomeWidth && other == _preferredWidth) return;
        _preferredHomeWidth = home;
        _preferredWidth = other;
        Project();
    }

    /// <summary>Resizes home alone at its edge, or all columns at another edge.</summary>
    public void ResizeColumn(double delta, int columnIndex)
    {
        if (!double.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
        if (columnIndex < 0 || columnIndex >= _columnCount) throw new ArgumentOutOfRangeException(nameof(columnIndex));
        if (delta == 0) return;
        if (_hasHomeColumn && columnIndex == 0)
        {
            var nextHome = Math.Clamp(_homeWidth + delta, MinimumColumnWidth, MaximumColumnWidth);
            SetPreferredWidths(nextHome, _preferredWidth == 0 ? _columnWidth : _preferredWidth);
            return;
        }
        var increment = delta / (columnIndex + 1d);
        var lower = MinimumColumnWidth - _columnWidth;
        var upper = MaximumColumnWidth - _columnWidth;
        if (_hasHomeColumn)
        {
            lower = Math.Max(lower, MinimumColumnWidth - _homeWidth);
            upper = Math.Min(upper, MaximumColumnWidth - _homeWidth);
        }
        increment = Math.Clamp(increment, lower, upper);
        SetPreferredWidths(_hasHomeColumn ? _homeWidth + increment : _preferredHomeWidth, _columnWidth + increment);
    }

    /// <summary>Restores automatic fitting for the dragged home column or the entire timeline.</summary>
    public void FitColumn(int columnIndex)
    {
        if (_hasHomeColumn && columnIndex == 0)
            SetPreferredWidths(0, _preferredWidth == 0 ? _columnWidth : _preferredWidth);
        else
            SetPreferredWidths(0, 0);
    }

    private void Project()
    {
        var count = Math.Max(1, _columnCount);
        var fitted = Math.Max(MinimumColumnWidth, _viewportWidth / count);
        if (!_hasHomeColumn)
        {
            _homeWidth = _columnWidth = _preferredWidth == 0 ? fitted : _preferredWidth;
        }
        else if (_columnCount <= 1)
        {
            _homeWidth = _preferredHomeWidth == 0 ? fitted : _preferredHomeWidth;
            _columnWidth = _preferredWidth == 0 ? fitted : _preferredWidth;
        }
        else
        {
            _homeWidth = _preferredHomeWidth == 0
                ? (_preferredWidth == 0 ? fitted : Math.Max(MinimumColumnWidth, _viewportWidth - _preferredWidth * (_columnCount - 1)))
                : _preferredHomeWidth;
            _columnWidth = _preferredWidth == 0
                ? (_preferredHomeWidth == 0 ? fitted : Math.Max(MinimumColumnWidth, (_viewportWidth - _homeWidth) / (_columnCount - 1)))
                : _preferredWidth;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
