// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Windows;

// ReSharper disable once CheckNamespace
namespace Wpf.Ui.Controls;

/// <summary>
/// Represents a single tab within a <see cref="TabView"/>.
/// </summary>
[TemplatePart(Name = "PART_CloseButton", Type = typeof(System.Windows.Controls.Button))]
public class TabViewItem : System.Windows.Controls.TabItem
{
    private System.Windows.Controls.Button? _closeButton;
    private bool _isGroupCollapsed;

    /// <summary>Identifies the provisional <see cref="GroupId"/> dependency property.</summary>
    public static readonly DependencyProperty GroupIdProperty = DependencyProperty.Register(
        nameof(GroupId),
        typeof(string),
        typeof(TabViewItem),
        new PropertyMetadata(null, OnGroupIdChanged)
    );

    /// <summary>Gets or sets the provisional group identifier. Null denotes an ungrouped tab.</summary>
    public string? GroupId
    {
        get => (string?)GetValue(GroupIdProperty);
        set => SetValue(GroupIdProperty, value);
    }

    internal void SetGroupCollapsed(bool collapsed)
    {
        if (_isGroupCollapsed == collapsed)
        {
            return;
        }
        _isGroupCollapsed = collapsed;
        CoerceValue(VisibilityProperty);
    }

    private static void OnGroupIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(d) is TabView owner)
        {
            owner.InvalidateGroupLayout();
        }
    }

    /// <summary>Identifies the <see cref="IsClosable"/> dependency property.</summary>
    public static readonly DependencyProperty IsClosableProperty = DependencyProperty.Register(
        nameof(IsClosable),
        typeof(bool),
        typeof(TabViewItem),
        new PropertyMetadata(true)
    );

    /// <summary>
    /// Gets or sets a value indicating whether the tab can be closed.
    /// </summary>
    public bool IsClosable
    {
        get => (bool)GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    /// <summary>
    /// Occurs when the close button is clicked.
    /// </summary>
    public event RoutedEventHandler? CloseRequested;

    /// <summary>
    /// Raises the <see cref="CloseRequested"/> event.
    /// </summary>
    internal void OnCloseRequested()
    {
        if (IsClosable)
        {
            CloseRequested?.Invoke(this, new RoutedEventArgs());
        }
    }

    static TabViewItem()
    {
        VisibilityProperty.OverrideMetadata(
            typeof(TabViewItem),
            new FrameworkPropertyMetadata(
                Visibility.Visible,
                null,
                (d, value) => ((TabViewItem)d)._isGroupCollapsed ? Visibility.Collapsed : value
            )
        );
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(TabViewItem),
            new FrameworkPropertyMetadata(typeof(TabViewItem))
        );
    }

    public override void OnApplyTemplate()
    {
        if (_closeButton != null)
        {
            _closeButton.Click -= OnCloseButtonClick;
        }

        base.OnApplyTemplate();

        _closeButton = GetTemplateChild("PART_CloseButton") as System.Windows.Controls.Button;
        if (_closeButton != null)
        {
            _closeButton.Click += OnCloseButtonClick;
        }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        OnCloseRequested();
    }
}
