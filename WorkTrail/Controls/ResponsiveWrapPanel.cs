// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WorkTrail.Controls;

/// <summary>Places controls in rows, wrapping whole controls when the available width is exhausted.</summary>
public sealed class ResponsiveWrapPanel : Panel
{
    /// <summary>Gets or sets the space between adjacent controls.</summary>
    public double HorizontalSpacing { get; set; } = 12;

    /// <summary>Gets or sets the space between rows.</summary>
    public double VerticalSpacing { get; set; } = 12;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Layout(availableSize.Width, false);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, true);
        return finalSize;
    }

    private Size Layout(double width, bool arrange)
    {
        double rowWidth = 0, y = 0, rowHeight = 0, usedWidth = 0;
        var row = new List<UIElement>();
        void CompleteRow()
        {
            double x = 0;
            if (arrange)
                foreach (var item in row)
                {
                    var itemWidth = Math.Min(item.DesiredSize.Width, width);
                    item.Arrange(new Rect(x, y, itemWidth, rowHeight));
                    x += itemWidth + HorizontalSpacing;
                }
            usedWidth = Math.Max(usedWidth, rowWidth);
        }
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            var childWidth = Math.Min(size.Width, width);
            if (row.Count > 0 && rowWidth + HorizontalSpacing + childWidth > width)
            {
                CompleteRow();
                y += rowHeight + VerticalSpacing;
                row.Clear();
                rowWidth = 0;
                rowHeight = 0;
            }
            rowWidth += (row.Count == 0 ? 0 : HorizontalSpacing) + childWidth;
            row.Add(child);
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        CompleteRow();
        return new Size(usedWidth, y + rowHeight);
    }
}
