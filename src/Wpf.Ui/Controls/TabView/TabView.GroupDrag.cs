// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wpf.Ui.Controls;

/// <summary>Group-aware drag and drop for document tabs.</summary>
public partial class TabView
{
    private Canvas? _dropOverlay;
    private Border? _dropIndicator;

    private bool RejectForeignGroupDrag(TabDragData data, DragEventArgs e)
    {
        if ((!IsGroupingActive && data.Group == null) || data.Owner == this)
        {
            return false;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
        ShowDropIndicator(null);
        return true;
    }

    // Anchors are data items, never indices in the projected header strip.
    private sealed record GroupDropTarget(
        string? GroupId,
        object? Anchor,
        bool After,
        bool IsHeader,
        FrameworkElement Visual
    );

    private GroupDropTarget? GetGroupDropTarget(DragEventArgs e, TabDragData data)
    {
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

        if (_groupPanel != null)
        {
            Point point = e.GetPosition(_groupPanel);
            var blocks = _groupPanel.GetDropBlocks();
            // Reserve the strip's leading edge for inserting outside the first group.
            // Local panel coordinates already follow data order in both LTR and RTL.
            if (
                blocks.Count > 0
                && blocks[0].Group is TabGroup firstGroup
                && point.X >= blocks[0].Bounds.Left
                && point.X < (blocks[0].Bounds.Left + 6)
                && point.Y >= blocks[0].Bounds.Top
                && point.Y < blocks[0].Bounds.Bottom
            )
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    if (
                        ItemContainerGenerator.ContainerFromIndex(i) is TabViewItem member
                        && member.GroupId == firstGroup.Id
                    )
                    {
                        return new(null, Items[i], false, false, _groupPanel);
                    }
                }
            }
        }

        FrameworkElement? hit = _groupPanel?.GetDropElement(e.GetPosition(_groupPanel));
        if (hit is System.Windows.Controls.Button header)
        {
            foreach (KeyValuePair<TabGroup, Button> pair in _groupHeaders)
            {
                if (pair.Value == header && header.Visibility == Visibility.Visible)
                {
                    object? last = null;
                    for (int i = 0; i < Items.Count; i++)
                    {
                        if (
                            !IsSameItem(Items[i], data.Item)
                            && ItemContainerGenerator.ContainerFromIndex(i) is TabViewItem member
                            && member.GroupId == pair.Key.Id
                        )
                        {
                            last = Items[i];
                        }
                    }

                    // A header appends to its group, including when its members are collapsed.
                    return new(pair.Key.Id, last, true, true, header);
                }
            }
        }

        TabItem? tab = hit as TabItem;
        int index = tab == null ? -1 : ItemContainerGenerator.IndexFromContainer(tab);
        Point tabPoint = tab == null ? new Point(-1, -1) : e.GetPosition(tab);
        if (index >= 0 && tab!.Visibility == Visibility.Visible)
        {
            TryGetGroup(tab, out TabGroup? group);
            bool after = tabPoint.X >= tab.ActualWidth / 2;
            // WPF has already mirrored these local coordinates in RTL layouts.

            return new(group?.Id, Items[index], after, false, tab);
        }

        // Empty space in the strip is an explicit ungrouped append target; content/add are excluded.
        if (_groupPanel != null)
        {
            Point point = e.GetPosition(_groupPanel);
            if (
                point.X >= 0
                && point.X <= _groupPanel.ActualWidth
                && point.Y >= 0
                && point.Y < _groupPanel.ActualHeight
            )
            {
                return new(null, null, true, false, _groupPanel);
            }
        }

        return null;
    }

    private void UpdateGroupDragFeedback(TabDragData data, DragEventArgs e)
    {
        if (data.Group != null)
        {
            UpdateWholeGroupDragFeedback(data, e);
            return;
        }

        GroupDropTarget? destination = GetGroupDropTarget(e, data);
        bool allowed = _isDragging && CanApplyGroupDrop(data, destination);
        e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        ShowDropIndicator(allowed ? destination : null);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (
            e.Data.GetData(typeof(TabDragData)) is TabDragData data
            && !RejectForeignGroupDrag(data, e)
            && data.Owner == this
            && IsGroupingActive
        )
        {
            UpdateGroupDragFeedback(data, e);
        }
    }

    private bool CanApplyGroupDrop(TabDragData data, GroupDropTarget? target) =>
        TryPrepareGroupDrop(data, target, out _, out _, out _, out _, out _);

    private bool TryPrepareGroupDrop(
        TabDragData data,
        GroupDropTarget? target,
        out IList? collection,
        out TabViewItem? tab,
        out GroupIdWrite? write,
        out int oldIndex,
        out int newIndex
    )
    {
        collection = GetMutableCollection();
        oldIndex = GetItemIndex(data.Item);
        newIndex = oldIndex;
        tab = oldIndex < 0 ? null : ItemContainerGenerator.ContainerFromIndex(oldIndex) as TabViewItem;
        write = null;
        if (
            _isUpdating
            || !IsGroupingActive
            || !CanReorderTabs
            || target == null
            || collection == null
            || tab == null
            || tab != _draggedTab
            || tab.GroupId != data.GroupId
            || Items.Cast<object>().Count(item => IsSameItem(item, data.Item)) != 1
            || (target.GroupId != null && !_groupsById.ContainsKey(target.GroupId))
        )
        {
            return false;
        }

        // The projection treats empty/unknown IDs as ungrouped. Preserve those raw model values
        // when only reordering ungrouped tabs, including with OneWay membership bindings.
        TryGetGroup(tab, out TabGroup? sourceGroup);
        if (sourceGroup?.Id != target.GroupId)
        {
            write = GroupIdWrite.TryCreate(tab, IsSameItem(data.Item, tab));
            if (write == null)
            {
                return false;
            }
        }

        if (target.Anchor != null)
        {
            int anchor = GetItemIndex(target.Anchor);
            if (anchor < 0 || Items.Cast<object>().Count(item => IsSameItem(item, target.Anchor)) != 1)
            {
                return false;
            }

            if (anchor != oldIndex)
            {
                // Resolve the anchor in the source after conceptually removing the dragged item.
                newIndex = anchor - (oldIndex < anchor ? 1 : 0) + (target.After ? 1 : 0);
            }
        }
        else if (target.IsHeader && target.GroupId == data.GroupId)
        {
            // The source is the group's sole member. Keep its first-occurrence position.
            newIndex = oldIndex;
        }
        else
        {
            newIndex = collection.Count - 1;
        }

        return true;
    }

    private bool ApplyGroupDrop(TabDragData data, GroupDropTarget? target)
    {
        if (
            !TryPrepareGroupDrop(
                data,
                target,
                out IList? mutable,
                out TabViewItem? tab,
                out GroupIdWrite? write,
                out int oldIndex,
                out int newIndex
            )
        )
        {
            return false;
        }

        IList collection = mutable!;
        object? selected = SelectedItem;
        object?[] before = collection.Cast<object?>().ToArray();
        List<object?> after = before.ToList();
        after.RemoveAt(oldIndex);
        after.Insert(newIndex, data.Item);
        string? oldGroup = tab!.GroupId;
        TryGetGroup(tab, out TabGroup? sourceGroup);
        TabGroup? targetGroup = target!.GroupId == null ? null : _groupsById[target.GroupId];
        string? anchorGroupId = (ItemContainerGenerator.ContainerFromItem(target.Anchor) as TabViewItem)?.GroupId;
        string? expectedGroup = write == null ? oldGroup : target.GroupId;
        bool success = false;
        bool membershipAttempted = false;
        bool moveAttempted = false;

        bool IsValid(IReadOnlyList<object?> order) =>
            ReferenceEquals(collection, GetMutableCollection())
            && MatchesItems(collection, order)
            && Items.Cast<object>().Count(item => IsSameItem(item, data.Item)) == 1
            && ItemContainerGenerator.ContainerFromItem(data.Item) is TabViewItem current
            && current.GroupId == expectedGroup
            && (target.Anchor == null
                || (ItemContainerGenerator.ContainerFromItem(target.Anchor) is TabItem anchor
                    && (anchor as TabViewItem)?.GroupId == anchorGroupId))
            && (sourceGroup == null || (OwnsGroup(sourceGroup) && sourceGroup.Id == oldGroup))
            && (targetGroup == null || (OwnsGroup(targetGroup) && targetGroup.Id == target.GroupId));

        _isUpdating = true;
        try
        {
            if (write != null)
            {
                membershipAttempted = true;
                if (!write.Set(target.GroupId))
                {
                    return false;
                }
            }

            // A model setter may run arbitrary application code. Revalidate before touching the list.
            if (!IsValid(before))
            {
                return false;
            }

            if (oldIndex != newIndex)
            {
                moveAttempted = true;
                TabViewCollection.Move(collection, oldIndex, newIndex);
            }

            RefreshCollection(collection);
            // Plain IList refreshes regenerate containers; validate their resolved bindings too.
            UpdateLayout();
            if (!IsValid(after))
            {
                return false;
            }

            if (selected == null || GetItemIndex(selected) >= 0)
            {
                SetCurrentValue(SelectedItemProperty, selected);
            }
            success = IsValid(after);
            return success;
        }
        finally
        {
            try
            {
                if (!success)
                {
                    // Restore only our known sequence, never overwrite observer list mutations.
                    try
                    {
                        if (moveAttempted && ReferenceEquals(collection, GetMutableCollection()) && MatchesItems(collection, after))
                        {
                            TabViewCollection.Move(collection, newIndex, oldIndex);
                            RefreshCollection(collection);
                            UpdateLayout();
                        }
                    }
                    catch
                    {
                        // Observers may also reject rollback; still try to restore membership.
                    }

                    try
                    {
                        if (membershipAttempted && ReferenceEquals(collection, GetMutableCollection())
                            && ItemContainerGenerator.ContainerFromItem(data.Item) is TabViewItem current
                            && current.GroupId == expectedGroup)
                        {
                            GroupIdWrite.TryCreate(current, IsSameItem(data.Item, current))?.Set(oldGroup);
                        }
                    }
                    catch
                    {
                        // Keep external membership changes and rejected setters intact.
                    }
                }
                InvalidateGroupLayout();
                if (selected == null || GetItemIndex(selected) >= 0)
                {
                    SetCurrentValue(SelectedItemProperty, selected);
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }
    }

    private static bool MatchesItems(IList collection, IReadOnlyList<object?> expected)
    {
        if (collection.Count != expected.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (!IsSameItem(collection[i], expected[i]))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class GroupIdWrite(PropertyWrite write)
    {
        internal static GroupIdWrite? TryCreate(TabViewItem tab, bool ownContainer)
        {
            PropertyWrite? write = PropertyWrite.TryCreate(tab, TabViewItem.GroupIdProperty, ownContainer);
            return write == null ? null : new(write);
        }

        internal bool Set(string? value) => write.Set(value);
    }

    private void ShowDropIndicator(GroupDropTarget? target)
    {
        if (_dropOverlay == null)
        {
            return;
        }

        if (target == null)
        {
            if (_dropIndicator != null)
            {
                _dropIndicator.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
            }

            return;
        }

        if (_dropIndicator == null)
        {
            _dropIndicator = new Border
            {
                IsHitTestVisible = false,
                BorderBrush = SystemColors.HighlightBrush,
            };
            _dropOverlay.Children.Add(_dropIndicator);
        }

        Point point = target.Visual.TranslatePoint(new Point(0, 0), _dropOverlay);
        bool outline = target.IsHeader;
        double x = point.X + (!outline && target.After ? target.Visual.ActualWidth : 0);
        if (target.Visual == _groupPanel && target.Anchor == null)
        {
            x = _groupPanel!.TranslatePoint(new Point(GetHeaderAppendBounds().Right, 0), _dropOverlay).X;
        }
        _dropIndicator.SetCurrentValue(WidthProperty, outline ? target.Visual.ActualWidth : 2);
        _dropIndicator.SetCurrentValue(HeightProperty, target.Visual.ActualHeight);
        _dropIndicator.SetCurrentValue(Border.BorderThicknessProperty, new Thickness(outline ? 2 : 1));
        Canvas.SetLeft(_dropIndicator, Math.Max(0, x - (outline ? 0 : 1)));
        Canvas.SetTop(_dropIndicator, point.Y);
        _dropIndicator.SetCurrentValue(VisibilityProperty, Visibility.Visible);
    }

    /// <inheritdoc />
    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        ShowDropIndicator(null);
    }

    /// <inheritdoc />
    protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
    {
        base.OnQueryContinueDrag(e);
        if (e.EscapePressed || e.Action == DragAction.Cancel)
        {
            e.Action = DragAction.Cancel;
            EndDrag();
        }
    }
}
