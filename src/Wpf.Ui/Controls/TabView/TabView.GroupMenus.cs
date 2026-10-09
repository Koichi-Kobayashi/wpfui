// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;

namespace Wpf.Ui.Controls;

public partial class TabView
{
    private ContextMenu? _groupMenu;
    private Popup? _groupEditor;
    private TabGroup? _editingGroup;
    private System.Windows.Controls.TextBox? _groupEditorName;
    private System.Windows.Controls.TextBlock? _groupEditorError;
    private Color _groupEditorColor;

    private static readonly (string Name, Color Color)[] GroupColors =
    {
        ("Gray", Colors.SlateGray),
        ("Blue", Colors.DodgerBlue),
        ("Green", Colors.MediumSeaGreen),
        ("Yellow", Colors.Goldenrod),
        ("Orange", Colors.Coral),
        ("Red", Colors.IndianRed),
        ("Purple", Colors.MediumPurple),
        ("Pink", Colors.HotPink),
    };

    private bool HasCustomGroupMenu(FrameworkElement target) =>
        ContextMenu != null
        || DependencyPropertyHelper.GetValueSource(this, ContextMenuProperty).BaseValueSource
            != BaseValueSource.Default
        || target.ContextMenu != null
        || DependencyPropertyHelper.GetValueSource(target, ContextMenuProperty).BaseValueSource
            != BaseValueSource.Default;

    /// <inheritdoc />
    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);
        if (e.Handled || !IsGroupingActive || !_groupSubscriptionsEnabled)
        {
            return;
        }

        DependencyObject? hit = e.OriginalSource as DependencyObject;
        FrameworkElement? target = null;
        for (DependencyObject? current = hit; current != null && current != this; )
        {
            if (current is FrameworkElement element && element.ContextMenu != null)
            {
                return;
            }

            if (current is TabViewItem item && _containers.Contains(item))
            {
                target = item;
                break;
            }

            if (current is FrameworkElement header && _groupHeaders.Values.Any(button => button == header))
            {
                target = header;
                break;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        if (target == null || HasCustomGroupMenu(target))
        {
            return;
        }

        EndDrag();
        CloseGroupManagementUI();
        ContextMenu? menu = BuildGroupMenu(target);
        if (menu == null)
        {
            return;
        }

        e.Handled = true;
        _groupMenu = menu;
        menu.SetCurrentValue(ContextMenu.IsOpenProperty, true);
    }

    private ContextMenu? BuildGroupMenu(FrameworkElement target)
    {
        if (!IsGroupingActive || HasCustomGroupMenu(target))
        {
            return null;
        }

        ContextMenu menu = new() { PlacementTarget = target };
        menu.Resources.MergedDictionaries.Add(Resources);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_groupMenu, menu))
            {
                _groupMenu = null;
            }
        };
        if (target is TabViewItem tab && _containers.Contains(tab))
        {
            AddGroupMenuItem(
                menu.Items,
                "Add to new group…",
                MutableGroupDefinitions() != null && MembershipWrite(tab) != null,
                () =>
                {
                    TabGroup? group = CreateGroupForTab(tab);
                    if (group != null)
                    {
                        ShowGroupEditor(group, target);
                    }
                    else
                    {
                        ShowGroupOperationError(target);
                    }
                }
            );
            MenuItem existing = new() { Header = "Add to existing group" };
            foreach (TabGroup group in _groupsById.Values)
            {
                AddGroupMenuItem(
                    existing.Items,
                    GroupLabel(group),
                    group.Id != tab.GroupId && CanChangeMembership(tab, group.Id),
                    () =>
                    {
                        if (!ChangeMembership(tab, group.Id))
                        {
                            ShowGroupOperationError(target);
                        }
                    }
                );
            }

            existing.IsEnabled = existing.Items.Cast<MenuItem>().Any(item => item.IsEnabled);
            menu.Items.Add(existing);
            if (tab.GroupId != null)
            {
                AddGroupMenuItem(
                    menu.Items,
                    "Remove from group",
                    CanChangeMembership(tab, null),
                    () =>
                    {
                        if (!ChangeMembership(tab, null))
                        {
                            ShowGroupOperationError(target);
                        }
                    }
                );
            }
        }
        else
        {
            TabGroup? group = _groupHeaders.FirstOrDefault(pair => pair.Value == target).Key;
            if (group == null || !OwnsGroup(group))
            {
                return null;
            }

            bool editable =
                PropertyWrite.TryCreate(group, TabGroup.HeaderProperty, true) != null
                || PropertyWrite.TryCreate(group, TabGroup.ColorProperty, true) != null;
            AddGroupMenuItem(
                menu.Items,
                "Edit group name and color…",
                editable,
                () =>
                {
                    if (OwnsGroup(group))
                    {
                        ShowGroupEditor(group, target);
                    }
                }
            );
            AddGroupMenuItem(
                menu.Items,
                "Ungroup tabs",
                CanUngroup(group),
                () =>
                {
                    if (!Ungroup(group))
                    {
                        ShowGroupOperationError(target);
                    }
                }
            );
        }

        return menu;
    }

    private static string GroupLabel(TabGroup group) =>
        string.IsNullOrEmpty(group.Header) ? "Unnamed group" : group.Header;

    private static void AddGroupMenuItem(ItemCollection items, string title, bool enabled, Action action)
    {
        MenuItem item = new() { Header = title, IsEnabled = enabled };
        item.Click += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        items.Add(item);
    }

    private Popup BuildGroupEditor(TabGroup group, FrameworkElement target)
    {
        CloseGroupManagementUI();
        _editingGroup = group;
        _groupEditorColor = group.Color;
        StackPanel content = new() { Width = 264 };
        content.Children.Add(
            new System.Windows.Controls.TextBlock { Text = "Group name", Margin = new Thickness(0, 0, 0, 6) }
        );
        _groupEditorName = new System.Windows.Controls.TextBox
        {
            Text = group.Header,
            IsReadOnly = PropertyWrite.TryCreate(group, TabGroup.HeaderProperty, true) == null,
        };
        System.Windows.Automation.AutomationProperties.SetName(_groupEditorName, "Group name");
        content.Children.Add(_groupEditorName);
        WrapPanel colors = new() { Margin = new Thickness(0, 8, 0, 8) };
        List<(System.Windows.Controls.Button Button, Color Color)> swatches = new();
        foreach ((string Name, Color Color) preset in GroupColors)
        {
            System.Windows.Controls.Button button = new()
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Margin = new Thickness(1),
                ToolTip = preset.Name,
                IsEnabled = PropertyWrite.TryCreate(group, TabGroup.ColorProperty, true) != null,
                Content = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(preset.Color),
                },
                BorderThickness = new Thickness(group.Color == preset.Color ? 2 : 1),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"{preset.Name} group color");
            button.Click += (_, e) =>
            {
                e.Handled = true;
                if (!ReferenceEquals(_editingGroup, group))
                {
                    return;
                }

                _groupEditorColor = preset.Color;
                RefreshGroupEditorPreview();
                foreach ((System.Windows.Controls.Button Button, Color Color) swatch in swatches)
                {
                    swatch.Button.SetCurrentValue(
                        BorderThicknessProperty,
                        new Thickness(swatch.Color == preset.Color ? 2 : 1)
                    );
                }
            };
            swatches.Add((button, preset.Color));
            colors.Children.Add(button);
        }

        content.Children.Add(colors);
        _groupEditorError = new System.Windows.Controls.TextBlock
        {
            Text = "The application rejected this update. Check the bound data before retrying.",
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        content.Children.Add(_groupEditorError);
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        System.Windows.Controls.Button save = new() { Content = "Apply", MinWidth = 72 };
        System.Windows.Controls.Button cancel = new()
        {
            Content = "Cancel",
            MinWidth = 72,
            Margin = new Thickness(8, 0, 0, 0),
        };
        save.Click += (_, _) => CommitGroupEditor();
        cancel.Click += (_, _) => CloseGroupManagementUI();
        actions.Children.Add(save);
        actions.Children.Add(cancel);
        content.Children.Add(actions);
        Border border = new()
        {
            Child = content,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = SystemColors.ControlDarkBrush,
        };
        border.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        border.Resources.MergedDictionaries.Add(Resources);
        border.PreviewKeyDown += OnGroupEditorKeyDown;
        Popup popup = new()
        {
            Child = border,
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
            StaysOpen = false,
        };
        popup.Opened += (_, _) =>
        {
            _groupEditorName?.Focus();
            _groupEditorName?.SelectAll();
        };
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_groupEditor, popup))
            {
                _groupEditor = null;
                _editingGroup = null;
                _groupEditorName = null;
                _groupEditorError = null;
                RefreshGroupEditorPreview();
            }
        };
        _groupEditor = popup;
        return popup;
    }

    private void ShowGroupEditor(TabGroup group, FrameworkElement target) =>
        BuildGroupEditor(group, target).SetCurrentValue(Popup.IsOpenProperty, true);

    private bool CommitGroupEditor()
    {
        if (
            _editingGroup != null
            && _groupEditorName != null
            && EditGroup(_editingGroup, _groupEditorName.Text, _groupEditorColor)
        )
        {
            CloseGroupManagementUI();
            return true;
        }

        _groupEditorError?.SetCurrentValue(VisibilityProperty, Visibility.Visible);
        return false;
    }

    private void OnGroupEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseGroupManagementUI();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitGroupEditor();
        }
    }

    private void ShowGroupOperationError(FrameworkElement target)
    {
        CloseGroupManagementUI();
        System.Windows.Controls.TextBlock message = new()
        {
            Text = "The application rejected this group change.",
            Margin = new Thickness(12),
        };
        Border border = new() { Background = SystemColors.WindowBrush, Child = message };
        border.SetCurrentValue(TextElement.ForegroundProperty, SystemColors.WindowTextBrush);
        _groupEditor = new Popup
        {
            Child = border,
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
        };
        _groupEditor.SetCurrentValue(Popup.IsOpenProperty, true);
    }

    private void CloseGroupManagementUI()
    {
        bool hadPreview = _editingGroup != null;
        if (_groupMenu != null)
        {
            _groupMenu.SetCurrentValue(ContextMenu.IsOpenProperty, false);
        }

        if (_groupEditor != null)
        {
            _groupEditor.SetCurrentValue(Popup.IsOpenProperty, false);
        }

        _groupMenu = null;
        _groupEditor = null;
        _editingGroup = null;
        _groupEditorName = null;
        _groupEditorError = null;
        if (hadPreview)
        {
            RefreshGroupEditorPreview();
        }
    }

    private Color GetDisplayedGroupColor(TabGroup group) =>
        _groupEditor != null && ReferenceEquals(_editingGroup, group) ? _groupEditorColor : group.Color;

    private void RefreshGroupEditorPreview()
    {
        foreach (KeyValuePair<TabGroup, Button> pair in _groupHeaders)
        {
            UpdateGroupHeader(pair.Key, pair.Value);
        }

        UpdateGroupBackgroundAppearance(UseHighContrastGroupBackground);
    }

    private void ValidateGroupEditor()
    {
        if (_editingGroup != null && !OwnsGroup(_editingGroup))
        {
            CloseGroupManagementUI();
        }
    }
}
