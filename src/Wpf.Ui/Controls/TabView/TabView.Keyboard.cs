// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Wpf.Ui.Controls;

/// <summary>Navigation and focus recovery for the projected group header strip.</summary>
public partial class TabView
{
    private object? _focusedHeaderItem;
    private TabGroup? _focusedHeaderGroup;
    private FrameworkElement? _focusedHeaderElement;
    private bool _headerFocusInvalidated;
    private int _focusedHeaderOrdinal;
    private DispatcherOperation? _headerFocusRecovery;
    private DispatcherOperation? _groupAutomationRefresh;

    internal bool SetGroupExpanded(TabGroup group, bool expanded)
    {
        if (!IsGroupingActive || !OwnsGroup(group))
        {
            return false;
        }

        if (group.IsExpanded == expanded)
        {
            return true;
        }

        PropertyWrite? write = PropertyWrite.TryCreate(group, TabGroup.IsExpandedProperty, true);
        bool before = group.IsExpanded;
        try
        {
            if (write != null && write.Set(expanded))
            {
                return true;
            }
        }
        catch
        { /* Application bindings may reject an expansion request. */
        }
        try
        {
            write?.Set(before);
        }
        catch
        { /* Keep focus recovery available even if a setter rejects restoration. */
        }
        return false;
    }

    internal List<FrameworkElement> GroupKeyboardEntries() =>
        !IsGroupingActive
            ? new()
            : GetGroupLayoutEntries(_groupPanel!.Children.Cast<UIElement>().ToList())
                .OfType<FrameworkElement>()
                .Where(entry => entry.Visibility == Visibility.Visible && entry.IsEnabled && entry.Focusable)
                .ToList();

    private FrameworkElement? FindHeaderEntry(DependencyObject? source)
    {
        for (DependencyObject? current = source; current != null && current != this; )
        {
            if (current is FrameworkElement element && _groupHeaders.Values.Any(header => header == element))
            {
                return element;
            }

            if (current is TabItem tab && ItemContainerGenerator.IndexFromContainer(tab) >= 0)
            {
                return tab;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private FrameworkElement? GroupKeyDestination(Key key, ModifierKeys modifiers, FrameworkElement current)
    {
        if (modifiers != ModifierKeys.None)
        {
            return null;
        }

        List<FrameworkElement> entries = GroupKeyboardEntries();
        int index = entries.IndexOf(current);
        if (index < 0 || entries.Count == 0)
        {
            return null;
        }

        if (key == Key.Home)
            return entries[0];
        if (key == Key.End)
            return entries[entries.Count - 1];
        if (key != Key.Left && key != Key.Right)
            return null;
        int direction = key == Key.Right ? 1 : -1;
        if (FlowDirection == FlowDirection.RightToLeft)
            direction = -direction;
        return entries[(index + direction + entries.Count) % entries.Count];
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || !IsGroupingActive || !_groupSubscriptionsEnabled)
        {
            return;
        }

        FrameworkElement? current = FindHeaderEntry(e.OriginalSource as DependencyObject);
        // Editable header children, close buttons and content retain their own key handling.
        if (current == null || e.OriginalSource != current)
        {
            return;
        }

        if (
            Keyboard.Modifiers == ModifierKeys.None
            && (e.Key == Key.Enter || e.Key == Key.Space)
            && current is TabViewGroupHeader header
        )
        {
            SetGroupExpanded(header.Group, !header.Group.IsExpanded);
            e.Handled = true;
            return;
        }

        FrameworkElement? destination = GroupKeyDestination(e.Key, Keyboard.Modifiers, current);
        if (destination != null)
        {
            destination.Focus();
            EnsureGroupEntryVisible(destination);
            e.Handled = true;
        }
    }

    private bool HandleGroupTabSwitch(Key key, ModifierKeys modifiers)
    {
        if (
            !IsGroupingActive
            || !_groupSubscriptionsEnabled
            || key != Key.Tab
            || (modifiers != ModifierKeys.Control && modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
        )
        {
            return false;
        }

        List<TabItem> tabs = GroupKeyboardEntries().OfType<TabItem>().ToList();
        if (tabs.Count > 0)
        {
            int current = tabs.FindIndex(tab => tab.IsSelected);
            bool previous = (modifiers & ModifierKeys.Shift) != 0;
            int index =
                current < 0
                    ? (previous ? tabs.Count - 1 : 0)
                    : (current + (previous ? -1 : 1) + tabs.Count) % tabs.Count;
            TabItem tab = tabs[index];
            SetCurrentValue(SelectedItemProperty, Items[ItemContainerGenerator.IndexFromContainer(tab)]);
            tab.Focus();
            EnsureGroupEntryVisible(tab);
        }
        return true;
    }

    private void EnsureGroupEntryVisible(FrameworkElement entry)
    {
        if (_headerScroller == null || _groupPanel == null || entry.Visibility != Visibility.Visible)
        {
            return;
        }

        Point position = entry.TranslatePoint(new Point(0, 0), _groupPanel);
        double offset = _headerScroller.HorizontalOffset;
        double next =
            position.X < offset ? position.X
            : position.X + entry.ActualWidth > offset + _headerScroller.ViewportWidth
                ? position.X + entry.ActualWidth - _headerScroller.ViewportWidth
            : offset;
        if (Math.Abs(next - offset) > .5)
        {
            StopHeaderScrollAnimation();
            _headerScroller.ScrollToHorizontalOffset(
                Math.Max(0, Math.Min(_headerScroller.ScrollableWidth, next))
            );
        }
    }

    /// <inheritdoc />
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (!IsGroupingActive)
            return;
        FrameworkElement? entry = FindHeaderEntry(e.NewFocus as DependencyObject);
        if (entry == null)
        {
            if (e.NewFocus == this && HeaderFocusWasRemoved())
            {
                return;
            }

            _focusedHeaderItem = null;
            _focusedHeaderGroup = null;
            _focusedHeaderElement = null;
            return;
        }

        _focusedHeaderElement = entry;
        _headerFocusInvalidated = false;
        _focusedHeaderOrdinal = Math.Max(0, GroupKeyboardEntries().IndexOf(entry));
        _focusedHeaderGroup = (entry as TabViewGroupHeader)?.Group;
        int index = ItemContainerGenerator.IndexFromContainer(entry);
        _focusedHeaderItem = index < 0 ? null : Items[index];
        EnsureGroupEntryVisible(entry);
    }

    /// <inheritdoc />
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (e.NewFocus != null && !IsKeyboardFocusWithin)
        {
            if (e.NewFocus == Window.GetWindow(this))
            {
                QueueGroupFocusRecovery();
                return;
            }

            _focusedHeaderItem = null;
            _focusedHeaderGroup = null;
            _focusedHeaderElement = null;
        }
    }

    private bool HeaderFocusWasRemoved() =>
        _focusedHeaderElement != null
        && (
            _headerFocusInvalidated
            || !_focusedHeaderElement.IsVisible
            || VisualTreeHelper.GetParent(_focusedHeaderElement) == null
            || (
                _focusedHeaderElement is TabItem
                && (
                    ItemContainerGenerator.IndexFromContainer(_focusedHeaderElement) < 0
                    || (
                        _focusedHeaderItem != null
                        && !Items.Cast<object>().Any(item => IsSameItem(item, _focusedHeaderItem))
                    )
                )
            )
        );

    private void BeforeGroupContainerCleared(DependencyObject element)
    {
        if (element == _focusedHeaderElement)
        {
            _headerFocusInvalidated = true;
            QueueGroupFocusRecovery();
        }
    }

    private void UpdateGroupKeyboardAndAutomation()
    {
        List<FrameworkElement> entries = GroupKeyboardEntries();
        for (int index = 0; index < entries.Count; index++)
        {
            // Only our own headers get navigation indices. User tab styles and bindings stay authoritative.
            if (entries[index] is TabViewGroupHeader header)
            {
                header.SetCurrentValue(KeyboardNavigation.TabIndexProperty, index);
            }
        }

        if (
            UIElementAutomationPeer.FromElement(this) is AutomationPeer peer
            && _groupAutomationRefresh == null
        )
        {
            // Wait until container generation has completed before rebuilding the automation child tree.
            _groupAutomationRefresh = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    _groupAutomationRefresh = null;
                    peer.ResetChildrenCache();
                    peer.InvalidatePeer();
                })
            );
        }

        QueueGroupFocusRecovery();
    }

    private void QueueGroupFocusRecovery()
    {
        if ((_focusedHeaderItem != null || _focusedHeaderGroup != null) && _headerFocusRecovery == null)
        {
            _headerFocusRecovery = Dispatcher.BeginInvoke(
                // WPF's deferred focus validation must finish before selecting a replacement header.
                DispatcherPriority.ContextIdle,
                new Action(RecoverGroupHeaderFocus)
            );
        }
    }

    private void RecoverGroupHeaderFocus()
    {
        _headerFocusRecovery = null;
        if (
            !IsGroupingActive
            || !_groupSubscriptionsEnabled
            || (_focusedHeaderGroup == null && _focusedHeaderItem == null)
        )
            return;
        if (Keyboard.FocusedElement == Window.GetWindow(this) && !HeaderFocusWasRemoved())
        {
            _focusedHeaderItem = null;
            _focusedHeaderGroup = null;
            _focusedHeaderElement = null;
            return;
        }

        if (
            Keyboard.FocusedElement is DependencyObject focused
            && FindHeaderEntry(focused) == null
            && focused != this
            && focused != Window.GetWindow(this)
        )
        {
            return;
        }

        List<FrameworkElement> entries = GroupKeyboardEntries();
        FrameworkElement? desired =
            _focusedHeaderGroup != null
                ? entries
                    .OfType<TabViewGroupHeader>()
                    .FirstOrDefault(header => header.Group == _focusedHeaderGroup)
                : ItemContainerGenerator.ContainerFromItem(_focusedHeaderItem) as FrameworkElement;
        if (desired == null || !entries.Contains(desired))
        {
            desired = entries.Count == 0 ? null : entries[Math.Min(_focusedHeaderOrdinal, entries.Count - 1)];
        }

        if (desired == null)
        {
            Focus();
        }
        else
        {
            if (!desired.IsKeyboardFocusWithin)
                desired.Focus();
            EnsureGroupEntryVisible(desired);
        }
    }

    private void CancelGroupFocusRecovery()
    {
        _headerFocusRecovery?.Abort();
        _headerFocusRecovery = null;
        _groupAutomationRefresh?.Abort();
        _groupAutomationRefresh = null;
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new TabViewAutomationPeer(this);

    internal List<FrameworkElement>? GroupAutomationEntries() =>
        !IsGroupingActive
            ? null
            : GetGroupLayoutEntries(_groupPanel!.Children.Cast<UIElement>().ToList())
                .OfType<FrameworkElement>()
                .ToList();
}

/// <summary>Keeps standard tab item peers and adds the separately templated group headers.</summary>
internal sealed class TabViewAutomationPeer(TabView owner) : TabControlAutomationPeer(owner)
{
    protected override List<AutomationPeer>? GetChildrenCore()
    {
        List<AutomationPeer>? standard = base.GetChildrenCore();
        List<FrameworkElement>? entries = ((TabView)Owner).GroupAutomationEntries();
        if (entries == null)
            return standard;
        List<AutomationPeer> result = new();
        foreach (FrameworkElement entry in entries)
        {
            AutomationPeer? peer;
            if (entry is TabViewGroupHeader)
            {
                peer = UIElementAutomationPeer.CreatePeerForElement(entry);
            }
            else
            {
                // During removal the generator can still expose indices from the old item list.
                // Resolve the container's item directly and match against the standard peer snapshot.
                object item = ((TabView)Owner).ItemContainerGenerator.ItemFromContainer(entry);
                peer =
                    ReferenceEquals(item, DependencyProperty.UnsetValue)
                        ? null
                        : standard
                            ?.OfType<ItemAutomationPeer>()
                            .FirstOrDefault(itemPeer => Equals(itemPeer.Item, item));
            }
            if (peer != null && !result.Contains(peer))
                result.Add(peer);
        }
        // Preserve standard peers for not-yet-generated items and selected collapsed content.
        if (standard != null)
        {
            result.AddRange(standard.Where(peer => !result.Contains(peer)));
        }
        return result;
    }
}
