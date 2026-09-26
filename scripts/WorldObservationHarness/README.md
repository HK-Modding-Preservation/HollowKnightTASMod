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
