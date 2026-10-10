// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Wpf.Ui.Controls;

/// <summary>Template panel that arranges group headers in a separate Canvas, never in its item children.</summary>
public sealed class TabViewPanel : TabPanel
{
    private List<UIElement> _entries = new();

    internal List<(TabGroup? Group, FrameworkElement Visual, Rect Bounds)> GetDropBlocks()
    {
        List<(TabGroup? Group, FrameworkElement Visual, Rect Bounds)> blocks = new();
        foreach (FrameworkElement entry in _entries.OfType<FrameworkElement>())
        {
            if (entry.Visibility != Visibility.Visible)
            {
                continue;
            }

            TabGroup? group = Owner?.GetLayoutGroup(entry);
            Rect slot = LayoutInformation.GetLayoutSlot(entry);
            if (group != null && blocks.Count > 0 && blocks[blocks.Count - 1].Group == group)
            {
                (TabGroup? Group, FrameworkElement Visual, Rect Bounds) previous = blocks[blocks.Count - 1];
                slot.Union(previous.Bounds);
                blocks[blocks.Count - 1] = (group, previous.Visual, slot);
            }
            else
            {
                blocks.Add((group, entry, slot));
            }
        }

        return blocks;
    }

    private TabView? Owner => ItemsControl.GetItemsOwner(this) as TabView ?? TemplatedParent as TabView;

    // Use allocated slots, including margins and rounded corners, rather than the routed hit source.
    internal FrameworkElement? GetDropElement(Point point)
    {
        foreach (FrameworkElement entry in _entries.OfType<FrameworkElement>())
        {
            Rect slot = LayoutInformation.GetLayoutSlot(entry);
            if (
                entry.Visibility == Visibility.Visible
                && point.X >= slot.Left
                && point.X < slot.Right
                && point.Y >= slot.Top
                && point.Y < slot.Bottom
            )
            {
                return entry;
            }
        }
        return null;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Initialize the item generator before resolving its owner, including replacement templates.
        UIElementCollection children = InternalChildren;
        TabView? owner = Owner;
        if (owner == null || !owner.IsGroupingActive)
        {
            _entries.Clear();
            return base.MeasureOverride(availableSize);
        }

        // The generator still owns every child; this is only a visual-order projection.
        _entries = owner.GetGroupLayoutEntries(children.Cast<UIElement>().ToList());
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
        TabView? owner = Owner;
        if (owner?.IsGroupingActive != true)
        {
            return base.ArrangeOverride(finalSize);
        }

        double x = 0;
        TabGroup? currentGroup = null;
        double groupStart = 0;
        List<(TabGroup Group, Rect Bounds)> backgrounds = new();
        foreach (UIElement entry in _entries)
        {
            TabGroup? group = owner.GetLayoutGroup(entry);
            if (group != currentGroup)
            {
                if (currentGroup != null)
                {
                    backgrounds.Add(
                        (currentGroup, new Rect(groupStart, 0, x - groupStart, finalSize.Height))
                    );
                }

                currentGroup = group;
                groupStart = x;
            }

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

        if (currentGroup != null)
        {
            backgrounds.Add((currentGroup, new Rect(groupStart, 0, x - groupStart, finalSize.Height)));
        }

        owner.ArrangeGroupBackgrounds(backgrounds);

        return finalSize;
    }
}
