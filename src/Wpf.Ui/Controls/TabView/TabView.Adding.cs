// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections;

namespace Wpf.Ui.Controls;

public partial class TabView
{
    private enum TabAddMode
    {
        Append,
        AfterSelection,
        GroupEnd,
    }

    private bool AddTargetIsValid(TabGroup? group, string? id) =>
        IsGroupingActive && (group == null ? id == null : OwnsGroup(group) && group.Id == id);

    private bool CanAddGroupTab(TabGroup group) =>
        CanAddTabs
        && !_isUpdating
        && AddTargetIsValid(group, group.Id)
        && GetMutableCollection() != null
        && (group.IsExpanded || PropertyWrite.TryCreate(group, TabGroup.IsExpandedProperty, true) != null);

    private void AddTabToGroup(TabGroup group)
    {
        if (CanAddGroupTab(group))
        {
            AddTabCore(null, null, TabAddMode.GroupEnd, group);
        }
    }

    private int ResolveAddIndex(TabAddMode mode, object[] items, object? selected, string? id)
    {
        int anchor =
            mode == TabAddMode.GroupEnd
                ? Array.FindLastIndex(
                    items,
                    item =>
                        ItemContainerGenerator.ContainerFromItem(item) is TabViewItem tab && tab.GroupId == id
                )
                : Array.FindIndex(items, item => IsSameItem(item, selected));
        return anchor < 0 ? items.Length : anchor + 1;
    }

    // Group projection preserves member order, so a data anchor plus the same membership
    // places the new tab directly after its selected predecessor in the projected strip.
    private void InsertRequestedTab(
        IList collection,
        object item,
        object? selected,
        TabGroup? group,
        object[] before,
        int index,
        string? targetGroupId
    )
    {
        if (before.Any(entry => IsSameItem(entry, item)))
        {
            return;
        }

        // Expansion is preflighted, but is applied only after insertion and membership succeed.
        PropertyWrite? expansion = group is { IsExpanded: false }
            ? PropertyWrite.TryCreate(group, TabGroup.IsExpandedProperty, true)
            : null;
        if (
            !AddTargetIsValid(group, targetGroupId)
            || (group != null && !group.IsExpanded && expansion == null)
        )
        {
            return;
        }

        List<object> after = before.ToList();
        after.Insert(index, item);
        GroupIdWrite? membership = null;
        string? previousGroup = null;
        bool expandedBefore = group?.IsExpanded ?? true;
        bool success = false;
        bool expansionAttempted = false;
        try
        {
            collection.Insert(index, item);
            RefreshCollection(collection);
            if (
                !ReferenceEquals(collection, GetMutableCollection())
                || !MatchesItems(collection, after)
                || !AddTargetIsValid(group, targetGroupId)
            )
            {
                return;
            }

            // Model membership is resolved through the actual container binding, never a
            // guessed CLR property name. Handlers may instead initialize the model themselves.
            UpdateLayout();
            TabViewItem? tab = ItemContainerGenerator.ContainerFromItem(item) as TabViewItem;
            string? desiredGroup = targetGroupId;
            if (tab == null)
            {
                return;
            }

            previousGroup = tab.GroupId;
            if (previousGroup != desiredGroup)
            {
                membership = GroupIdWrite.TryCreate(tab, IsSameItem(tab, item));
                if (membership == null || !membership.Set(desiredGroup))
                {
                    return;
                }
            }

            if (
                !ReferenceEquals(collection, GetMutableCollection())
                || !MatchesItems(collection, after)
                || !AddTargetIsValid(group, targetGroupId)
            )
            {
                return;
            }

            if (expansion != null)
            {
                expansionAttempted = true;
                if (!expansion.Set(true))
                {
                    return;
                }
            }

            if (
                !ReferenceEquals(collection, GetMutableCollection())
                || !MatchesItems(collection, after)
                || !AddTargetIsValid(group, targetGroupId)
            )
            {
                return;
            }

            SetCurrentValue(SelectedItemProperty, item);
            success =
                ReferenceEquals(collection, GetMutableCollection())
                && MatchesItems(collection, after)
                && AddTargetIsValid(group, targetGroupId)
                && IsSameItem(SelectedItem, item);
        }
        catch
        {
            // Application setters and collection observers may reject the operation.
        }
        finally
        {
            if (!success)
            {
                // Restore only our insertion while the list still has the expected sequence.
                // Arbitrary observer mutations and throwing observers cannot be made atomic.
                try
                {
                    membership?.Set(previousGroup);
                    if (expansionAttempted)
                    {
                        expansion?.Set(expandedBefore);
                    }
                    if (MatchesItems(collection, after))
                    {
                        collection.RemoveAt(index);
                        RefreshCollection(collection);
                    }
                    if (MatchesItems(collection, before) && GetMutableCollection() == collection)
                    {
                        SetCurrentValue(SelectedItemProperty, selected);
                    }
                }
                catch
                {
                    // Leave external mutations intact if observers also reject restoration.
                }
            }

            InvalidateGroupLayout();
        }
    }
}
