// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Wpf.Ui.Controls;

/// <summary>Group-aware drag and drop for document tabs.</summary>
public partial class TabView
{
    private Canvas? _dropOverlay;
    private Border? _dropIndicator;

    private bool RejectForeignGroupDrag(TabDragData data, DragEventArgs e)
    {
        if (!IsGroupingActive || data.Owner == this)
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
        DependencyObject? hit = e.OriginalSource as DependencyObject;
        for (
            DependencyObject? current = hit;
            current != null && current != this;
            current = GetDragParent(current)
        )
        {
            if (current is System.Windows.Controls.Button header)
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
        }

        TabItem? tab = GetDropTarget(e);
        int index = tab == null ? -1 : ItemContainerGenerator.IndexFromContainer(tab);
        Point tabPoint = tab == null ? new Point(-1, -1) : e.GetPosition(tab);
        if (
            index >= 0
            && tab!.Visibility == Visibility.Visible
            && tabPoint.X >= 0
            && tabPoint.X <= tab.ActualWidth
            && tabPoint.Y >= 0
            && tabPoint.Y <= tab.ActualHeight
        )
        {
            TryGetGroup(tab, out TabGroup? group);
            bool after = tabPoint.X >= tab.ActualWidth / 2;
            if (FlowDirection == FlowDirection.RightToLeft)
            {
                after = !after;
            }

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

    private static DependencyObject? GetDragParent(DependencyObject current) =>
        current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);

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
        _isUpdating = true;
        try
        {
            if (write != null && !write.Set(target!.GroupId))
            {
                write.Set(oldGroup);
                return false;
            }

            // A model setter may run arbitrary application code. Revalidate before touching the list.
            if (
                !ReferenceEquals(collection, GetMutableCollection())
                || !MatchesItems(collection, before)
                || (target!.GroupId != null && !_groupsById.ContainsKey(target.GroupId))
            )
            {
                write?.Set(oldGroup);
                return false;
            }

            if (oldIndex != newIndex)
            {
                TabViewCollection.Move(collection, oldIndex, newIndex);
            }

            RefreshCollection(collection);
            return true;
        }
        catch
        {
            // Notifications and custom setters are not transactional. Only undo our known move,
            // never overwrite unrelated changes made by application callbacks.
            try
            {
                if (oldIndex != newIndex && MatchesItems(collection, after))
                {
                    TabViewCollection.Move(collection, newIndex, oldIndex);
                }
            }
            finally
            {
                write?.Set(oldGroup);
                RefreshCollection(collection);
            }

            throw;
        }
        finally
        {
            try
            {
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

    // Conservative support for synchronous, plain CLR string properties. Complex/async conversions
    // and validation require application-owned transactions and are intentionally rejected.
    private sealed class GroupIdWrite(TabViewItem tab, BindingExpression? binding, PropertyInfo? property)
    {
        internal static GroupIdWrite? TryCreate(TabViewItem tab, bool ownContainer)
        {
            BindingExpression? expression = BindingOperations.GetBindingExpression(
                tab,
                TabViewItem.GroupIdProperty
            );
            if (!BindingOperations.IsDataBound(tab, TabViewItem.GroupIdProperty))
            {
                return
                    ownContainer
                    && !DependencyPropertyHelper.GetValueSource(tab, TabViewItem.GroupIdProperty).IsExpression
                    ? new(tab, null, null)
                    : null;
            }

            if (expression == null || expression.Status != BindingStatus.Active || expression.HasError)
            {
                return null;
            }

            Binding binding = expression.ParentBinding;
            object? source = expression.ResolvedSource;
            PropertyInfo? property = source
                ?.GetType()
                .GetProperty(expression.ResolvedSourcePropertyName ?? string.Empty);
            if (
                binding.Mode != BindingMode.TwoWay
                || binding.IsAsync
                || binding.Delay != 0
                || binding.Converter != null
                || binding.ValidationRules.Count != 0
                || (binding.ValidatesOnDataErrors && source is System.ComponentModel.IDataErrorInfo)
                || (
                    binding.ValidatesOnNotifyDataErrors
                    && source is System.ComponentModel.INotifyDataErrorInfo
                )
                || !string.IsNullOrEmpty(binding.BindingGroupName)
                || source == null
                || source.GetType().IsValueType
                || property?.PropertyType != typeof(string)
                || property.GetIndexParameters().Length != 0
                || property.GetMethod?.IsPublic != true
                || property.SetMethod?.IsPublic != true
                || property
                    .SetMethod.ReturnParameter.GetRequiredCustomModifiers()
                    .Any(type => type.FullName == "System.Runtime.CompilerServices.IsExternalInit")
                || !Equals(property.GetValue(source), tab.GroupId)
            )
            {
                return null;
            }

            return new(tab, expression, property);
        }

        internal bool Set(string? value)
        {
            if (
                binding != null
                && BindingOperations.GetBindingExpression(tab, TabViewItem.GroupIdProperty) != binding
            )
            {
                return false;
            }

            tab.SetCurrentValue(TabViewItem.GroupIdProperty, value);

            // PropertyChanged bindings may already have written synchronously. Explicit/LostFocus
            // bindings still need UpdateSource; do not call a model setter twice for the same drop.
            if (
                binding != null
                && (binding.HasError || !Equals(property!.GetValue(binding.ResolvedSource), value))
            )
            {
                binding.UpdateSource();
            }

            return tab.GroupId == value
                && (
                    binding == null
                    || (!binding.HasError && Equals(property!.GetValue(binding.ResolvedSource), value))
                );
        }
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
