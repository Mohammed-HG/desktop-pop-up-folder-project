using System;
using System.Windows;
using System.Windows.Controls;

namespace DesktopFolders;

internal sealed class TileGrid(int preferredColumns, double iconSize, double spacing, double labelSize) : Panel
{
    public int VisibleColumns { get; private set; } = 1;
    private double TileWidth => Math.Max(88, iconSize + 20);
    private double RowHeight => iconSize + 14 + (labelSize > 0 ? Math.Ceiling(labelSize * 1.55) + 4 : 0);

    private (int Columns, double Width, double Gap) Layout(double availableWidth)
    {
        var gap = spacing;
        if (!double.IsFinite(availableWidth))
            availableWidth = preferredColumns * (TileWidth + gap) - gap;
        var columns = Math.Clamp((int)Math.Floor((availableWidth + gap) / (TileWidth + gap)), 1, preferredColumns);
        return (columns, Math.Max(0, Math.Min(TileWidth, availableWidth)), gap);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, width, gap) = Layout(availableSize.Width);
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(width, RowHeight));
        var rows = (InternalChildren.Count + columns - 1) / columns;
        return new Size(columns * width + (columns - 1) * gap, Math.Max(0, rows * (RowHeight + gap) - gap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, width, gap) = Layout(finalSize.Width);
        VisibleColumns = columns;
        var left = Math.Max(0, (finalSize.Width - (columns * width + (columns - 1) * gap)) / 2);
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var x = left + index % columns * (width + gap);
            var y = index / columns * (RowHeight + gap);
            InternalChildren[index].Arrange(new Rect(x, y, width, RowHeight));
        }
        return finalSize;
    }
}
