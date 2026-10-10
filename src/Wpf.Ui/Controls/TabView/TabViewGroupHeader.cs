// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Wpf.Ui.Controls;

/// <summary>An internal header, separate from the tab item generator and selection model.</summary>
internal sealed class TabViewGroupHeader(TabView tabView, TabGroup group) : Button
{
    internal static readonly DependencyProperty DefaultToolTipProperty = DependencyProperty.Register(
        "DefaultToolTip",
        typeof(object),
        typeof(TabViewGroupHeader),
        new PropertyMetadata(null, (d, _) => d.CoerceValue(ToolTipProperty))
    );

    static TabViewGroupHeader()
    {
        // Coercion preserves the application's local value, binding and style underneath.
        ToolTipProperty.OverrideMetadata(
            typeof(TabViewGroupHeader),
            new FrameworkPropertyMetadata(null, null, (d, value) =>
            {
                TabViewGroupHeader header = (TabViewGroupHeader)d;
                return header.TabView.ResolveGroupToolTip(header, value);
            })
        );
    }

    internal TabView TabView { get; } = tabView;
    internal TabGroup Group { get; } = group;

    protected override AutomationPeer OnCreateAutomationPeer() => new TabViewGroupHeaderAutomationPeer(this);

    internal void RefreshAutomationState() =>
        (UIElementAutomationPeer.FromElement(this) as TabViewGroupHeaderAutomationPeer)?.RefreshState();
}

/// <summary>Exposes expansion and focus without presenting the group as a selected tab.</summary>
internal sealed class TabViewGroupHeaderAutomationPeer : ButtonAutomationPeer, IExpandCollapseProvider
{
    private string _name;
    private ExpandCollapseState _state;

    internal TabViewGroupHeaderAutomationPeer(TabViewGroupHeader owner)
        : base(owner)
    {
        _name = HeaderName;
        _state = ExpandCollapseState;
    }

    private TabViewGroupHeader Header => (TabViewGroupHeader)Owner;
    private string HeaderName =>
        string.IsNullOrEmpty(Header.Group.Header) ? "Unnamed group" : Header.Group.Header;
    public ExpandCollapseState ExpandCollapseState =>
        Header.Group.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    protected override string GetNameCore() => HeaderName;

    protected override string GetClassNameCore() => nameof(TabViewGroupHeader);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface switch
        {
            PatternInterface.ExpandCollapse => this,
            PatternInterface.Invoke or PatternInterface.Selection or PatternInterface.SelectionItem => null,
            _ => base.GetPattern(patternInterface),
        };

    public void Expand() => ChangeExpansion(true);

    public void Collapse() => ChangeExpansion(false);

    private void ChangeExpansion(bool expanded)
    {
        if (!Header.IsEnabled)
        {
            throw new ElementNotEnabledException();
        }

        if (!Header.TabView.SetGroupExpanded(Header.Group, expanded))
        {
            throw new InvalidOperationException(
                "The group is no longer available or its expansion state is read-only."
            );
        }
    }

    internal void RefreshState()
    {
        string name = HeaderName;
        ExpandCollapseState state = ExpandCollapseState;
        if (ListenerExists(AutomationEvents.PropertyChanged))
        {
            if (_name != name)
            {
                RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, _name, name);
            }

            if (_state != state)
            {
                RaisePropertyChangedEvent(
                    ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                    _state,
                    state
                );
            }
        }

        _name = name;
        _state = state;
        InvalidatePeer();
    }
}
