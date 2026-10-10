// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.ObjectModel;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveApi_PreflightDoesNotMutateAndExecutionNeedsNoMouseDrag(bool collapsed) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            view.CanReorderTabs = false;
            TabGroup destination = view.TabGroups!.Last();
            destination.IsExpanded = !collapsed;
            GroupDocument tab = source[0];
            object[] order = source.Cast<object>().ToArray();
            view.SelectedItem = tab;
            object content = view.SelectedContent;
            BindingExpression binding = BindingOperations.GetBindingExpression(Container(view, tab), TabViewItem.GroupIdProperty)!;
            Assert.True(view.CanMoveTabToGroup(tab, destination.Id));
            Assert.Equal(order, source.Cast<object>());
            Assert.Equal("a", tab.GroupId);
            Assert.True(view.TryMoveTabToGroup(tab, destination.Id));
            Assert.Same(tab, source.Last());
            Assert.Equal(destination.Id, tab.GroupId);
            Assert.Equal(!collapsed, destination.IsExpanded);
            Assert.Same(tab, view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
            Assert.Same(binding, BindingOperations.GetBindingExpression(Container(view, tab), TabViewItem.GroupIdProperty));
            Assert.True(view.TryMoveTabToGroup(tab, null));
            Assert.Null(tab.GroupId);
        });

    [Fact]
    public void MoveApi_OneWayMembershipAllowsOnlySameGroupReordering() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = new()
            {
                new() { Header = "First", GroupId = "a" },
                new() { Header = "Second", GroupId = "a" },
                new() { Header = "Other", GroupId = "b" },
            };
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            GroupDocument tab = source[0];
            GroupDocument anchor = source[1];
            Assert.False(view.CanMoveTabToGroup(tab, "b"));
            Assert.False(view.TryMoveTabToGroup(tab, "b"));
            Assert.True(view.CanMoveTabToGroup(tab, "a", anchor));
            Assert.True(view.TryMoveTabToGroup(tab, "a", anchor));
            Assert.Same(tab, source[1]);
            Assert.Equal("a", tab.GroupId);
        });

    [Fact]
    public void MoveApi_RejectsUnknownItemsGroupsAnchorsAndForeignDefinitions() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            object[] before = source.Cast<object>().ToArray();
            TabGroup group = view.TabGroups!.First();
            TabGroup foreign = new() { Id = "b" };
            Assert.False(view.CanMoveTabToGroup(new object(), "a"));
            Assert.False(view.TryMoveTabToGroup(new object(), "a"));
            Assert.False(view.CanMoveTabToGroup(source[0], "missing"));
            Assert.False(view.TryMoveTabToGroup(source[0], "missing"));
            Assert.False(view.CanMoveTabToGroup(source[0], "b", new object()));
            Assert.False(view.TryMoveTabToGroup(source[0], "b", new object()));
            Assert.False(view.CanMoveTabGroup(foreign, group));
            Assert.False(view.TryMoveTabGroup(foreign, group));
            Assert.False(view.CanMoveTabGroup(group, foreign));
            Assert.False(view.TryMoveTabGroup(group, foreign));
            Assert.Equal(before, source.Cast<object>());
        });

    [Fact]
    public void MoveApi_GroupMovePreservesScatteredMembersMembershipsAndSelection() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = new()
            {
                new() { Header = "A1", GroupId = "a" },
                new() { Header = "Home" },
                new() { Header = "B1", GroupId = "b" },
                new() { Header = "A2", GroupId = "a" },
                new() { Header = "B2", GroupId = "b" },
            };
            TabView view = CreateGroupDragView(source);
            view.CanReorderTabs = false;
            TabGroup group = view.TabGroups!.First();
            TabGroup target = view.TabGroups!.Last();
            object[] before = source.Cast<object>().ToArray();
            GroupDocument selected = source[3];
            view.SelectedItem = selected;
            object content = view.SelectedContent;
            Assert.True(view.CanMoveTabGroup(group, target, after: true));
            Assert.Equal(before, source.Cast<object>());
            Assert.True(view.TryMoveTabGroup(group, target, after: true));
            Assert.True(source.IndexOf(source.First(t => t.GroupId == "a")) > source.IndexOf(source.First(t => t.GroupId == "b")));
            Assert.Equal(new[] { "A1", "A2" }, source.Where(t => t.GroupId == "a").Select(t => t.Header));
            Assert.Equal(new[] { "B1", "B2" }, source.Where(t => t.GroupId == "b").Select(t => t.Header));
            Assert.Same(selected, view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
        });

    [Fact]
    public void MoveApi_CollectionRejectionRestoresMembershipAndAllowsRetry() =>
        RunSta(() =>
        {
            ThrowingGroupCollection source = new();
            foreach (GroupDocument document in DragDocuments())
            {
                source.Add(document);
            }

            TabView view = CreateGroupDragView(source);
            GroupDocument tab = source[0];
            object[] before = source.Cast<object>().ToArray();
            view.SelectedItem = tab;
            Assert.True(view.CanMoveTabToGroup(tab, "b"));
            Assert.False(view.TryMoveTabToGroup(tab, "b"));
            Assert.Equal(before, source.Cast<object>());
            Assert.Equal("a", tab.GroupId);
            Assert.Same(tab, view.SelectedItem);
            source.ThrowOnMove = false;
            Assert.True(view.TryMoveTabToGroup(tab, "b"));
            Assert.Equal("b", tab.GroupId);
        });
}
