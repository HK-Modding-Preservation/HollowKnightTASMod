using System;
using System.Collections.Generic;
using GlobalEnums;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Inspector
{
    public sealed class TickWatchProvider : IWatchProvider
    {
        private readonly WatchDescriptor[] descriptors;

        public TickWatchProvider(int sampleEveryMovieTicks)
        {
            descriptors = new[]
            {
                Descriptor(
                    "tick.input",
                    SemanticValueKind.Int64,
                    "Input tick",
                    sampleEveryMovieTicks),
                Descriptor(
                    "tick.visual",
                    SemanticValueKind.Int64,
                    "Visual tick",
                    sampleEveryMovieTicks),
                Descriptor(
                    "tick.fixed",
                    SemanticValueKind.Int64,
                    "Fixed tick",
                    sampleEveryMovieTicks),
                Descriptor(
                    "tick.movie",
                    SemanticValueKind.Int64,
                    "Movie tick",
                    sampleEveryMovieTicks),
                Descriptor(
                    "tick.tft",
                    SemanticValueKind.Float32Bits,
                    "T-FT",
                    sampleEveryMovieTicks),
                new WatchDescriptor(
                    new WatchKey("scene.name", true),
                    SemanticValueKind.Utf8String,
                    "scene",
                    sampleEveryMovieTicks,
                    "Scene"),
                new WatchDescriptor(
                    new WatchKey("scene.epoch", true),
                    SemanticValueKind.Int32,
                    "scene",
                    sampleEveryMovieTicks,
                    "Scene epoch"),
                new WatchDescriptor(
                    new WatchKey("game.state", true),
                    SemanticValueKind.Utf8String,
                    "scene",
                    sampleEveryMovieTicks,
                    "Game state")
            };
        }

        public string ProviderId => "default.tick-scene";

        public IEnumerable<WatchDescriptor> Describe()
        {
            return descriptors;
        }

        public void Sample(
            WatchFrameBuilder builder,
            WatchSampleContext context)
        {
            var scene = USceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
            {
                throw new InvalidOperationException(
                    "The active scene is unavailable.");
            }

            var gameManager = GameManager.instance
                              ?? throw new InvalidOperationException(
                                  "GameManager.instance is unavailable.");
            var gameState = gameManager.gameState;
            if (!Enum.IsDefined(typeof(GameState), gameState))
            {
                throw new InvalidOperationException(
                    "GameManager.gameState is outside the known enum.");
            }

            builder.AddInt64(
                "tick.input",
                checked((long)context.Stamp.InputTick));
            builder.AddInt64(
                "tick.visual",
                context.Stamp.VisualTick);
            builder.AddInt64(
                "tick.fixed",
                context.Stamp.FixedTick);
            builder.AddInt64("tick.movie", context.MovieTick);
            builder.AddFloat32(
                "tick.tft",
                Time.time - Time.fixedTime);
            builder.AddString("scene.name", scene.name);
            builder.AddInt32(
                "scene.epoch",
                context.Stamp.SceneEpoch);
            builder.AddString("game.state", gameState.ToString());
        }

        private static WatchDescriptor Descriptor(
            string key,
            SemanticValueKind kind,
            string label,
            int interval)
        {
            return new WatchDescriptor(
                new WatchKey(key, true),
                kind,
                "tick",
                interval,
                label);
        }
    }
}
