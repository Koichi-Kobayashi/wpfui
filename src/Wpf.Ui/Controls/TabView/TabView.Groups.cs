// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;

namespace Wpf.Ui.Controls;

public partial class TabView
{
    private readonly HashSet<TabGroup> _subscribedGroups = new();
    private readonly Dictionary<string, TabGroup> _groupsById = new(StringComparer.Ordinal);
    private readonly Dictionary<TabGroup, Button> _groupHeaders = new();
    private INotifyCollectionChanged? _groupCollection;
    private Canvas? _groupHeaderLayer;
    private TabViewPanel? _groupPanel;
    private bool _groupSubscriptionsEnabled = true;

    /// <summary>Identifies the provisional <see cref="TabGroups"/> dependency property.</summary>
    public static readonly DependencyProperty TabGroupsProperty = DependencyProperty.Register(
        nameof(TabGroups),
        typeof(IEnumerable<TabGroup>),
        typeof(TabView),
        new PropertyMetadata(null, OnTabGroupsChanged)
    );

    /// <summary>Gets or sets provisional group definitions. Null preserves the original tab behavior.</summary>
    public IEnumerable<TabGroup>? TabGroups
    {
        get => (IEnumerable<TabGroup>?)GetValue(TabGroupsProperty);
        set => SetValue(TabGroupsProperty, value);
    }

    internal bool IsGroupingActive => TabGroups != null && _groupPanel != null && _groupHeaderLayer != null;

    private static void OnTabGroupsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        TabView owner = (TabView)d;
        owner.EndDrag();
        owner.InvalidateGroupLayout();
    }

    private void AttachGroupTemplate()
    {
        _groupPanel = GetTemplateChild("HeaderPanel") as TabViewPanel;
        _groupHeaderLayer = GetTemplateChild("PART_GroupHeaders") as Canvas;
        _dropOverlay = GetTemplateChild("PART_DropOverlay") as Canvas;
        _groupBackgroundLayer = GetTemplateChild("PART_GroupBackgrounds") as Canvas;
        _headerScroller = GetTemplateChild("PART_HeaderScroller") as ScrollViewer;
        _scrollLeftButton = GetTemplateChild("PART_ScrollLeftButton") as System.Windows.Controls.Button;
        _scrollRightButton = GetTemplateChild("PART_ScrollRightButton") as System.Windows.Controls.Button;
        AttachHeaderStrip();
        InvalidateGroupLayout();
    }

    private void DetachGroupTemplate()
    {
        CancelGroupFocusRecovery();
        CloseGroupManagementUI();
        DetachHeaderStrip();
        ClearGroupBackgrounds();
        _groupBackgroundLayer = null;
        _headerScroller = null;
        _scrollLeftButton = null;
        _scrollRightButton = null;
        ClearGroupHeaders();
        _dropOverlay?.Children.Clear();
        _dropOverlay = null;
        _dropIndicator = null;
        _groupPanel = null;
        _groupHeaderLayer = null;
        foreach (TabViewItem container in _containers)
        {
            container.SetGroupCollapsed(false);
        }
    }

    private void ClearGroupHeaders()
    {
        foreach (Button button in _groupHeaders.Values)
        {
            button.Click -= OnGroupHeaderClick;
        }

        _groupHeaders.Clear();
        _groupHeaderLayer?.Children.Clear();
    }

    private void DetachGroupSubscriptions()
    {
        if (_groupCollection != null)
        {
            _groupCollection.CollectionChanged -= OnGroupCollectionChanged;
        }

        _groupCollection = null;
        foreach (TabGroup group in _subscribedGroups)
        {
            // Freeze clears Changed handlers itself and disallows removing them afterwards.
            if (!group.IsFrozen)
            {
                group.Changed -= OnGroupChanged;
            }
        }

        _subscribedGroups.Clear();
    }

    private void SuspendGroups()
    {
        CancelGroupFocusRecovery();
        CloseGroupManagementUI();
        DetachHeaderStrip();
        ClearGroupBackgrounds();
        _groupSubscriptionsEnabled = false;
        DetachGroupSubscriptions();
        ClearGroupHeaders();
    }

    private void ResumeGroups()
    {
        _groupSubscriptionsEnabled = true;
        AttachHeaderStrip();
        InvalidateGroupLayout();
    }

    private void OnGroupCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isUpdating)
        {
            CloseGroupManagementUI();
        }
        EndDrag();
        InvalidateGroupLayout();
    }

    private void OnGroupChanged(object? sender, EventArgs e)
    {
        EndDrag();
        InvalidateGroupLayout();
    }

    internal void InvalidateGroupLayout()
    {
        if (_deferGroupLayout)
        {
            return;
        }

        DetachGroupSubscriptions();
        _groupsById.Clear();
        if (TabGroups != null)
        {
            foreach (TabGroup group in TabGroups)
            {
                if (group == null)
                {
                    continue;
                }

                // PoC policy: the first definition wins; empty IDs denote no group.
                if (!string.IsNullOrEmpty(group.Id) && !_groupsById.ContainsKey(group.Id))
                {
                    _groupsById.Add(group.Id, group);
                }

                if (_groupSubscriptionsEnabled && !group.IsFrozen && _subscribedGroups.Add(group))
                {
                    group.Changed += OnGroupChanged;
                }
            }

            if (_groupSubscriptionsEnabled && TabGroups is INotifyCollectionChanged collection)
            {
                _groupCollection = collection;
                collection.CollectionChanged += OnGroupCollectionChanged;
            }
        }

        if (IsGroupingActive && _groupSubscriptionsEnabled)
        {
            foreach (
                TabGroup removed in _groupHeaders
                    .Keys.Where(group => !_groupsById.ContainsValue(group))
                    .ToList()
            )
            {
                Button button = _groupHeaders[removed];
                button.Click -= OnGroupHeaderClick;
                _groupHeaderLayer!.Children.Remove(button);
                _groupHeaders.Remove(removed);
            }

            foreach (TabGroup group in _groupsById.Values)
            {
                if (!_groupHeaders.TryGetValue(group, out Button? button))
                {
                    button = new TabViewGroupHeader(this, group)
                    {
                        Tag = group,
                        MinHeight = 36,
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(2, 0, 2, 0),
                        Background = Brushes.Transparent,
                    };
                    // The internal header subtype must retain the existing Fluent button style.
                    button.SetResourceReference(StyleProperty, typeof(Button));
                    button.Click += OnGroupHeaderClick;
                    _groupHeaders.Add(group, button);
                    _groupHeaderLayer!.Children.Add(button);
                }

                UpdateGroupHeader(group, button);
            }
        }
        else
        {
            ClearGroupHeaders();
        }

        foreach (TabViewItem container in _containers)
        {
            TryGetGroup(container, out TabGroup? group);
            bool collapsed = IsGroupingActive && group != null && !group.IsExpanded;
            if (
                collapsed
                && HasHeaderKeyboardFocus(container)
                && group != null
                && _groupHeaders.TryGetValue(group, out Button? header)
            )
            {
                header.Focus();
            }

            container.SetGroupCollapsed(collapsed);
        }

        _groupPanel?.InvalidateMeasure();
        _groupHeaderLayer?.InvalidateMeasure();
        if (!IsGroupingActive)
        {
            ClearGroupBackgrounds();
        }

        UpdateGroupBackgroundAppearance(UseHighContrastGroupBackground);
        UpdateHeaderScrollButtons();
        ValidateGroupEditor();
        UpdateGroupKeyboardAndAutomation();
    }

    internal void OnGroupMembershipChanged()
    {
        if (!_isUpdating)
        {
            EndDrag();
        }

        InvalidateGroupLayout();
    }

    private bool TryGetGroup(UIElement item, out TabGroup? group)
    {
        group = null;
        return item is TabViewItem tab
            && !string.IsNullOrEmpty(tab.GroupId)
            && _groupsById.TryGetValue(tab.GroupId!, out group);
    }

    internal TabGroup? GetLayoutGroup(UIElement element)
    {
        foreach (KeyValuePair<TabGroup, Button> pair in _groupHeaders)
        {
            if (pair.Value == element)
            {
                return pair.Key;
            }
        }

        TryGetGroup(element, out TabGroup? group);
        return group;
    }

    private static bool HasHeaderKeyboardFocus(TabViewItem container)
    {
        // Content can remain a logical child of TabItem while being presented outside its visual tree.
        // Only move focus if the focused element actually belongs to the disappearing header.
        for (DependencyObject? current = Keyboard.FocusedElement as DependencyObject; current != null; )
        {
            if (current == container)
            {
                return true;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void UpdateGroupHeader(TabGroup group, Button button)
    {
        bool selected = _containers.Any(tab => tab.IsSelected && tab.GroupId == group.Id);
        SolidColorBrush color = new(GetDisplayedGroupColor(group));
        StackPanel? content = button.Content as StackPanel;
        if (
            content == null
            || content.Children.Count != 2
            || content.Children[0] is not Border
            || content.Children[1] is not System.Windows.Controls.TextBlock
        )
        {
            content = new() { Orientation = Orientation.Horizontal };
            content.Children.Add(
                new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = color,
                    Margin = new Thickness(0, 0, 6, 0),
                }
            );
            content.Children.Add(
                new System.Windows.Controls.TextBlock
                {
                    Text = group.Header,
                    FontWeight = selected ? FontWeights.Bold : FontWeights.Normal,
                }
            );
            button.SetCurrentValue(ContentControl.ContentProperty, content);
        }

        // Keep the hit visual alive during mouse-down, selection changes and editor preview cleanup.
        ((Border)content.Children[0]).SetCurrentValue(Border.BackgroundProperty, color);
        System.Windows.Controls.TextBlock label = (System.Windows.Controls.TextBlock)content.Children[1];
        label.SetCurrentValue(System.Windows.Controls.TextBlock.TextProperty, group.Header);
        label.SetCurrentValue(
            System.Windows.Controls.TextBlock.FontWeightProperty,
            selected ? FontWeights.Bold : FontWeights.Normal
        );
        button.SetCurrentValue(BorderBrushProperty, color);
        // The Fluent button template uses separate brushes during hover and press.
        button.SetCurrentValue(Button.MouseOverBorderBrushProperty, color);
        button.SetCurrentValue(Button.PressedBorderBrushProperty, color);
        // Keep the group accent visible regardless of selection or membership binding mode.
        button.SetCurrentValue(BorderThicknessProperty, new Thickness(0, 0, 0, 3));
        // Frozen definitions cannot expand, but their tabs can still be moved as a group.
        button.SetCurrentValue(IsEnabledProperty, true);
        button.SetCurrentValue(ToolTipProperty, group.IsExpanded ? "Collapse group" : "Expand group");
        System.Windows.Automation.AutomationProperties.SetName(button, GroupLabel(group));
        (button as TabViewGroupHeader)?.RefreshAutomationState();
    }

    private void OnGroupHeaderClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_isDragging && _draggedGroupHeader == sender)
        {
            return;
        }

        if (sender is Button { Tag: TabGroup group } && !group.IsFrozen)
        {
            SetGroupExpanded(group, !group.IsExpanded);
        }
    }

    internal List<UIElement> GetGroupLayoutEntries(List<UIElement> tabs)
    {
        List<UIElement> entries = new();
        HashSet<TabGroup> emitted = new();
        foreach (UIElement tab in tabs)
        {
            if (
                TryGetGroup(tab, out TabGroup? group) && _groupHeaders.TryGetValue(group!, out Button? header)
            )
            {
                if (!emitted.Add(group!))
                {
                    continue;
                }

                entries.Add(header);
                entries.AddRange(
                    tabs.Where(member =>
                        TryGetGroup(member, out TabGroup? memberGroup) && memberGroup == group
                    )
                );
            }
            else
            {
                entries.Add(tab);
            }
        }

        foreach (KeyValuePair<TabGroup, Button> pair in _groupHeaders)
        {
            pair.Value.SetCurrentValue(
                VisibilityProperty,
                emitted.Contains(pair.Key) ? Visibility.Visible : Visibility.Collapsed
            );
        }

        return entries;
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_isUpdating)
        {
            CloseGroupManagementUI();
            EndDrag();
        }

        base.OnItemsChanged(e);
        InvalidateGroupLayout();
    }

    /// <inheritdoc />
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (_deferGroupLayout)
        {
            return;
        }
        foreach (KeyValuePair<TabGroup, Button> pair in _groupHeaders)
        {
            UpdateGroupHeader(pair.Key, pair.Value);
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && HandleGroupTabSwitch(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
