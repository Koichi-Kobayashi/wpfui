// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

namespace Wpf.Ui.Controls;

public partial class TabView
{
    /// <summary>Checks whether an existing tab can be moved to the specified group and position.</summary>
    /// <param name="item">An item in this view with a generated container.</param>
    /// <param name="groupId">An existing group identifier, or null for an ungrouped destination.</param>
    /// <param name="anchor">An item before or after which to insert, or null for the destination end.</param>
    /// <param name="after">Whether to insert after the anchor. Group-end insertion uses true.</param>
    /// <returns>Whether the current destination and membership write are supported.</returns>
    /// <remarks>
    /// Does not mutate state. Requires an applied group-aware template and a mutable source.
    /// The result is a preflight check; application callbacks can still reject execution.
    /// CanReorderTabs controls built-in mouse gestures and does not disable this programmatic operation.
    /// </remarks>
    public bool CanMoveTabToGroup(object item, string? groupId, object? anchor = null, bool after = true) =>
        item != null && GetItemIndex(item) >= 0 && TryPrepareGroupDrop(
            new TabDragData(this, item),
            ExternalTabTarget(item, groupId, anchor, after),
            out _, out _, out _, out _, out _, true);

    /// <summary>Tries to move a tab and change its membership, preserving supported bindings and selection.</summary>
    /// <param name="item">An item in this view with a generated container.</param>
    /// <param name="groupId">An existing group identifier, or null for an ungrouped destination.</param>
    /// <param name="anchor">An item before or after which to insert, or null for the destination end.</param>
    /// <param name="after">Whether to insert after the anchor. Group-end insertion uses true.</param>
    /// <returns>Whether the move and membership change succeeded.</returns>
    /// <remarks>
    /// Uses the built-in drop validation and restoration. Known changes are restored on failure;
    /// arbitrary application callback changes are not overwritten. No mouse drag is required.
    /// InvalidOperationException from a rejecting collection is returned as false after restoration.
    /// </remarks>
    public bool TryMoveTabToGroup(object item, string? groupId, object? anchor = null, bool after = true)
    {
        if (item == null || GetItemIndex(item) < 0)
        {
            return false;
        }

        try
        {
            return ApplyGroupDrop(new TabDragData(this, item), ExternalTabTarget(item, groupId, anchor, after), true);
        }
        catch (InvalidOperationException)
        {
            // The existing drop operation restores known changes before propagating collection rejection.
            return false;
        }
    }

    /// <summary>Checks whether all members of an owned group can be moved together.</summary>
    /// <param name="group">The source definition owned by this view.</param>
    /// <param name="targetGroup">An owned destination group, or null to use the item anchor.</param>
    /// <param name="anchor">An ungrouped item anchor, or null to append when targetGroup is also null.</param>
    /// <param name="after">Whether to insert after the target group or item anchor.</param>
    /// <returns>Whether the current source and destination support the move.</returns>
    /// <remarks>This non-mutating check does not require mouse drag state or CanReorderTabs.</remarks>
    public bool CanMoveTabGroup(TabGroup group, TabGroup? targetGroup, object? anchor = null, bool after = false) =>
        group != null && CreateWholeGroupDragData(group) is TabDragData data
        && PlanWholeGroupDrop(data, new(targetGroup, anchor, after, Rect.Empty), true) != null;

    /// <summary>Tries to move all group members while retaining their order, memberships and selection.</summary>
    /// <param name="group">The source definition owned by this view.</param>
    /// <param name="targetGroup">An owned destination group, or null to use the item anchor.</param>
    /// <param name="anchor">An ungrouped item anchor, or null to append when targetGroup is also null.</param>
    /// <param name="after">Whether to insert after the target group or item anchor.</param>
    /// <returns>Whether the complete group move succeeded.</returns>
    /// <remarks>Uses the built-in validation and rollback without requiring an active mouse drag.</remarks>
    public bool TryMoveTabGroup(TabGroup group, TabGroup? targetGroup, object? anchor = null, bool after = false) =>
        group != null && CreateWholeGroupDragData(group) is TabDragData data
        && ApplyWholeGroupDrop(data, new(targetGroup, anchor, after, Rect.Empty), true);

    private GroupDropTarget ExternalTabTarget(object item, string? groupId, object? anchor, bool after)
    {
        bool header = anchor == null && groupId != null;
        if (header)
        {
            anchor = Items.Cast<object>().LastOrDefault(candidate => !IsSameItem(candidate, item)
                && (ItemContainerGenerator.ContainerFromItem(candidate) as TabViewItem)?.GroupId == groupId);
        }

        return new(groupId, anchor, after, header, this);
    }
}
