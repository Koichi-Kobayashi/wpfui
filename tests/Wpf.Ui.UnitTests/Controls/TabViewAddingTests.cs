// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Theory]
    [InlineData("A1", "a")]
    [InlineData("A2", "a")]
    [InlineData("B1", "b")]
    [InlineData("B2", "b")]
    [InlineData("Home", null)]
    [InlineData("Notes", null)]
    public void GroupAdding_ButtonInsertsAfterSelectedModelInProjectedOrder(string header, string? groupId) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument selected = source.Single(item => item.Header == header);
            view.SelectedItem = selected;
            GroupDocument added = new() { Header = "Added" };
            int events = 0;
            view.TabAdding += (_, args) =>
            {
                events++;
                args.Item = added;
            };
            int index = source.IndexOf(selected);
            ClickGroupAdd(view);
            Assert.Equal(1, events);
            Assert.Same(added, source[index + 1]);
            Assert.Equal(groupId, added.GroupId);
            Assert.Same(added, view.SelectedItem);
            Layout(view);
            FrameworkElement[] entries = KeyboardEntries(view);
            Assert.Same(
                Container(view, added),
                entries[Array.IndexOf(entries, Container(view, selected)) + 1]
            );
            Assert.NotNull(
                BindingOperations.GetBindingExpression(Container(view, added), TabViewItem.GroupIdProperty)
            );
        });

    [Fact]
    public void GroupAdding_NoSelectionAppendsUngroupedModel() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            view.SelectedIndex = -1;
            GroupDocument added = new() { Header = "Added", GroupId = "a" };
            view.TabAdding += (_, args) => args.Item = added;
            ClickGroupAdd(view);
            Assert.Same(added, source.Last());
            Assert.Null(added.GroupId);
            Assert.Same(added, view.SelectedItem);
        });

    [Fact]
    public void GroupAdding_OwnContainersUseSelectedGroupAndPreservePublicAppendApi() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            view.TabGroups = new[]
            {
                new TabGroup { Id = "a", Header = "A" },
            };
            TabViewItem first = new() { Header = "First", GroupId = "a" };
            TabViewItem last = new() { Header = "Last" };
            view.Items.Add(first);
            view.Items.Add(last);
            Layout(view);
            view.SelectedItem = first;
            ClickGroupAdd(view);
            TabViewItem added = Assert.IsType<TabViewItem>(view.Items[1]);
            Assert.Equal("a", added.GroupId);
            view.SelectedItem = first;
            view.AddTab("content", "Public append");
            TabViewItem appended = Assert.IsType<TabViewItem>(view.Items[view.Items.Count - 1]);
            Assert.Equal("Public append", appended.Header);
            Assert.Equal("content", appended.Content);
            Assert.Null(appended.GroupId);
        });

    [Fact]
    public void GroupAdding_CollapsedDestinationExpandsAfterSuccessfulInsert() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            view.SelectedItem = source[0];
            group.IsExpanded = false;
            GroupDocument added = new() { Header = "Added", GroupId = "a" };
            view.TabAdding += (_, args) => args.Item = added;
            source.CollectionChanged += (_, _) => Assert.False(group.IsExpanded);
            ClickGroupAdd(view);
            Assert.True(group.IsExpanded);
            Assert.Same(added, view.SelectedItem);
            Assert.Equal(Visibility.Visible, Container(view, added).Visibility);
        });

    [Fact]
    public void GroupAdding_HeaderFocusUsesSelectedItem() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            WithHeaderWindow(
                view,
                () =>
                {
                    GroupDocument selected = source.Single(item => item.Header == "Notes");
                    view.SelectedItem = selected;
                    GroupHeader(view, view.TabGroups!.First()).Focus();
                    GroupDocument added = new() { Header = "Added" };
                    view.TabAdding += (_, args) => args.Item = added;
                    ClickGroupAdd(view);
                    Assert.Same(added, source[source.IndexOf(selected) + 1]);
                    Assert.Null(added.GroupId);
                }
            );
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupAdding_HandlerOwnedAdditionIsNotRepeated(bool cancel) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument added = new() { Header = "Handler added" };
            int events = 0;
            view.TabAdding += (_, args) =>
            {
                events++;
                source.Add(added);
                view.SelectedItem = added;
                args.Item = added;
                args.Cancel = cancel;
            };
            ClickGroupAdd(view);
            Assert.Equal(1, events);
            Assert.Equal(7, source.Count);
            Assert.Single(source, item => ReferenceEquals(item, added));
            Assert.Same(added, view.SelectedItem);
        });

    [Theory]
    [InlineData("readonly")]
    [InlineData("sorted")]
    [InlineData("filtered")]
    public void GroupAdding_RejectsUnsupportedSources(string kind) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            object items =
                kind == "readonly" ? new ReadOnlyObservableCollection<GroupDocument>(source) : source;
            TabView view = CreateGroupDragView(items);
            if (kind == "sorted")
            {
                view.Items.SortDescriptions.Add(
                    new System.ComponentModel.SortDescription(
                        "Header",
                        System.ComponentModel.ListSortDirection.Ascending
                    )
                );
            }
            if (kind == "filtered")
            {
                view.Items.Filter = _ => true;
            }
            GroupDocument selected = source[0];
            view.SelectedItem = selected;
            view.TabAdding += (_, args) => args.Item = new GroupDocument { Header = "Added", GroupId = "a" };
            ClickGroupAdd(view);
            Assert.Equal(6, source.Count);
            Assert.Same(selected, view.SelectedItem);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupAdding_OneWayMembershipAllowsPreparedModelAndRejectsMismatch(bool prepared) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            view.SelectedItem = source[0];
            GroupDocument added = new() { Header = "Added", GroupId = prepared ? "a" : null };
            view.TabAdding += (_, args) => args.Item = added;
            ClickGroupAdd(view);
            Assert.Equal(prepared ? 7 : 6, source.Count);
            Assert.Same(prepared ? added : source[0], view.SelectedItem);
        });

    [Fact]
    public void GroupAdding_ReadOnlyCollapsedExpansionLeavesSourceUnchanged() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            group.IsExpanded = false;
            group.Freeze();
            view.SelectedItem = source[0];
            view.TabAdding += (_, args) => args.Item = new GroupDocument { Header = "Added", GroupId = "a" };
            ClickGroupAdd(view);
            Assert.Equal(6, source.Count);
            Assert.False(group.IsExpanded);
        });

    [Fact]
    public void GroupAdding_UngroupedButtonAndGroupedPublicApiStillAppend() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            view.SelectedItem = source[0];
            GroupDocument added = new() { Header = "Added", GroupId = "b" };
            view.TabAdding += (_, args) => args.Item = added;
            view.AddTab();
            Assert.Same(added, source.Last());
            Assert.Equal("b", added.GroupId);
            view.TabGroups = null;
            GroupDocument ungrouped = new() { Header = "Ungrouped" };
            view.TabAdding += (_, args) => args.Item = ungrouped;
            view.SelectedItem = source[0];
            ClickGroupAdd(view);
            Assert.Same(ungrouped, source.Last());
        });

    [Fact]
    public void GroupAdding_CanceledRequestDoesNotExpandOrInsert() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            group.IsExpanded = false;
            view.SelectedItem = source[0];
            view.TabAdding += (_, args) => args.Cancel = true;
            ClickGroupAdd(view);
            Assert.Equal(6, source.Count);
            Assert.False(group.IsExpanded);
            Assert.Same(source[0], view.SelectedItem);
        });

    [Fact]
    public void GroupAdding_ThrowingObserverRestoresInsertionAndAllowsNextRequest() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument selected = source[0];
            view.SelectedItem = selected;
            GroupDocument added = new() { Header = "Added", GroupId = "a" };
            view.TabAdding += (_, args) => args.Item = added;
            bool reject = true;
            source.CollectionChanged += (_, args) =>
            {
                if (reject && args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    throw new InvalidOperationException("Observer rejected insertion.");
                }
            };
            ClickGroupAdd(view);
            Assert.Equal(6, source.Count);
            Assert.Same(selected, view.SelectedItem);
            reject = false;
            ClickGroupAdd(view);
            Assert.Equal(7, source.Count);
            Assert.Same(added, view.SelectedItem);
        });

    [Fact]
    public void GroupAdding_HandlerReplacementWithSameCountIsNotFollowedByAnotherInsertion() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument replacement = new() { Header = "Replacement" };
            view.TabAdding += (_, args) =>
            {
                source[source.Count - 1] = replacement;
                args.Item = new GroupDocument { Header = "Must not be added" };
            };
            ClickGroupAdd(view);
            Assert.Equal(6, source.Count);
            Assert.Same(replacement, source.Last());
        });

    private static void ClickGroupAdd(TabView view)
    {
        view.CanAddTabs = true;
        ((System.Windows.Controls.Button)view.Template.FindName("PART_AddButton", view)).RaiseEvent(
            new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)
        );
    }
}
