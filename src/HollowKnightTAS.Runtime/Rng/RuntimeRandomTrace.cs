using System;
using System.Globalization;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Runtime.Playback;
using HollowKnightTAS.Runtime.ReplaySave;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Rng
{
    // Opt-in forensic capture. Every hook calls the original exactly once;
    // neither random state nor action results are replaced.
    internal sealed class RuntimeRandomTrace : IDisposable
    {
        private const long MaximumBytes = 512 * 1024;
        private readonly RuntimeReplayJournal journal;
        private readonly UnityRandomStateCodec_1_5_78_11833 codec;
        private readonly StreamWriter writer;
        private long bytes;
        private bool disposed;

        public static RuntimeRandomTrace? TryStart(string sessionId, RuntimeReplayJournal journal)
        {
            var directory = SavePathResolver.Current.GetTasDataPath("diagnostics");
            if (!File.Exists(Path.Combine(directory, "rng-trace.enabled"))) return null;
            var resolution = UnityRandomStateCodec_1_5_78_11833.Resolve();
            if (!resolution.Ready) throw new InvalidOperationException(resolution.Detail);
            return new RuntimeRandomTrace(directory, sessionId, journal, resolution.Codec!);
        }

        private RuntimeRandomTrace(string directory, string sessionId,
            RuntimeReplayJournal journal, UnityRandomStateCodec_1_5_78_11833 codec)
        {
            this.journal = journal;
            this.codec = codec;
            writer = new StreamWriter(new FileStream(Path.Combine(directory, "rng-" + sessionId + ".tsv"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
            try
            {
                journal.MovieTickCommitted += OnTick;
                InControl.InputManager.OnUpdate += OnRawInput;
                USceneManager.activeSceneChanged += OnScene;
                On.HutongGames.PlayMaker.Actions.RandomFloat.OnEnter += OnFloat;
                On.HutongGames.PlayMaker.Actions.RandomInt.OnEnter += OnInt;
                On.HutongGames.PlayMaker.Actions.SendRandomEvent.OnEnter += OnEvent;
                On.UnityStandardAssets.ImageEffects.FastNoise.DrawNoiseQuadGrid += OnNoise;
                Write("trace-start", "");
            }
            catch { Dispose(); throw; }
        }

        private void OnTick(long tick, InputSample input, TickStamp stamp)
        {
            // Compare at the same input-commit phase in authoring and restore.
            // Keep this opt-in and bounded; end-of-frame API snapshots are a
            // different phase and must not be compared as if simultaneous.
            if (tick >= 32 && (tick < 560 || tick > 1600)) return;
            try
            {
                var hero = HeroController.SilentInstance;
                var position = hero == null ? Vector3.zero : hero.transform.position;
                Write("tick", string.Format(CultureInfo.InvariantCulture,
                    "heroX={0:R};heroY={1:R};hp={2};scale={3:R};fixedTime={4:R}",
                    position.x, position.y, PlayerData.instance?.health ?? -1,
                    Time.timeScale, Time.fixedTime));
            }
            catch { Write("tick-unavailable", ""); }
        }
        private void OnScene(Scene previous, Scene next) => Write("scene", previous.name + " -> " + next.name);
        private void OnRawInput(ulong inputTick, float inputDelta)
        {
            var tick = journal.LastCommittedMovieTick;
            if (tick < 1230 || tick > 1310) return;
            try
            {
                var hero = HeroController.SilentInstance;
                var position = hero == null ? Vector3.zero : hero.transform.position;
                Write("damage-raw", string.Format(CultureInfo.InvariantCulture,
                    "raw={0};inputDelta={1:R};scale={2:R};delta={3:R};unscaledDelta={4:R};fixedTime={5:R};heroX={6:R};heroY={7:R};hp={8}",
                    inputTick, inputDelta, Time.timeScale, Time.deltaTime, Time.unscaledDeltaTime,
                    Time.fixedTime, position.x, position.y, PlayerData.instance?.health ?? -1));
            }
            catch { Write("damage-raw-unavailable", ""); }
        }
        private void OnFloat(On.HutongGames.PlayMaker.Actions.RandomFloat.orig_OnEnter original,
            HutongGames.PlayMaker.Actions.RandomFloat self)
        {
            Write("float-before", Describe(self));
            try { original(self); }
            finally { Write("float-after", Describe(self) + " result=" + ReadResult(self)); }
        }
        private void OnInt(On.HutongGames.PlayMaker.Actions.RandomInt.orig_OnEnter original,
            HutongGames.PlayMaker.Actions.RandomInt self)
        {
            Write("int-before", Describe(self));
            try { original(self); }
            finally { Write("int-after", Describe(self)); }
        }
        private void OnEvent(On.HutongGames.PlayMaker.Actions.SendRandomEvent.orig_OnEnter original,
            HutongGames.PlayMaker.Actions.SendRandomEvent self)
        {
            Write("event-before", Describe(self));
            try { original(self); }
            finally { Write("event-after", Describe(self)); }
        }
        private static string Describe(FsmStateAction action)
        {
            try { return (action.Fsm?.GameObject?.name ?? "") + "/" + (action.Fsm?.Name ?? "") + "/" + (action.State?.Name ?? ""); }
            catch { return "unavailable"; }
        }

        private void OnNoise(On.UnityStandardAssets.ImageEffects.FastNoise.orig_DrawNoiseQuadGrid original,
            RenderTexture source, RenderTexture destination, Material material, Texture2D noise,
            int pass, int frameMultiple)
        {
            var tick = journal.LastCommittedMovieTick;
            var capture = (tick >= 0 && tick < 16 || tick >= 610 && tick <= 620)
                && frameMultiple > 0 && Time.frameCount % frameMultiple == 0;
            var detail = "frameMultiple=" + frameMultiple.ToString(CultureInfo.InvariantCulture);
            if (capture) Write("noise-before", detail);
            try { original(source, destination, material, noise, pass, frameMultiple); }
            finally { if (capture) Write("noise-after", detail); }
        }
        private static string ReadResult(HutongGames.PlayMaker.Actions.RandomFloat action)
        {
            try { return action.storeResult.Value.ToString("R", CultureInfo.InvariantCulture); }
            catch { return "unavailable"; }
        }

        private void Write(string kind, string detail)
        {
            if (disposed || bytes >= MaximumBytes) return;
            try
            {
                var line = string.Join("\t", kind, journal.LastCommittedMovieTick.ToString(CultureInfo.InvariantCulture),
                    Time.frameCount.ToString(CultureInfo.InvariantCulture), USceneManager.GetActiveScene().name,
                    codec.CaptureCurrent().Sha256, detail.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')) + "\n";
                var length = Encoding.UTF8.GetByteCount(line);
                if (bytes + length > MaximumBytes - 32)
                {
                    writer.Write("trace-budget-exhausted\n");
                    bytes = MaximumBytes;
                    return;
                }
                writer.Write(line);
                bytes += length;
            }
            catch (Exception exception)
            {
                // Preserve why capture stopped without calling Unity again.
                // Even the fallback is best effort: diagnostics cannot interrupt gameplay.
                try
                {
                    var message = exception.GetType().FullName + ": " + exception.Message;
                    if (message.Length > 512) message = message.Substring(0, 512);
                    var failure = "trace-error\t" + message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ') + "\n";
                    if (bytes + Encoding.UTF8.GetByteCount(failure) <= MaximumBytes) writer.Write(failure);
                }
                catch { }
                bytes = MaximumBytes;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            Write("trace-stop", "");
            disposed = true;
            journal.MovieTickCommitted -= OnTick;
            InControl.InputManager.OnUpdate -= OnRawInput;
            USceneManager.activeSceneChanged -= OnScene;
            On.HutongGames.PlayMaker.Actions.RandomFloat.OnEnter -= OnFloat;
            On.HutongGames.PlayMaker.Actions.RandomInt.OnEnter -= OnInt;
            On.HutongGames.PlayMaker.Actions.SendRandomEvent.OnEnter -= OnEvent;
            On.UnityStandardAssets.ImageEffects.FastNoise.DrawNoiseQuadGrid -= OnNoise;
            try { writer.Dispose(); } catch { }
        }
    }
}
