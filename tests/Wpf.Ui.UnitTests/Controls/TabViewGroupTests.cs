// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Fact]
    public void Groups_NullPreservesLegacyTabPanelWrappingAndPositions() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            TabView legacy = CreateView();
            legacy.Template = Assert.IsType<ControlTemplate>(
                System.Windows.Markup.XamlReader.Parse(
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TabControl'><Grid><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TabPanel x:Name='HeaderPanel' IsItemsHost='True'/><Button x:Name='PART_AddButton' Grid.Column='1' Width='36' Height='36' Margin='4,0,0,0' VerticalAlignment='Top'/></Grid><Border Grid.Row='1' BorderThickness='0,1,0,0'><ContentPresenter x:Name='PART_SelectedContentHost' ContentSource='SelectedContent'/></Border></Grid></ControlTemplate>"
                )
            );
            for (int i = 0; i < 9; i++)
            {
                view.Items.Add(new TabViewItem { Header = $"Document {i}" });
                legacy.Items.Add(new TabViewItem { Header = $"Document {i}" });
            }

            foreach (TabView control in new[] { view, legacy })
            {
                control.Measure(new Size(280, 400));
                control.Arrange(new Rect(0, 0, 280, 400));
                control.UpdateLayout();
            }

            for (int i = 0; i < view.Items.Count; i++)
            {
                TabViewItem tab = Assert.IsType<TabViewItem>(view.Items[i]);
                TabViewItem reference = Assert.IsType<TabViewItem>(legacy.Items[i]);
                Assert.Equal(
                    reference.TranslatePoint(new Point(0, 0), legacy),
                    tab.TranslatePoint(new Point(0, 0), view)
                );
                Assert.Equal(reference.RenderSize, tab.RenderSize);
            }

            Assert.Empty(GroupLayer(view).Children.Cast<UIElement>());
        });

    [Fact]
    public void Groups_ProjectHeadersAndScatteredTabsWithoutChangingItems() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            TabGroup development = new() { Id = "dev", Header = "Development" };
            TabGroup reference = new()
            {
                Id = "ref",
                Header = "Reference",
                Color = Colors.Green,
            };
            view.TabGroups = new ObservableCollection<TabGroup> { development, reference };
            TabViewItem home = new() { Header = "Home" };
            TabViewItem first = new() { Header = "First", GroupId = "dev" };
            TabViewItem notes = new() { Header = "Notes" };
            TabViewItem last = new() { Header = "Last", GroupId = "dev" };
            TabViewItem readme = new() { Header = "README", GroupId = "ref" };
            foreach (TabViewItem tab in new[] { home, first, notes, last, readme })
            {
                view.Items.Add(tab);
            }

            Layout(view);
            Assert.Equal(new[] { home, first, notes, last, readme }, view.Items.Cast<TabViewItem>());
            Assert.Equal(5, view.Items.Count);
            Assert.Equal(2, GroupLayer(view).Children.Count);
            Assert.True(X(home, view) < X(GroupHeader(view, development), view));
            Assert.True(X(GroupHeader(view, development), view) < X(first, view));
            Assert.True(X(first, view) < X(last, view));
            Assert.True(X(last, view) < X(notes, view));
            Assert.True(X(notes, view) < X(GroupHeader(view, reference), view));
            Assert.True(X(GroupHeader(view, reference), view) < X(readme, view));
            Assert.All(
                new UIElement[] { home, first, notes, last, readme, GroupHeader(view, development) },
                element => Assert.Equal(0, element.TranslatePoint(new Point(0, 0), GroupLayer(view)).Y, 5)
            );
            view.Measure(new Size(240, 400));
            view.Arrange(new Rect(0, 0, 240, 400));
            view.UpdateLayout();
            Assert.True(X(first, view) < X(last, view));
            Assert.True(X(last, view) < X(notes, view));
        });

    [Fact]
    public void Groups_CollapsePreservesSelectionContentContainerAndVisibilityBinding() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            TabGroup group = new() { Id = "dev", Header = "Development" };
            view.TabGroups = new[] { group };
            TextBlock content = new() { Text = "selected content" };
            TabViewItem tab = new()
            {
                Header = "Document",
                Content = content,
                GroupId = "dev",
            };
            VisibilityState state = new();
            BindingOperations.SetBinding(
                tab,
                UIElement.VisibilityProperty,
                new Binding(nameof(VisibilityState.Value)) { Source = state }
            );
            view.Items.Add(tab);
            view.SelectedItem = tab;
            Layout(view);
            BindingExpression binding = BindingOperations.GetBindingExpression(
                tab,
                UIElement.VisibilityProperty
            )!;
            GroupHeader(view, group).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(view);
            Assert.False(group.IsExpanded);
            Assert.Equal(Visibility.Collapsed, tab.Visibility);
            Assert.Same(tab, view.SelectedItem);
            Assert.Same(content, view.SelectedContent);
            Assert.Same(
                content,
                Assert
                    .IsType<ContentPresenter>(view.Template.FindName("PART_SelectedContentHost", view))
                    .Content
            );
            Assert.Same(tab, view.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Same(binding, BindingOperations.GetBindingExpression(tab, UIElement.VisibilityProperty));
            Assert.Equal(3, GroupHeader(view, group).BorderThickness.Bottom);
            state.Value = Visibility.Hidden;
            group.IsExpanded = true;
            Layout(view);
            Assert.Equal(Visibility.Hidden, tab.Visibility);
            state.Value = Visibility.Visible;
            Assert.Equal(Visibility.Visible, tab.Visibility);
        });

    [Fact]
    public void Groups_DynamicDefinitionsMembershipAndNullRestoreTheOriginalTabs() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            ObservableCollection<TabGroup> groups = new();
            view.TabGroups = groups;
            TabViewItem tab = new() { Header = "Document", GroupId = "dev" };
            view.Items.Add(tab);
            Layout(view);
            Assert.Equal(Visibility.Visible, tab.Visibility);
            TabGroup group = new()
            {
                Id = "dev",
                Header = "Development",
                IsExpanded = false,
            };
            groups.Add(group);
            Layout(view);
            Assert.Equal(Visibility.Collapsed, tab.Visibility);
            group.Header = "Renamed";
            group.Color = Colors.Orange;
            Assert.Equal(
                Colors.Orange,
                Assert.IsType<SolidColorBrush>(GroupHeader(view, group).BorderBrush).Color
            );
            Assert.Contains(
                "Renamed",
                System.Windows.Automation.AutomationProperties.GetName(GroupHeader(view, group))
            );
            tab.GroupId = null;
            Layout(view);
            Assert.Equal(Visibility.Visible, tab.Visibility);
            tab.GroupId = "dev";
            Assert.Equal(Visibility.Collapsed, tab.Visibility);
            groups.Remove(group);
            Layout(view);
            Assert.Equal(Visibility.Visible, tab.Visibility);
            Assert.Empty(GroupLayer(view).Children.Cast<UIElement>());
            groups.Add(group);
            Layout(view);
            view.TabGroups = null;
            Layout(view);
            Assert.Equal(Visibility.Visible, tab.Visibility);
            Assert.Empty(GroupLayer(view).Children.Cast<UIElement>());
            Reorder(view, 0, 0);
        });

    [Fact]
    public void Groups_ModelBindingSourceExchangeAddAndCloseReuseExistingOperations() =>
        RunSta(() =>
        {
            TabView view = CreateView();

            // A standalone layout test must raise Initialized, as a XAML/window host does.
            view.BeginInit();
            view.EndInit();
            TabGroup group = new() { Id = "dev", Header = "Development" };
            view.TabGroups = new[] { group };
            Style style = new(typeof(TabViewItem), (Style)view.FindResource(typeof(TabViewItem)));
            style.Setters.Add(
                new Setter(
                    TabViewItem.GroupIdProperty,
                    new Binding(nameof(GroupDocument.GroupId)) { Mode = BindingMode.TwoWay }
                )
            );
            style.Setters.Add(
                new Setter(HeaderedContentControl.HeaderProperty, new Binding(nameof(GroupDocument.Header)))
            );
            view.ItemContainerStyle = style;
            GroupDocument first = new() { Header = "First", GroupId = "dev" };
            ObservableCollection<GroupDocument> source = new() { first };
            view.ItemsSource = source;
            view.SelectedItem = first;
            Layout(view);
            TabViewItem original = Assert.IsType<TabViewItem>(
                view.ItemContainerGenerator.ContainerFromIndex(0)
            );
            Assert.Same(first, original.Content);
            Assert.Same(first, view.SelectedContent);
            group.IsExpanded = false;
            Assert.Same(first, view.SelectedItem);
            Assert.Same(first, view.SelectedContent);
            first.GroupId = null;
            Assert.Equal(Visibility.Visible, original.Visibility);
            GroupDocument second = new() { Header = "Second", GroupId = "dev" };
            view.TabAdding += (_, e) => e.Item = second;
            view.AddTab();
            Layout(view);
            Assert.Equal(2, source.Count);
            Assert.Same(second, view.SelectedItem);
            Assert.Same(second, view.SelectedContent);
            Close(Assert.IsType<TabViewItem>(view.ItemContainerGenerator.ContainerFromIndex(1)));
            Layout(view);
            Assert.Single(source);
            Assert.Same(first, view.SelectedItem);
            ObservableCollection<GroupDocument> replacement = new()
            {
                new() { Header = "Replacement", GroupId = "dev" },
            };
            view.ItemsSource = replacement;
            Layout(view);
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<TabViewItem>(view.ItemContainerGenerator.ContainerFromIndex(0)).Visibility
            );
            Assert.Equal(Visibility.Visible, original.Visibility);
            view.TabGroups = new[] { new TabGroup { Id = "other" } };
            Layout(view);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<TabViewItem>(view.ItemContainerGenerator.ContainerFromIndex(0)).Visibility
            );
            group.IsExpanded = true;
            Assert.Single(GroupLayer(view).Children.Cast<UIElement>());
        });

    [Fact]
    public void Groups_TemplateAndUnloadDetachOldHeadersWithoutDuplicateClicks() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            TabGroup group = new() { Id = "dev", Header = "Development" };
            view.TabGroups = new[] { group };
            TabViewItem tab = new() { Header = "Document", GroupId = "dev" };
            view.Items.Add(tab);
            Layout(view);
            Button old = GroupHeader(view, group);
            view.OnApplyTemplate();
            Layout(view);
            old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(group.IsExpanded);
            GroupHeader(view, group).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(group.IsExpanded);
            old = GroupHeader(view, group);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(group.IsExpanded);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Layout(view);
            GroupHeader(view, group).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(group.IsExpanded);
            group.IsExpanded = false;
            view.Template = AddTemplate();
            view.ApplyTemplate();
            Assert.Equal(Visibility.Visible, tab.Visibility);
            old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(group.IsExpanded);
        });

    [Fact]
    public void Groups_ExpansionSupportsTwoWayBindingAndFrozenDefinitions() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            ExpansionState state = new();
            TabGroup group = new() { Id = "dev", Header = "Development" };
            BindingOperations.SetBinding(
                group,
                TabGroup.IsExpandedProperty,
                new Binding(nameof(ExpansionState.Expanded)) { Source = state, Mode = BindingMode.TwoWay }
            );
            view.TabGroups = new[] { group };
            view.Items.Add(new TabViewItem { Header = "Document", GroupId = "dev" });
            Layout(view);
            GroupHeader(view, group).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(state.Expanded);
            Assert.NotNull(BindingOperations.GetBindingExpression(group, TabGroup.IsExpandedProperty));
            state.Expanded = true;
            BindingOperations.GetBindingExpression(group, TabGroup.IsExpandedProperty)!.UpdateTarget();
            Assert.True(group.IsExpanded);
            TabGroup frozen = new() { Id = "dev", Header = "Frozen" };
            view.TabGroups = new[] { frozen };
            Layout(view);
            frozen.Freeze();
            Layout(view);
            Assert.False(GroupHeader(view, frozen).IsEnabled);
        });

    [Fact]
    public void Groups_ReplacingDefinitionsDetachesOldCollectionAndGroupNotifications() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            TabGroup old = new() { Id = "dev", Header = "Old" };
            ObservableCollection<TabGroup> original = new() { old };
            view.TabGroups = original;
            view.Items.Add(new TabViewItem { Header = "Document", GroupId = "dev" });
            Layout(view);
            TabGroup replacement = new() { Id = "dev", Header = "Replacement" };
            view.TabGroups = new ObservableCollection<TabGroup> { replacement };
            Layout(view);
            Button header = GroupHeader(view, replacement);
            object content = header.Content;
            old.Header = "Old changed";
            original.Add(new TabGroup { Id = "other" });
            Assert.Same(content, header.Content);
            Assert.Single(GroupLayer(view).Children.Cast<UIElement>());
            replacement.IsExpanded = false;
            Assert.Equal(Visibility.Collapsed, Assert.IsType<TabViewItem>(view.Items[0]).Visibility);
        });

    [Fact]
    public void Groups_RejectReorderingAndResumeItAfterDisablingGroups() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            view.Items.Add(new TabViewItem { Header = "First", GroupId = "dev" });
            view.Items.Add(new TabViewItem { Header = "Second" });
            view.TabGroups = new[] { new TabGroup { Id = "dev" } };
            Layout(view);
            object first = view.Items[0];
            Reorder(view, 0, 1);
            Assert.Same(first, view.Items[0]);
            TabViewItem firstTab = Assert.IsType<TabViewItem>(first);
            SetDragState(view, firstTab);
            System.Windows.DragEventArgs drop = CreateDrop(
                view,
                first,
                view,
                Assert.IsType<TabViewItem>(view.Items[1])
            );
            Invoke(view, "OnDrop", drop);
            Assert.Equal(System.Windows.DragDropEffects.None, drop.Effects);
            AssertDragCleared(view);
            view.TabGroups = null;
            Layout(view);
            Reorder(view, 0, 1);
            Assert.Same(first, view.Items[1]);
        });

    private static Canvas GroupLayer(TabView view) =>
        Assert.IsType<Canvas>(view.Template.FindName("PART_GroupHeaders", view));

    private static Button GroupHeader(TabView view, TabGroup group) =>
        GroupLayer(view).Children.OfType<Button>().Single(button => ReferenceEquals(button.Tag, group));

    private static double X(UIElement element, TabView view) =>
        element.TranslatePoint(new Point(0, 0), view).X;

    private sealed class ExpansionState
    {
        public bool Expanded { get; set; } = true;
    }

    private sealed class VisibilityState : INotifyPropertyChanged
    {
        private Visibility _value = Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Visibility Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }

    private sealed class GroupDocument : INotifyPropertyChanged
    {
        private string? _groupId;

        public string Header { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string? GroupId
        {
            get => _groupId;
            set
            {
                _groupId = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupId)));
            }
        }
    }
}
