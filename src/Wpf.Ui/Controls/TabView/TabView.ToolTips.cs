// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace Wpf.Ui.Controls;

public partial class TabView
{
    // Share one presenter for a visual, rather than parenting it to two headers' tooltips.
    private static readonly ConditionalWeakTable<UIElement, System.Windows.Controls.ToolTip> VisualToolTips =
        new();
    private System.Windows.Controls.ToolTip? _suppressedGroupToolTip;

    private void OnGroupToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is Button { Tag: TabGroup group } header)
        {
            // Null-to-null source changes do not raise a DP value-change notification.
            UpdateGroupToolTip(group, header);
            if (ReferenceEquals(header.ToolTip, _suppressedGroupToolTip))
            {
                e.Handled = true;
            }
        }
    }

    private void OnGroupToolTipChanged(object? sender, EventArgs e)
    {
        if (sender is TabGroup group && _groupHeaders.TryGetValue(group, out Button? header))
        {
            UpdateGroupToolTip(group, header);
        }
    }

    private void UpdateGroupToolTip(TabGroup group, Button header)
    {
        if (header is not TabViewGroupHeader groupHeader)
        {
            return;
        }

        string key = group.IsExpanded ? "TabViewGroupCollapseToolTip" : "TabViewGroupExpandToolTip";
        if (header.TryFindResource(key) != null)
        {
            groupHeader.SetResourceReference(TabViewGroupHeader.DefaultToolTipProperty, key);
        }
        else
        {
            groupHeader.SetCurrentValue(
                TabViewGroupHeader.DefaultToolTipProperty,
                group.IsExpanded ? "Collapse group" : "Expand group"
            );
        }
        groupHeader.CoerceValue(ToolTipProperty);
    }

    internal object? ResolveGroupToolTip(TabViewGroupHeader header, object? baseValue)
    {
        TabGroup group = header.Group;

        ValueSource source = DependencyPropertyHelper.GetValueSource(group, TabGroup.ToolTipProperty);
        bool unset =
            source.BaseValueSource == BaseValueSource.Default
            && !source.IsExpression
            && !source.IsAnimated
            && !source.IsCoerced;
        if (unset)
        {
            ValueSource headerSource = DependencyPropertyHelper.GetValueSource(header, ToolTipProperty);
            return headerSource.BaseValueSource != BaseValueSource.Default || headerSource.IsExpression
                ? baseValue ?? (_suppressedGroupToolTip ??= new System.Windows.Controls.ToolTip())
                : header.GetValue(TabViewGroupHeader.DefaultToolTipProperty);
        }

        object? content = group.ToolTip;
        if (content is UIElement visual && content is not System.Windows.Controls.ToolTip)
        {
            if (!visual.CheckAccess())
            {
                content = null;
            }
            else if (VisualToolTips.TryGetValue(visual, out System.Windows.Controls.ToolTip? presenter))
            {
                content = presenter;
            }
            else if (
                LogicalTreeHelper.GetParent(visual) != null
                || VisualTreeHelper.GetParent(visual) != null
            )
            {
                // Do not steal application content from its existing parent.
                content = null;
            }
            else
            {
                content = VisualToolTips.GetValue(
                    visual,
                    element => new System.Windows.Controls.ToolTip { Content = element }
                );
            }
        }
        // A non-null placeholder lets the opening event re-check ClearValue/binding changes.
        // The placeholder is never shown: the opening handler cancels it.
        return content ?? (_suppressedGroupToolTip ??= new System.Windows.Controls.ToolTip());
    }
}
