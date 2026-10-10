// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Windows.Automation.Peers;
using Wpf.Ui.Controls;

namespace Wpf.Ui.UnitTests.Controls;

public partial class TabViewTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(5, false)]
    [InlineData(5, true)]
    public void GroupAutomation_QueriesDuringContainerRemovalDoNotUseStaleIndices(int index, bool collapsed) =>
        RunSta(() =>
        {
            var source = WholeGroupDocuments();
            AutomationPeer? automation = null;
            int queries = 0;
            // Query after the source shrinks, before the view/generator consume the notification.
            source.CollectionChanged += (_, _) =>
            {
                if (automation != null)
                {
                    queries++;
                    Invoke(automation, "GetChildrenCore");
                }
            };
            TabView view = CreateGroupDragView(source);
            var removed = source[index];
            view.SelectedItem = removed;
            foreach (TabGroup group in view.TabGroups!)
            {
                group.IsExpanded = !collapsed;
            }
            Layout(view);
            automation = UIElementAutomationPeer.CreatePeerForElement(view);
            Close(Container(view, removed));
            Assert.True(queries > 0);
            Assert.DoesNotContain(removed, source);
            Layout(view);
            automation.InvalidatePeer();
            PumpHeaderDispatcher();
            var children = automation.GetChildren();
            Assert.Equal(source.Count, children.Count(peer => peer is ItemAutomationPeer));
            Assert.DoesNotContain(children.OfType<ItemAutomationPeer>(), peer => ReferenceEquals(peer.Item, removed));
        });
}
