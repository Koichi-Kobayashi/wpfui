// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Theory]
    [InlineData("a", false)]
    [InlineData("b", false)]
    [InlineData("a", true)]
    [InlineData("b", true)]
    public void GroupAdding_MenuTargetsGroupEndRegardlessOfSelection(string id, bool collapsed) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            view.CanAddTabs = true;
            TabGroup group = view.TabGroups!.Single(definition => definition.Id == id);
            view.SelectedItem = source.First(item => item.GroupId != id);
            group.IsExpanded = !collapsed;
            GroupDocument last = source.Last(item => item.GroupId == id);
            GroupDocument added = new() { Header = "Added" };
            int events = 0;
            view.TabAdding += (_, args) =>
            {
                events++;
                Assert.Equal(id, args.TargetGroupId);
                added.GroupId = args.TargetGroupId;
                args.Item = added;
            };
            GroupAddMenuItem(view, group).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(1, events);
            Assert.Same(added, source[source.IndexOf(last) + 1]);
            Assert.Same(added, view.SelectedItem);
            Assert.True(group.IsExpanded);
            Layout(view);
            FrameworkElement[] entries = KeyboardEntries(view);
            Assert.Same(Container(view, added), entries[Array.IndexOf(entries, Container(view, last)) + 1]);
            Assert.NotNull(
                BindingOperations.GetBindingExpression(Container(view, added), TabViewItem.GroupIdProperty)
            );
        });

    [Fact]
    public void GroupAdding_MenuSupportsOneWayPreparedModelsAndHeaderFocus() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            view.CanAddTabs = true;
            WithHeaderWindow(
                view,
                () =>
                {
                    TabGroup target = view.TabGroups!.Last();
                    view.SelectedItem = source[0];
                    GroupHeader(view, view.TabGroups.First()).Focus();
                    GroupDocument added = new() { Header = "Added" };
                    view.TabAdding += (_, args) =>
                    {
                        added.GroupId = args.TargetGroupId;
                        args.Item = added;
                    };
                    GroupAddMenuItem(view, target).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert.Equal("b", added.GroupId);
                    Assert.Same(added, view.SelectedItem);
                    Assert.Equal(
                        BindingMode.OneWay,
                        BindingOperations
                            .GetBinding(Container(view, added), TabViewItem.GroupIdProperty)!
                            .Mode
                    );
                }
            );
        });

    [Fact]
    public void GroupAdding_MenuCancellationPreservesSelectionExpansionAndContent() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            view.CanAddTabs = true;
            TabGroup group = view.TabGroups!.Last();
            group.IsExpanded = false;
            object selected = view.SelectedItem;
            object content = view.SelectedContent;
            int events = 0;
            view.TabAdding += (_, args) =>
            {
                events++;
                args.Cancel = true;
            };
            GroupAddMenuItem(view, group).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(1, events);
            Assert.Equal(6, view.Items.Count);
            Assert.Same(selected, view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
            Assert.False(group.IsExpanded);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupAdding_MenuDoesNotRepeatHandlerOwnedAddition(bool cancel) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            view.CanAddTabs = true;
            GroupDocument added = new() { Header = "Handled" };
            int events = 0;
            view.TabAdding += (_, args) =>
            {
                events++;
                added.GroupId = args.TargetGroupId;
                source.Add(added);
                view.SelectedItem = added;
                args.Item = added;
                args.Cancel = cancel;
                view.AddTab();
            };
            GroupAddMenuItem(view, view.TabGroups!.Last())
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(1, events);
            Assert.Equal(7, source.Count);
            Assert.Same(added, view.SelectedItem);
        });

    [Theory]
    [InlineData("before-click")]
    [InlineData("event")]
    [InlineData("insert")]
    [InlineData("rename-id")]
    [InlineData("selection")]
    public void GroupAdding_DeletedOrChangedTargetRejectsRequest(string when) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            view.CanAddTabs = true;
            TabGroup group = view.TabGroups!.Last();
            object selected = view.SelectedItem;
            MenuItem menuItem = GroupAddMenuItem(view, group);
            int events = 0;
            TabViewItemAddingEventArgs? captured = null;
            view.TabAdding += (_, args) =>
            {
                events++;
                captured = args;
                args.Item = new GroupDocument { Header = "Added", GroupId = args.TargetGroupId };
                if (when == "event")
                    ((ObservableCollection<TabGroup>)view.TabGroups).Remove(group);
                if (when == "rename-id")
                    group.Id = "changed";
            };
            if (when == "before-click")
                ((ObservableCollection<TabGroup>)view.TabGroups).Remove(group);
            if (when == "insert")
                source.CollectionChanged += (_, args) =>
                {
                    if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                        ((ObservableCollection<TabGroup>)view.TabGroups).Remove(group);
                };
            if (when == "selection")
            {
                view.SelectionChanged += (_, _) =>
                {
                    if (view.SelectedItem is GroupDocument { Header: "Added" })
                    {
                        ((ObservableCollection<TabGroup>)view.TabGroups).Remove(group);
                    }
                };
            }
            menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(when == "before-click" ? 0 : 1, events);
            Assert.Equal(6, source.Count);
            Assert.Same(selected, view.SelectedItem);
            if (captured != null)
                Assert.Equal("b", captured.TargetGroupId);
        });

    [Fact]
    public void GroupAdding_ReadOnlySourceDisablesGroupMenuOperation() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(new ReadOnlyObservableCollection<GroupDocument>(source));
            view.CanAddTabs = true;
            MenuItem add = GroupAddMenuItem(view, view.TabGroups!.Last());
            Assert.False(add.IsEnabled);
            int events = 0;
            view.TabAdding += (_, _) => events++;
            Invoke(view, "AddTabToGroup", view.TabGroups.Last());
            Assert.Equal(0, events);
            Assert.Equal(6, source.Count);
        });

    [Theory]
    [InlineData("A1", "a")]
    [InlineData("B1", "b")]
    [InlineData("Home", null)]
    [InlineData("Notes", null)]
    [InlineData(null, null)]
    public void GroupAdding_PlusExposesSelectedTargetAndPublicAddAlwaysExposesNull(
        string? header,
        string? id
    ) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            view.SelectedItem = header == null ? null : source.Single(item => item.Header == header);
            string? captured = "not called";
            view.TabAdding += (_, args) =>
            {
                captured = args.TargetGroupId;
                args.Item = new GroupDocument { Header = "Added", GroupId = args.TargetGroupId };
            };
            ClickGroupAdd(view);
            Assert.Equal(id, captured);
            view.AddTab();
            Assert.Null(captured);
            Assert.Equal(8, source.Count);
            Assert.Same(source.Last(), view.SelectedItem);
        });

    [Fact]
    public void GroupAdding_EventArgsRemainConstructibleAndTargetIsReadOnly() =>
        RunSta(() =>
        {
            TabViewItemAddingEventArgs args = new();
            Assert.Null(args.TargetGroupId);
            Assert.Null(
                typeof(TabViewItemAddingEventArgs).GetProperty(nameof(args.TargetGroupId))!.SetMethod
            );
            args.Item = new object();
            args.TabItem = new TabViewItem();
            args.Cancel = true;
            Assert.NotNull(args.Item);
            Assert.NotNull(args.TabItem);
            Assert.True(args.Cancel);
        });

    private static MenuItem GroupAddMenuItem(TabView view, TabGroup group)
    {
        ContextMenu menu = MenuResult<ContextMenu>(view, "BuildGroupMenu", GroupHeader(view, group));
        Assert.Contains(
            menu.Items.Cast<MenuItem>(),
            item => Equals(item.Header, "Edit group name and color…")
        );
        Assert.Contains(menu.Items.Cast<MenuItem>(), item => Equals(item.Header, "Ungroup tabs"));
        return menu.Items.Cast<MenuItem>().Single(item => Equals(item.Header, "Add new tab to this group"));
    }
}
