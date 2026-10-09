// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections;

namespace Wpf.Ui.Controls;

public partial class TabView
{
    private bool OwnsGroup(TabGroup group) =>
        _groupsById.TryGetValue(group.Id, out TabGroup? current) && ReferenceEquals(current, group);

    private IList? MutableGroupDefinitions() =>
        TabGroups is IList { IsReadOnly: false, IsFixedSize: false } groups ? groups : null;

    private GroupIdWrite? MembershipWrite(TabViewItem tab)
    {
        int index = ItemContainerGenerator.IndexFromContainer(tab);
        if (
            !IsGroupingActive
            || _isUpdating
            || index < 0
            || GetMutableCollection() == null
            || Items.Cast<object>().Count(item => IsSameItem(item, Items[index])) != 1
        )
        {
            return null;
        }

        return GroupIdWrite.TryCreate(tab, IsSameItem(Items[index], tab));
    }

    private bool CanChangeMembership(TabViewItem tab, string? id) =>
        (id == null || _groupsById.ContainsKey(id)) && MembershipWrite(tab) != null;

    private bool ChangeMembership(TabViewItem tab, string? id) =>
        ChangeMemberships(new[] { tab }, id, null, null);

    private TabGroup? CreateGroupForTab(TabViewItem tab)
    {
        List<TabGroup> definitions = TabGroups?.Where(group => group != null).ToList() ?? new();
        string name = "New group";
        for (int number = 2; definitions.Any(group => group.Header == name); number++)
        {
            name = $"New group {number}";
        }

        Color color = GroupColors
            .Select(preset => preset.Color)
            .OrderBy(candidate => definitions.Count(group => group.Color == candidate))
            .First();
        TabGroup group = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Header = name,
            Color = color,
        };
        return ChangeMemberships(new[] { tab }, group.Id, group, null) ? group : null;
    }

    private List<TabViewItem> GroupMembers(TabGroup group) =>
        Items
            .Cast<object>()
            .Select(item => ItemContainerGenerator.ContainerFromItem(item))
            .OfType<TabViewItem>()
            .Where(tab => tab.GroupId == group.Id)
            .ToList();

    private bool CanUngroup(TabGroup group) =>
        !group.IsFrozen
        && OwnsGroup(group)
        && MutableGroupDefinitions() != null
        && GetMutableCollection() != null
        && Enumerable
            .Range(0, Items.Count)
            .All(index => ItemContainerGenerator.ContainerFromIndex(index) != null)
        && GroupMembers(group).All(tab => MembershipWrite(tab) != null);

    private bool Ungroup(TabGroup group) =>
        CanUngroup(group) && ChangeMemberships(GroupMembers(group), null, null, group);

    // Menus never reorder Items. Preflight every write before changing any membership or definition.
    private bool ChangeMemberships(
        IReadOnlyList<TabViewItem> tabs,
        string? id,
        TabGroup? added,
        TabGroup? removed
    )
    {
        if (
            _isUpdating
            || !IsGroupingActive
            || GetMutableCollection() is not IList collection
            || (added == null && id != null && !_groupsById.ContainsKey(id))
        )
        {
            return false;
        }

        IList? definitions = MutableGroupDefinitions();
        if (
            (added != null || removed != null)
            && (
                definitions == null
                || (removed != null && (!OwnsGroup(removed) || removed.IsFrozen))
                || (added != null && TabGroups!.Any(group => group != null && group.Id == added.Id))
            )
        )
        {
            return false;
        }

        List<(TabViewItem Tab, GroupIdWrite Write, string? Before)> writes = new();
        foreach (TabViewItem tab in tabs)
        {
            GroupIdWrite? write = MembershipWrite(tab);
            if (write == null)
            {
                return false;
            }

            writes.Add((tab, write, tab.GroupId));
        }

        object?[] beforeItems = collection.Cast<object?>().ToArray();
        object?[]? beforeGroups = definitions?.Cast<object?>().ToArray();
        List<object?>? expectedGroups = beforeGroups?.ToList();
        object? selected = SelectedItem;
        int removedIndex = removed == null ? -1 : definitions!.IndexOf(removed);
        bool success = false;
        EndDrag();
        _isUpdating = true;
        try
        {
            if (added != null)
            {
                expectedGroups!.Add(added);
                definitions!.Add(added);
            }

            foreach ((TabViewItem Tab, GroupIdWrite Write, string? Before) entry in writes)
            {
                if (
                    !entry.Write.Set(id)
                    || !ReferenceEquals(collection, GetMutableCollection())
                    || !MatchesItems(collection, beforeItems)
                    || (
                        definitions != null
                        && (
                            !ReferenceEquals(TabGroups, definitions)
                            || !MatchesItems(definitions, expectedGroups!)
                        )
                    )
                    || (id != null && !_groupsById.ContainsKey(id))
                )
                {
                    return false;
                }
            }

            if (removed != null)
            {
                expectedGroups!.RemoveAt(removedIndex);
                definitions!.RemoveAt(removedIndex);
            }

            success =
                ReferenceEquals(collection, GetMutableCollection())
                && MatchesItems(collection, beforeItems)
                && (
                    definitions == null
                    || (ReferenceEquals(TabGroups, definitions) && MatchesItems(definitions, expectedGroups!))
                );
            return success;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (!success)
            {
                // Only undo our known definition change; never replace application callback changes.
                if (
                    definitions != null
                    && ReferenceEquals(TabGroups, definitions)
                    && MatchesItems(definitions, expectedGroups!)
                )
                {
                    try
                    {
                        if (added != null && definitions.Contains(added))
                        {
                            definitions.Remove(added);
                        }

                        if (removed != null && !definitions.Contains(removed))
                        {
                            definitions.Insert(removedIndex, removed);
                        }
                    }
                    catch
                    { /* Custom collection notifications are not transactional. */
                    }
                }

                foreach (
                    (TabViewItem Tab, GroupIdWrite Write, string? Before) entry in writes
                        .AsEnumerable()
                        .Reverse()
                )
                {
                    try
                    {
                        if (entry.Tab.GroupId != entry.Before)
                        {
                            entry.Write.Set(entry.Before);
                        }
                    }
                    catch
                    { /* Restore the other members even if an application setter rejects rollback. */
                    }
                }
            }

            _isUpdating = false;
            InvalidateGroupLayout();
            if (selected == null || GetItemIndex(selected) >= 0)
            {
                SetCurrentValue(SelectedItemProperty, selected);
            }
        }
    }

    private bool EditGroup(TabGroup group, string header, Color color)
    {
        if (_isUpdating || !OwnsGroup(group) || group.IsFrozen)
        {
            return false;
        }

        List<(PropertyWrite Write, DependencyProperty Property, object Before, object After)> writes = new();
        foreach (
            (DependencyProperty Property, object After) change in new[]
            {
                (TabGroup.HeaderProperty, (object)header),
                (TabGroup.ColorProperty, (object)color),
            }
        )
        {
            object before = group.GetValue(change.Property);
            if (Equals(before, change.After))
            {
                continue;
            }

            PropertyWrite? write = PropertyWrite.TryCreate(group, change.Property, true);
            if (write == null)
            {
                return false;
            }

            writes.Add((write, change.Property, before, change.After));
        }

        string id = group.Id;
        bool success = false;
        EndDrag();
        _isUpdating = true;
        try
        {
            foreach (
                (
                    PropertyWrite Write,
                    DependencyProperty Property,
                    object Before,
                    object After
                ) change in writes
            )
            {
                if (!change.Write.Set(change.After) || !OwnsGroup(group) || group.Id != id)
                {
                    return false;
                }
            }

            success = true;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (!success)
            {
                foreach (
                    (
                        PropertyWrite Write,
                        DependencyProperty Property,
                        object Before,
                        object After
                    ) change in writes.AsEnumerable().Reverse()
                )
                {
                    try
                    {
                        if (!Equals(group.GetValue(change.Property), change.Before))
                        {
                            change.Write.Set(change.Before);
                        }
                    }
                    catch
                    { /* Keep restoring the remaining properties. */
                    }
                }
            }

            _isUpdating = false;
            InvalidateGroupLayout();
        }
    }
}
