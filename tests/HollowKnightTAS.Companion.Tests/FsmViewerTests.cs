using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HollowKnightTAS.Companion.Controls;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class FsmViewerTests
    {
        private static void Sta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)));
            if (failure != null) throw new AssertFailedException(failure.ToString(), failure);
        }
        private static FsmViewerController Controller() => new(() => null, () => false,
            (_, _, _) => throw new InvalidOperationException("Unexpected IPC"));
        private static FsmSelection Target(string id, string path = "/Boss", string name = "Control", string[]? ancestors = null) =>
            new() { Id = id, ObjectId = id + "-object", Path = path, Name = name, Scene = "Arena", Ancestors = ancestors ?? Array.Empty<string>() };
        [TestMethod]
        public void ColdRebuildRebindsOnlyUniqueTargetsAndDoesNotCarryCachedGraphs() => Sta(() =>
        {
            using var c = Controller(); var old = Target("old");
            c.ApplyCatalog(Array.Empty<FsmObject>(), new[] { old });
            old.Selected = true;
            c.ApplyCatalog(Array.Empty<FsmObject>(), new[] { Target("new") });
            Assert.IsTrue(c.Targets.Single().Selected);
            Assert.AreEqual("new", c.Targets.Single().Id);
            Assert.AreEqual(0, c.Cards.Count);
            c.ApplyCatalog(Array.Empty<FsmObject>(), new[] { Target("clone1"), Target("clone2") });
            Assert.IsTrue(c.Targets.All(t => !t.Selected), "Ambiguous names must never guess an instance.");
        });
        [TestMethod]
        public void ObjectSelectionUsesIdentityAndAncestryRatherThanPathPrefix() => Sta(() =>
        {
            using var c = Controller();
            c.ApplyCatalog(Array.Empty<FsmObject>(), new[] { Target("boss"), Target("child", "/Boss/Weapon", ancestors: new[] { "boss-object" }), Target("other", "/Boss/Clone") });
            c.SelectObject(new("boss-object", "/Boss", "Arena", true));
            Assert.IsTrue(c.Targets[0].Selected); Assert.IsTrue(c.Targets[1].Selected); Assert.IsFalse(c.Targets[2].Selected);
            var first = c.Targets[0];
            c.ApplyCatalog(Array.Empty<FsmObject>(), new[] { Target("boss"), Target("child", "/Boss/Weapon"), Target("other", "/Boss/Clone") });
            Assert.AreSame(first, c.Targets[0], "Catalog refresh must preserve checkbox bindings.");
        });
        [TestMethod]
        public void ObjectSelectionHasAnExplicit32MachineLimit() => Sta(() =>
        {
            using var c = Controller(); c.ApplyCatalog(Array.Empty<FsmObject>(), Enumerable.Range(0, 50).Select(i => Target("f" + i, ancestors: new[] { "root" })));
            c.SelectObject(new("root", "/", "Arena", true));
            Assert.AreEqual(32, c.Targets.Count(t => t.Selected));
        });
        [TestMethod]
        public void GraphPreservesCyclesGlobalsAndUnloadedActions()
        {
            using var doc = JsonDocument.Parse("""
                {"startState":"Idle","globalTransitions":[{"event":"HIT","toState":"Hurt"}],"states":[
                {"name":"Idle","transitions":[{"event":"LOOP","toState":"Idle"},{"event":"ATTACK","toState":"Attack"}],"actionsLoaded":false,"actionTypes":null},
                {"name":"Attack","transitions":[{"event":"FINISHED","toState":"Idle"}],"actionsLoaded":true,"actionTypes":["Wait"]}]}
                """);
            var graph = FsmGraph.Parse(doc.RootElement);
            Assert.AreEqual("Idle", graph.Nodes[0].Edges[0].Target); Assert.AreEqual("HIT", graph.Globals[0].Event);
            Assert.IsFalse(graph.Nodes[0].ActionsLoaded); Assert.AreEqual("Wait", graph.Nodes[1].Actions[0]);
        }
        [TestMethod]
        public void GraphRendersCurrentStateAndClearsHighlightForStaleDataWithoutRelayout() => Sta(() =>
        {
            var graph = new FsmGraph("Idle", new[] {
                new FsmNode("Idle", new[] { new FsmEdge("ATTACK", "Attack") }, Array.Empty<string>(), false),
                new FsmNode("Attack", new[] { new FsmEdge("FINISHED", "Idle"), new FsmEdge("REPEAT", "Attack") }, new[]{ "Wait" }, true),
                new FsmNode("Hurt", new[]{new FsmEdge("FINISHED", "Idle")}, Array.Empty<string>(), false)
            }, new[]{new FsmEdge("HIT", "Hurt")});
            var card = new FsmCard { Target = Target("test"), Graph = graph, Version = "1", Current = "Attack", Live = true };
            var canvas = new FsmGraphView.GraphCanvas(); canvas.Update(card);
            var positions = canvas.Positions.ToDictionary(p => p.Key, p => p.Value);
            byte[] Render(string label)
            {
                canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
                var folder = Environment.GetEnvironmentVariable("HKTAS_FSM_TEST_OUTPUT");
                if (!string.IsNullOrEmpty(folder)) { Directory.CreateDirectory(folder); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder, label + ".png")); encoder.Save(file); }
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
            }
            var live = Render("fsm-active"); card.Live = false; canvas.Update(card); var stale = Render("fsm-stale");
            Assert.IsFalse(live.SequenceEqual(stale));
            foreach (var pair in positions) Assert.AreEqual(pair.Value, canvas.Positions[pair.Key]);
        });
    }
}
