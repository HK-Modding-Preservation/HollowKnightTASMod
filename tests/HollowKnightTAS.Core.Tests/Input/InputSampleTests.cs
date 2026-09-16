using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using HollowKnightTAS.Core.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Input
{
    [TestClass]
    public sealed class InputSampleTests
    {
        [TestMethod]
        public void FixedEdgeV1_HasExpectedHeldAndEdgeSequence()
        {
            var samples = InputFixture.CreateFixedEdgeV1();

            Assert.AreEqual(60, samples.Count);
            Assert.AreEqual(TasAction.None, samples[0].Held);
            Assert.AreEqual(TasAction.Right, samples[10].Pressed);
            Assert.AreEqual(TasAction.Right, samples[10].Held);
            Assert.AreEqual(TasAction.Jump, samples[30].Pressed);
            Assert.AreEqual(TasAction.Jump, samples[39].Released);
            Assert.AreEqual(TasAction.Attack, samples[40].Pressed);
            Assert.AreEqual(TasAction.Attack, samples[41].Released);
            Assert.AreEqual(TasAction.Dash, samples[42].Pressed);
            Assert.AreEqual(TasAction.Dash, samples[43].Released);
            Assert.AreEqual(TasAction.Right, samples[44].Released);
            Assert.AreEqual(TasAction.None, samples[59].Held);

            Assert.AreEqual(
                4,
                samples.Sum(sample => CountBits(sample.Pressed)));
            Assert.AreEqual(
                4,
                samples.Sum(sample => CountBits(sample.Released)));
        }

        [TestMethod]
        public void FromHeld_ComputesPressedAndReleasedFromPreviousHeld()
        {
            var sample = InputSample.FromHeld(
                8,
                TasAction.Right | TasAction.Attack,
                TasAction.Left | TasAction.Attack);

            Assert.AreEqual(TasAction.Right, sample.Pressed);
            Assert.AreEqual(TasAction.Left, sample.Released);
            Assert.AreEqual(TasAction.Right | TasAction.Attack, sample.Held);
        }

        [TestMethod]
        public void Constructor_RejectsAxisOutsideContract()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => new InputSample(
                    0,
                    TasAction.None,
                    TasAction.None,
                    TasAction.None,
                    10001,
                    0));
        }

        [TestMethod]
        public void Constructor_RejectsDigitalDirectionWithAnalogAxis()
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new InputSample(
                    0,
                    TasAction.Right,
                    TasAction.Right,
                    TasAction.None,
                    5000,
                    0));
        }

        [TestMethod]
        public void GetValue_MapsSignedAxesToDirectionalSources()
        {
            var sample = new InputSample(
                0,
                TasAction.Attack,
                TasAction.Attack,
                TasAction.None,
                -2500,
                7500);

            Assert.AreEqual(0.25f, sample.GetValue(TasAction.Left), 0.0001f);
            Assert.AreEqual(0f, sample.GetValue(TasAction.Right), 0.0001f);
            Assert.AreEqual(0.75f, sample.GetValue(TasAction.Up), 0.0001f);
            Assert.AreEqual(0f, sample.GetValue(TasAction.Down), 0.0001f);
            Assert.AreEqual(1f, sample.GetValue(TasAction.Attack), 0.0001f);
        }

        [TestMethod]
        public void FixtureFile_DeclaresSameNameAndTickCount()
        {
            var fixturePath = Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "input",
                "edge-sequence-v1.hktas-input.json");
            using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));

            Assert.AreEqual(
                InputFixture.FixedEdgeV1Name,
                document.RootElement.GetProperty("name").GetString());
            Assert.AreEqual(
                InputFixture.FixedEdgeV1TickCount,
                document.RootElement.GetProperty("tickCount").GetInt32());
            Assert.AreEqual(
                9,
                document.RootElement.GetProperty("segments").GetArrayLength());
        }

        private static int CountBits(TasAction value)
        {
            var bits = (ushort)value;
            var count = 0;
            while (bits != 0)
            {
                count += bits & 1;
                bits >>= 1;
            }

            return count;
        }
    }
}
