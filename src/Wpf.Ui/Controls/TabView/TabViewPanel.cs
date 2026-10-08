// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Wpf.Ui.Controls;

/// <summary>Provisional template panel. Group headers are arranged in a separate Canvas, never in its item children.</summary>
public sealed class TabViewPanel : TabPanel
{
    private List<UIElement> _entries = new();

    private TabView? Owner => ItemsControl.GetItemsOwner(this) as TabView;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        TabView? owner = Owner;
        if (owner == null || !owner.IsGroupingActive)
        {
            _entries.Clear();
            return base.MeasureOverride(availableSize);
        }

        // The generator still owns every child; this is only a visual-order projection.
        _entries = owner.GetGroupLayoutEntries(InternalChildren.Cast<UIElement>().ToList());
        double width = 0;
        double height = 0;
        foreach (UIElement entry in _entries)
        {
            entry.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += entry.DesiredSize.Width;
            height = Math.Max(height, entry.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Owner?.IsGroupingActive != true)
        {
            return base.ArrangeOverride(finalSize);
        }

        double x = 0;
        foreach (UIElement entry in _entries)
        {
            if (entry is System.Windows.Controls.Button header)
            {
                Canvas.SetLeft(header, x);
                Canvas.SetTop(header, 0);
                header.Arrange(new Rect(x, 0, entry.DesiredSize.Width, finalSize.Height));
            }
            else
            {
                entry.Arrange(new Rect(x, 0, entry.DesiredSize.Width, finalSize.Height));
            }

            x += entry.DesiredSize.Width;
        }

        return finalSize;
    }
}
