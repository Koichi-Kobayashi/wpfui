// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Diagnostics.CodeAnalysis;
using System.Windows.Controls;

namespace Wpf.Ui.Controls;

public partial class TabView
{
    /// <summary>Tries to create a group and assign an existing item without reordering items.</summary>
    /// <param name="item">An item in this view with a generated container.</param>
    /// <param name="group">The created group on success; otherwise null.</param>
    /// <returns>Whether the definition and membership changes succeeded.</returns>
    /// <remarks>
    /// Requires an applied group-aware template and mutable collections. Supported membership bindings
    /// are preserved. Known changes are restored on failure; arbitrary application callback changes
    /// cannot be made transactional. No menu or editor is opened.
    /// </remarks>
    public bool TryCreateTabGroup(object item, [NotNullWhen(true)] out TabGroup? group)
    {
        group = item != null && ItemContainerGenerator.ContainerFromItem(item) is TabViewItem tab
            ? CreateGroupForTab(tab)
            : null;
        return group != null;
    }

    /// <summary>Tries to change membership without reordering items.</summary>
    /// <param name="item">An item in this view with a generated container.</param>
    /// <param name="groupId">An existing group identifier, or null to remove membership.</param>
    /// <returns>Whether the binding-preserving membership change succeeded.</returns>
    public bool TryAssignTabToGroup(object item, string? groupId) =>
        item != null && ItemContainerGenerator.ContainerFromItem(item) is TabViewItem tab
        && ChangeMembership(tab, groupId);

    /// <summary>Tries to remove a group and clear its memberships without closing or reordering tabs.</summary>
    /// <param name="group">A definition owned by this view.</param>
    /// <returns>Whether all memberships and the definition were removed successfully.</returns>
    public bool TryRemoveTabGroup(TabGroup group) => group != null && Ungroup(group);

    /// <summary>Tries to change a group's name and color together, preserving supported bindings.</summary>
    /// <param name="group">A definition owned by this view.</param>
    /// <param name="header">The new displayed name.</param>
    /// <param name="color">The new marker color.</param>
    /// <returns>Whether both changes succeeded. Known changes are restored on failure.</returns>
    /// <remarks>This operation changes state only; it does not open the built-in editor.</remarks>
    public bool TryEditTabGroup(TabGroup group, string header, Color color) =>
        group != null && EditGroup(group, header, color);

    /// <summary>Tries to change expansion state while preserving supported bindings.</summary>
    /// <param name="group">A definition owned by this view.</param>
    /// <param name="expanded">Whether member headers should be expanded.</param>
    /// <returns>Whether the state was applied or already matched the request.</returns>
    /// <remarks>The selected item's content is not changed by collapsing its group.</remarks>
    public bool TrySetGroupExpanded(TabGroup group, bool expanded) =>
        group != null && SetGroupExpanded(group, expanded);

    internal ContextMenu? CreateTabGroupMenu(object item, FrameworkElement placementTarget) =>
        ItemContainerGenerator.ContainerFromItem(item) is TabViewItem tab
            ? BuildGroupMenuForPresentation(tab, placementTarget, true)
            : null;

    internal ContextMenu? CreateGroupMenu(TabGroup group, FrameworkElement placementTarget) =>
        _groupHeaders.TryGetValue(group, out Button? header)
            ? BuildGroupMenuForPresentation(header, placementTarget, true)
            : null;
}
