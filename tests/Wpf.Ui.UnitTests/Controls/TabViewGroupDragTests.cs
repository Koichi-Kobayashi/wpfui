// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupDrag_SameGroupMovesInBothDirectionsUsingMove(bool backwards) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = new()
            {
                new() { Header = "First", GroupId = "a" },
                new() { Header = "Ungrouped" },
                new() { Header = "Last", GroupId = "a" },
            };
            TabView view = CreateGroupDragView(source);
            object dragged = source[backwards ? 2 : 0];
            object anchor = source[backwards ? 0 : 2];
            int notifications = 0;
            source.CollectionChanged += (_, e) =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Move, e.Action);
                notifications++;
            };
            view.SelectedItem = dragged;
            DragEventArgs drop = GroupDrop(view, dragged, Container(view, anchor), !backwards);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Same(dragged, source[backwards ? 0 : 2]);
            Assert.Equal(1, notifications);
            Assert.Same(dragged, view.SelectedItem);
            Assert.Same(dragged, view.SelectedContent);
            Layout(view);
            Assert.True(
                X(Container(view, backwards ? dragged : anchor), view)
                    < X(Container(view, backwards ? anchor : dragged), view)
            );
            AssertDragCleared(view);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupDrag_HeaderAppendsAndPreservesExpansionBindingAndContent(bool collapsed) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[0];
            TabViewItem container = Container(view, dragged);
            BindingExpression binding = BindingOperations.GetBindingExpression(
                container,
                TabViewItem.GroupIdProperty
            )!;
            TabGroup destination = view.TabGroups!.Last();
            destination.IsExpanded = !collapsed;
            Layout(view);
            view.SelectedItem = dragged;
            object content = view.SelectedContent;
            DragEventArgs drop = GroupDrop(view, dragged, GroupHeader(view, destination));
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal("b", dragged.GroupId);
            Assert.Same(dragged, source.Last());
            Assert.Same(
                binding,
                BindingOperations.GetBindingExpression(container, TabViewItem.GroupIdProperty)
            );
            Assert.Equal(!collapsed, destination.IsExpanded);
            Assert.Same(dragged, view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
            Layout(view);
            Assert.Equal(
                collapsed ? Visibility.Collapsed : Visibility.Visible,
                Container(view, dragged).Visibility
            );
            Assert.Equal(3, GroupHeader(view, destination).BorderThickness.Bottom);
            Assert.Equal(2, view.TabGroups!.Count());
            AssertDragCleared(view);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupDrag_GroupedAndUngroupedTabsCanExchangeMembership(bool toGroup) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[toGroup ? 1 : 0];
            GroupDocument anchor = source[toGroup ? 0 : 1];
            DragEventArgs drop = GroupDrop(view, dragged, Container(view, anchor), toGroup);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal(toGroup ? "a" : null, dragged.GroupId);
            Assert.Contains(dragged, source);
        });

    [Fact]
    public void GroupDrag_UpdatesMembershipEvenWhenDataIndexDoesNotChange() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[1];
            int notifications = 0;
            source.CollectionChanged += (_, _) => notifications++;
            DragEventArgs drop = GroupDrop(view, dragged, Container(view, source[0]), true);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Same(dragged, source[1]);
            Assert.Equal("a", dragged.GroupId);
            Assert.Equal(0, notifications);
        });

    [Fact]
    public void GroupDrag_DirectTabsPreserveUserValuesAndUseDataAnchors() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            view.TabGroups = new[]
            {
                new TabGroup { Id = "a" },
                new TabGroup { Id = "b" },
            };
            TabViewItem first = new()
            {
                Header = "First",
                GroupId = "a",
                Tag = "user tag",
            };
            TabViewItem middle = new() { Header = "Middle" };
            TabViewItem last = new() { Header = "Last", GroupId = "a" };
            foreach (TabViewItem tab in new[] { first, middle, last })
            {
                view.Items.Add(tab);
            }

            Layout(view);
            object background = first.Background;
            object template = first.Template;
            DragEventArgs drop = GroupDrop(view, first, middle, false);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Null(first.GroupId);
            Assert.Same(first, view.Items[0]);
            Assert.Equal("user tag", first.Tag);
            Assert.Same(background, first.Background);
            Assert.Same(template, first.Template);
        });

    [Fact]
    public void GroupDrag_OneWayMembershipRejectsCrossGroupButAllowsReorder() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            source.Add(new() { Header = "Second A", GroupId = "a" });
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            GroupDocument dragged = source[0];
            DragEventArgs rejected = GroupDrop(view, dragged, Container(view, source[2]));
            Invoke(view, "OnDrop", rejected);
            Assert.Equal(DragDropEffects.None, rejected.Effects);
            Assert.Equal("a", dragged.GroupId);
            Assert.Same(dragged, source[0]);
            DragEventArgs allowed = GroupDrop(view, dragged, Container(view, source[3]), true);
            Invoke(view, "OnDrop", allowed);
            Assert.Equal(DragDropEffects.Move, allowed.Effects);
            Assert.Same(dragged, source.Last());
        });

    [Theory]
    [InlineData("readonly")]
    [InlineData("sort")]
    [InlineData("filter")]
    public void GroupDrag_RejectsSourcesThatCannotBeSafelyMutated(string kind) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            object[] before = source.Cast<object>().ToArray();
            TabView view = CreateGroupDragView(source);
            if (kind == "readonly")
            {
                view.ItemsSource = new ReadOnlyObservableCollection<GroupDocument>(source);
            }
            else
            {
                ListCollectionView collectionView = new(source);
                if (kind == "sort")
                {
                    collectionView.SortDescriptions.Add(
                        new SortDescription("Header", ListSortDirection.Ascending)
                    );
                }
                else
                {
                    collectionView.Filter = _ => true;
                }

                view.ItemsSource = collectionView;
            }

            Layout(view);
            DragEventArgs drop = GroupDrop(view, source[0], Container(view, source[2]));
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal(before, source.Cast<object>());
            Assert.Equal("a", source[0].GroupId);
            AssertDragCleared(view);
        });

    [Fact]
    public void GroupDrag_RejectsReadOnlyModelProperty() =>
        RunSta(() =>
        {
            ObservableCollection<ReadOnlyGroupDocument> source = new()
            {
                new("First", "a"),
                new("Second", "b"),
            };
            TabView view = CreateGroupDragView(source);
            DragEventArgs drop = GroupDrop(view, source[0], Container(view, source[1]));
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal("a", source[0].GroupId);
            AssertDragCleared(view);
        });

    [Fact]
    public void GroupDrag_DragOverShowsOverlayWithoutChangingModelsAndEscClearsIt() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            object[] before = source.Cast<object>().ToArray();
            DragEventArgs over = GroupDrop(view, source[0], GroupHeader(view, view.TabGroups!.Last()));
            Invoke(view, "OnDragOver", over);
            Assert.Equal(DragDropEffects.Move, over.Effects);
            Canvas overlay = DropOverlay(view);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<Border>(Assert.Single(overlay.Children.Cast<UIElement>())).Visibility
            );
            Assert.Equal(before, source.Cast<object>());
            Assert.Equal("a", source[0].GroupId);
            QueryContinueDragEventArgs cancel = Assert.IsType<QueryContinueDragEventArgs>(
                Activator.CreateInstance(
                    typeof(QueryContinueDragEventArgs),
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new object[] { true, DragDropKeyStates.None },
                    null
                )
            );
            Invoke(view, "OnQueryContinueDrag", cancel);
            Assert.Equal(DragAction.Cancel, cancel.Action);
            Assert.Equal(Visibility.Collapsed, ((Border)overlay.Children[0]).Visibility);
            Assert.Equal(before, source.Cast<object>());
            AssertDragCleared(view);
        });

    [Fact]
    public void GroupDrag_InvalidDropHasNoIndicatorAndDragLeaveClearsValidIndicator() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            DragEventArgs over = GroupDrop(view, source[0], Container(view, source[2]));
            Invoke(view, "OnDragOver", over);
            Assert.Equal(DragDropEffects.None, over.Effects);
            Assert.Empty(DropOverlay(view).Children.Cast<UIElement>());
            over = GroupDrop(view, source[0], Container(view, source[0]));
            Invoke(view, "OnDragOver", over);
            Assert.Equal(DragDropEffects.Move, over.Effects);
            Invoke(view, "OnDragLeave", over);
            Assert.Equal(Visibility.Collapsed, ((Border)DropOverlay(view).Children[0]).Visibility);
            Invoke(view, "OnDrop", over);
            AssertDragCleared(view);
        });

    [Fact]
    public void GroupDrag_MoveFailureRestoresMembershipSelectionAndAllowsNextDrop() =>
        RunSta(() =>
        {
            ThrowingGroupCollection source = new();
            foreach (GroupDocument document in DragDocuments())
            {
                source.Add(document);
            }

            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[0];
            view.SelectedItem = dragged;
            object[] before = source.Cast<object>().ToArray();
            DragEventArgs drop = GroupDrop(view, dragged, Container(view, source[2]), true);
            Assert.Throws<InvalidOperationException>(() => Invoke(view, "OnDrop", drop));
            Assert.Equal(before, source.Cast<object>());
            Assert.Equal("a", dragged.GroupId);
            Assert.Same(dragged, view.SelectedItem);
            AssertDragCleared(view);
            source.ThrowOnMove = false;
            drop = GroupDrop(view, dragged, Container(view, source[2]), true);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal("b", dragged.GroupId);
        });

    [Theory]
    [InlineData("template")]
    [InlineData("source")]
    [InlineData("groups")]
    [InlineData("membership")]
    [InlineData("unload")]
    public void GroupDrag_LifecycleChangesCancelPendingDrop(string change) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            DragEventArgs drop = GroupDrop(view, source[0], Container(view, source[2]), true);
            Invoke(view, "OnDragOver", drop);
            Canvas oldOverlay = DropOverlay(view);
            if (change == "template")
            {
                view.OnApplyTemplate();
            }
            else if (change == "source")
            {
                view.ItemsSource = DragDocuments();
            }
            else if (change == "groups")
            {
                ((ObservableCollection<TabGroup>)view.TabGroups!).RemoveAt(1);
            }
            else if (change == "membership")
            {
                source[0].GroupId = null;
            }
            else
            {
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            }

            AssertDragCleared(view);
            Assert.All(
                oldOverlay.Children.Cast<Border>(),
                indicator => Assert.Equal(Visibility.Collapsed, indicator.Visibility)
            );
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal("First", source[0].Header);
            Assert.Equal(change == "membership" ? null : "a", source[0].GroupId);
        });

    private static TabView CreateGroupDragView(object source, BindingMode mode = BindingMode.TwoWay)
    {
        TabView view = CreateView();
        view.BeginInit();
        view.EndInit();
        view.TabGroups = new ObservableCollection<TabGroup>
        {
            new() { Id = "a", Header = "A" },
            new() { Id = "b", Header = "B" },
        };
        Style style = new(typeof(TabViewItem), (Style)view.FindResource(typeof(TabViewItem)));
        style.Setters.Add(new Setter(HeaderedContentControl.HeaderProperty, new Binding("Header")));
        style.Setters.Add(new Setter(TabViewItem.GroupIdProperty, new Binding("GroupId") { Mode = mode }));
        view.ItemContainerStyle = style;
        view.ItemsSource = (System.Collections.IEnumerable)source;
        Layout(view);
        return view;
    }

    [Theory]
    [InlineData(UpdateSourceTrigger.PropertyChanged)]
    [InlineData(UpdateSourceTrigger.Explicit)]
    [InlineData(UpdateSourceTrigger.LostFocus)]
    public void GroupDrag_BindingTriggersWriteModelOnceAndKeepExpression(UpdateSourceTrigger trigger) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[0];
            TabViewItem tab = Container(view, dragged);
            BindingOperations.SetBinding(
                tab,
                TabViewItem.GroupIdProperty,
                new Binding("GroupId")
                {
                    Source = dragged,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = trigger,
                }
            );
            BindingExpression expression = BindingOperations.GetBindingExpression(
                tab,
                TabViewItem.GroupIdProperty
            )!;
            int writes = 0;
            dragged.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "GroupId")
                {
                    writes++;
                }
            };
            DragEventArgs drop = GroupDrop(view, dragged, Container(view, source[2]), true);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal("b", dragged.GroupId);
            Assert.Equal(1, writes);
            Assert.Same(expression, BindingOperations.GetBindingExpression(tab, TabViewItem.GroupIdProperty));
        });

    [Fact]
    public void GroupDrag_NotificationFailureAfterMoveRollsBackKnownMove() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[0];
            object[] before = source.Cast<object>().ToArray();
            bool throwOnce = true;
            source.CollectionChanged += (_, e) =>
            {
                if (throwOnce && e.Action == NotifyCollectionChangedAction.Move)
                {
                    throwOnce = false;
                    throw new InvalidOperationException("Notification rejected");
                }
            };
            DragEventArgs drop = GroupDrop(view, dragged, Container(view, source[2]), true);
            Assert.Throws<InvalidOperationException>(() => Invoke(view, "OnDrop", drop));
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal(before, source.Cast<object>());
            Assert.Equal("a", dragged.GroupId);
            AssertDragCleared(view);
        });

    [Fact]
    public void GroupDrag_ExternalItemChangesCancelWithoutApplyingStaleDrop() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[0];
            DragEventArgs drop = GroupDrop(view, dragged, Container(view, source[2]), true);
            source.Add(new() { Header = "External" });
            AssertDragCleared(view);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal("a", dragged.GroupId);
            Assert.Same(dragged, source[0]);
        });

    [Fact]
    public void GroupDrag_SoleMemberHeaderKeepsItsDataPosition() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[0];
            DragEventArgs drop = GroupDrop(view, dragged, GroupHeader(view, view.TabGroups!.First()));
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Same(dragged, source[0]);
            Assert.Equal("a", dragged.GroupId);
        });

    [Fact]
    public void GroupDrag_RejectsForeignOwnerWithNoDropEffect() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            ObservableCollection<GroupDocument> otherSource = DragDocuments();
            TabView owner = CreateGroupDragView(source);
            TabView other = CreateGroupDragView(otherSource);
            SetDragState(owner, Container(owner, source[0]));
            DragEventArgs drop = CreateDrop(owner, source[0], other, Container(other, otherSource[2]));
            Invoke(other, "OnDragOver", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Invoke(other, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal("a", source[0].GroupId);
            Assert.Equal(3, source.Count);
            Assert.Equal(3, otherSource.Count);
            Assert.Empty(DropOverlay(other).Children.Cast<UIElement>());
            Invoke(owner, "EndDrag");
            AssertDragCleared(owner);
        });

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    public void GroupDrag_UnknownAndEmptyIdsReorderAsUngroupedWithoutWritingMembership(string groupId) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            GroupDocument dragged = source[0];
            dragged.GroupId = groupId;
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            DragEventArgs drop = GroupDrop(view, dragged, Container(view, source[1]), true);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal(groupId, dragged.GroupId);
            Assert.Same(dragged, source[1]);
            AssertDragCleared(view);
        });

    private static ObservableCollection<GroupDocument> DragDocuments() =>
        new()
        {
            new() { Header = "First", GroupId = "a" },
            new() { Header = "Ungrouped" },
            new() { Header = "Last", GroupId = "b" },
        };

    private static TabViewItem Container(TabView view, object item) =>
        Assert.IsType<TabViewItem>(view.ItemContainerGenerator.ContainerFromItem(item));

    private static Canvas DropOverlay(TabView view) =>
        Assert.IsType<Canvas>(view.Template.FindName("PART_DropOverlay", view));

    private static DragEventArgs GroupDrop(
        TabView view,
        object item,
        FrameworkElement target,
        bool after = true
    )
    {
        SetDragState(view, Container(view, item));
        Type payloadType = typeof(TabView).GetNestedType("TabDragData", BindingFlags.NonPublic)!;
        object payload = Activator.CreateInstance(payloadType, view, item)!;
        Point point = target.TranslatePoint(
            new Point(target.ActualWidth * (after ? .75 : .25), target.ActualHeight / 2),
            view
        );
        DragEventArgs args = Assert.IsType<DragEventArgs>(
            Activator.CreateInstance(
                typeof(DragEventArgs),
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new object[]
                {
                    new DataObject(payload),
                    DragDropKeyStates.None,
                    DragDropEffects.Move,
                    view,
                    point,
                },
                null
            )
        );
        args.RoutedEvent = DragDrop.DropEvent;
        args.Source = target;
        return args;
    }

    private sealed record ReadOnlyGroupDocument(string Header, string? GroupId);

    private sealed class ThrowingGroupCollection : ObservableCollection<GroupDocument>
    {
        public bool ThrowOnMove { get; set; } = true;

        protected override void MoveItem(int oldIndex, int newIndex)
        {
            if (ThrowOnMove)
            {
                throw new InvalidOperationException("Move rejected");
            }

            base.MoveItem(oldIndex, newIndex);
        }
    }
}
