// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Theory]
    [InlineData(BindingMode.TwoWay, false)]
    [InlineData(BindingMode.TwoWay, true)]
    [InlineData(BindingMode.OneWay, false)]
    [InlineData(BindingMode.OneWay, true)]
    public void GroupUnderline_HoverAndPressKeepGroupColorIncludingLiveColorChanges(
        BindingMode mode,
        bool pressed
    ) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments(), mode);
            TabGroup group = view.TabGroups!.First();
            System.Windows.Controls.Button header = GroupHeader(view, group);
            // WPF's IsMouseOver getter reads a cached flag as well as its read-only DP.
            MethodInfo writeFlag = typeof(UIElement).GetMethod(
                "WriteFlag",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!;
            writeFlag.Invoke(
                header,
                new[]
                {
                    Enum.Parse(writeFlag.GetParameters()[0].ParameterType, "IsMouseOverCache"),
                    (object)true,
                }
            );
            header.SetValue(
                (DependencyPropertyKey)
                    typeof(UIElement)
                        .GetField("IsMouseOverPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!
                        .GetValue(null)!,
                true
            );
            header.SetValue(
                (DependencyPropertyKey)
                    typeof(ButtonBase)
                        .GetField("IsPressedPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!
                        .GetValue(null)!,
                pressed
            );
            Assert.True(header.IsMouseOver);
            Assert.Equal(pressed, header.IsPressed);
            Border border = Assert.IsType<Border>(header.Template.FindName("ContentBorder", header));
            Assert.Equal(
                pressed ? "PressedBorderBrush" : "MouseOverBorderBrush",
                BindingOperations
                    .GetBindingExpression(border, Border.BorderBrushProperty)!
                    .ParentBinding.Path.Path
            );
            Assert.Equal(3, border.BorderThickness.Bottom);
            Assert.Equal(group.Color, Assert.IsType<SolidColorBrush>(border.BorderBrush).Color);
            group.Color = Colors.Orange;
            Assert.Equal(group.Color, Assert.IsType<SolidColorBrush>(border.BorderBrush).Color);
        });

    [Fact]
    public void GroupUnderline_AlignsWithBackgroundEdgeWithoutChangingTabOrContentBorders() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            System.Windows.Controls.Button header = GroupHeader(view, group);
            Border background = GroupBackground(view, group);
            Border outer = Assert.IsType<Border>(header.Template.FindName("ContentBorder", header));
            Border inset = Assert.IsType<Border>(header.Template.FindName("InsetBorder", header));
            Border underline = outer.BorderThickness.Bottom > 0 ? outer : inset;
            Assert.Equal(1, background.BorderThickness.Bottom);
            Assert.Equal(3, outer.BorderThickness.Bottom + inset.BorderThickness.Bottom);
            Assert.Equal(
                Canvas.GetTop(background) + background.Height,
                underline.TranslatePoint(new Point(0, underline.ActualHeight), BackgroundLayer(view)).Y,
                5
            );
            Assert.True(header.SnapsToDevicePixels);
            TabViewItem selected = Container(view, view.SelectedItem);
            Border tabBorder = Assert.IsType<Border>(selected.Template.FindName("Border", selected));
            Assert.Equal(new Thickness(1, 1, 1, 0), tabBorder.BorderThickness);
            Border contentBorder = Assert.IsType<Border>(view.Template.FindName("Border", view));
            Assert.Equal(new Thickness(0, 1, 0, 0), contentBorder.BorderThickness);
            Assert.Equal(
                Canvas.GetTop(background) + background.Height,
                contentBorder.TranslatePoint(new Point(0, 0), BackgroundLayer(view)).Y,
                5
            );
            Assert.Equal(
                FontWeights.Bold,
                ((System.Windows.Controls.TextBlock)((StackPanel)header.Content).Children[1]).FontWeight
            );
        });

    [Theory]
    [InlineData(1.0, BindingMode.TwoWay)]
    [InlineData(1.25, BindingMode.TwoWay)]
    [InlineData(1.5, BindingMode.TwoWay)]
    [InlineData(1.0, BindingMode.OneWay)]
    [InlineData(1.25, BindingMode.OneWay)]
    [InlineData(1.5, BindingMode.OneWay)]
    public void GroupUnderline_RasterizedThicknessMatchesThreeDipForBothBindingModes(
        double scale,
        BindingMode mode
    ) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments(), mode);
            TabGroup group = view.TabGroups!.First();
            group.Color = Colors.DodgerBlue;
            Layout(view);
            FrameworkElement header = GroupHeader(view, group);
            int width = (int)Math.Ceiling(header.ActualWidth * scale);
            int height = (int)Math.Ceiling(header.ActualHeight * scale);
            RenderTargetBitmap bitmap = new(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(header);
            byte[] pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            int coloredRows = 0;
            // Sample the straight middle of the underline, excluding the rounded corners and text.
            for (int y = Math.Max(0, height - (int)Math.Ceiling(6 * scale)); y < height; y++)
            {
                int pixel = (y * width + width / 2) * 4;
                if (
                    Math.Abs(pixels[pixel] - group.Color.B) <= 1
                    && Math.Abs(pixels[pixel + 1] - group.Color.G) <= 1
                    && Math.Abs(pixels[pixel + 2] - group.Color.R) <= 1
                    && pixels[pixel + 3] == 255
                )
                {
                    coloredRows++;
                }
            }
            Assert.InRange(coloredRows, (int)Math.Floor(3 * scale), (int)Math.Ceiling(3 * scale));
        });

    [Fact]
    public void GroupUnderline_WritableAndReadOnlySamplesMatchEvenWithDifferentSelections() =>
        RunSta(() =>
        {
            var source = DragDocuments();
            TabView writable = CreateGroupDragView(source, BindingMode.TwoWay);
            TabView readOnly = CreateGroupDragView(source, BindingMode.OneWay);
            readOnly.CanAddTabs = false;
            foreach (var item in source)
            {
                Container(readOnly, item).IsClosable = false;
            }
            writable.SelectedItem = source[0];
            readOnly.SelectedItem = source[1];
            Layout(writable);
            Layout(readOnly);
            foreach (TabGroup group in writable.TabGroups!)
            {
                TabGroup other = readOnly.TabGroups!.Single(candidate => candidate.Id == group.Id);
                Assert.Equal(new Thickness(0, 0, 0, 3), GroupHeader(writable, group).BorderThickness);
                Assert.Equal(
                    GroupHeader(writable, group).BorderThickness,
                    GroupHeader(readOnly, other).BorderThickness
                );
            }
        });
}
