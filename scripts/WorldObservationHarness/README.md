# WorldObservationHarness

`WorldObservationHarness` is an installation acceptance harness for the protected full-run Runtime. It does not use desktop automation or write game saves. It owns the current-user Companion control mutex, starts the verified shadow-save launch, and receives the Runtime registration through the formal `ControlPipeServer` path:

`SingleInstanceCoordinator -> ControlPipeServer -> SessionRegistry -> RuntimeSessionClient`

The installed Companion may be configured with `AutoStartCompanion=true`; when it sees the harness mutex it forwards the registration to the harness. If another Companion or Studio already owns the mutex, stop it before starting the harness. The harness does not edit global settings. A direct formal `--bootstrap-pipe=...` or `--bootstrap-handle=...` invocation is also accepted and is registered through the same `SessionRegistry`; the token is never logged or written to the report.

Build against the installed Companion/Core bundle:

```powershell
dotnet build scripts/WorldObservationHarness/WorldObservationHarness.csproj -p:SkipHKTASInstall=true
```

`Author-Fixture.py` only produces a candidate input fixture for acceptance runs. If it copies a header from an older environment, confirm that header against a real installed run before treating the fixture as valid. `--mode=probe` is available for exploratory runs and relaxes the final-scene enemy assertion; `--boss-frame=<movieFrame>` overrides the default boss observation frame.

Run from the repository root after the build (the root agent performs the real-game run):

```powershell
$env:HKTAS_FULL_RUN_BOSS_TRACE = '1'
dotnet run --project scripts/WorldObservationHarness/WorldObservationHarness.csproj --no-build -- --mode=observe --output="$PWD/artifacts/world-observation-observe" --fixture="$PWD/fixtures/full-run/false-knight-startup-v2.hktas"
dotnet run --project scripts/WorldObservationHarness/WorldObservationHarness.csproj --no-build -- --mode=baseline --output="$PWD/artifacts/world-observation-baseline" --fixture="$PWD/fixtures/full-run/false-knight-startup-v2.hktas"
```

`observe` queries world/collider snapshots at movie frames 1, 1500, and 8500, retrieves hero/enemy details with immutable pagination, and saves each JSON response plus `report.json`. `baseline` executes the same protected frame milestones and completion checks without observation calls. Both modes seek to the fixture's final movie frame, require `Completed`, fault `0`, mismatch `0`, and verify original saves before quitting the exact owned game process.

`--sample-frames=8250,8350,...,10149` accepts a comma-separated list of movie frames after the boss milestone and before completion. It captures paused world snapshots at every requested frame, plus custom EnviousMarmu details when the population changes and at the last sample. For `fixtures/full-run/envious-marmu-simple-v2.hktas`, use `--boss-frame=8250` and samples 8250 through 10050 in increments of 100, followed by 10149. The report records the actual fixture, Runtime, Core and environment hashes. With `--overlay`, it also verifies hide/show at the boss milestone and saves `boss-overlay-live.png`.

## Interactive authoring

`--mode=interactive` starts the protected replay and pauses at Movie frame 1, then writes `ready.json` in a **new, dedicated output directory**. `Interact.py` sends one numbered command and waits up to 60 seconds for its result; issue commands sequentially. A timeout means the command may still be pending: inspect the harness/report before sending another command.

```powershell
python scripts/WorldObservationHarness/Interact.py artifacts/marmu-authoring --frame 1500
python scripts/WorldObservationHarness/Interact.py artifacts/marmu-authoring --all --inactive --details "$objectName"
python scripts/WorldObservationHarness/Interact.py artifacts/marmu-authoring --source "$fixture" --prefix 1500 --segments "1:openInventory" "15:-" --frame 1516
python scripts/WorldObservationHarness/Interact.py artifacts/marmu-authoring --quit
```

The edit example assumes the current frame is 1500 and `$fixture` contains the exact already-executed prefix. Runtime permits **future inputs only** and rejects changes to that prefix; movement targets must be current or future frames. Inputs use `count:action,action` segments (`-` means neutral). The helper appends 15000 neutral frames as temporary authoring room, so freeze and trim the final Movie before delivery. Object detail names are exact and should come from the preceding world snapshot. Each command writes world/detail JSON, before/after status and a `-done.json` receipt; observations must preserve the paused native frame and leave fault/mismatch counts zero.

Interactive success validates the commands performed, not completion or reproducibility of a final Movie. Prefer complete cold-start replays of the **same frozen fixture and installed build** for final evidence, with matching checkpoint samples and a separate exported run. The watchdog bounds interactive sessions to 30 minutes and normal runs to 4 minutes, host private memory to 1 GiB and game private memory to 3 GiB. Both paths audit original saves and close only the owned game process.

## Fixed-replay MP4 export

Use `--mode=observe --video-output=<new-absolute-mp4-path> --ffmpeg=<existing-ffmpeg-exe> --video-start=<movieFrame>`. `--video-start` defaults to 1400 and must be within 1..1500; set it to 1 to include the subsequent menu, save-entry and battle sequence. The selected start frame has already executed and is not recaptured. The Movie must use constant 50 fps; the current exporter has a 20000 rendered-frame safety limit.

With `$fixture`, `$bossFrame` and `$sampleFrames` set to the frozen Movie and its actual milestones:

```powershell
dotnet run --project scripts/WorldObservationHarness/WorldObservationHarness.csproj --no-build -- `
  --mode=observe "--fixture=$fixture" "--boss-frame=$bossFrame" "--sample-frames=$sampleFrames" `
  "--output=$PWD/artifacts/marmu-export" "--video-output=$PWD/artifacts/marmu-export/fight.mp4" `
  "--ffmpeg=$((Get-Command ffmpeg).Source)" --video-start=1
```

The harness starts capture through the formal Runtime command, then advances the same replay milestones and checkpoints. Actual loading frames enter the 50 fps H.264/AAC output; paused queries add no video time. It waits for `videoExport.state=Completed` (up to 2 minutes for finalization, still subject to the overall watchdog) and checks that the MP4 exists before quitting. `report.json` retains the export status and start Movie frame. Independently decode/probe the finished MP4 and compare the frozen replay checkpoints; file existence or an interactive run alone is not proof of a correct defeat video. Existing output files are never overwritten. See [v2 export semantics](../../docs/MP4-V2.md).

For an exact final comparison, set `HKTAS_FULL_RUN_BOSS_TRACE=1` before both runs. The Runtime writes `boss-trace.csv` under each report's `shadowRoot/HollowKnightTAS/sessions/full-run-<runId>`. Compare the two reports without opening the game:

```powershell
pwsh -File scripts/WorldObservationHarness/Compare-Traces.ps1 `
  -BaselineReport "$PWD/artifacts/world-observation-baseline/report.json" `
  -ObserveReport "$PWD/artifacts/world-observation-observe/report.json" `
  -Output "$PWD/artifacts/world-observation-trace-compare.json"
```

The comparison requires equal Runtime/Core assembly hashes, unique and identical `movieFrame` row sets, and equal semantic fields: scene, hero, boss, `deltaTime`, and RNG. `nativeFrame`, `time`, `fixedTime`, and `frameCount` are recorded as informational timing fields because normal loading loops may shift them; their differences appear under `timingDifferences` and do not fail acceptance. The report exposes `semanticEqual`, `allColumnsEqual`, and `success`; `success` follows the semantic acceptance boundary. Missing traces, duplicate rows, or semantic differences exit nonzero.

With `--overlay` on an `observe` run, the harness records `sanctum-overlay-live.png` and verifies that the click-through collider window becomes visible, hides, and shows again while the paused native frame remains unchanged. Overlay is opt-in and independent of the world snapshot protocol.

`Compare-WorldSamples.py` compares two successful runs containing `sample-<movieFrame>-world.json` and optional `sample-<movieFrame>-custom-<index>.json` files. Both `report.json` files must include matching `fixtureSha256`, `runtimeAssemblySha256`, and `coreAssemblySha256`; the nonempty sample frame sets must match. Run:

```powershell
python scripts/WorldObservationHarness/Compare-WorldSamples.py artifacts/run-a artifacts/run-b --output artifacts/world-samples-compare.json
python scripts/WorldObservationHarness/Compare-WorldSamples.py --self-test
```

The projection compares the hero's transform, rigidbody and state/resources/cooldowns, all EnviousMarmu objects' transforms, health, rigidbodies and FSM active states/variables, optional custom fields, and static terrain world-space geometry. Objects form a sorted **multiset**, so same-name clones are never deduplicated. Custom detail coverage must match between runs at each sample, but detail files are not required at every sample. Unity object references resolve to scene/name/path/type when the target is present in the sample.

The JSON report explicitly lists excluded identity, absolute timing and rendering fields. It reports current HP, clone counts, observed/inferred tiers and HP decreases for objects present at adjacent samples; those decreases are not total damage because samples can miss an object's final damage before destruction. Missing pages, collector errors, mismatched identities/hashes or semantic changes fail with exit code 2. This is checkpoint evidence, not per-frame or RNG equivalence. `boss-trace.csv` and `Compare-Traces.ps1` remain specific to the existing False Knight instrumentation and must not be used as Marmu evidence. The self-test reads the archived `artifacts/world-observation/marmu-final` schema and checks identity normalization plus deliberately changed health, position, FSM, clone count, custom fields, terrain and pagination; it never starts the game.

The projection also compares hero and EnviousMarmu colliders, including definitions, bounds, world paths, and display/physics body poses; only `geometry.attachedRigidbodyInstanceId` is removed as instance identity. `rawEqual`/`rawDifferences` preserve the unadjusted selected values alongside `semanticEqual`. Two narrowly scoped semantic rules are recorded in the report with per-sample raw diagnostics: finite nonpositive `HeroController.preventCastByDialogueEndTimer` values are already expired (`CanCast` only tests `> 0`), and an EnviousMarmu Control FSM's `Voice Player` reference to `Audio Player Actor 2D(Clone)` compares its exact name/type/componentType independently of active-list resolution. That variable is stored only by the two loaded `AudioPlayerOneShot` actions. Positive timer values, all other timers, null audio references, differently named/typed audio references and all other object-reference resolution changes remain exact. The installed timer IL evidence is `artifacts/envious-marmu-simple/dialogue-timer-il.txt`; the report diagnoses audio-pool resolution changes without claiming that an absent target must have been destroyed.
