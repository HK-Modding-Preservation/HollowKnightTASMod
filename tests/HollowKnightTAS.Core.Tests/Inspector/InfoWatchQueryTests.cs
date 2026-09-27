using System;
using System.Collections.Generic;
using HollowKnightTAS.Core.Inspector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Inspector
{
    [TestClass]
    public sealed class InfoWatchQueryTests
    {
        private class BaseState { private readonly float timer = .625f; public float ReadForCompiler() => timer; }
        private sealed class State : BaseState
        {
            public int Health { get; } = 9;
            public int GetterCalls;
            public int Dangerous { get { GetterCalls++; return 42; } }
            public State? Missing = null;
            public readonly List<int> charms = new() { 3, 7 };
            public readonly int[] steps = { 10, 20 };
            public readonly double Invalid = double.NaN;
            public static int Static = 123;
            public int Mutate() { GetterCalls++; return 1; }
        }

        [TestMethod]
        public void ReadsPrivateInheritedAndAutoPropertyBackingFieldsWithoutGetters()
        {
            var state = new State();
            Assert.AreEqual(.625f, InfoWatchQuery.Parse("hero.timer").Read(state));
            Assert.AreEqual(9, InfoWatchQuery.Parse("player.Health").Read(state));
            Assert.ThrowsExactly<InvalidOperationException>(() => InfoWatchQuery.Parse("hero.Dangerous").Read(state));
            Assert.AreEqual(0, state.GetterCalls);
            Assert.ThrowsExactly<InvalidOperationException>(() => InfoWatchQuery.Parse("hero.Static").Read(state));
        }
        [TestMethod]
        public void ReadsOnlyBuiltInArrayAndListIndicesAndBoundsChecksThem()
        {
            var state = new State();
            Assert.AreEqual(7, InfoWatchQuery.Parse("player.charms[1]").Read(state));
            Assert.AreEqual(20, InfoWatchQuery.Parse("hero.steps[1]").Read(state));
            Assert.ThrowsExactly<IndexOutOfRangeException>(() => InfoWatchQuery.Parse("hero.steps[2]").Read(state));
            Assert.ThrowsExactly<InvalidOperationException>(() => InfoWatchQuery.Parse("hero.Missing.Health").Read(state));
        }
        [TestMethod]
        [DataRow("hero.Mutate()")]
        [DataRow("hero.Health = 0")]
        [DataRow("hero.Health; player.geo")]
        [DataRow("System.Environment.Exit(0)")]
        [DataRow("hero.steps[-1]")]
        [DataRow("hero.steps[9999999]")]
        [DataRow("component(\"Knight\",\"HeroController\").timer")]
        public void RejectsCallsAssignmentsAndUnboundedSelectors(string expression)
            => Assert.ThrowsExactly<FormatException>(() => InfoWatchQuery.Parse(expression));

        [TestMethod]
        public void ParsesExplicitComponentAndFsmSelectorsIncludingSpaces()
        {
            var component = InfoWatchQuery.Parse("component(\"/Root/Enemy(Clone)\", \"Custom.ModType\").Health");
            Assert.AreEqual("/Root/Enemy(Clone)", component.ObjectPath);
            Assert.AreEqual("Custom.ModType", component.ComponentName);
            Assert.AreEqual(9, component.Read(new State()));
            var fsm = InfoWatchQuery.Parse("fsm(\"/Knight\", \"Spell Control\", \"MP Cost\")");
            Assert.AreEqual("Spell Control", fsm.FsmName); Assert.AreEqual("MP Cost", fsm.VariableName);
            Assert.AreEqual(33f, fsm.Read(33f));
        }
        [TestMethod]
        public void RefusesObjectGraphsNonfiniteValuesAndLongPaths()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => InfoWatchQuery.Parse("hero.Invalid").Read(new State()));
            Assert.ThrowsExactly<InvalidOperationException>(() => InfoWatchQuery.Parse("hero.steps").Read(new State()));
            Assert.ThrowsExactly<FormatException>(() => InfoWatchQuery.Parse("hero." + new string('x', 512)));
            Assert.ThrowsExactly<FormatException>(() => InfoWatchQuery.Parse("hero" + string.Concat(System.Linq.Enumerable.Repeat(".Missing", 17))));
        }
    }
}
