using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using HollowKnightTAS.Companion.Controls;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StudioResourceBoundsTests
    {
        [TestMethod]
        public void BusyUiKeepsOnlyLatestProgressAndAcceptsNextBatch()
        {
            var queue = new Queue<Action>();
            var updates = new LatestUiUpdate();
            var displayed = -1;
            for (var i = 0; i < 100000; i++)
            {
                var frame = i;
                updates.Post(() => displayed = frame, queue.Enqueue);
            }
            Assert.AreEqual(1, queue.Count);
            queue.Dequeue()();
            Assert.AreEqual(99999, displayed);
            updates.Post(() => displayed = 100000, queue.Enqueue);
            Assert.AreEqual(1, queue.Count);
            queue.Dequeue()();
            Assert.AreEqual(100000, displayed);
        }

        [TestMethod]
        public void AutomationOfTenMillionFramesDoesNotExpandMovie()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var header = new MovieV2Header("game", "api", "mod", "profile",
                        MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450);
                    var rows = new VirtualInputRows(new MovieV2Document("large.hktas", header,
                        new[] { new NativeFrameRun(10000000, Array.Empty<GameInputSample>(),
                            new MovieSourceSpan("large.hktas", 1, 1, 1)) }), 0);
                    var grid = new InputFrameGrid { ItemsSource = rows, AutoGenerateColumns = false,
                        EnableRowVirtualization = true, Height = 400, Width = 800 };
                    grid.Columns.Add(new DataGridTextColumn { Header = "Frame" });
                    grid.Measure(new Size(800, 400));
                    grid.Arrange(new Rect(0, 0, 800, 400));
                    grid.UpdateLayout();
                    var peer = UIElementAutomationPeer.CreatePeerForElement(grid)!;
                    Assert.IsNotInstanceOfType(peer, typeof(DataGridAutomationPeer));
                    for (var i = 0; i < 100; i++)
                    {
                        var children = peer.GetChildren();
                        Assert.IsTrue(children == null || children.Count <= 128);
                        if (children != null) foreach (var child in children) child.GetName();
                    }
                    Assert.IsTrue(rows.CachedCount < 128, "Automation must not enumerate virtual rows.");
                    Assert.IsNull(peer.GetPattern(PatternInterface.Grid));
                    Assert.IsNull(peer.GetPattern(PatternInterface.ItemContainer));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Bounded traversal timed out.");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
