// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using TextBox = System.Windows.Controls.TextBox;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Theory]
    [InlineData(Key.Right, 0, 1)]
    [InlineData(Key.Right, 2, 3)]
    [InlineData(Key.Left, 4, 3)]
    [InlineData(Key.Left, 0, 7)]
    [InlineData(Key.Home, 6, 0)]
    [InlineData(Key.End, 0, 7)]
    public void GroupKeyboard_UsesProjectedOrder(Key key, int from, int to) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            FrameworkElement[] entries = KeyboardEntries(view);
            Assert.Equal(
                new[] { "A", "A1", "A2", "Home", "B", "B1", "B2", "Notes" },
                entries.Select(EntryName)
            );
            Assert.Same(
                entries[to],
                MenuResult<FrameworkElement>(
                    view,
                    "GroupKeyDestination",
                    key,
                    ModifierKeys.None,
                    entries[from]
                )
            );
            Assert.Equal(
                new[] { "A1", "Home", "B1", "A2", "Notes", "B2" },
                source.Select(item => item.Header)
            );
        });

    [Theory]
    [InlineData(Key.Right, 7)]
    [InlineData(Key.Left, 1)]
    public void GroupKeyboard_ReversesArrowsInRtl(Key key, int destination) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            view.FlowDirection = FlowDirection.RightToLeft;
            FrameworkElement[] entries = KeyboardEntries(view);
            Assert.Same(
                entries[destination],
                MenuResult<FrameworkElement>(view, "GroupKeyDestination", key, ModifierKeys.None, entries[0])
            );
        });

    [Fact]
    public void GroupKeyboard_SkipsCollapsedMembersButKeepsHeader() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            view.SelectedItem = source[0];
            object content = view.SelectedContent;
            view.TabGroups!.First().IsExpanded = false;
            Assert.Equal(
                new[] { "A", "Home", "B", "B1", "B2", "Notes" },
                KeyboardEntries(view).Select(EntryName)
            );
            Assert.Same(source[0], view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
        });

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void GroupKeyboard_ActivationTogglesOnlyExpansion(Key key) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            System.Windows.Controls.Button header = GroupHeader(view, group);
            view.SelectedItem = source[3];
            KeyEventArgs args = HeaderKey(key, header);
            Invoke(view, "OnPreviewKeyDown", args);
            Assert.True(args.Handled);
            Assert.False(group.IsExpanded);
            Assert.Same(source[3], view.SelectedItem);
            Invoke(view, "OnPreviewKeyDown", HeaderKey(key, header));
            Assert.True(group.IsExpanded);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupKeyboard_ControlTabSelectsVisibleTabsInProjectedOrder(bool previous) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            view.SelectedItem = source[0];
            ModifierKeys modifiers =
                ModifierKeys.Control | (previous ? ModifierKeys.Shift : ModifierKeys.None);
            Assert.True(MenuResult<bool>(view, "HandleGroupTabSwitch", Key.Tab, modifiers));
            Assert.Same(previous ? source[4] : source[3], view.SelectedItem);
            view.TabGroups!.First().IsExpanded = false;
            view.SelectedItem = source[0];
            Assert.True(MenuResult<bool>(view, "HandleGroupTabSwitch", Key.Tab, modifiers));
            Assert.Same(previous ? source[4] : source[1], view.SelectedItem);
        });

    [Theory]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    [InlineData(Key.Home)]
    [InlineData(Key.End)]
    public void GroupKeyboard_LeavesEditableChildrenAndContentKeysAlone(Key key) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            TextBox child = new();
            Container(view, source[0]).Header = child;
            Layout(view);
            KeyEventArgs args = HeaderKey(key, child);
            Invoke(view, "OnPreviewKeyDown", args);
            Assert.False(args.Handled);
            TextBox content = new();
            Container(view, source[0]).Content = content;
            view.SelectedItem = source[0];
            Layout(view);
            args = HeaderKey(key, content);
            Invoke(view, "OnPreviewKeyDown", args);
            Assert.False(args.Handled);
        });

    [Fact]
    public void GroupKeyboard_DoesNotInterceptNormalTabOrUngroupedNavigation() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            KeyEventArgs args = HeaderKey(Key.Tab, GroupHeader(view, view.TabGroups!.First()));
            Invoke(view, "OnPreviewKeyDown", args);
            Assert.False(args.Handled);
            view.TabGroups = null;
            foreach (Key key in new[] { Key.Left, Key.Right, Key.Home, Key.End })
            {
                args = HeaderKey(key, Container(view, view.Items[0]));
                Invoke(view, "OnPreviewKeyDown", args);
                Assert.False(args.Handled);
            }
            Assert.False(MenuResult<bool>(view, "HandleGroupTabSwitch", Key.Tab, ModifierKeys.Control));
        });

    [Fact]
    public void GroupKeyboard_ScrollsOffscreenEntryIntoView() =>
        RunSta(() =>
        {
            TabView view = LongHeaderView();
            FrameworkElement last = (FrameworkElement)view.Items[view.Items.Count - 1];
            Invoke(view, "EnsureGroupEntryVisible", last);
            view.UpdateLayout();
            Assert.True(HeaderScroller(view).HorizontalOffset > 0);
            double x = last.TranslatePoint(new Point(0, 0), HeaderScroller(view)).X;
            Assert.InRange(x, -.5, HeaderScroller(view).ViewportWidth);
            Invoke(view, "EnsureGroupEntryVisible", GroupHeader(view, view.TabGroups!.First()));
            view.UpdateLayout();
            Assert.InRange(HeaderScroller(view).HorizontalOffset, 0, 2);
        });

    [Fact]
    public void GroupAutomation_ExposesNameExpansionAndNoSelection() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(GroupHeader(view, group));
            Assert.Equal("A", peer.GetName());
            Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
            Assert.True(GroupHeader(view, group).Focusable);
            Assert.Same(view.FindResource(typeof(Wpf.Ui.Controls.Button)), GroupHeader(view, group).Style);
            Assert.Null(peer.GetPattern(PatternInterface.SelectionItem));
            Assert.Null(peer.GetPattern(PatternInterface.Selection));
            IExpandCollapseProvider provider = Assert.IsAssignableFrom<IExpandCollapseProvider>(
                peer.GetPattern(PatternInterface.ExpandCollapse)
            );
            view.SelectedItem = source[0];
            object content = view.SelectedContent;
            provider.Collapse();
            Assert.Equal(
                System.Windows.Automation.ExpandCollapseState.Collapsed,
                provider.ExpandCollapseState
            );
            Assert.Same(source[0], view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
            group.Header = "Renamed";
            Assert.Equal("Renamed", peer.GetName());
            provider.Expand();
            Assert.Equal(
                System.Windows.Automation.ExpandCollapseState.Expanded,
                provider.ExpandCollapseState
            );
        });

    [Fact]
    public void GroupAutomation_PreservesStandardTabSelectionPeers() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(view);
            Assert.NotNull(peer.GetPattern(PatternInterface.Selection));
            List<AutomationPeer> children = peer.GetChildren();
            Assert.Equal(
                2,
                children.Count(child => child.GetAutomationControlType() == AutomationControlType.Group)
            );
            Assert.Equal(
                6,
                children.Count(child => child.GetPattern(PatternInterface.SelectionItem) != null)
            );
            Assert.Equal("A", children[0].GetName());
            view.TabGroups = null;
            peer.InvalidatePeer();
            PumpHeaderDispatcher();
            Assert.Equal(6, peer.GetChildren().Count);
            Assert.All(
                peer.GetChildren(),
                child => Assert.NotNull(child.GetPattern(PatternInterface.SelectionItem))
            );
        });

    [Theory]
    [InlineData(BindingMode.TwoWay)]
    [InlineData(BindingMode.Default)]
    public void GroupAutomation_PreservesWritableExpansionBinding(BindingMode mode) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            TabGroup group = view.TabGroups!.First();
            ExpansionModel model = new();
            BindingOperations.SetBinding(
                group,
                TabGroup.IsExpandedProperty,
                new Binding(nameof(ExpansionModel.Expanded)) { Source = model, Mode = mode }
            );
            BindingExpression expression = BindingOperations.GetBindingExpression(
                group,
                TabGroup.IsExpandedProperty
            )!;
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(GroupHeader(view, group));
            ((IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)).Collapse();
            Assert.False(model.Expanded);
            Assert.Same(
                expression,
                BindingOperations.GetBindingExpression(group, TabGroup.IsExpandedProperty)
            );
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupAutomation_RejectsReadOnlyExpansion(bool frozen) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            TabGroup group = view.TabGroups!.First();
            if (frozen)
                group.Freeze();
            else
                BindingOperations.SetBinding(
                    group,
                    TabGroup.IsExpandedProperty,
                    new Binding(nameof(ExpansionModel.Expanded))
                    {
                        Source = new ExpansionModel(),
                        Mode = BindingMode.OneWay,
                    }
                );
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(GroupHeader(view, group));
            Assert.Throws<InvalidOperationException>(() =>
                ((IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)).Collapse()
            );
            Assert.True(group.IsExpanded);
        });

    [Fact]
    public void GroupAutomation_RejectsRemovedHeaderAndUsesReplacementTemplate() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            TabGroup group = view.TabGroups!.First();
            System.Windows.Controls.Button old = GroupHeader(view, group);
            AutomationPeer oldPeer = UIElementAutomationPeer.CreatePeerForElement(old);
            view.OnApplyTemplate();
            Layout(view);
            System.Windows.Controls.Button current = GroupHeader(view, group);
            Assert.NotSame(old, current);
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(current);
            ((IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)).Collapse();
            Assert.False(group.IsExpanded);
            ((ObservableCollection<TabGroup>)view.TabGroups!).Remove(group);
            Assert.Throws<InvalidOperationException>(() =>
                ((IExpandCollapseProvider)oldPeer.GetPattern(PatternInterface.ExpandCollapse)).Expand()
            );
        });

    [Theory]
    [InlineData("collapse")]
    [InlineData("group-remove")]
    [InlineData("item-remove")]
    [InlineData("source-swap")]
    [InlineData("template")]
    public void GroupKeyboard_RecoversFocusAfterHeaderChanges(string change) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            Window window = new()
            {
                Content = view,
                Width = 800,
                Height = 400,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            try
            {
                window.Show();
                PumpHeaderDispatcher();
                TabGroup group = view.TabGroups!.First();
                FrameworkElement original =
                    change == "group-remove" || change == "template"
                        ? GroupHeader(view, group)
                        : Container(view, source[0]);
                Assert.True(original.Focus());
                switch (change)
                {
                    case "collapse":
                        group.IsExpanded = false;
                        break;
                    case "group-remove":
                        ((ObservableCollection<TabGroup>)view.TabGroups!).Remove(group);
                        break;
                    case "item-remove":
                        source.RemoveAt(0);
                        break;
                    case "source-swap":
                        view.ItemsSource = WholeGroupDocuments();
                        break;
                    case "template":
                        view.OnApplyTemplate();
                        break;
                }
                view.UpdateLayout();
                PumpHeaderDispatcher();
                Assert.Contains(Keyboard.FocusedElement, KeyboardEntries(view));
                if (change == "collapse")
                    Assert.Same(GroupHeader(view, group), Keyboard.FocusedElement);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void GroupKeyboard_NaturalTabTraversalReachesHeadersAndPreservesSelection() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            TextBox before = new();
            TextBox after = new();
            StackPanel root = new();
            root.Children.Add(before);
            root.Children.Add(view);
            root.Children.Add(after);
            WithHeaderWindow(
                root,
                () =>
                {
                    before.Focus();
                    HashSet<object> reached = new();
                    for (int i = 0; i < 30 && Keyboard.FocusedElement != after; i++)
                    {
                        ((UIElement)Keyboard.FocusedElement).MoveFocus(
                            new TraversalRequest(FocusNavigationDirection.Next)
                        );
                        reached.Add(Keyboard.FocusedElement);
                    }
                    Assert.All(view.TabGroups!, group => Assert.Contains(GroupHeader(view, group), reached));
                    Assert.Same(after, Keyboard.FocusedElement);
                    view.SelectedItem = source[3];
                    System.Windows.Controls.Button header = GroupHeader(view, view.TabGroups!.First());
                    AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(header);
                    peer.SetFocus();
                    Assert.True(peer.HasKeyboardFocus());
                    Assert.True(peer.IsKeyboardFocusable());
                    Assert.Same(source[3], view.SelectedItem);
                    Assert.True(header.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)));
                }
            );
        });

    [Fact]
    public void GroupKeyboard_ArrowKeyMovesActualFocusToHeaderWithoutChangingSelection() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            WithHeaderWindow(
                view,
                () =>
                {
                    TabViewItem tab = Container(view, source[0]);
                    Assert.True(tab.Focus());
                    KeyEventArgs args = HeaderKey(Key.Left, tab);
                    Invoke(view, "OnPreviewKeyDown", args);
                    Assert.True(args.Handled);
                    Assert.Same(GroupHeader(view, view.TabGroups!.First()), Keyboard.FocusedElement);
                    Assert.Same(source[0], view.SelectedItem);
                }
            );
        });

    [Fact]
    public void GroupKeyboard_DoesNotStealFocusFromAnotherControlAfterChanges() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            TextBox outside = new();
            StackPanel root = new();
            root.Children.Add(view);
            root.Children.Add(outside);
            WithHeaderWindow(
                root,
                () =>
                {
                    TabGroup group = view.TabGroups!.First();
                    GroupHeader(view, group).Focus();
                    outside.Focus();
                    ((ObservableCollection<TabGroup>)view.TabGroups).Remove(group);
                    PumpHeaderDispatcher();
                    Assert.Same(outside, Keyboard.FocusedElement);
                }
            );
        });

    [Fact]
    public void GroupKeyboard_ReorderingKeepsHeaderFocusAndNewProjectedNavigation() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            WithHeaderWindow(
                view,
                () =>
                {
                    TabGroup moved = view.TabGroups!.Last();
                    System.Windows.Controls.Button header = GroupHeader(view, moved);
                    header.Focus();
                    Invoke(
                        view,
                        "OnDrop",
                        WholeGroupDrop(view, moved, GroupHeader(view, view.TabGroups.First()), false)
                    );
                    view.UpdateLayout();
                    PumpHeaderDispatcher();
                    Assert.Same(header, Keyboard.FocusedElement);
                    Assert.Same(header, KeyboardEntries(view)[0]);
                    Invoke(view, "OnPreviewKeyDown", HeaderKey(Key.Right, header));
                    Assert.Same(
                        Container(view, source.Single(item => item.Header == "B1")),
                        Keyboard.FocusedElement
                    );
                }
            );
        });

    [Fact]
    public void GroupKeyboard_ReloadRecreatesAutomationAndNavigation() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            AutomationPeer owner = UIElementAutomationPeer.CreatePeerForElement(view);
            _ = owner.GetChildren();
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Layout(view);
            PumpHeaderDispatcher();
            Assert.Equal(8, KeyboardEntries(view).Length);
            Assert.Equal(
                2,
                owner.GetChildren().Count(child => child.GetPattern(PatternInterface.ExpandCollapse) != null)
            );
        });

    private static void WithHeaderWindow(UIElement content, Action action)
    {
        Window window = new()
        {
            Content = content,
            Width = 800,
            Height = 600,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        try
        {
            window.Show();
            PumpHeaderDispatcher();
            action();
        }
        finally
        {
            window.Close();
        }
    }

    private static FrameworkElement[] KeyboardEntries(TabView view) =>
        MenuResult<List<FrameworkElement>>(view, "GroupKeyboardEntries").ToArray();

    private static string EntryName(FrameworkElement element) =>
        element is TabItem tab
            ? tab.Header.ToString()!
            : System.Windows.Automation.AutomationProperties.GetName(element);

    private static KeyEventArgs HeaderKey(Key key, object source) =>
        new(Keyboard.PrimaryDevice, new MenuKeySource(), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
            Source = source,
        };

    private static void PumpHeaderDispatcher()
    {
        DispatcherFrame frame = new();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false)
        );
        Dispatcher.PushFrame(frame);
    }

    private sealed class ExpansionModel
    {
        public bool Expanded { get; set; } = true;
    }
}
