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

For an exact final comparison, set `HKTAS_FULL_RUN_BOSS_TRACE=1` before both runs. The Runtime writes `boss-trace.csv` under each report's `shadowRoot/HollowKnightTAS/sessions/full-run-<runId>`. Compare the two reports without opening the game:

```powershell
pwsh -File scripts/WorldObservationHarness/Compare-Traces.ps1 `
  -BaselineReport "$PWD/artifacts/world-observation-baseline/report.json" `
  -ObserveReport "$PWD/artifacts/world-observation-observe/report.json" `
  -Output "$PWD/artifacts/world-observation-trace-compare.json"
```

The comparison requires equal Runtime/Core assembly hashes, unique and identical `movieFrame` row sets, and equal semantic fields: scene, hero, boss, `deltaTime`, and RNG. `nativeFrame`, `time`, `fixedTime`, and `frameCount` are recorded as informational timing fields because normal loading loops may shift them; their differences appear under `timingDifferences` and do not fail acceptance. The report exposes `semanticEqual`, `allColumnsEqual`, and `success`; `success` follows the semantic acceptance boundary. Missing traces, duplicate rows, or semantic differences exit nonzero.

With `--overlay` on an `observe` run, the harness records `sanctum-overlay-live.png` and verifies that the click-through collider window becomes visible, hides, and shows again while the paused native frame remains unchanged. Overlay is opt-in and independent of the world snapshot protocol.
