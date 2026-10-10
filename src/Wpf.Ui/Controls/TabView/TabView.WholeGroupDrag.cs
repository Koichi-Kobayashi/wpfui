// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wpf.Ui.Controls;

/// <summary>Moves whole groups through the same drag lifecycle as individual tabs.</summary>
public partial class TabView
{
    private TabGroup? _draggedGroup;
    private System.Windows.Controls.Button? _draggedGroupHeader;
    private bool _deferGroupLayout;

    private sealed record WholeGroupDropTarget(TabGroup? Group, object? Anchor, bool After, Rect Bounds);

    private bool TryBeginWholeGroupDrag(DependencyObject source, Point start)
    {
        if (!IsGroupingActive)
        {
            return false;
        }

        for (DependencyObject? current = source; current != null && current != this; )
        {
            foreach (KeyValuePair<TabGroup, Button> pair in _groupHeaders)
            {
                if (current == pair.Value && pair.Value.Visibility == Visibility.Visible)
                {
                    CloseGroupManagementUI();
                    _draggedGroup = pair.Key;
                    _draggedGroupHeader = pair.Value;
                    _dragStartPoint = start;
                    return true;
                }
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private TabDragData? CreateWholeGroupDragData(TabGroup group)
    {
        if (!OwnsGroup(group) || GetMutableCollection() == null)
        {
            return null;
        }

        object[] items = Items.Cast<object>().ToArray();
        string?[] ids = items
            .Select(item => (ItemContainerGenerator.ContainerFromItem(item) as TabViewItem)?.GroupId)
            .ToArray();
        int first = Array.FindIndex(ids, id => id == group.Id);
        return first < 0
            ? null
            : new TabDragData(this, items[first])
            {
                Group = group,
                Snapshot = items,
                Memberships = ids,
                Definitions = TabGroups!.ToArray(),
            };
    }

    private WholeGroupDropTarget? GetWholeGroupDropTarget(DragEventArgs e)
    {
        if (_groupPanel == null)
        {
            return null;
        }

        if (_headerScroller != null)
        {
            Point viewport = e.GetPosition(_headerScroller);
            if (
                viewport.X < 0
                || viewport.X >= _headerScroller.ActualWidth
                || viewport.Y < 0
                || viewport.Y >= _headerScroller.ActualHeight
            )
            {
                return null;
            }
        }

        Point point = e.GetPosition(_groupPanel);
        if (
            point.Y < 0
            || point.Y >= _groupPanel.ActualHeight
            || point.X < 0
            || point.X > _groupPanel.ActualWidth
        )
        {
            return null;
        }

        List<(TabGroup? Group, FrameworkElement Visual, Rect Bounds)> blocks = _groupPanel.GetDropBlocks();
        foreach ((TabGroup? Group, FrameworkElement Visual, Rect Bounds) block in blocks)
        {
            if (point.X >= block.Bounds.Left && point.X < block.Bounds.Right)
            {
                bool after = point.X >= block.Bounds.Left + block.Bounds.Width / 2;
                // Block coordinates follow data order, including WPF's RTL mirroring.

                int index = ItemContainerGenerator.IndexFromContainer(block.Visual);
                return new(block.Group, index < 0 ? null : Items[index], after, block.Bounds);
            }
        }

        // Empty strip space is an explicit append position; the add button and content are excluded.
        return new(null, null, true, GetHeaderAppendBounds());
    }

    private Rect GetHeaderAppendBounds()
    {
        var blocks = _groupPanel?.GetDropBlocks();
        double end = blocks != null && blocks.Count > 0 ? blocks[blocks.Count - 1].Bounds.Right : 0;
        return new Rect(0, 0, end, _groupPanel?.ActualHeight ?? 0);
    }

    private bool WholeGroupDragIsValid(TabDragData data, IList collection)
    {
        if (
            !IsGroupingActive
            || !CanReorderTabs
            || !_isDragging
            || data.Owner != this
            || data.Group == null
            || data.Group != _draggedGroup
            || !OwnsGroup(data.Group)
            || data.Group.Id != data.GroupId
            || data.Snapshot == null
            || data.Memberships == null
            || data.Definitions == null
            || !ReferenceEquals(collection, GetMutableCollection())
            || !TabGroups!.SequenceEqual(data.Definitions)
        )
        {
            return false;
        }

        foreach (object item in data.Snapshot)
        {
            int index = GetItemIndex(item);
            int original = Array.FindIndex(data.Snapshot, candidate => IsSameItem(candidate, item));
            if (
                index < 0
                || Items.Cast<object>().Count(candidate => IsSameItem(candidate, item)) != 1
                || ItemContainerGenerator.ContainerFromIndex(index) is not TabItem tab
                || (tab as TabViewItem)?.GroupId != data.Memberships[original]
            )
            {
                return false;
            }
        }

        return collection.Count == data.Snapshot.Length;
    }

    // Project data identities to group blocks, without generating or modifying containers.
    private List<object> ProjectWholeGroupOrder(IEnumerable<object> items, TabDragData data)
    {
        List<object> blocks = new();
        HashSet<TabGroup> emitted = new();
        foreach (object item in items)
        {
            int index = Array.FindIndex(data.Snapshot!, candidate => IsSameItem(candidate, item));
            string? id = data.Memberships![index];
            if (id != null && _groupsById.TryGetValue(id, out TabGroup? group))
            {
                if (emitted.Add(group))
                {
                    blocks.Add(group);
                }
            }
            else
            {
                blocks.Add(item);
            }
        }

        return blocks;
    }

    private object[]? PlanWholeGroupDrop(TabDragData data, WholeGroupDropTarget? target)
    {
        IList? collection = GetMutableCollection();
        if (
            _isUpdating
            || target == null
            || collection == null
            || !WholeGroupDragIsValid(data, collection)
            || !MatchesItems(collection, data.Snapshot!)
        )
        {
            return null;
        }

        object[] before = data.Snapshot!;
        if (target.Group == data.Group)
        {
            return before;
        }

        List<object> members = before.Where((_, index) => data.Memberships![index] == data.GroupId).ToList();
        List<object> remaining = before
            .Where(item => !members.Any(member => IsSameItem(member, item)))
            .ToList();
        object? anchor = target.Anchor;
        if (target.Group != null)
        {
            if (!OwnsGroup(target.Group))
            {
                return null;
            }

            anchor = remaining.FirstOrDefault(item =>
            {
                int index = Array.FindIndex(before, candidate => IsSameItem(candidate, item));
                return data.Memberships![index] == target.Group.Id;
            });
            if (anchor == null)
            {
                return null;
            }
        }

        int insertion =
            anchor == null ? remaining.Count : remaining.FindIndex(item => IsSameItem(item, anchor));
        if (insertion < 0)
        {
            return null;
        }

        if (anchor != null && target.After)
        {
            insertion++;
        }

        remaining.InsertRange(insertion, members);
        // Do not normalize scattered members when the requested visual position is unchanged.
        List<object> oldBlocks = ProjectWholeGroupOrder(before, data);
        List<object> newBlocks = ProjectWholeGroupOrder(remaining, data);
        return oldBlocks.Count == newBlocks.Count && oldBlocks.Zip(newBlocks, IsSameItem).All(same => same)
            ? before
            : remaining.ToArray();
    }

    private void UpdateWholeGroupDragFeedback(TabDragData data, DragEventArgs e)
    {
        WholeGroupDropTarget? target = GetWholeGroupDropTarget(e);
        bool allowed = PlanWholeGroupDrop(data, target) != null;
        e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        ShowWholeGroupDropIndicator(allowed ? target : null);
        e.Handled = true;
    }

    private void ShowWholeGroupDropIndicator(WholeGroupDropTarget? target)
    {
        if (target == null || _groupPanel == null || _dropOverlay == null)
        {
            ShowDropIndicator(null);
            return;
        }

        ShowDropIndicator(new(null, null, false, false, _groupPanel));
        Point point = _groupPanel.TranslatePoint(
            new Point(target.After ? target.Bounds.Right : target.Bounds.Left, target.Bounds.Top),
            _dropOverlay
        );
        Canvas.SetLeft(_dropIndicator!, Math.Max(0, point.X - 1));
        Canvas.SetTop(_dropIndicator!, point.Y);
        _dropIndicator!.SetCurrentValue(HeightProperty, target.Bounds.Height);
    }

    private bool ApplyWholeGroupDrop(TabDragData data, WholeGroupDropTarget? target)
    {
        object[]? after = PlanWholeGroupDrop(data, target);
        IList? collection = GetMutableCollection();
        if (after == null || collection == null)
        {
            return false;
        }

        if (MatchesItems(collection, after))
        {
            return true;
        }

        object? selected = SelectedItem;
        List<(int Old, int New, object[] Before, object[] After)> journal = new();
        object[] expected = data.Snapshot!;
        bool success = false;
        _isUpdating = true;
        _deferGroupLayout = true;
        try
        {
            object[] members = data.Snapshot!.Where((_, index) => data.Memberships![index] == data.GroupId)
                .ToArray();
            int last = Array.FindIndex(after, item => IsSameItem(item, members[members.Length - 1]));
            object? next = last + 1 < after.Length ? after[last + 1] : null;
            foreach (object member in members.AsEnumerable().Reverse())
            {
                if (!WholeGroupDragIsValid(data, collection) || !MatchesItems(collection, expected))
                {
                    return false;
                }

                int oldIndex = Array.FindIndex(expected, item => IsSameItem(item, member));
                int anchorIndex =
                    next == null
                        ? expected.Length
                        : Array.FindIndex(expected, item => IsSameItem(item, next));
                int newIndex = anchorIndex - (oldIndex < anchorIndex ? 1 : 0);
                if (oldIndex != newIndex)
                {
                    List<object> step = expected.ToList();
                    step.RemoveAt(oldIndex);
                    step.Insert(newIndex, member);
                    object[] stepAfter = step.ToArray();
                    // Record before calling Move: notifications may throw after the mutation.
                    journal.Add((oldIndex, newIndex, expected, stepAfter));
                    try
                    {
                        TabViewCollection.Move(collection, oldIndex, newIndex);
                    }
                    finally
                    {
                        RefreshCollection(collection);
                    }
                    expected = stepAfter;
                    if (!WholeGroupDragIsValid(data, collection) || !MatchesItems(collection, expected))
                    {
                        return false;
                    }
                }

                next = member;
            }

            success = MatchesItems(collection, after);
            return success;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (!success)
                {
                    foreach (
                        (int Old, int New, object[] Before, object[] After) step in journal
                            .AsEnumerable()
                            .Reverse()
                    )
                    {
                        if (!ReferenceEquals(collection, GetMutableCollection()))
                        {
                            break;
                        }

                        if (MatchesItems(collection, step.Before))
                        {
                            continue;
                        }

                        if (!MatchesItems(collection, step.After))
                        {
                            break;
                        }

                        try
                        {
                            TabViewCollection.Move(collection, step.New, step.Old);
                        }
                        catch
                        {
                            if (!MatchesItems(collection, step.Before))
                            {
                                break;
                            }
                        }
                        finally
                        {
                            RefreshCollection(collection);
                        }
                    }
                }
            }
            catch
            {
                // Application collections may reject restoration too. Always release the update scope.
            }

            try
            {
                RefreshCollection(collection);
                if (selected == null || GetItemIndex(selected) >= 0)
                {
                    SetCurrentValue(SelectedItemProperty, selected);
                }
            }
            finally
            {
                _deferGroupLayout = false;
                _isUpdating = false;
                InvalidateGroupLayout();
            }
        }
    }
}
