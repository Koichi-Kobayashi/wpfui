// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    private static ObservableCollection<GroupDocument> WholeGroupDocuments() =>
        new()
        {
            new() { Header = "A1", GroupId = "a" },
            new() { Header = "Home" },
            new() { Header = "B1", GroupId = "b" },
            new() { Header = "A2", GroupId = "a" },
            new() { Header = "Notes" },
            new() { Header = "B2", GroupId = "b" },
        };

    [Theory]
    [InlineData("b", false, "Home,A1,A2,B1,Notes,B2")]
    [InlineData("b", true, "Home,B1,A1,A2,Notes,B2")]
    [InlineData("Home", false, "A1,Home,B1,A2,Notes,B2")]
    [InlineData("Home", true, "Home,A1,A2,B1,Notes,B2")]
    [InlineData("Notes", false, "Home,B1,A1,A2,Notes,B2")]
    [InlineData("Notes", true, "Home,B1,Notes,A1,A2,B2")]
    [InlineData("end", true, "Home,B1,Notes,B2,A1,A2")]
    public void WholeGroupDrag_MovesScatteredMembersUsingDataAnchors(
        string target,
        bool after,
        string expected
    ) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            FrameworkElement destination =
                target == "b" ? GroupHeader(view, view.TabGroups!.Last())
                : target == "end" ? (FrameworkElement)view.Template.FindName("HeaderPanel", view)
                : Container(view, source.Single(item => item.Header == target));
            int moves = 0;
            source.CollectionChanged += (_, e) =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Move, e.Action);
                moves++;
            };
            GroupDocument selected = source[3];
            view.SelectedItem = selected;
            object content = view.SelectedContent;
            TabViewItem selectedTab = Container(view, selected);
            BindingExpression binding = BindingOperations.GetBindingExpression(
                selectedTab,
                TabViewItem.GroupIdProperty
            )!;
            DragEventArgs drop = WholeGroupDrop(view, group, destination, after, target == "end");
            Invoke(view, "OnDragOver", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal(expected, string.Join(",", source.Select(item => item.Header)));
            Assert.Equal(
                new[] { "A1", "A2" },
                source.Where(item => item.GroupId == "a").Select(item => item.Header)
            );
            Assert.InRange(moves, 0, 2);
            Assert.Same(selected, view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
            Assert.Same(
                binding,
                BindingOperations.GetBindingExpression(selectedTab, TabViewItem.GroupIdProperty)
            );
            AssertWholeGroupDragCleared(view);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeGroupDrag_MovesSecondGroupToStartAndKeepsCollapsedState(bool collapsed) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            TabGroup group = view.TabGroups!.Last();
            group.IsExpanded = !collapsed;
            Layout(view);
            GroupDocument[] before = source.ToArray();
            DragEventArgs drop = WholeGroupDrop(
                view,
                group,
                GroupHeader(view, view.TabGroups!.First()),
                false
            );
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal("B1,B2,A1,Home,A2,Notes", string.Join(",", source.Select(item => item.Header)));
            Assert.Equal(!collapsed, group.IsExpanded);
            Assert.Equal(new[] { "a", null, "b", "a", null, "b" }, before.Select(item => item.GroupId));
            Layout(view);
            Assert.True(
                X(GroupHeader(view, group), view) < X(GroupHeader(view, view.TabGroups!.First()), view)
            );
        });

    [Theory]
    [InlineData("self")]
    [InlineData("adjacent")]
    public void WholeGroupDrag_SameVisualPositionDoesNotNormalizeOrNotify(string target) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            GroupDocument[] before = source.ToArray();
            TabView view = CreateGroupDragView(source);
            int moves = 0;
            source.CollectionChanged += (_, _) => moves++;
            TabGroup group = view.TabGroups!.First();
            FrameworkElement destination =
                target == "self" ? GroupHeader(view, group) : Container(view, source[1]);
            Invoke(view, "OnDrop", WholeGroupDrop(view, group, destination, false));
            Assert.Equal(before, source);
            Assert.Equal(0, moves);
            AssertWholeGroupDragCleared(view);
        });

    [Fact]
    public void WholeGroupDrag_FrozenAndReadOnlyDefinitionsAllowMove() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            TabGroup group = view.TabGroups!.Last();
            group.Freeze();
            view.TabGroups = new ReadOnlyCollection<TabGroup>(view.TabGroups!.ToList());
            Layout(view);
            Assert.True(GroupHeader(view, group).IsEnabled);
            Invoke(
                view,
                "OnDrop",
                WholeGroupDrop(view, group, GroupHeader(view, view.TabGroups.First()), false)
            );
            Assert.Same(group, view.TabGroups.Last());
            Assert.Equal("B1", source[0].Header);
            Assert.True(group.IsFrozen);
        });

    [Theory]
    [InlineData("readonly")]
    [InlineData("sort")]
    [InlineData("filter")]
    [InlineData("group")]
    public void WholeGroupDrag_UnsafeSourcesAreRejected(string kind) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            DragEventArgs drop = WholeGroupDrop(view, group, Container(view, source[4]), true);
            if (kind == "readonly")
                view.ItemsSource = new ReadOnlyCollection<GroupDocument>(source);
            else if (kind == "sort")
                view.Items.SortDescriptions.Add(
                    new("Header", System.ComponentModel.ListSortDirection.Ascending)
                );
            else if (kind == "filter")
                view.Items.Filter = _ => true;
            else
                view.Items.GroupDescriptions.Add(new PropertyGroupDescription("GroupId"));
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal("A1,Home,B1,A2,Notes,B2", string.Join(",", source.Select(item => item.Header)));
            AssertWholeGroupDragCleared(view);
        });

    [Theory]
    [InlineData("escape")]
    [InlineData("template")]
    [InlineData("source")]
    [InlineData("definition")]
    public void WholeGroupDrag_LifecycleCancelsStalePayload(string action) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            GroupDocument[] before = source.ToArray();
            TabView view = CreateGroupDragView(source);
            DragEventArgs drop = WholeGroupDrop(
                view,
                view.TabGroups!.First(),
                Container(view, source[4]),
                true
            );
            Invoke(view, "OnDragOver", drop);
            if (action == "escape")
                Invoke(
                    view,
                    "OnQueryContinueDrag",
                    (QueryContinueDragEventArgs)
                        Activator.CreateInstance(
                            typeof(QueryContinueDragEventArgs),
                            BindingFlags.NonPublic | BindingFlags.Instance,
                            null,
                            new object[] { true, DragDropKeyStates.None },
                            null
                        )!
                );
            else if (action == "template")
                view.OnApplyTemplate();
            else if (action == "source")
                view.ItemsSource = WholeGroupDocuments();
            else
                ((ObservableCollection<TabGroup>)view.TabGroups!).RemoveAt(0);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal(before, source);
            AssertWholeGroupDragCleared(view);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeGroupDrag_NotificationFailureRestoresKnownMoves(bool secondMove) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            GroupDocument[] before = source.ToArray();
            TabView view = CreateGroupDragView(source);
            int notifications = 0;
            source.CollectionChanged += (_, _) =>
            {
                if (++notifications == (secondMove ? 2 : 1))
                    throw new InvalidOperationException("Rejected notification");
            };
            DragEventArgs drop = WholeGroupDrop(
                view,
                view.TabGroups!.Last(),
                GroupHeader(view, view.TabGroups.First()),
                false
            );
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal(before, source);
            AssertWholeGroupDragCleared(view);
        });

    [Fact]
    public void WholeGroupDrag_ExternalMembershipChangeStopsAndRestoresOnlyOrder() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            GroupDocument[] before = source.ToArray();
            TabView view = CreateGroupDragView(source);
            bool once = true;
            source.CollectionChanged += (_, _) =>
            {
                if (once)
                {
                    once = false;
                    before[0].GroupId = null;
                }
            };
            Invoke(
                view,
                "OnDrop",
                WholeGroupDrop(view, view.TabGroups!.Last(), GroupHeader(view, view.TabGroups.First()), false)
            );
            Assert.Equal(before, source);
            Assert.Null(before[0].GroupId);
            AssertWholeGroupDragCleared(view);
        });

    [Fact]
    public void WholeGroupDrag_HeadersClickAndCustomMenuStillWork() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            TabGroup group = view.TabGroups!.First();
            System.Windows.Controls.Button header = GroupHeader(view, group);
            MenuResult<bool>(view, "TryBeginWholeGroupDrag", header, new Point());
            header.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.False(group.IsExpanded);
            Invoke(view, "EndDrag");
            view.OnApplyTemplate();
            Layout(view);
            header = GroupHeader(view, group);
            header.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.True(group.IsExpanded);
            Assert.NotNull(MenuResult<ContextMenu>(view, "BuildGroupMenu", header));
            header.ContextMenu = new ContextMenu();
            Assert.Null(MenuResult<ContextMenu?>(view, "BuildGroupMenu", header));
        });

    [Fact]
    public void WholeGroupDrag_ScrolledDropUsesBlockBoundaryAndPreservesTabDrag() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            for (int i = 0; i < 12; i++)
            {
                source.Add(new() { Header = $"Long document {i}", GroupId = "a" });
            }

            TabView view = CreateGroupDragView(source);
            LayoutWidth(view, 320);
            ScrollImmediately(view, HeaderScroller(view).ScrollableWidth);
            TabGroup moved = view.TabGroups!.First();
            FrameworkElement target = Container(view, source.Single(item => item.Header == "Notes"));
            DragEventArgs drop = WholeGroupDrop(view, moved, target, false);
            Invoke(view, "OnDragOver", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            view.UpdateLayout();
            Border marker = Assert.IsType<Border>(DropOverlay(view).Children[0]);
            Assert.InRange(X(marker, view) - X(target, view), -3, 2);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            AssertWholeGroupDragCleared(view);
            Layout(view);
            ScrollImmediately(view, 0);
            GroupDocument tab = source.First(item => item.GroupId == "a");
            DragEventArgs tabDrop = GroupDrop(view, tab, GroupHeader(view, view.TabGroups.Last()));
            Invoke(view, "OnDrop", tabDrop);
            Assert.Equal(DragDropEffects.Move, tabDrop.Effects);
            Assert.Equal("b", tab.GroupId);
        });

    [Fact]
    public void WholeGroupDrag_ForeignDropIsRejected() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            ObservableCollection<GroupDocument> otherSource = WholeGroupDocuments();
            TabView other = CreateGroupDragView(otherSource);
            DragEventArgs drop = WholeGroupDrop(
                view,
                view.TabGroups!.First(),
                GroupHeader(other, other.TabGroups!.Last()),
                true,
                receiver: other
            );
            Invoke(other, "OnDragEnter", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Invoke(other, "OnDragOver", drop);
            Invoke(other, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal("A1", source[0].Header);
            Assert.Equal("A1", otherSource[0].Header);
            Invoke(view, "EndDrag");
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeGroupDrag_DirectItemsAndPlainListsMove(bool directItems) =>
        RunSta(() =>
        {
            TabView view;
            if (directItems)
            {
                view = CreateView();
                view.TabGroups = new[]
                {
                    new TabGroup { Id = "a" },
                    new TabGroup { Id = "b" },
                };
                foreach (GroupDocument document in WholeGroupDocuments())
                {
                    view.Items.Add(new TabViewItem { Header = document.Header, GroupId = document.GroupId });
                }
                Layout(view);
            }
            else
            {
                view = CreateGroupDragView(WholeGroupDocuments().ToList());
            }

            DragEventArgs drop = WholeGroupDrop(
                view,
                view.TabGroups!.Last(),
                GroupHeader(view, view.TabGroups.First()),
                false
            );
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal("B1", Container(view, view.Items[0]).Header);
            AssertWholeGroupDragCleared(view);
        });

    [Fact]
    public void WholeGroupDrag_ReentrantCollectionChangeIsNotOverwritten() =>
        RunSta(() =>
        {
            WholeGroupExternalCollection source = new(WholeGroupDocuments());
            TabView view = CreateGroupDragView(source);
            DragEventArgs drop = WholeGroupDrop(
                view,
                view.TabGroups!.Last(),
                GroupHeader(view, view.TabGroups.First()),
                false
            );
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal(7, source.Count);
            Assert.Equal("External", source.Last().Header);
            AssertWholeGroupDragCleared(view);
        });

    [Fact]
    public void WholeGroupDrag_SourceBindingAndDragClickSuppressionArePreserved() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            BindingOperations.SetBinding(
                view,
                ItemsControl.ItemsSourceProperty,
                new Binding { Source = source }
            );
            BindingExpression binding = BindingOperations.GetBindingExpression(
                view,
                ItemsControl.ItemsSourceProperty
            )!;
            Layout(view);
            TabGroup group = view.TabGroups!.Last();
            DragEventArgs drop = WholeGroupDrop(
                view,
                group,
                GroupHeader(view, view.TabGroups.First()),
                false
            );
            GroupHeader(view, group)
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.True(group.IsExpanded);
            Invoke(view, "OnDrop", drop);
            Assert.Same(
                binding,
                BindingOperations.GetBindingExpression(view, ItemsControl.ItemsSourceProperty)
            );
        });

    [Fact]
    public void WholeGroupDrag_StandardUngroupedTabItemCanCoexistWithGroupedItems() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            TabGroup group = new() { Id = "a" };
            view.TabGroups = new[] { group };
            TabViewItem first = new() { Header = "A1", GroupId = "a" };
            TabItem ordinary = new() { Header = "Ordinary", Content = new object() };
            TabViewItem last = new() { Header = "A2", GroupId = "a" };
            view.Items.Add(first);
            view.Items.Add(ordinary);
            view.Items.Add(last);
            Layout(view);
            view.SelectedItem = ordinary;
            DragEventArgs drop = WholeGroupDrop(view, group, ordinary, true);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Same(ordinary, view.Items[0]);
            Assert.Same(ordinary, view.SelectedItem);
            Assert.Same(ordinary.Content, view.SelectedContent);
            AssertWholeGroupDragCleared(view);
        });

    [Fact]
    public void WholeGroupDrag_NextClickKeepsHeaderContentAndTogglesAfterMove() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            TabGroup moved = view.TabGroups!.Last();
            Invoke(
                view,
                "OnDrop",
                WholeGroupDrop(view, moved, GroupHeader(view, view.TabGroups.First()), false)
            );
            Layout(view);
            foreach (TabGroup group in view.TabGroups)
            {
                System.Windows.Controls.Button header = GroupHeader(view, group);
                object content = header.Content;
                Assert.True(MenuResult<bool>(view, "TryBeginWholeGroupDrag", header, new Point()));
                // Replacing the clicked visual during mouse-down disrupts ButtonBase's real click.
                Assert.Same(content, header.Content);
                Invoke(view, "EndDrag");
                bool expanded = group.IsExpanded;
                header.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal(!expanded, group.IsExpanded);
                header.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal(expanded, group.IsExpanded);
            }
        });

    [Fact]
    public void WholeGroupDrag_BlankSpaceShowsSameAppendLineAsTabDrag() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            LayoutWidth(view, 1600);
            FrameworkElement panel = (FrameworkElement)view.Template.FindName("HeaderPanel", view);
            TabViewItem last = Container(view, source.Single(item => item.Header == "Notes"));
            DragEventArgs whole = WholeGroupDrop(view, view.TabGroups!.First(), panel, true, end: true);
            Invoke(view, "OnDragOver", whole);
            view.UpdateLayout();
            Border marker = Assert.IsType<Border>(DropOverlay(view).Children[0]);
            double groupX = X(marker, view);
            Assert.InRange(groupX - X(last, view) - last.ActualWidth, -2, 2);
            Invoke(view, "EndDrag");
            Point blank = panel.TranslatePoint(
                new Point(panel.ActualWidth - 1, panel.ActualHeight / 2),
                view
            );
            DragEventArgs tab = GroupDrop(view, source[0], panel, pointInView: blank);
            Invoke(view, "OnDragOver", tab);
            view.UpdateLayout();
            Assert.Equal(groupX, X(marker, view), 5);
            Assert.Equal(DragDropEffects.Move, tab.Effects);
            Invoke(view, "EndDrag");
        });

    private sealed class WholeGroupExternalCollection(IEnumerable<GroupDocument> items)
        : ObservableCollection<GroupDocument>(items)
    {
        private bool _once = true;

        protected override void MoveItem(int oldIndex, int newIndex)
        {
            base.MoveItem(oldIndex, newIndex);
            if (_once)
            {
                _once = false;
                Add(new() { Header = "External" });
            }
        }
    }

    private static DragEventArgs WholeGroupDrop(
        TabView view,
        TabGroup group,
        FrameworkElement target,
        bool after,
        bool end = false,
        TabView? receiver = null
    )
    {
        Assert.True(MenuResult<bool>(view, "TryBeginWholeGroupDrag", GroupHeader(view, group), new Point()));
        object payload = MenuResult<object>(view, "CreateWholeGroupDragData", group);
        typeof(TabView)
            .GetField("_isDragging", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(view, true);
        TabView destination = receiver ?? view;
        Point point;
        TabGroup? targetGroup = destination.TabGroups!.FirstOrDefault(candidate =>
            GroupHeader(destination, candidate) == target
        );
        if (targetGroup != null)
        {
            Border background = GroupBackground(destination, targetGroup);
            point = background.TranslatePoint(
                new Point(background.Width * (after ? .75 : .25), background.Height / 2),
                destination
            );
        }
        else
        {
            point = target.TranslatePoint(
                new Point(
                    end ? target.ActualWidth - 1 : target.ActualWidth * (after ? .75 : .25),
                    target.ActualHeight / 2
                ),
                destination
            );
        }

        DragEventArgs args = (DragEventArgs)
            Activator.CreateInstance(
                typeof(DragEventArgs),
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new object[]
                {
                    new DataObject(payload),
                    DragDropKeyStates.None,
                    DragDropEffects.Move,
                    destination,
                    point,
                },
                null
            )!;
        args.RoutedEvent = DragDrop.DropEvent;
        args.Source = target;
        return args;
    }

    private static void AssertWholeGroupDragCleared(TabView view)
    {
        AssertDragCleared(view);
        Assert.Null(
            typeof(TabView)
                .GetField("_draggedGroup", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(view)
        );
        Assert.False(
            (bool)
                typeof(TabView)
                    .GetField("_deferGroupLayout", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(view)!
        );
    }
}
