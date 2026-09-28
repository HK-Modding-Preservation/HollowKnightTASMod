using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class KeyboardFrameInputTests
{
    private static Dictionary<string, string> Bindings()
    {
        var bindings = KeyboardFrameInput.Actions.ToDictionary(a => a, _ => "");
        bindings["Up"] = "W"; bindings["Attack"] = "J"; bindings["Jump"] = "K";
        return bindings;
    }

    [TestMethod]
    public void UpAttackChordAndHeldJumpAreSampledAgainForEachStep()
    {
        var keys = new HashSet<int> { 0x57, 0x4A, 0x56 };
        var bindings = Bindings();
        var first = KeyboardFrameInput.Capture(bindings, Key.V, keys.Contains);
        Assert.IsTrue(first["Up"] && first["Attack"]);
        Assert.IsFalse(first["Jump"]);
        keys.Clear(); keys.Add(0x4B); keys.Add(0x56);
        for (var i = 0; i < 4; i++) Assert.IsTrue(KeyboardFrameInput.Capture(bindings, Key.V, keys.Contains)["Jump"]);
        keys.Remove(0x4B);
        Assert.IsFalse(KeyboardFrameInput.Capture(bindings, Key.V, keys.Contains)["Jump"]);
        Assert.IsTrue(first["Attack"], "Earlier capture must be immutable when physical keys change.");
    }

    [TestMethod]
    public void CombosExclusionsAlternativesAndKeyboardOnlyEmptyBindings()
    {
        var bindings = Bindings(); bindings["Jump"] = "LeftControl,K,!RightAlt;Space";
        var keys = new HashSet<int> { 0xA2, 0x4B };
        Assert.IsTrue(KeyboardFrameInput.Capture(bindings, Key.V, keys.Contains)["Jump"]);
        keys.Add(0xA5);
        Assert.IsFalse(KeyboardFrameInput.Capture(bindings, Key.V, keys.Contains)["Jump"]);
        keys.Add(0x20);
        var states = KeyboardFrameInput.Capture(bindings, Key.V, keys.Contains);
        Assert.IsTrue(states["Jump"]);
        Assert.IsFalse(states["Dash"]);
    }

    [TestMethod]
    public void MissingMappingUnsupportedKeyAndStepConflictFailBeforeWriting()
    {
        var bindings = Bindings(); bindings.Remove("Jump");
        Assert.ThrowsExactly<InvalidOperationException>(() => KeyboardFrameInput.Capture(bindings, Key.V, _ => false));
        bindings["Jump"] = "UnknownKey";
        Assert.ThrowsExactly<InvalidOperationException>(() => KeyboardFrameInput.Capture(bindings, Key.V, _ => false));
        bindings["Jump"] = "V";
        Assert.ThrowsExactly<InvalidOperationException>(() => KeyboardFrameInput.Capture(bindings, Key.V, _ => false));
    }

    [TestMethod]
    public void WritesNextFramePreservesPrefixRateSeedAndAuthorsFutureEdges()
    {
        var span = new MovieSourceSpan("test", 1, 1, 1);
        var original = new MovieV2Document("test", new MovieV2Header("game", "api", "mod",
            MovieProtocolV2.NativeProfileId, MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450),
            new[] { new NativeFrameRun(5, Array.Empty<GameInputSample>(), span, 50, false),
                new NativeFrameRun(1, Array.Empty<GameInputSample>(), span, 60, false, 42),
                new NativeFrameRun(5, Array.Empty<GameInputSample>(), span, 50, false) });
        var input = KeyboardFrameInput.Capture(Bindings(), Key.V, key => key == 0x57 || key == 0x4A);
        var result = KeyboardFrameInput.WriteFrame(original, 5, input);
        Assert.IsTrue(MovieV2Prefix.Matches(original, result, 5));
        var run = result.Runs[1];
        Assert.AreEqual(60, run.FramesPerSecond); Assert.AreEqual(42, run.RngSeed);
        var hero = run.Samples.Single(s => s.Channel == GameInputChannel.Hero);
        Assert.AreEqual(short.MaxValue, hero.Values[2]); Assert.AreEqual(short.MaxValue, hero.Values[15]);
        Assert.AreEqual((short)0, hero.Values[10]);
        Assert.IsTrue(result.Runs.Skip(1).All(r => r.Authored));
        var released = KeyboardFrameInput.WriteFrame(result, 5, KeyboardFrameInput.Capture(Bindings(), Key.V, _ => false));
        Assert.IsTrue(released.Runs[1].Samples.All(s => s.Values.All(v => v == 0)));
    }
}
