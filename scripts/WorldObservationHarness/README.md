# WorldObservationHarness

`WorldObservationHarness` is an installation acceptance harness for the protected full-run Runtime. It does not use desktop automation or write game saves. It owns the current-user Companion control mutex, starts the verified shadow-save launch, and receives the Runtime registration through the formal `ControlPipeServer` path:

`SingleInstanceCoordinator -> ControlPipeServer -> SessionRegistry -> RuntimeSessionClient`

The installed Companion may be configured with `AutoStartCompanion=true`; when it sees the harness mutex it forwards the registration to the harness. If another Companion or Studio already owns the mutex, stop it before starting the harness. The harness does not edit global settings. A direct formal `--bootstrap-pipe=...` or `--bootstrap-handle=...` invocation is also accepted and is registered through the same `SessionRegistry`; the token is never logged or written to the report.

Build against the installed Companion/Core bundle:

```powershell
dotnet build scripts/WorldObservationHarness/WorldObservationHarness.csproj -p:SkipHKTASInstall=true
```

Run from the repository root after the build (the root agent performs the real-game run):

```powershell
$env:HKTAS_FULL_RUN_BOSS_TRACE = '1'
dotnet run --project scripts/WorldObservationHarness/WorldObservationHarness.csproj --no-build -- --mode=observe --output="$PWD/artifacts/world-observation-observe" --fixture="$PWD/fixtures/full-run/false-knight-startup-v2.hktas"
dotnet run --project scripts/WorldObservationHarness/WorldObservationHarness.csproj --no-build -- --mode=baseline --output="$PWD/artifacts/world-observation-baseline" --fixture="$PWD/fixtures/full-run/false-knight-startup-v2.hktas"
```

`observe` queries world/collider snapshots at movie frames 1, 1500, and 8500, retrieves hero/enemy details with immutable pagination, and saves each JSON response plus `report.json`. `baseline` executes the same protected frame milestones and completion checks without observation calls. Both modes seek to the fixture's final movie frame, require `Completed`, fault `0`, mismatch `0`, and verify original saves before quitting the exact owned game process.

