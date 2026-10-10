// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Fact]
    public void GroupToolTip_DefaultTracksExpansionAndResourceChanges() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            Assert.Null(group.ToolTip);
            Assert.Equal(DependencyProperty.UnsetValue, group.ReadLocalValue(TabGroup.ToolTipProperty));
            var header = GroupHeader(view, group);
            Assert.Equal("Collapse group", header.ToolTip);
            view.Resources["TabViewGroupCollapseToolTip"] = "グループを折りたたむ";
            Assert.Equal("グループを折りたたむ", header.ToolTip);
            group.IsExpanded = false;
            Assert.Equal("Expand group", header.ToolTip);
            view.Resources["TabViewGroupExpandToolTip"] = "グループを展開する";
            Assert.Equal("グループを展開する", header.ToolTip);
        });

    [Fact]
    public void GroupToolTip_CustomStringAndNullUpdateWithoutLayoutOrDragChanges() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            var header = GroupHeader(view, group);
            var size = header.DesiredSize;
            Assert.True(MenuResult<bool>(view, "TryBeginWholeGroupDrag", header, new Point(0, 0)));
            group.ToolTip = "My tooltip";
            Assert.Equal("My tooltip", header.ToolTip);
            Assert.Same(header, GroupHeader(view, group));
            Assert.Equal(size, header.DesiredSize);
            Assert.Same(group, MenuField<TabGroup>(view, "_draggedGroup"));
            group.ToolTip = null;
            Assert.True(RaiseGroupToolTipOpening(header).Handled);
            group.ClearValue(TabGroup.ToolTipProperty);
            Assert.False(RaiseGroupToolTipOpening(header).Handled);
            Assert.Equal("Collapse group", header.ToolTip);
        });

    [Fact]
    public void GroupToolTip_ExplicitNullAfterUnsetSuppressesOpeningAndClearValueRestoresDefault() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            group.ToolTip = null;
            Assert.True(RaiseGroupToolTipOpening(GroupHeader(view, group)).Handled);
            group.ClearValue(TabGroup.ToolTipProperty);
            Assert.False(RaiseGroupToolTipOpening(GroupHeader(view, group)).Handled);
            Assert.Equal("Collapse group", GroupHeader(view, group).ToolTip);
        });

    [Theory]
    [InlineData(null)]
    [InlineData("Initial tooltip")]
    public void GroupToolTip_BindingIsPreservedIncludingNullAndSourceUpdates(string? initial) =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            ToolTipModel model = new() { Value = initial };
            BindingExpressionBase expression = BindingOperations.SetBinding(
                group,
                TabGroup.ToolTipProperty,
                new Binding(nameof(ToolTipModel.Value)) { Source = model, Mode = BindingMode.OneWay }
            );
            Assert.Equal(initial == null, RaiseGroupToolTipOpening(GroupHeader(view, group)).Handled);
            if (initial != null)
            {
                Assert.Equal(initial, GroupHeader(view, group).ToolTip);
            }
            model.Value = "Changed tooltip";
            Assert.Equal(model.Value, GroupHeader(view, group).ToolTip);
            model.Value = null;
            Assert.True(RaiseGroupToolTipOpening(GroupHeader(view, group)).Handled);
            group.IsExpanded = false;
            group.Header = "Renamed";
            view.SelectedItem = view.Items[1];
            Assert.True(RaiseGroupToolTipOpening(GroupHeader(view, group)).Handled);
            Assert.Same(expression, BindingOperations.GetBindingExpression(group, TabGroup.ToolTipProperty));
            Assert.False(group.CanFreeze);
        });

    [Fact]
    public void GroupToolTip_PreservesBindingInstalledOnHeader() =>
        RunSta(() =>
        {
            TabView view = CreateGroupDragView(DragDocuments());
            TabGroup group = view.TabGroups!.First();
            var header = GroupHeader(view, group);
            ToolTipModel model = new() { Value = "Header binding" };
            BindingExpressionBase expression = BindingOperations.SetBinding(
                header,
                FrameworkElement.ToolTipProperty,
                new Binding(nameof(ToolTipModel.Value)) { Source = model }
            );
            group.ToolTip = "Group tooltip";
            group.IsExpanded = false;
            Assert.Equal("Group tooltip", header.ToolTip);
            Assert.Same(
                expression,
                BindingOperations.GetBindingExpression(header, FrameworkElement.ToolTipProperty)
            );
            group.ClearValue(TabGroup.ToolTipProperty);
            Assert.Equal(model.Value, header.ToolTip);
        });

    [Fact]
    public void GroupToolTip_SharedVisualUsesOnePresenterWithoutReparentingApplicationContent() =>
        RunSta(() =>
        {
            var source = DragDocuments();
            TabView first = CreateGroupDragView(source);
            TabView second = CreateGroupDragView(source);
            TabGroup group = first.TabGroups!.First();
            second.TabGroups = first.TabGroups;
            Layout(second);
            var visual = new System.Windows.Controls.TextBlock { Text = "Custom UI content" };
            group.ToolTip = visual;
            var tooltip = Assert.IsType<System.Windows.Controls.ToolTip>(GroupHeader(first, group).ToolTip);
            Assert.Same(tooltip, GroupHeader(second, group).ToolTip);
            Assert.Same(visual, tooltip.Content);
            Assert.Same(tooltip, LogicalTreeHelper.GetParent(visual));
            var parent = new StackPanel();
            var parented = new System.Windows.Controls.TextBlock();
            parent.Children.Add(parented);
            group.ToolTip = parented;
            Assert.True(RaiseGroupToolTipOpening(GroupHeader(first, group)).Handled);
            Assert.Same(parent, LogicalTreeHelper.GetParent(parented));
        });

    [Fact]
    public void GroupToolTip_FrozenDefinitionAndCloneRetainLocalValue() =>
        RunSta(() =>
        {
            TabGroup group = new()
            {
                Id = "a",
                Header = "A",
                ToolTip = "Frozen tooltip",
            };
            group.Freeze();
            TabView view = CreateGroupDragView(DragDocuments());
            view.TabGroups = new[] { group };
            Layout(view);
            Assert.Equal("Frozen tooltip", GroupHeader(view, group).ToolTip);
            Assert.Throws<InvalidOperationException>(() => group.ToolTip = "Changed");
            TabGroup copy = (TabGroup)group.Clone();
            Assert.Equal(group.ToolTip, copy.ToolTip);
            Assert.False(copy.IsFrozen);
        });

    private static ToolTipEventArgs RaiseGroupToolTipOpening(FrameworkElement header)
    {
        var args = (ToolTipEventArgs)
            Activator.CreateInstance(
                typeof(ToolTipEventArgs),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new object[] { true },
                null
            )!;
        header.RaiseEvent(args);
        return args;
    }

    private sealed class ToolTipModel : INotifyPropertyChanged
    {
        private object? _value;
        public event PropertyChangedEventHandler? PropertyChanged;
        public object? Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }
}
