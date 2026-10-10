// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Fact]
    public void StateApi_CreateAndAssignPreserveOrderSelectionAndBindings() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            object[] order = source.Cast<object>().ToArray();
            view.SelectedItem = source[1];
            BindingExpression binding = BindingOperations.GetBindingExpression(Container(view, source[0]), TabViewItem.GroupIdProperty)!;
            Assert.True(view.TryCreateTabGroup(source[0], out TabGroup? group));
            Assert.NotNull(group);
            Assert.False(string.IsNullOrEmpty(group.Id));
            Assert.Contains(group, view.TabGroups!);
            Assert.Equal(group.Id, source[0].GroupId);
            Assert.True(view.TryAssignTabToGroup(source[2], group.Id));
            Assert.Equal(group.Id, source[2].GroupId);
            Assert.True(view.TryAssignTabToGroup(source[2], null));
            Assert.Null(source[2].GroupId);
            Assert.Equal(order, source.Cast<object>());
            Assert.Same(source[1], view.SelectedItem);
            Assert.Same(binding, BindingOperations.GetBindingExpression(Container(view, source[0]), TabViewItem.GroupIdProperty));
        });

    [Fact]
    public void StateApi_RemoveKeepsTabsAndEditExpandKeepSelectedContent() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            object[] order = source.Cast<object>().ToArray();
            view.SelectedItem = source[0];
            object content = view.SelectedContent;
            Assert.True(view.TryEditTabGroup(group, "Work", Colors.Coral));
            Assert.Equal("Work", group.Header);
            Assert.Equal(Colors.Coral, group.Color);
            Assert.True(view.TrySetGroupExpanded(group, false));
            Assert.False(group.IsExpanded);
            Assert.Same(source[0], view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
            Assert.True(view.TrySetGroupExpanded(group, true));
            Assert.True(view.TryRemoveTabGroup(group));
            Assert.DoesNotContain(group, view.TabGroups!);
            Assert.Null(source[0].GroupId);
            Assert.Equal(order, source.Cast<object>());
        });

    [Fact]
    public void StateApi_RejectsUnsupportedBindingsForeignGroupsAndUnknownItems() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            int count = view.TabGroups!.Count();
            Assert.False(view.TryCreateTabGroup(source[0], out TabGroup? rejected));
            Assert.Null(rejected);
            Assert.False(view.TryAssignTabToGroup(source[0], "b"));
            Assert.False(view.TryRemoveTabGroup(view.TabGroups!.First()));
            Assert.False(view.TryAssignTabToGroup(new object(), "a"));
            Assert.False(view.TryCreateTabGroup(new object(), out _));
            TabGroup foreign = new() { Id = "a" };
            Assert.False(view.TryEditTabGroup(foreign, "Other", Colors.Red));
            Assert.False(view.TrySetGroupExpanded(foreign, false));
            Assert.False(view.TryRemoveTabGroup(foreign));
            TabGroup frozen = view.TabGroups!.Last();
            frozen.Freeze();
            Assert.False(view.TryEditTabGroup(frozen, "Frozen", Colors.Red));
            Assert.False(view.TrySetGroupExpanded(frozen, false));
            Assert.Equal(count, view.TabGroups!.Count());
            Assert.Equal("a", source[0].GroupId);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StateApi_DefinitionNotificationFailureRestoresOriginalState(bool remove) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            MenuDefinitions definitions = new(view.TabGroups!);
            view.TabGroups = definitions;
            if (remove)
            {
                definitions.RejectRemove = true;
                Assert.False(view.TryRemoveTabGroup(definitions[0]));
            }
            else
            {
                definitions.RejectAdd = true;
                Assert.False(view.TryCreateTabGroup(source[0], out TabGroup? group));
                Assert.Null(group);
            }
            Assert.Equal(2, definitions.Count);
            Assert.Equal("a", source[0].GroupId);
        });

    [Fact]
    public void StateApi_MenuFactoriesAndOldMutationNamesAreNotPublic()
    {
        string[] names = { "CreateTabGroupMenu", "CreateGroupMenu", "CreateTabGroup", "SetTabGroup", "EditTabGroup", "RemoveTabGroup", "SetGroupExpanded" };
        foreach (string name in names)
        {
            Assert.Null(typeof(TabView).GetMethod(name, BindingFlags.Public | BindingFlags.Instance));
        }
    }
}
