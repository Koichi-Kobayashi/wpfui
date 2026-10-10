// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

namespace Wpf.Ui.Controls;

/// <summary>Defines the identity, appearance and expansion state of a TabView group.</summary>
/// <remarks>
/// Empty groups retain their definitions but have no visible header. Interactive edits preserve
/// bindings and support synchronous TwoWay CLR property bindings of the same type, without
/// converters, delays, validation rules or binding groups. Other bindings remain display-only.
/// Frozen definitions cannot be edited, but their tabs can still be reordered.
/// </remarks>
public sealed class TabGroup : Freezable
{
    internal bool IsChangingToolTip { get; private set; }

    internal event EventHandler? ToolTipChanged;

    /// <summary>Identifies the <see cref="ToolTip"/> dependency property.</summary>
    public static readonly DependencyProperty ToolTipProperty = DependencyProperty.Register(
        nameof(ToolTip),
        typeof(object),
        typeof(TabGroup),
        new PropertyMetadata(null)
    );

    /// <summary>Gets or sets the content of the group header tooltip.</summary>
    /// <remarks>
    /// A configured value takes precedence over the header's local value, binding or style.
    /// An unset value uses the header's configured tooltip, or the expanded or collapsed
    /// description when the header has no tooltip configuration. An explicitly set null,
    /// including a binding that resolves to null, hides the tooltip. Strings, bindings and
    /// custom content are supported. UI elements already parented elsewhere are not reparented.
    /// The default descriptions can be localized with the TabViewGroupCollapseToolTip and
    /// TabViewGroupExpandToolTip resource keys.
    /// </remarks>
    public object? ToolTip
    {
        get => GetValue(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        bool previous = IsChangingToolTip;
        IsChangingToolTip = e.Property == ToolTipProperty;
        try
        {
            base.OnPropertyChanged(e);
            if (IsChangingToolTip)
            {
                ToolTipChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsChangingToolTip = previous;
        }
    }

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
    /// <remarks>
    /// Null and empty identifiers do not define a group. For duplicate identifiers the first
    /// definition in TabView.TabGroups is used. Changing an identifier does not rewrite tab memberships.
    /// </remarks>
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
