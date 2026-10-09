// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Fact]
    public void HeaderStrip_BackgroundCoversHeaderAndScatteredMembersButNotUngroupedTabs() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            source.Add(new() { Header = "Second A", GroupId = "a" });
            TabView view = CreateGroupDragView(source);
            TabGroup group = view.TabGroups!.First();
            Border background = GroupBackground(view, group);
            FrameworkElement header = GroupHeader(view, group);
            FrameworkElement last = Container(view, source[3]);
            Canvas layer = BackgroundLayer(view);
            double start = Canvas.GetLeft(background);
            double end = start + background.Width;
            Assert.Equal(header.TranslatePoint(new Point(0, 0), layer).X - header.Margin.Left, start, 5);
            Assert.Equal(last.TranslatePoint(new Point(0, 0), layer).X + last.ActualWidth, end, 5);
            Assert.True(Container(view, source[1]).TranslatePoint(new Point(0, 0), layer).X >= end);
            Assert.Equal(2, layer.Children.Count);
            Assert.False(layer.IsHitTestVisible);
            Assert.False(background.IsHitTestVisible);
            Assert.Equal(new CornerRadius(8), background.CornerRadius);
        });

    [Fact]
    public void HeaderStrip_ColorChangesImmediatelyAndCollapseShrinksBackground() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            Border background = GroupBackground(view, group);
            double width = background.Width;
            group.Color = Colors.Orange;
            Color color = Assert.IsType<SolidColorBrush>(background.Background).Color;
            Assert.Equal(Colors.Orange.R, color.R);
            Assert.Equal(Colors.Orange.G, color.G);
            Assert.InRange(color.A, (byte)1, (byte)63);
            group.IsExpanded = false;
            Layout(view);
            Assert.True(background.Width < width);
            FrameworkElement header = GroupHeader(view, group);
            Assert.Equal(header.ActualWidth + header.Margin.Left + header.Margin.Right, background.Width, 5);
            Assert.Same(background, GroupBackground(view, group));
        });

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void HeaderStrip_ThemeBackgroundDoesNotReplaceUserValuesOrSelection(string theme) =>
        RunSta(() =>
        {
            TabView view = CreateView();
            view.Resources.MergedDictionaries[0].Source = new Uri(
                $"pack://application:,,,/Wpf.Ui;component/Resources/Theme/{theme}.xaml"
            );
            TabGroup group = new() { Id = "a", Color = Colors.Green };
            view.TabGroups = new[] { group };
            SolidColorBrush userBackground = new(Colors.Purple);
            TabViewItem tab = new()
            {
                Header = "A",
                GroupId = "a",
                Background = userBackground,
                Tag = "user",
                Content = "content",
            };
            view.Items.Add(tab);
            view.SelectedItem = tab;
            Layout(view);
            object template = tab.Template;
            object style = tab.Style;
            group.Color = Colors.Blue;
            Layout(view);
            Assert.Same(userBackground, tab.Background);
            Assert.Equal("user", tab.Tag);
            Assert.Same(template, tab.Template);
            Assert.Same(style, tab.Style);
            Assert.Same(tab, view.SelectedItem);
            Assert.Equal("content", view.SelectedContent);
            Assert.InRange(
                Assert.IsType<SolidColorBrush>(GroupBackground(view, group).Background).Color.A,
                (byte)1,
                (byte)63
            );
            Assert.NotNull(tab.FocusVisualStyle);
        });

    [Fact]
    public void HeaderStrip_HighContrastUsesSystemFillAndOutline() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            Invoke(view, "UpdateGroupBackgroundAppearance", true);
            Border background = GroupBackground(view, view.TabGroups!.First());
            Assert.Same(SystemColors.WindowBrush, background.Background);
            Assert.Same(SystemColors.WindowTextBrush, background.BorderBrush);
            Assert.Equal(new Thickness(1), background.BorderThickness);
        });

    [Fact]
    public void HeaderStrip_OverflowButtonsTrackBothEndsAndKeepAddOutsideScroller() =>
        RunSta(() =>
        {
            TabView view = LongHeaderView();
            ScrollViewer scroller = HeaderScroller(view);
            Assert.Equal(ScrollBarVisibility.Hidden, scroller.HorizontalScrollBarVisibility);
            Assert.Equal(Visibility.Visible, ScrollButton(view, false).Visibility);
            Assert.Equal(Visibility.Visible, ScrollButton(view, true).Visibility);
            Assert.False(ScrollButton(view, false).IsEnabled);
            Assert.True(ScrollButton(view, true).IsEnabled);
            ScrollImmediately(view, scroller.ScrollableWidth);
            Assert.True(ScrollButton(view, false).IsEnabled);
            Assert.False(ScrollButton(view, true).IsEnabled);
            Assert.True(scroller.HorizontalOffset > 0);
            var add = (FrameworkElement)view.Template.FindName("PART_AddButton", view);
            double x = X(add, view);
            ScrollImmediately(view, 0);
            Assert.Equal(x, X(add, view));
        });

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void HeaderStrip_ScrollIconsHaveSpaceInsideFluentButtons(string theme) =>
        RunSta(() =>
        {
            TabView view = LongHeaderView();
            view.Resources.MergedDictionaries[0].Source = new Uri(
                $"pack://application:,,,/Wpf.Ui;component/Resources/Theme/{theme}.xaml"
            );
            LayoutWidth(view, 320);
            foreach (bool right in new[] { false, true })
            {
                System.Windows.Controls.Button button = ScrollButton(view, right);
                SymbolIcon icon = Assert.IsType<SymbolIcon>(button.Content);
                Assert.Equal(right ? SymbolRegular.ChevronRight24 : SymbolRegular.ChevronLeft24, icon.Symbol);
                double contentWidth =
                    button.ActualWidth
                    - button.Padding.Left
                    - button.Padding.Right
                    - button.BorderThickness.Left
                    - button.BorderThickness.Right;
                Assert.True(
                    contentWidth >= icon.FontSize,
                    $"Only {contentWidth}px available for a {icon.FontSize}px icon."
                );
                Assert.True(icon.ActualWidth >= icon.FontSize);
                Assert.True(icon.ActualHeight >= icon.FontSize);
            }
        });

    [Fact]
    public void HeaderStrip_ButtonsHideWhenFitsAndAfterCollapseOrResize() =>
        RunSta(() =>
        {
            TabView view = LongHeaderView();
            TabGroup group = view.TabGroups!.First();
            ScrollImmediately(view, HeaderScroller(view).ScrollableWidth);
            group.IsExpanded = false;
            LayoutWidth(view, 320);
            Assert.Equal(Visibility.Collapsed, ScrollButton(view, true).Visibility);
            Assert.Equal(Visibility.Collapsed, ScrollButton(view, false).Visibility);
            Assert.Equal(0, HeaderScroller(view).HorizontalOffset);
            group.IsExpanded = true;
            LayoutWidth(view, 320);
            Assert.Equal(Visibility.Visible, ScrollButton(view, true).Visibility);
            ScrollImmediately(view, HeaderScroller(view).ScrollableWidth);
            LayoutWidth(view, 900);
            Assert.InRange(HeaderScroller(view).HorizontalOffset, 0, HeaderScroller(view).ScrollableWidth);
            LayoutWidth(view, 5000);
            Assert.Equal(Visibility.Collapsed, ScrollButton(view, true).Visibility);
            Assert.Equal(Visibility.Collapsed, ScrollButton(view, false).Visibility);
            Assert.Equal(0, HeaderScroller(view).HorizontalOffset);
        });

    [Fact]
    public void HeaderStrip_SmallResizeCanHideButtonsWithoutStickyOverflow() =>
        RunSta(() =>
        {
            TabView view = LongHeaderView();
            double extent = HeaderScroller(view).ExtentWidth;
            FrameworkElement add = (FrameworkElement)view.Template.FindName("PART_AddButton", view);
            LayoutWidth(view, extent + add.ActualWidth + add.Margin.Left + add.Margin.Right + 2);
            Assert.Equal(Visibility.Collapsed, ScrollButton(view, true).Visibility);
            Assert.Equal(Visibility.Collapsed, ScrollButton(view, false).Visibility);
        });

    [Fact]
    public void HeaderStrip_ScrollButtonStartsBoundedAnimationAndUnloadStopsIt() =>
        RunSta(() =>
        {
            TabView view = LongHeaderView();
            DependencyProperty position = ScrollPositionProperty();
            ScrollButton(view, true)
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            double target = Assert.IsType<double>(view.GetAnimationBaseValue(position));
            Assert.InRange(target, 1, HeaderScroller(view).ScrollableWidth);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            double stopped = Assert.IsType<double>(view.GetAnimationBaseValue(position));
            ScrollButton(view, true)
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Equal(stopped, view.GetAnimationBaseValue(position));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            LayoutWidth(view, 320);
            ScrollButton(view, true)
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.True(Assert.IsType<double>(view.GetAnimationBaseValue(position)) > stopped);
        });

    [Fact]
    public void HeaderStrip_ReapplyingTemplateDetachesOldButtons() =>
        RunSta(() =>
        {
            TabView view = LongHeaderView();
            System.Windows.Controls.Button old = ScrollButton(view, true);
            ControlTemplate template = view.Template;
            view.Template = AddTemplate();
            view.ApplyTemplate();
            view.Template = template;
            LayoutWidth(view, 320);
            Assert.NotSame(old, ScrollButton(view, true));
            double position = Assert.IsType<double>(view.GetAnimationBaseValue(ScrollPositionProperty()));
            old.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Equal(position, view.GetAnimationBaseValue(ScrollPositionProperty()));
            ScrollButton(view, true)
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.True(
                Assert.IsType<double>(view.GetAnimationBaseValue(ScrollPositionProperty())) > position
            );
        });

    [Fact]
    public void HeaderStrip_MissingNewPartsFallsBackToExistingScrollbarAndGrouping() =>
        RunSta(() =>
        {
            TabView view = CreateView();
            view.Template = Assert.IsType<ControlTemplate>(
                System.Windows.Markup.XamlReader.Parse(
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:ui='clr-namespace:Wpf.Ui.Controls;assembly=Wpf.Ui' TargetType='ui:TabView'><Grid><ScrollViewer x:Name='PART_HeaderScroller' HorizontalScrollBarVisibility='Auto' VerticalScrollBarVisibility='Disabled'><Grid><ui:TabViewPanel x:Name='HeaderPanel' IsItemsHost='True'/><Canvas x:Name='PART_GroupHeaders'/></Grid></ScrollViewer></Grid></ControlTemplate>"
                )
            );
            view.TabGroups = new[]
            {
                new TabGroup { Id = "a", Header = "Development" },
            };
            for (int i = 0; i < 12; i++)
            {
                view.Items.Add(new TabViewItem { Header = $"Long document {i}", GroupId = "a" });
            }

            LayoutWidth(view, 320);
            Assert.Equal(ScrollBarVisibility.Auto, HeaderScroller(view).HorizontalScrollBarVisibility);
            Assert.NotEmpty(GroupLayer(view).Children.Cast<UIElement>());
            Assert.True(
                HeaderScroller(view).ScrollableWidth > 0,
                $"Extent={HeaderScroller(view).ExtentWidth}, viewport={HeaderScroller(view).ViewportWidth}"
            );
        });

    [Fact]
    public void HeaderStrip_DisablingGroupsRestoresWrappingAndRemovesBackgroundAndButtons() =>
        RunSta(() =>
        {
            TabView view = LongHeaderView();
            ScrollImmediately(view, HeaderScroller(view).ScrollableWidth);
            view.TabGroups = null;
            LayoutWidth(view, 320);
            Assert.Empty(BackgroundLayer(view).Children.Cast<UIElement>());
            Assert.Equal(Visibility.Collapsed, ScrollButton(view, false).Visibility);
            Assert.Equal(Visibility.Collapsed, ScrollButton(view, true).Visibility);
            Assert.Equal(ScrollBarVisibility.Disabled, HeaderScroller(view).HorizontalScrollBarVisibility);
            Assert.Contains(
                view.Items.Cast<TabViewItem>(),
                tab => tab.TranslatePoint(new Point(0, 0), view).Y > 0
            );
        });

    [Fact]
    public void HeaderStrip_ScrolledDropUsesCorrectModelAndOverlayCoordinates() =>
        RunSta(() =>
        {
            ObservableCollection<GroupDocument> source = DragDocuments();
            for (int i = 0; i < 12; i++)
            {
                source.Add(new() { Header = $"Long document {i}", GroupId = "a" });
            }

            TabView view = CreateGroupDragView(source);
            LayoutWidth(view, 320);
            GroupDocument dragged = source[0];
            GroupDocument destination = source[2];
            ScrollImmediately(view, HeaderScroller(view).ScrollableWidth);
            TabViewItem target = Container(view, destination);
            DragEventArgs drop = GroupDrop(view, dragged, target, true);
            Invoke(view, "OnDragOver", drop);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            view.UpdateLayout();
            Border marker = Assert.IsType<Border>(DropOverlay(view).Children[0]);
            Assert.InRange(X(marker, view) - X(target, view) - target.ActualWidth, -2, 2);
            Invoke(view, "OnDrop", drop);
            Assert.Equal("b", dragged.GroupId);
            Assert.Equal(DragDropEffects.Move, drop.Effects);
            AssertDragCleared(view);
            Assert.Equal(Visibility.Collapsed, marker.Visibility);
        });

    [Fact]
    public void HeaderStrip_MouseWheelScrollsHeadersAndLeavesBoundaryWheelForParent() =>
        RunSta(() =>
        {
            TabView view = LongHeaderView();
            MouseWheelEventArgs wheel = new(Mouse.PrimaryDevice, 0, -120)
            {
                RoutedEvent = Mouse.PreviewMouseWheelEvent,
            };
            HeaderScroller(view).RaiseEvent(wheel);
            Assert.True(wheel.Handled);
            Assert.True(Assert.IsType<double>(view.GetAnimationBaseValue(ScrollPositionProperty())) > 0);
            ScrollImmediately(view, HeaderScroller(view).ScrollableWidth);
            MouseWheelEventArgs boundary = new(Mouse.PrimaryDevice, 0, -120)
            {
                RoutedEvent = Mouse.PreviewMouseWheelEvent,
            };
            HeaderScroller(view).RaiseEvent(boundary);
            Assert.False(boundary.Handled);
        });

    private static TabView LongHeaderView()
    {
        TabView view = CreateView();
        view.TabGroups = new[]
        {
            new TabGroup { Id = "a", Header = "Development" },
        };
        for (int i = 0; i < 12; i++)
        {
            view.Items.Add(new TabViewItem { Header = $"Long document {i}", GroupId = "a" });
        }

        LayoutWidth(view, 320);
        return view;
    }

    private static void LayoutWidth(TabView view, double width)
    {
        view.Measure(new Size(width, 400));
        view.Arrange(new Rect(0, 0, width, 400));
        view.UpdateLayout();
    }

    private static Canvas BackgroundLayer(TabView view) =>
        (Canvas)view.Template.FindName("PART_GroupBackgrounds", view);

    private static Border GroupBackground(TabView view, TabGroup group)
    {
        IDictionary backgrounds = Assert.IsAssignableFrom<IDictionary>(
            typeof(TabView)
                .GetField("_groupBackgrounds", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(view)
        );
        return Assert.IsType<Border>(backgrounds[group]);
    }

    private static ScrollViewer HeaderScroller(TabView view) =>
        (ScrollViewer)view.Template.FindName("PART_HeaderScroller", view);

    private static System.Windows.Controls.Button ScrollButton(TabView view, bool right) =>
        Assert.IsAssignableFrom<System.Windows.Controls.Button>(
            view.Template.FindName(right ? "PART_ScrollRightButton" : "PART_ScrollLeftButton", view)
        );

    private static DependencyProperty ScrollPositionProperty() =>
        Assert.IsType<DependencyProperty>(
            typeof(TabView)
                .GetField("HeaderScrollPositionProperty", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)
        );

    private static void ScrollImmediately(TabView view, double offset)
    {
        Invoke(view, "ScrollHeaderTo", offset, false);
        view.UpdateLayout();
    }
}
