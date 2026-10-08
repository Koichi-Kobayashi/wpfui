// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

namespace Wpf.Ui.Controls;

/// <summary>Provisional group definition for the TabView grouping proof of concept.</summary>
public sealed class TabGroup : Freezable
{
    /// <summary>Identifies the <see cref="Id"/> dependency property.</summary>
    public static readonly DependencyProperty IdProperty = DependencyProperty.Register(
        nameof(Id),
        typeof(string),
        typeof(TabGroup),
        new PropertyMetadata(string.Empty)
    );

    /// <summary>Identifies the <see cref="Header"/> dependency property.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(TabGroup),
        new PropertyMetadata(string.Empty)
    );

    /// <summary>Identifies the <see cref="Color"/> dependency property.</summary>
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color),
        typeof(Color),
        typeof(TabGroup),
        new PropertyMetadata(Colors.DodgerBlue)
    );

    /// <summary>Identifies the <see cref="IsExpanded"/> dependency property.</summary>
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded),
        typeof(bool),
        typeof(TabGroup),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault)
    );

    /// <summary>Gets or sets the group identifier.</summary>
    public string Id
    {
        get => (string)GetValue(IdProperty);
        set => SetValue(IdProperty, value);
    }

    /// <summary>Gets or sets the displayed group name.</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Gets or sets the group marker color.</summary>
    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the group's tab headers are expanded.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new TabGroup();
}
