// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Review_TabDropBeforeLeadingGroup(bool rtl, bool collapsed, bool groupedSource) =>
        RunSta(() =>
        {
            var source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            view.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            TabGroup first = view.TabGroups!.First();
            first.IsExpanded = !collapsed;
            Layout(view);
            GroupDocument dragged = source[groupedSource ? 2 : 1];
            view.SelectedItem = dragged;
            object content = view.SelectedContent;
            var panel = (FrameworkElement)view.Template.FindName("HeaderPanel", view);
            var drop = GroupDrop(
                view,
                dragged,
                GroupHeader(view, first),
                pointInView: panel.TranslatePoint(new Point(1, panel.ActualHeight / 2), view)
            );
            Invoke(view, "OnDragOver", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            view.UpdateLayout();
            var marker = Assert.IsType<Border>(DropOverlay(view).Children[0]);
            Assert.Equal(2, marker.Width);
            Assert.InRange(Canvas.GetLeft(marker), 0, 1);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Same(dragged, source[0]);
            Assert.Null(dragged.GroupId);
            Assert.Equal(!collapsed, first.IsExpanded);
            Assert.Same(dragged, view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
            AssertDragCleared(view);
        });

    [Fact]
    public void Review_AutomationUsesUnnamedOnlyWhileHeaderIsEmpty() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(WholeGroupDocuments());
            TabGroup group = view.TabGroups!.Last();
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(
                GroupHeader(view, group)
            );
            Assert.Equal("B", peer.GetName());
            group.Header = "";
            Assert.Equal("Unnamed group", peer.GetName());
            Invoke(view, "OnDrop", WholeGroupDrop(view, group, GroupHeader(view, view.TabGroups.First()), false));
            group.IsExpanded = false;
            group.Header = "Reference";
            Assert.Equal("Reference", peer.GetName());
        });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Review_InvalidIdDuringEditingClosesEditorAndPreservesBinding(string? id) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            ToolTipModel model = new() { Value = group.Id };
            var binding = BindingOperations.SetBinding(
                group,
                TabGroup.IdProperty,
                new Binding(nameof(ToolTipModel.Value)) { Source = model }
            );
            Popup editor = MenuResult<Popup>(view, "BuildGroupEditor", group, GroupHeader(view, group));
            editor.IsOpen = true;
            model.Value = id;
            Assert.False(MenuResult<bool>(view, "OwnsGroup", group));
            Assert.False(editor.IsOpen);
            Assert.Same(binding, BindingOperations.GetBindingExpression(group, TabGroup.IdProperty));
            Assert.Equal(3, view.Items.Count);
        });

    [Fact]
    public void Review_TargetRemovedDuringMoveRejectsAndRestoresOwnChanges() =>
        RunSta(() =>
        {
            var source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup target = view.TabGroups!.Last();
            object[] before = source.Cast<object>().ToArray();
            GroupDocument dragged = source[0];
            view.SelectedItem = dragged;
            object content = view.SelectedContent;
            source.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Move)
                {
                    ((ObservableCollection<TabGroup>)view.TabGroups!).Remove(target);
                }
            };
            var drop = GroupDrop(view, dragged, Container(view, source[2]));
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal(before, source.Cast<object>());
            Assert.Equal("a", dragged.GroupId);
            Assert.DoesNotContain(target, view.TabGroups!);
            Assert.Same(dragged, view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
            AssertDragCleared(view);
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Review_TabDropUsesLogicalCoordinatesAndMatchingMarker(bool rtl, bool after) =>
        RunSta(() =>
        {
            var source = new ObservableCollection<GroupDocument>
            {
                new() { Header = "First", GroupId = "a" },
                new() { Header = "Middle", GroupId = "a" },
                new() { Header = "Last", GroupId = "a" },
            };
            TabView view = CreateGroupDragView(source);
            view.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            Layout(view);
            GroupDocument dragged = source[1];
            TabViewItem anchor = Container(view, after ? source[2] : source[0]);
            var drop = GroupDrop(view, dragged, anchor, after);
            Invoke(view, "OnDragOver", drop);
            view.UpdateLayout();
            var marker = Assert.IsType<Border>(DropOverlay(view).Children[0]);
            double expected = anchor.TranslatePoint(new Point(after ? anchor.ActualWidth : 0, 0), DropOverlay(view)).X;
            Assert.InRange(Canvas.GetLeft(marker) - expected, -1.01, 0.01);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal(after ? "First,Last,Middle" : "Middle,First,Last", string.Join(",", source.Select(item => item.Header)));
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Review_WholeGroupDropUsesLogicalCoordinates(bool rtl, bool after) =>
        RunSta(() =>
        {
            var source = WholeGroupDocuments();
            TabView view = CreateGroupDragView(source);
            view.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            Layout(view);
            TabGroup moved = view.TabGroups!.Last();
            var drop = WholeGroupDrop(view, moved, GroupHeader(view, view.TabGroups.First()), after);
            Invoke(view, "OnDragOver", drop);
            view.UpdateLayout();
            var background = GroupBackground(view, view.TabGroups.First());
            var marker = Assert.IsType<Border>(DropOverlay(view).Children[0]);
            double boundary = background.TranslatePoint(new Point(after ? background.Width : 0, 0), DropOverlay(view)).X;
            Assert.InRange(Canvas.GetLeft(marker) - boundary, -1.01, 0.01);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal(after ? "A1,B1,B2,Home,A2,Notes" : "B1,B2,A1,Home,A2,Notes", string.Join(",", source.Select(item => item.Header)));
        });

    [Fact]
    public void Review_HeaderStyleToolTipSurvivesUpdatesAndGroupOverride() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            Style style = new(typeof(Wpf.Ui.Controls.Button), (Style)view.FindResource(typeof(Wpf.Ui.Controls.Button)));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "Style tooltip"));
            view.Resources[typeof(Wpf.Ui.Controls.Button)] = style;
            TabGroup group = new() { Id = "a" };
            view.TabGroups = new[] { group };
            view.Items.Add(new TabViewItem { GroupId = "a" });
            Layout(view);
            var header = GroupHeader(view, group);
            Assert.Equal("Style tooltip", header.ToolTip);
            group.ToolTip = "Group tooltip";
            Assert.Equal("Group tooltip", header.ToolTip);
            group.ToolTip = null;
            Assert.True(RaiseGroupToolTipOpening(header).Handled);
            group.ClearValue(TabGroup.ToolTipProperty);
            RaiseGroupToolTipOpening(header);
            Assert.Equal("Style tooltip", header.ToolTip);
            Assert.Same(style, header.Style);
        });

    [Fact]
    public void Review_HeaderLocalToolTipSurvivesGroupOverride() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            var header = GroupHeader(view, group);
            header.ToolTip = "Local tooltip";
            group.IsExpanded = false;
            Assert.Equal("Local tooltip", header.ToolTip);
            group.ToolTip = "Group tooltip";
            Assert.Equal("Group tooltip", header.ToolTip);
            Assert.Equal("Local tooltip", header.ReadLocalValue(FrameworkElement.ToolTipProperty));
            group.ClearValue(TabGroup.ToolTipProperty);
            Assert.Equal("Local tooltip", header.ToolTip);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Review_HeaderBindingSurvivesGroupOverrideAndSourceUpdates(bool twoWay) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            var header = GroupHeader(view, group);
            ToolTipModel model = new() { Value = "Header tooltip" };
            var expression = BindingOperations.SetBinding(header, FrameworkElement.ToolTipProperty,
                new Binding(nameof(ToolTipModel.Value))
                {
                    Source = model,
                    Mode = twoWay ? BindingMode.TwoWay : BindingMode.OneWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                });
            group.ToolTip = "Group tooltip";
            Assert.Equal("Header tooltip", model.Value);
            Assert.Equal("Group tooltip", header.ToolTip);
            model.Value = "Updated header tooltip";
            Assert.Equal("Group tooltip", header.ToolTip);
            Assert.Equal("Updated header tooltip", model.Value);
            group.ClearValue(TabGroup.ToolTipProperty);
            Assert.Equal(model.Value, header.ToolTip);
            Assert.Same(expression, BindingOperations.GetBindingExpression(header, FrameworkElement.ToolTipProperty));
        });

    [Fact]
    public void Review_DropRejectsReplacedDefinitionWithSameId() =>
        RunSta(() =>
        {
            var source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup replacement = new() { Id = "b", Header = "Replacement" };
            bool once = true;
            source.CollectionChanged += (_, e) =>
            {
                if (once && e.Action == NotifyCollectionChangedAction.Move)
                {
                    once = false;
                    ((ObservableCollection<TabGroup>)view.TabGroups!)[1] = replacement;
                }
            };
            GroupDocument dragged = source[0];
            var drop = GroupDrop(view, dragged, Container(view, source[2]));
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Same(replacement, view.TabGroups!.Last());
            Assert.Same(dragged, source[0]);
            Assert.Equal("a", dragged.GroupId);
        });

    [Fact]
    public void Review_DropDoesNotOverwriteExternalMembershipChange() =>
        RunSta(() =>
        {
            var source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[0];
            source.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Move)
                {
                    dragged.GroupId = "external";
                }
            };
            var drop = GroupDrop(view, dragged, Container(view, source[2]));
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal("external", dragged.GroupId);
            Assert.Same(dragged, source[0]);
        });

    [Fact]
    public void Review_DropRejectsChangedAnchorMembership() =>
        RunSta(() =>
        {
            var source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[0];
            GroupDocument anchor = source[2];
            source.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Move)
                {
                    anchor.GroupId = "external";
                }
            };
            var drop = GroupDrop(view, dragged, Container(view, anchor));
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.Equal("external", anchor.GroupId);
            Assert.Equal("a", dragged.GroupId);
            Assert.Same(dragged, source[0]);
        });

    [Fact]
    public void Review_PlainListDropRegeneratesContainersAndPreservesMembership() =>
        RunSta(() =>
        {
            var source = DragDocuments().ToList();
            TabView view = CreateGroupDragView(source);
            GroupDocument dragged = source[0];
            var drop = GroupDrop(view, dragged, Container(view, source[2]));
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Same(dragged, source.Last());
            Assert.Equal("b", dragged.GroupId);
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Review_ScrolledTabDropUsesCorrectBoundary(bool rtl, bool after) =>
        RunSta(() =>
        {
            var source = new ObservableCollection<GroupDocument>(Enumerable.Range(0, 12)
                .Select(index => new GroupDocument { Header = $"Long tab {index}", GroupId = "a" }));
            TabView view = CreateGroupDragView(source);
            view.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            LayoutWidth(view, 320);
            ScrollImmediately(view, HeaderScroller(view).ScrollableWidth);
            GroupDocument dragged = source[0];
            GroupDocument anchor = source.Last();
            var drop = GroupDrop(view, dragged, Container(view, anchor), after);
            Invoke(view, "OnDragOver", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal(after ? source.Count - 1 : source.Count - 2, source.IndexOf(dragged));
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Review_WholeGroupAppendAndScrolledDropMaintainOrder(bool rtl, bool scrolled) =>
        RunSta(() =>
        {
            var source = WholeGroupDocuments();
            for (int index = 0; index < 10; index++)
            {
                source.Add(new() { Header = $"Long tab {index}", GroupId = "a" });
            }
            TabView view = CreateGroupDragView(source);
            view.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            LayoutWidth(view, scrolled ? 320 : 2400);
            TabGroup moved = view.TabGroups!.First();
            FrameworkElement destination;
            if (scrolled)
            {
                ScrollImmediately(view, HeaderScroller(view).ScrollableWidth);
                destination = Container(view, source.Single(item => item.Header == "Notes"));
            }
            else
            {
                destination = (FrameworkElement)view.Template.FindName("HeaderPanel", view);
            }
            var drop = WholeGroupDrop(view, moved, destination, true, end: !scrolled);
            Invoke(view, "OnDragOver", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Invoke(view, "OnDrop", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            Assert.Equal("Home", source[0].Header);
            Assert.Equal("B1", source[1].Header);
            Assert.Equal("Notes", source[2].Header);
            Assert.Equal(scrolled ? "A1" : "B2", source[3].Header);
        });
}
