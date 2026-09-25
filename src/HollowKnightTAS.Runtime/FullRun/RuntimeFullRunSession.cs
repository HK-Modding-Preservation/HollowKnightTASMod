using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Threading;
using GlobalEnums;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Runtime.Input;
using HollowKnightTAS.Runtime.Timing;
using HollowKnightTAS.Runtime.Rng;

namespace HollowKnightTAS.Runtime.FullRun
{
    public sealed class FullRunResult
    {
        public FullRunResult(bool success, string error, long frameIndex, string movieId)
        {
            if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));
            Success = success;
            Error = error ?? throw new ArgumentNullException(nameof(error));
            FrameIndex = frameIndex;
            MovieId = movieId ?? throw new ArgumentNullException(nameof(movieId));
        }
        public bool Success { get; }
        public string Error { get; }
        public long FrameIndex { get; }
        public string MovieId { get; }
    }

    public sealed class FullRunStatus
    {
        public FullRunStatus(string mode, long nativeFrame, long movieFrame,
            long skippedLoadFrames,
            string frameBoundary, bool runtimeInputReady,
            long mismatchCount, string error, string sceneName, int saveSlot,
            string heroX, string heroY, string respawnScene, int heroHealth,
            bool bossSceneEntered, bool bossDeathObserved,
            bool bossesDeadObserved, bool bossSceneCompleteObserved,
            long bossDeathFrame, long bossSceneEntryMovieFrame)
        {
            if (nativeFrame < 0 || movieFrame < 0 || skippedLoadFrames < 0
                || mismatchCount < 0 || saveSlot < 0)
                throw new ArgumentOutOfRangeException(nameof(nativeFrame));
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            NativeFrame = nativeFrame;
            MovieFrame = movieFrame;
            SkippedLoadFrames = skippedLoadFrames;
            FrameBoundary = frameBoundary ?? throw new ArgumentNullException(nameof(frameBoundary));
            RuntimeInputReady = runtimeInputReady;
            MismatchCount = mismatchCount;
            Error = error ?? throw new ArgumentNullException(nameof(error));
            SceneName = sceneName ?? throw new ArgumentNullException(nameof(sceneName));
            SaveSlot = saveSlot;
            HeroX = heroX ?? throw new ArgumentNullException(nameof(heroX));
            HeroY = heroY ?? throw new ArgumentNullException(nameof(heroY));
            RespawnScene = respawnScene ?? throw new ArgumentNullException(nameof(respawnScene));
            HeroHealth = heroHealth;
            BossSceneEntered = bossSceneEntered;
            BossDeathObserved = bossDeathObserved;
            BossesDeadObserved = bossesDeadObserved;
            BossSceneCompleteObserved = bossSceneCompleteObserved;
            BossDeathFrame = bossDeathFrame;
            BossSceneEntryMovieFrame = bossSceneEntryMovieFrame;
        }
        public string Mode { get; }
        public long NativeFrame { get; }
        public long MovieFrame { get; }
        public long SkippedLoadFrames { get; }
        public string FrameBoundary { get; }
        public bool RuntimeInputReady { get; }
        public long MismatchCount { get; }
        public string Error { get; }
        public string SceneName { get; }
        public int SaveSlot { get; }
        public string HeroX { get; }
        public string HeroY { get; }
        public string RespawnScene { get; }
        public int HeroHealth { get; }
        public bool BossSceneEntered { get; }
        public bool BossDeathObserved { get; }
        public bool BossesDeadObserved { get; }
        public bool BossSceneCompleteObserved { get; }
        public long BossDeathFrame { get; }
        public long BossSceneEntryMovieFrame { get; }
    }

    public sealed class RuntimeFullRunSession : IDisposable
    {
        private readonly NativeFullRunFrameClock clock;
        private readonly FullRunActionSetAdapter input;
        private readonly FullRunMouseBridge mouse;
        private readonly string sessionDirectory;
        private FullRunFrameJournal? journal;
        private MovieV2Document? replayMovie;
        private MovieV2Header? recordingHeader;
        private MovieV2Document? recordedMovie;
        private string recordedCanonical = string.Empty;
        private long replayLength;
        private long bootstrapFrame = -1;
        private long movieFrame;
        private long skippedLoadFrames;
        private long expectedNativeStart = -1;
        private bool frameInputEnabled;
        private bool titleReadySeen;
        private bool returningToMainMenu;
        private bool returnToMainMenuHooked;
        private string frameBoundary = "Bootstrap";
        private UIManager? uiManager;
        private static readonly FieldInfo MainMenuScreenField = typeof(UIManager).GetField(
            "mainMenuScreen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException("UIManager.mainMenuScreen");
        private long mismatchCount;
        private string mode = "Idle";
        private string error = string.Empty;
        private bool inputReady;
        private bool mouseEnabled;
        private bool disposed;
        private BossSceneController? boundBossController;
        private readonly System.Collections.Generic.List<HealthManager> boundBosses =
            new System.Collections.Generic.List<HealthManager>();
        private bool bossSceneEntered;
        private bool bossDeathObserved;
        private bool bossesDeadObserved;
        private bool bossSceneCompleteObserved;
        private long bossDeathFrame = -1;
        private long bossSceneEntryMovieFrame = -1;
        private readonly bool bossTraceEnabled = string.Equals(
            Environment.GetEnvironmentVariable("HKTAS_FULL_RUN_BOSS_TRACE"),
            "1", StringComparison.Ordinal);
        private readonly StringBuilder bossTrace = new StringBuilder("nativeFrame,movieFrame,scene,heroX,heroY,heroHealth,bossHp,bossX,bossY,bossDead,time,fixedTime,deltaTime,frameCount,rngSha256\n");
        private int bossTraceRows;
        private UnityRandomStateCodec_1_5_78_11833? bossTraceRngCodec;
        private EventWaitHandle? randomSeedRequest;
        private EventWaitHandle? randomSeedAcknowledged;
        private long randomSeedRequestNativeFrame = -1;
        private int randomSeedSceneHandle = -1;
        private bool randomSeedHandshakePending;

        public RuntimeFullRunSession(NativeFullRunFrameClock clock,
            FullRunActionSetAdapter input, FullRunMouseBridge mouse,
            string sessionDirectory)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
            if (string.IsNullOrWhiteSpace(sessionDirectory))
                throw new ArgumentException("Full-run session directory is required.", nameof(sessionDirectory));
            this.sessionDirectory = Path.GetFullPath(sessionDirectory);
            Directory.CreateDirectory(this.sessionDirectory);
        }

        public MovieV2Document? RecordedMovie => recordedMovie;
        public string RecordedCanonicalText => recordedCanonical;
        public string RecordedMoviePath => recordedMovie == null ? string.Empty
            : Path.Combine(sessionDirectory, "full-run", "movie.hktas");
        public bool MouseEnabled => mouseEnabled;
        public string Mode => mode;
        public double ClockStepSeconds => clock.StepSeconds;
        public int ActiveFrameRate => activeFrameRate;

        private int recordingFrameRate = 50;
        private int activeFrameRate = 50;
        private long pauseAtMovieFrame = -1;
        private int timingRunIndex;
        private long timingRunStart;
        public void ConfigureTiming(int fps, long target)
        {
            if (fps < 1 || fps > 1000 || target < -1 || target > MovieProtocolV2.MaximumExpandedFrames)
                throw new ArgumentOutOfRangeException(nameof(fps));
            recordingFrameRate = fps;
            pauseAtMovieFrame = target;
        }
        public void SetPauseTarget(long target, long expectedNativeFrame)
        {
            if (!clock.IsPaused || clock.CurrentFrameIndex != expectedNativeFrame || target <= movieFrame)
                throw new InvalidOperationException("Target requires a paused boundary and a future Movie frame.");
            pauseAtMovieFrame = target;
        }

        public string SnapshotMovie()
        {
            if (!clock.IsPaused) throw new InvalidOperationException("Movie snapshot requires a paused boundary.");
            var movie = mode == "Recording" ? journal!.Freeze(recordingHeader!, movieFrame) : replayMovie ?? recordedMovie;
            if (movie == null) throw new InvalidOperationException("No Movie is available.");
            var path = Path.Combine(sessionDirectory, "full-run", "snapshot-" + new MovieV2Codec().ComputeMovieId(movie).Substring(0, 32) + ".hktas");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllText(path, new MovieV2Codec().WriteCanonical(movie), new UTF8Encoding(false));
            return path;
        }

        public void UpdateFutureMovie(string path, long expectedNativeFrame)
        {
            if (!clock.IsPaused || clock.CurrentFrameIndex != expectedNativeFrame || !inputReady
                || (mode != "Recording" && mode != "Replay"))
                throw new InvalidOperationException("Pause at an input-ready boundary before updating the Movie.");
            var root = Path.GetFullPath(Path.Combine(sessionDirectory, "..", "..")) + Path.DirectorySeparatorChar;
            path = Path.GetFullPath(path);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || new FileInfo(path).Length > MovieProtocolV2.MaximumSourceUtf8Bytes)
                throw new InvalidDataException("Movie update must be inside the protected session.");
            MovieV2Document candidate;
            using (var reader = File.OpenText(path))
                candidate = new MovieV2Codec().Parse(reader, path).Document
                    ?? throw new InvalidDataException("Invalid Movie update.");
            var validation = new MovieV2Validator().Validate(candidate, MovieV2ValidationContext.CreateDefault());
            if (!validation.Success || CountFrames(candidate) <= movieFrame)
                throw new InvalidDataException("Updated Movie must contain the next input frame.");
            var original = mode == "Recording" ? journal!.Freeze(recordingHeader!, movieFrame) : replayMovie!;
            if (!MovieV2Prefix.Matches(original, candidate, movieFrame))
                throw new InvalidOperationException("过去的输入已经改变，请使用应用并重放到 Frame。");
            input.ReplaceFutureMovie(candidate, movieFrame);
            input.Sampled -= OnSampled;
            mouse.UseReplayInputs();
            replayMovie = candidate;
            replayLength = CountFrames(candidate);
            timingRunIndex = 0;
            timingRunStart = 0;
            mode = "Replay";
        }

        public FullRunResult BeginRecording(bool mouseEnabled, long expectedFrame)
        {
            if (mode != "Idle" || expectedFrame < 0 || clock.CurrentFrameIndex != expectedFrame)
                return Reject("NativeFrameMismatch");
            try
            {
                this.mouseEnabled = mouseEnabled;
                journal = new FullRunFrameJournal(sessionDirectory);
                bootstrapFrame = expectedFrame;
                clock.RegisterCompleted(OnNativeCompleted);
                mode = "Recording";
                return new FullRunResult(true, string.Empty, expectedFrame, string.Empty);
            }
            catch (Exception exception)
            {
                Fail("BootstrapInputFault: " + exception.Message);
                return Reject(error);
            }
        }

        public FullRunResult BeginReplay(MovieV2Document movie,
            MovieV2CompatibilityReport compatibility, long expectedFrame)
        {
            if (movie == null || compatibility == null || !compatibility.Allowed
                || mode != "Idle" || expectedFrame < 0 || clock.CurrentFrameIndex != expectedFrame)
                return Reject("ReplayBootstrapRejected");
            try
            {
                replayLength = CountFrames(movie);
                if (replayLength < 1)
                    return Reject("EmptyFullRunMovie");
                mouseEnabled = movie.Header.MouseEnabled;
                replayMovie = movie;
                bootstrapFrame = expectedFrame;
                clock.RegisterCompleted(OnNativeCompleted);
                mode = "Replay";
                return new FullRunResult(true, string.Empty, expectedFrame,
                    new MovieV2Codec().ComputeMovieId(movie));
            }
            catch (Exception exception)
            {
                Fail("ReplayBootstrapFault: " + exception.Message);
                return Reject(error);
            }
        }

        public void SetRecordingHeader(MovieV2Header header)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (mode != "Recording")
                throw new InvalidOperationException("Recording metadata requires an active full-run recording.");
            recordingHeader = header;
        }

        public FullRunStatus GetStatus()
        {
            var hero = HeroController.instance;
            var position = hero == null ? default(UnityEngine.Vector3) : hero.transform.position;
            return new FullRunStatus(mode, clock.CurrentFrameIndex, movieFrame,
                skippedLoadFrames, frameBoundary, inputReady,
                mismatchCount, error,
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? string.Empty,
                GameManager.instance?.profileID ?? 0,
                hero == null ? string.Empty : position.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                hero == null ? string.Empty : position.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                PlayerData.instance?.respawnScene ?? string.Empty,
                PlayerData.instance?.health ?? 0,
                bossSceneEntered, bossDeathObserved, bossesDeadObserved,
                bossSceneCompleteObserved, bossDeathFrame, bossSceneEntryMovieFrame);
        }

        public FullRunResult Stop(long expectedFrame)
        {
            if (expectedFrame < 0 || expectedFrame != clock.CurrentFrameIndex || !clock.IsPaused)
                return Reject("NativeFrameMismatch");
            if (mode == "Recording")
            {
                try
                {
                    clock.RequestPause();
                    if (recordingHeader == null)
                        return Reject("RecordingEnvironmentUnresolved");
                    recordedMovie = journal?.Freeze(recordingHeader, movieFrame)
                        ?? throw new InvalidOperationException("Full-run recording journal is missing.");
                    recordedCanonical = new MovieV2Codec().WriteCanonical(recordedMovie);
                    var id = new MovieV2Codec().ComputeMovieId(recordedMovie);
                    var finalPath = Path.Combine(sessionDirectory, "full-run", "movie.hktas");
                    Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                    using (var stream = new FileStream(finalPath, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        var bytes = new UTF8Encoding(false, true).GetBytes(recordedCanonical);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    clock.Finish();
                    mode = "Stopped";
                    return new FullRunResult(true, string.Empty, expectedFrame, id);
                }
                catch (Exception exception)
                {
                    Fail("RecordingFreezeFault: " + exception.Message);
                    return Reject(error);
                }
            }
            if (mode == "Replay" || mode == "Completed")
            {
                if (!clock.IsFinished) clock.Finish();
                mode = "Stopped";
                return new FullRunResult(true, string.Empty, expectedFrame,
                    replayMovie == null ? string.Empty
                        : new MovieV2Codec().ComputeMovieId(replayMovie));
            }
            return Reject("FullRunNotActive");
        }

        private void OnSampled(long frame, GameInputSample sample, ulong inputTick)
        {
            if (mode == "Recording")
                journal?.Append(frame, sample, inputTick);
        }

        private void OnInputFault(string message)
        {
            Fail("InputMismatch: " + message);
        }

        private void OnNativeBeforeFrame(long completed)
        {
            if (!inputReady || (mode != "Recording" && mode != "Replay")) return;
            try
            {
                if (completed != expectedNativeStart)
                    throw new InvalidDataException("Native frame start was not sequential.");
                var ready = IsMovieFrameReady(out var boundary);
                var sceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
                if (ready && sceneHandle != randomSeedSceneHandle)
                {
                    if (randomSeedRequest == null || randomSeedAcknowledged == null)
                    {
                        var runId = Environment.GetEnvironmentVariable("HKTAS_CLOCK_RUN_ID");
                        if (string.IsNullOrEmpty(runId))
                            throw new InvalidOperationException("External clock run ID is unavailable.");
                        var prefix = "HollowKnightTAS.V2.RngSync." + runId;
                        randomSeedRequest = new EventWaitHandle(false,
                            EventResetMode.AutoReset, prefix + ".request");
                        randomSeedAcknowledged = new EventWaitHandle(false,
                            EventResetMode.ManualReset, prefix + ".applied");
                    }
                    if (!randomSeedHandshakePending)
                    {
                        randomSeedAcknowledged.Reset();
                        randomSeedRequestNativeFrame = completed;
                        randomSeedHandshakePending = true;
                        randomSeedRequest.Set();
                    }
                    if (!randomSeedAcknowledged.WaitOne(0))
                    {
                        if (completed - randomSeedRequestNativeFrame > 120)
                            throw new InvalidOperationException("External random seed handshake timed out.");
                        ready = false;
                        boundary = "RandomSeedHandshake";
                    }
                    else
                    {
                        randomSeedSceneHandle = sceneHandle;
                        randomSeedHandshakePending = false;
                    }
                }
                activeFrameRate = recordingFrameRate;
                if (replayMovie != null)
                {
                    while (timingRunIndex + 1 < replayMovie.Runs.Count && movieFrame >= timingRunStart + replayMovie.Runs[timingRunIndex].RepeatCount)
                        timingRunStart += replayMovie.Runs[timingRunIndex++].RepeatCount;
                    activeFrameRate = replayMovie.Runs[timingRunIndex].FramesPerSecond;
                }
                clock.SetFrameRate(ready ? activeFrameRate : 50);
                frameInputEnabled = ready;
                frameBoundary = boundary;
                input.SetFrameInputEnabled(ready);
                mouse.SetFrameInputEnabled(ready);
            }
            catch (Exception exception)
            {
                Fail("FrameBoundaryFault: " + exception.Message);
            }
        }

        private bool IsMovieFrameReady(out string boundary)
        {
            var manager = GameManager.instance;
            if (manager == null)
            {
                boundary = "GameManagerUnavailable";
                return false;
            }
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (manager.IsInSceneTransition)
            {
                boundary = "SceneTransition";
                return false;
            }
            if (scene == "Menu_Title" && manager.gameState == GameState.MAIN_MENU)
            {
                uiManager = uiManager == null
                    ? UnityEngine.Object.FindObjectOfType<UIManager>() : uiManager;
                if (uiManager == null || uiManager.IsFadingMenu)
                {
                    boundary = "TitleLoading";
                    return false;
                }
                var screen = MainMenuScreenField.GetValue(uiManager) as UnityEngine.CanvasGroup;
                if (!titleReadySeen && (screen == null || !screen.interactable))
                {
                    boundary = "TitleLoading";
                    return false;
                }
                titleReadySeen = true;
                returningToMainMenu = false;
                boundary = "TitleInput";
                return true;
            }
            titleReadySeen = false;
            if (returningToMainMenu || scene == "Quit_To_Menu")
            {
                boundary = "QuitToMenuLoading";
                return false;
            }
            if (manager.gameState != GameState.PLAYING
                && manager.gameState != GameState.PAUSED
                && manager.gameState != GameState.CUTSCENE)
            {
                boundary = "GameState:" + manager.gameState;
                return false;
            }
            var hero = HeroController.SilentInstance;
            if (hero == null || !hero.gameObject.activeInHierarchy)
            {
                boundary = "HeroLoading";
                return false;
            }
            boundary = "GameplayInput";
            return true;
        }

        private void OnNativeCompleted(long completed)
        {
            if (mode != "Recording" && mode != "Replay") return;
            try
            {
                if (!inputReady && completed - 1 == bootstrapFrame)
                {
                    StartInputAt(completed);
                    return;
                }
                if (!inputReady || completed != expectedNativeStart + 1)
                    throw new InvalidDataException("Native frame completion was not sequential.");
                expectedNativeStart = completed;
                if (!frameInputEnabled)
                {
                    skippedLoadFrames++;
                    return;
                }
                input.CompleteFrame(movieFrame);
                if (input.Fault.Length != 0)
                {
                    Fail("InputMismatch: " + input.Fault);
                    return;
                }
                ObserveBoss();
                if (mode == "Recording") journal!.CompleteFrame(movieFrame, activeFrameRate);
                movieFrame++;
                clock.ReportMovieFrameCompleted();
                if (movieFrame == pauseAtMovieFrame) { pauseAtMovieFrame = -1; clock.RequestPause(); }
                if (mode == "Replay" && movieFrame == replayLength)
                {
                    if (bossTraceEnabled)
                        File.WriteAllText(Path.Combine(sessionDirectory, "boss-trace.csv"),
                            bossTrace.ToString(), new UTF8Encoding(false));
                    mode = "Completed";
                    clock.Finish();
                    return;
                }
                if (mode == "Replay" && movieFrame > replayLength)
                    throw new InvalidDataException("Replay exceeded the movie frame count.");
                input.PrepareFrame(movieFrame);
            }
            catch (Exception exception)
            {
                Fail("FrameCompletionFault: " + exception.Message);
            }
        }

        private void StartInputAt(long frame)
        {
            mouse.Configure(input, mode == "Replay");
            if (!mouse.TryInstall(mouseEnabled, out var mouseError))
                throw new InvalidOperationException("MouseBridgeUnavailable: " + mouseError);
            input.Faulted += OnInputFault;
            if (mode == "Recording")
            {
                input.Sampled += OnSampled;
                input.StartRecording();
            }
            else input.StartReplay(replayMovie
                ?? throw new InvalidOperationException("Replay movie is missing."));
            input.PrepareFrame(0);
            On.GameManager.ReturnToMainMenu += OnReturnToMainMenu;
            returnToMainMenuHooked = true;
            expectedNativeStart = frame;
            clock.RegisterBeforeFrame(OnNativeBeforeFrame);
            inputReady = true;
        }

        private System.Collections.IEnumerator OnReturnToMainMenu(
            On.GameManager.orig_ReturnToMainMenu original, GameManager self,
            GameManager.ReturnToMainMenuSaveModes saveMode, Action<bool> callback)
        {
            returningToMainMenu = true;
            return original(self, saveMode, callback);
        }

        private void ObserveBoss()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (bossTraceEnabled && bossSceneEntered && bossTraceRows < 4096)
            {
                bossTraceRows++;
                var hero = HeroController.instance;
                var bosses = BossSceneController.Instance?.bosses;
                var boss = bosses != null && bosses.Length > 0 ? bosses[0] : null;
                if (bossTraceRngCodec == null)
                    bossTraceRngCodec = UnityRandomStateCodec_1_5_78_11833.Resolve().Codec;
                bossTrace.Append(clock.CurrentFrameIndex).Append(',')
                    .Append(movieFrame).Append(',')
                    .Append(scene).Append(',')
                    .Append(hero == null ? string.Empty : hero.transform.position.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(hero == null ? string.Empty : hero.transform.position.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(PlayerData.instance?.health ?? 0).Append(',')
                    .Append(boss == null ? string.Empty : boss.hp.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(boss == null ? string.Empty : boss.transform.position.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(boss == null ? string.Empty : boss.transform.position.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(boss != null && boss.isDead ? "true" : "false").Append(',')
                    .Append(UnityEngine.Time.time.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(UnityEngine.Time.fixedTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(UnityEngine.Time.deltaTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(UnityEngine.Time.frameCount).Append(',')
                    .Append(bossTraceRngCodec == null ? string.Empty : bossTraceRngCodec.CaptureCurrent().Sha256).Append('\n');
            }
            if (scene != "GG_False_Knight") return;
            if (bossSceneEntryMovieFrame < 0) bossSceneEntryMovieFrame = movieFrame;
            bossSceneEntered = true;
            var controller = BossSceneController.Instance;
            if (controller == null || !controller.gameObject.activeInHierarchy) return;
            if (ReferenceEquals(controller, boundBossController))
            {
                BindBosses(controller);
                return;
            }
            UnbindBoss();
            boundBossController = controller;
            controller.OnBossesDead += OnBossesDead;
            controller.OnBossSceneComplete += OnBossSceneComplete;
            BindBosses(controller);
        }

        private void BindBosses(BossSceneController controller)
        {
            foreach (var boss in controller.bosses ?? Array.Empty<HealthManager>())
            {
                if (boss == null || boundBosses.Contains(boss)) continue;
                boss.OnDeath += OnBossDeath;
                boundBosses.Add(boss);
            }
        }

        private void OnBossDeath()
        {
            bossDeathObserved = true;
            if (bossDeathFrame < 0) bossDeathFrame = clock.CurrentFrameIndex;
        }

        private void OnBossesDead() => bossesDeadObserved = true;

        private void OnBossSceneComplete() => bossSceneCompleteObserved = true;

        private void UnbindBoss()
        {
            foreach (var boss in boundBosses)
                if (boss != null) boss.OnDeath -= OnBossDeath;
            boundBosses.Clear();
            if (boundBossController != null)
            {
                boundBossController.OnBossesDead -= OnBossesDead;
                boundBossController.OnBossSceneComplete -= OnBossSceneComplete;
            }
            boundBossController = null;
        }

        private void Fail(string detail)
        {
            if (mode == "Failed") return;
            mode = "Failed";
            error = detail;
            mismatchCount++;
            if (bossTraceEnabled && bossTraceRows > 0)
            {
                try
                {
                    File.WriteAllText(Path.Combine(sessionDirectory, "boss-trace-fault.csv"),
                        bossTrace.ToString(), new UTF8Encoding(false));
                }
                catch { }
            }
            try { clock.Fault(41); } catch { }
            try
            {
                File.WriteAllText(Path.Combine(sessionDirectory, "full-run-fault.txt"),
                    "frame=" + clock.CurrentFrameIndex + "\n" + detail + "\n",
                    new UTF8Encoding(false));
            }
            catch { }
        }

        private FullRunResult Reject(string reason)
            => new FullRunResult(false, reason, Math.Max(0, clock.CurrentFrameIndex),
                string.Empty);

        private static long CountFrames(MovieV2Document movie)
        {
            long total = 0;
            foreach (var run in movie.Runs) total = checked(total + run.RepeatCount);
            return total;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (returnToMainMenuHooked)
                On.GameManager.ReturnToMainMenu -= OnReturnToMainMenu;
            input.Sampled -= OnSampled;
            input.Faulted -= OnInputFault;
            UnbindBoss();
            input.Dispose();
            mouse.Dispose();
            randomSeedRequest?.Dispose();
            randomSeedAcknowledged?.Dispose();
        }
    }
}
