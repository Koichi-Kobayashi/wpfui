// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Theory]
    [InlineData("Renamed")]
    [InlineData("")]
    public void GroupMenu_EditNameAndColorKeepsIdAndSelection(string name) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            view.SelectedItem = source[0];
            Assert.True(MenuResult<bool>(view, "EditGroup", group, name, Colors.Coral));
            Assert.Equal(name, group.Header);
            Assert.Equal(Colors.Coral, group.Color);
            Assert.Equal("a", group.Id);
            Assert.Same(source[0], view.SelectedItem);
        });

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Escape)]
    public void GroupMenu_EditorKeysApplyOrDiscardDraft(Key key) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            Popup editor = MenuResult<Popup>(view, "BuildGroupEditor", group, GroupHeader(view, group));
            MenuField<System.Windows.Controls.TextBox>(view, "_groupEditorName").Text = "Draft";
            typeof(TabView)
                .GetField("_groupEditorColor", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(view, Colors.Coral);
            KeyEventArgs args = new(Keyboard.PrimaryDevice, new MenuKeySource(), 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            Invoke(view, "OnGroupEditorKeyDown", editor.Child, args);
            Assert.True(args.Handled);
            Assert.Equal(key == Key.Enter ? "Draft" : "A", group.Header);
            Assert.Equal(key == Key.Enter ? Colors.Coral : Colors.DodgerBlue, group.Color);
            Assert.Null(
                typeof(TabView)
                    .GetField("_groupEditor", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(view)
            );
        });

    [Fact]
    public void GroupMenu_CreateGroupsHasUniqueIdsAndDoesNotReorder() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            object[] before = source.Cast<object>().ToArray();
            TabGroup first = MenuResult<TabGroup>(view, "CreateGroupForTab", Container(view, source[0]));
            TabGroup second = MenuResult<TabGroup>(view, "CreateGroupForTab", Container(view, source[1]));
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(first.Id, source[0].GroupId);
            Assert.Equal(second.Id, source[1].GroupId);
            Assert.Equal(4, view.TabGroups!.Count());
            Assert.Equal(before, source.Cast<object>().ToArray());
        });

    [Theory]
    [InlineData("b")]
    [InlineData(null)]
    public void GroupMenu_MembershipKeepsBindingOrderSelectionAndDefinitions(string? id) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            view.SelectedItem = source[0];
            object content = view.SelectedContent;
            TabViewItem tab = Container(view, source[0]);
            BindingExpression binding = BindingOperations.GetBindingExpression(
                tab,
                TabViewItem.GroupIdProperty
            )!;
            object[] before = source.Cast<object>().ToArray();
            Assert.True(MenuResult<bool>(view, "ChangeMembership", tab, id!));
            Assert.Equal(id, source[0].GroupId);
            Assert.Same(binding, BindingOperations.GetBindingExpression(tab, TabViewItem.GroupIdProperty));
            Assert.Equal(before, source.Cast<object>().ToArray());
            Assert.Same(source[0], view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
            Assert.Equal(2, view.TabGroups!.Count());
        });

    [Fact]
    public void GroupMenu_DirectItemsUseCurrentValue() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            view.TabGroups = new ObservableCollection<TabGroup> { new() { Id = "a" } };
            TabViewItem tab = new() { Header = "Direct", Tag = "user" };
            view.Items.Add(tab);
            Layout(view);
            Assert.True(MenuResult<bool>(view, "ChangeMembership", tab, "a"));
            Assert.Equal("a", tab.GroupId);
            Assert.Equal("user", tab.Tag);
        });

    [Fact]
    public void GroupMenu_UngroupAllKeepsItemsAndSelectedContent() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            source.Add(new() { Header = "Second A", GroupId = "a" });
            TabView view = CreateGroupDragView(source);
            view.SelectedItem = source[0];
            TabGroup group = view.TabGroups!.First();
            Assert.True(MenuResult<bool>(view, "Ungroup", group));
            Assert.Null(source[0].GroupId);
            Assert.Null(source[3].GroupId);
            Assert.Equal(4, source.Count);
            Assert.DoesNotContain(group, view.TabGroups!);
            Assert.Same(source[0], view.SelectedItem);
        });

    [Fact]
    public void GroupMenu_MixedOneWayPreflightPreventsPartialUngroup() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            source.Add(new() { Header = "Second A", GroupId = "a" });
            TabView view = CreateGroupDragView(source);
            BindingOperations.SetBinding(
                Container(view, source[3]),
                TabViewItem.GroupIdProperty,
                new Binding("GroupId") { Source = source[3], Mode = BindingMode.OneWay }
            );
            Assert.False(MenuResult<bool>(view, "Ungroup", view.TabGroups!.First()));
            Assert.Equal("a", source[0].GroupId);
            Assert.Equal("a", source[3].GroupId);
            Assert.Equal(2, view.TabGroups!.Count());
        });

    [Fact]
    public void GroupMenu_FailingSetterRollsBackOtherMembers() =>
        RunSta(() =>
        {
            ObservableCollection<MenuModel> source = new()
            {
                new() { Header = "First", GroupId = "a" },
                new()
                {
                    Header = "Reject",
                    GroupId = "a",
                    RejectNull = true,
                },
            };
            TabView view = CreateGroupDragView(source);
            Assert.False(MenuResult<bool>(view, "Ungroup", view.TabGroups!.First()));
            Assert.All(source, item => Assert.Equal("a", item.GroupId));
            Assert.Equal(2, view.TabGroups!.Count());
        });

    [Fact]
    public void GroupMenu_FrozenAndReadOnlyDefinitionsRejectEditingAndDeletion() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            group.Freeze();
            Assert.False(MenuResult<bool>(view, "EditGroup", group, "No", Colors.Red));
            Assert.False(MenuResult<bool>(view, "Ungroup", group));
            view.TabGroups = new ReadOnlyObservableCollection<TabGroup>(new(view.TabGroups!));
            Assert.Null(MenuResult<TabGroup?>(view, "CreateGroupForTab", Container(view, source[1])));
            Assert.False(MenuResult<bool>(view, "Ungroup", view.TabGroups.Last()));
            Assert.Equal(2, view.TabGroups.Count());
        });

    [Theory]
    [InlineData("oneway")]
    [InlineData("readonly")]
    [InlineData("sorted")]
    [InlineData("filtered")]
    public void GroupMenu_UnsafeMembershipSourcesAreRejected(string kind) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(
                kind == "readonly" ? new ReadOnlyObservableCollection<GroupDocument>(source) : source,
                kind == "oneway" ? BindingMode.OneWay : BindingMode.TwoWay
            );
            if (kind == "sorted")
            {
                view.Items.SortDescriptions.Add(
                    new("Header", System.ComponentModel.ListSortDirection.Ascending)
                );
            }

            if (kind == "filtered")
            {
                view.Items.Filter = _ => true;
            }

            Layout(view);
            Assert.False(MenuResult<bool>(view, "ChangeMembership", Container(view, source[0]), "b"));
            Assert.Equal("a", source[0].GroupId);
        });

    [Theory]
    [InlineData(BindingMode.TwoWay)]
    [InlineData(BindingMode.OneWay)]
    public void GroupMenu_DefinitionBindingsArePreservedOrRejected(BindingMode mode) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            MenuModel model = new() { Header = "Bound", Color = Colors.Green };
            BindingOperations.SetBinding(
                group,
                TabGroup.HeaderProperty,
                new Binding("Header") { Source = model, Mode = mode }
            );
            BindingOperations.SetBinding(
                group,
                TabGroup.ColorProperty,
                new Binding("Color") { Source = model, Mode = mode }
            );
            BindingExpression header = BindingOperations.GetBindingExpression(
                group,
                TabGroup.HeaderProperty
            )!;
            BindingExpression color = BindingOperations.GetBindingExpression(group, TabGroup.ColorProperty)!;
            Assert.Equal(
                mode == BindingMode.TwoWay,
                MenuResult<bool>(view, "EditGroup", group, "Changed", Colors.Purple)
            );
            Assert.Equal(mode == BindingMode.TwoWay ? "Changed" : "Bound", model.Header);
            Assert.Equal(mode == BindingMode.TwoWay ? Colors.Purple : Colors.Green, model.Color);
            Assert.Same(header, BindingOperations.GetBindingExpression(group, TabGroup.HeaderProperty));
            Assert.Same(color, BindingOperations.GetBindingExpression(group, TabGroup.ColorProperty));
        });

    [Fact]
    public void GroupMenu_CustomMenusAndDisabledGroupingAreAuthoritative() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabViewItem tab = (TabViewItem)view.ItemContainerGenerator.ContainerFromIndex(0);
            ContextMenu custom = new();
            tab.ContextMenu = custom;
            Assert.Null(MenuResult<ContextMenu?>(view, "BuildGroupMenu", tab));
            Assert.Same(custom, tab.ContextMenu);
            tab.ClearValue(FrameworkElement.ContextMenuProperty);
            view.ContextMenu = custom;
            Assert.Null(MenuResult<ContextMenu?>(view, "BuildGroupMenu", tab));
            view.ClearValue(FrameworkElement.ContextMenuProperty);
            Assert.NotNull(MenuResult<ContextMenu?>(view, "BuildGroupMenu", tab));
            Assert.Null(tab.ContextMenu);
            view.TabGroups = null;
            Assert.Null(MenuResult<ContextMenu?>(view, "BuildGroupMenu", tab));
        });

    [Fact]
    public void GroupMenu_ReapplyAndReloadDoesNotDuplicateCommands() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            view.OnApplyTemplate();
            view.OnApplyTemplate();
            Layout(view);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Layout(view);
            int changes = 0;
            source[0].PropertyChanged += (_, _) => changes++;
            ContextMenu menu = MenuResult<ContextMenu>(view, "BuildGroupMenu", Container(view, source[0]));
            MenuItem existing = (MenuItem)menu.Items[1];
            ((MenuItem)existing.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(1, changes);
            Assert.Equal("b", source[0].GroupId);
            Assert.Equal(2, view.TabGroups!.Count());
        });

    [Theory]
    [InlineData("template")]
    [InlineData("unload")]
    [InlineData("source")]
    [InlineData("remove")]
    public void GroupMenu_LifecycleClosesStaleEditor(string action) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            MenuResult<Popup>(view, "BuildGroupEditor", group, GroupHeader(view, group));
            if (action == "template")
            {
                view.OnApplyTemplate();
            }

            if (action == "unload")
            {
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            }

            if (action == "source")
            {
                view.ItemsSource = DragDocuments();
            }

            if (action == "remove")
            {
                ((ObservableCollection<TabGroup>)view.TabGroups!).Remove(group);
            }

            Assert.Null(
                typeof(TabView)
                    .GetField("_groupEditor", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(view)
            );
        });

    [Fact]
    public void GroupMenu_PaletteSelectionIsADraftUntilApply() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            Popup editor = MenuResult<Popup>(view, "BuildGroupEditor", group, GroupHeader(view, group));
            StackPanel content = (StackPanel)((Border)editor.Child).Child;
            WrapPanel palette = (WrapPanel)content.Children[2];
            ((System.Windows.Controls.Button)palette.Children[2]).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)
            );
            Assert.Equal(Colors.DodgerBlue, group.Color);
            Assert.Equal(Colors.MediumSeaGreen, GroupEditorHeaderColor(view, group));
            Assert.True(MenuResult<bool>(view, "CommitGroupEditor"));
            Assert.Equal(Colors.MediumSeaGreen, group.Color);
            Assert.Equal(Colors.MediumSeaGreen, GroupEditorHeaderColor(view, group));
        });

    [Theory]
    [InlineData("CloseGroupManagementUI")]
    [InlineData("OnApplyTemplate")]
    public void GroupMenu_ColorPreviewIsDiscardedWhenEditorCloses(string closeMethod) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            Popup editor = MenuResult<Popup>(view, "BuildGroupEditor", group, GroupHeader(view, group));
            StackPanel content = (StackPanel)((Border)editor.Child).Child;
            WrapPanel palette = (WrapPanel)content.Children[2];
            ((System.Windows.Controls.Button)palette.Children[2]).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)
            );
            Assert.Equal(Colors.MediumSeaGreen, GroupEditorHeaderColor(view, group));
            Assert.Equal(Colors.DodgerBlue, group.Color);
            if (closeMethod == "OnApplyTemplate")
            {
                view.OnApplyTemplate();
                Layout(view);
            }
            else
            {
                MenuResult<object?>(view, closeMethod);
            }

            Assert.Equal(Colors.DodgerBlue, group.Color);
            Assert.Equal(Colors.DodgerBlue, GroupEditorHeaderColor(view, group));
        });

    private static Color GroupEditorHeaderColor(TabView view, TabGroup group) =>
        (
            (SolidColorBrush)((Border)((StackPanel)GroupHeader(view, group).Content).Children[0]).Background
        ).Color;

    [Fact]
    public void GroupMenu_OneWayCommandsAreDisabled() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source, BindingMode.OneWay);
            ContextMenu menu = MenuResult<ContextMenu>(view, "BuildGroupMenu", Container(view, source[0]));
            Assert.All(menu.Items.Cast<MenuItem>(), item => Assert.False(item.IsEnabled));
            ContextMenu header = MenuResult<ContextMenu>(
                view,
                "BuildGroupMenu",
                GroupHeader(view, view.TabGroups!.First())
            );
            Assert.True(((MenuItem)header.Items[0]).IsEnabled);
            Assert.False(((MenuItem)header.Items[1]).IsEnabled);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupMenu_DefinitionNotificationFailureRestoresMembershipAndDefinitions(bool remove) =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            MenuDefinitions definitions = new(view.TabGroups!);
            view.TabGroups = definitions;
            if (remove)
            {
                definitions.RejectRemove = true;
                Assert.False(MenuResult<bool>(view, "Ungroup", definitions[0]));
            }
            else
            {
                definitions.RejectAdd = true;
                Assert.Null(MenuResult<TabGroup?>(view, "CreateGroupForTab", Container(view, source[0])));
            }
            Assert.Equal(2, definitions.Count);
            Assert.Equal("a", definitions[0].Id);
            Assert.Equal("a", source[0].GroupId);
        });

    [Fact]
    public void GroupMenu_DefinitionCollectionBindingIsKeptWhenAdding() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            TabView view = CreateGroupDragView(source);
            MenuDefinitionModel model = new() { Groups = new(view.TabGroups!) };
            BindingOperations.SetBinding(
                view,
                TabView.TabGroupsProperty,
                new Binding("Groups") { Source = model, Mode = BindingMode.OneWay }
            );
            BindingExpression binding = BindingOperations.GetBindingExpression(
                view,
                TabView.TabGroupsProperty
            )!;
            Assert.NotNull(MenuResult<TabGroup?>(view, "CreateGroupForTab", Container(view, source[0])));
            Assert.Same(model.Groups, view.TabGroups);
            Assert.Equal(3, model.Groups.Count);
            Assert.Same(binding, BindingOperations.GetBindingExpression(view, TabView.TabGroupsProperty));
        });

    [Fact]
    public void GroupMenu_ReadOnlyClrPropertyCannotBeEdited() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            BindingOperations.SetBinding(
                group,
                TabGroup.HeaderProperty,
                new Binding("Header") { Source = new MenuReadOnlyName(), Mode = BindingMode.OneWay }
            );
            Assert.False(MenuResult<bool>(view, "EditGroup", group, "Changed", Colors.Red));
            Assert.Equal("Read only", group.Header);
            Assert.Equal(Colors.DodgerBlue, group.Color);
        });

    private sealed class MenuReadOnlyName
    {
        public string Header => "Read only";
    }

    private sealed class MenuDefinitionModel
    {
        public ObservableCollection<TabGroup> Groups { get; set; } = new();
    }

    private sealed class MenuDefinitions(IEnumerable<TabGroup> groups)
        : ObservableCollection<TabGroup>(groups)
    {
        public bool RejectAdd { get; set; }
        public bool RejectRemove { get; set; }

        protected override void InsertItem(int index, TabGroup item)
        {
            base.InsertItem(index, item);
            if (RejectAdd)
            {
                RejectAdd = false;
                throw new InvalidOperationException("Rejected after add");
            }
        }

        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);
            if (RejectRemove)
            {
                RejectRemove = false;
                throw new InvalidOperationException("Rejected after remove");
            }
        }
    }

    [Fact]
    public void GroupMenu_NewGroupsFromSecondGroupKeepOriginalMetadataAndUseDistinctDefaults() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            source.Add(new() { Header = "Second reference", GroupId = "b" });
            TabView view = CreateGroupDragView(source);
            TabGroup first = view.TabGroups!.First();
            TabGroup reference = view.TabGroups!.Last();
            reference.Color = Colors.MediumSeaGreen;
            TabView other = CreateGroupDragView(source, BindingMode.OneWay);
            other.TabGroups = view.TabGroups;
            string originalName = reference.Header;
            Color originalColor = reference.Color;
            TabGroup created = MenuResult<TabGroup>(view, "CreateGroupForTab", Container(view, source[2]));
            Layout(view);
            Layout(other);
            Assert.Equal(originalName, reference.Header);
            Assert.Equal(originalColor, reference.Color);
            Assert.Equal("b", source[3].GroupId);
            Assert.Equal(created.Id, source[2].GroupId);
            Assert.Equal(Visibility.Visible, GroupHeader(view, reference).Visibility);
            Assert.Equal(Visibility.Visible, GroupHeader(other, reference).Visibility);
            Assert.NotEqual(first.Color, created.Color);
            Assert.NotEqual(reference.Color, created.Color);
            TabGroup second = MenuResult<TabGroup>(view, "CreateGroupForTab", Container(view, source[3]));
            Assert.NotEqual(created.Color, second.Color);
            Assert.NotEqual(created.Header, second.Header);
            Assert.Contains(reference, view.TabGroups!);
            Assert.Equal(originalName, reference.Header);
            Assert.Equal(originalColor, reference.Color);
        });

    private static T MenuResult<T>(TabView view, string method, params object?[] args) =>
        (T)
            typeof(TabView)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(view, args)!;

    private static T MenuField<T>(TabView view, string field) =>
        (T)typeof(TabView).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;

    private sealed class MenuModel
    {
        private string? _groupId;
        public string Header { get; set; } = string.Empty;
        public Color Color { get; set; }
        public bool RejectNull { get; set; }
        public string? GroupId
        {
            get => _groupId;
            set
            {
                if (RejectNull && value == null)
                {
                    throw new InvalidOperationException("Rejected");
                }

                _groupId = value;
            }
        }
    }

    private sealed class MenuKeySource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;

        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
