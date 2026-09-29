# AI control guide

[中文](AI-CONTROL.md)

AI clients, scripts and Studio share the protected game-control path. Install the complete bundle using the [player guide](USER-MANUAL.en.md), then open Studio from the game's title menu to start a TAS session. Normal play does not expose a control session.

AI can launch `Companion/win-x64/HollowKnightTAS.Companion.exe --launch-game="full path to the game executable"`. This shows Studio and starts the protected game. If Studio is already open, the request goes to that window. An existing ordinary game must be closed first.

Human and AI editing can alternate without restarting Studio. While paused, `fullRunSnapshot` includes pending Studio edits; a successful `fullRunUpdateMovie` updates the grid and undo history before returning. AI step/play applies pending human future inputs first. `StudioDraftChanged` means the human edited after the AI snapshot: read again before updating. `StudioBusy` means a Studio save or edit operation is in progress. Past-input changes still require replay.

## Connect and acquire control

Tools are installed under `Companion/win-x64/Tools`:

- `HollowKnightTAS.Cli.exe`: command-line queries and control.
- `HollowKnightTAS.AgentBridge.exe`: a stdio MCP server launched as a child process by an MCP client.
- `SDK`: the .NET automation client and dependencies; connect with `AutomationClient`.

Tools read connection information, called the automation bootstrap, from `%LOCALAPPDATA%/HollowKnightTAS/automation/automation-v1.json` by default. CLI and MCP accept `--bootstrap=<full-path>` to select an entry explicitly. Connection information and authentication credentials are local to the machine and must not be distributed with sequences.

Example MCP configuration; replace the path with your installation:

```json
{
  "mcpServers": {
    "hollow-knight-tas": {
      "command": "D:/Games/Hollow Knight/hollow_knight_Data/Managed/Mods/HollowKnightTAS/Companion/win-x64/Tools/HollowKnightTAS.AgentBridge.exe",
      "args": []
    }
  }
}
```

MCP uses newline-delimited UTF-8 JSON-RPC. Complete `initialize` and the initialized notification before calling `tools/list`. stdout carries protocol messages; diagnostics go to stderr.

Sessions use `ApprovedControl` mode. Read-only queries need no lease. Writes require a short exclusive lease bound to the client, connection, session and scope. MCP provides `hktas_acquire_control` (`scopes` array and `ttlSeconds` from 1 to 300) and `hktas_release_control`. CLI acquires and releases a lease automatically for an individual write call. SDK callers manage their own leases. A lease acquired by one short-lived connection cannot be reused by another connection.

## Identify the session first

```powershell
$cli = 'D:/Games/Hollow Knight/hollow_knight_Data/Managed/Mods/HollowKnightTAS/Companion/win-x64/Tools/HollowKnightTAS.Cli.exe'
& $cli automation status
& $cli automation call getCapabilities observe.status
```

Use the commands, scopes and availability returned by `getCapabilities`. Studio uses full-run v2, starting at native startup frame 0. Studio timelines, checkpoints and quick-slot bindings last only for the current Studio session; save a sequence file to preserve input across sessions. Full-run control commands are available through CLI or the SDK's `CreateCommand` / `ExecuteAsync`. MCP exposes world-observation and video tools but has no dedicated `fullRun*` control tools.

CLI returns a result envelope with `success` as the string `"true"` or `"false"`. Decode `dataBase64` as UTF-8 JSON containing string values; some values contain nested JSON. This helper checks request success and decodes the payload:

```powershell
function Invoke-Tas([string[]]$CommandArgs) {
    $raw = & $cli @CommandArgs
    $exitCode = $LASTEXITCODE
    $r = $raw | ConvertFrom-Json
    if ($exitCode -ne 0 -or $r.success -ne 'true') {
        throw "$($r.resultCode): $($r.detail)"
    }
    [Text.Encoding]::UTF8.GetString(
        [Convert]::FromBase64String($r.dataBase64)) | ConvertFrom-Json
}
$s = Invoke-Tas @('automation', 'call', 'fullRunStatus', 'observe.status')
$s
```

`nativeFrame` counts native loops; Movie frames describe input sequence positions. Loading may advance only the former. Read the current mode and native frame before writing, and pass the observed value as a precondition. Do not substitute an input row number for `expectedNativeFrame`.

## Full-run recording and playback

| Command | Scope | Arguments and conditions |
| --- | --- | --- |
| `fullRunStatus` | `observe.status` | Read native frame, mode, connection, fault and save-protection status |
| `beginFullRunRecording` | `control.recording` | Paused at startup frame 0; `expectedNativeFrame=0`, `mouseEnabled=true/false`, optional `fps` |
| `beginFullRunReplay` | `control.playback` | Paused at startup frame 0; `expectedNativeFrame=0`, `movieBase64`, optional `pauseAtFrame` |
| `fullRunStep` | `control.step` | Paused; exact `expectedNativeFrame`; advance one native frame |
| `fullRunPlay` | `control.playback` | Paused; exact `expectedNativeFrame`; run continuously |
| `fullRunPause` | `control.playback` | Running; pass an observed `expectedNativeFrame` |
| `fullRunMovie` | `movie.read` | Read the Movie file location after Runtime connects |
| `fullRunSnapshot` | `movie.read` | At a paused boundary, save a complete Movie snapshot and return its `path` |
| `fullRunUpdateMovie` | `control.playback` | Paused; `moviePath` must be inside the current protected shadow directory; exact `expectedNativeFrame` |
| `fullRunSeek` | `control.playback` | Paused; `targetFrame` is a Movie position, with a separate exact `expectedNativeFrame`; set a forward-playback pause target |
| `fullRunStop` | `control.playback` | Paused; exact `expectedNativeFrame`; stop and return the document response |
| `quitGame` | `control.playback` | Exit normally at a paused boundary |

At startup frame 0, before recording or replay has been armed, this example starts recording and advances one native frame:

```powershell
Invoke-Tas @('automation','call','beginFullRunRecording','control.recording',
    'expectedNativeFrame=0','mouseEnabled=false','fps=50','--expected-mode=Paused')
$s = Invoke-Tas @('automation','call','fullRunStatus','observe.status')
Invoke-Tas @('automation','call','fullRunStep','control.step',
    "expectedNativeFrame=$($s.nativeFrame)",'--expected-mode=Paused')
```

Call `fullRunPlay` in the same manner to run. For pause, pass `--expected-mode=Running`. Avoid concurrent writes from an AI client and Studio. If a changed state invalidates a precondition, observe again and reconsider the action instead of repeatedly submitting a stale frame number.

`beginFullRunReplay` accepts Base64-encoded UTF-8 canonical v2 Movie bytes, not a `.hktaspack` container. Load sequence packages through Studio to apply their bound initial saves. Parse and canonicalize complete Movies with Core's `MovieV2Codec`, retaining the real environment header and input channel order. Use the SDK for longer documents to avoid command-line length limits, but requests still have a 900000-character field limit and a 1 MiB message limit. The SDK does not automatically chunk full-run replay requests. Open the file in Studio when it exceeds the request size.

Full-run channels are `hero`, `preMenu`, `binder`, `mouseInControl` and `mouseHollowKnight`. Menu and hero input are separate; changing a hero direction does not necessarily move a menu. Repeating the same held input maintains a press. Insert a released frame when a new press edge is needed. Input structure and action order are defined in `src/HollowKnightTAS.Core/Movie/MovieProtocolV2.cs` and `src/HollowKnightTAS.Core/Movie/MovieV2Codec.cs`.

The `path` returned by `fullRunSnapshot` points to a Movie snapshot in the current shadow directory. Read it and save a candidate alongside it. Submit future-input edits through `fullRunUpdateMovie`, retaining the executed prefix. After `fullRunSeek` sets a target, call `fullRunPlay` to start advancing. Returning to the past requires restarting from the bound starting conditions and replaying; Studio's replay-to-frame and timeline handle that process. `fullRunSeek` is not a backward restore command. Shared files must remain inside the session's shadow directory; do not modify real user saves.

The recording parameter `fps` accepts values from 1 to 1000 with up to 6 decimal places, for example `fps=99.999`. Movie v2 stores fractional rates as a reduced `fps / fpsDenominator` ratio: `{"repeatCount":1,"fps":99999,"fpsDenominator":1000,"samples":[]}`. Integer rates omit the denominator and the default of 50 still omits `fps`, preserving existing integer canonical content. The denominator requires `fps`; the result must be in range and exactly representable with up to 6 decimal places. Older parsers reject the new denominator field.

Movie v2 frame records accept an optional integer `rngSeed` from -2147483648 to 2147483647. Omit it to leave RNG unchanged. A seeded record must have `repeatCount: 1`, for example `{"repeatCount":1,"authored":true,"rngSeed":12345,"samples":[]}`. The seed applies once before that Movie frame, after scene RNG synchronization; loading frames do not apply it. It contributes to canonical content and worldline prefix identity, so editing an executed frame requires replay. Older parsers reject this field.

## Non-visual world observation

Once Runtime connects, full-run v2 supports these read-only queries. At a paused boundary they do not advance gameplay, clocks or random state:

```powershell
Invoke-Tas @('automation','call','getWorldSnapshot','observe.state.deep',
    'view=world','includeInactive=false','offset=0','limit=64')
```

MCP uses `hktas_get_world_snapshot`; SDK uses `GetWorldSnapshotAsync`, or `GetWorldSnapshotJsonAsync` for automatic pagination. `view` accepts `world`, `all` or `colliders`; `limit` is 1–128. `snapshotJson` contains `metadata`, `objects`, `total` and `nextOffset`. Subsequent pages must use the same `snapshotId`; `nextOffset=-1` ends the snapshot.

Objects include the hero, enemies, transforms, resources, charms, FSM states and variables, and collision shapes. Use `all` with `includeInactive=true` to include inactive objects. Object identifiers are bound to the session, scene and instance; refresh after scene changes.

For full component and FSM details:

```powershell
Invoke-Tas @('automation','call','getObjectDetails','observe.state.deep',
    'objectId=<object-ID-from-snapshot>','expectedNativeFrame=<current-paused-native-frame>',
    'cursor=0','maxCharacters=100000')
```

MCP uses `hktas_get_object_details`. SDK's `GetObjectDetailsJsonAsync` joins chunks and verifies SHA-256. For manual pagination, retain `objectId/detailsId`, join `detailsJson` in `cursor/nextCursor` order, and verify `totalCharacters` and the full UTF-8 `sha256` before parsing. The last chunk has `nextCursor=-1` and `complete=true`. Discard expired pages and start a new observation.

Inspect `errors`, `omitted`, `omittedCount` and scope metadata. Missing fields do not mean zero or false, and geometric bounds are not exact collision results. Screen projections use normalized game-viewport coordinates, not desktop coordinates. Snapshots have budgets; the full internal rules of a complex Mod may not be observable.

## Video, completion and failures

`startVideoExport` / `hktas_start_video_export` requires `control.playback` and a paused valid replay. The bundled encoder is used by default; `ffmpegPath` is an optional override (pass `null` in the SDK to use the bundled encoder). Required arguments are a new `outputPath`, a positive `maximumFrames`, and `expectedRuntimeMode=Paused`. `maximumFrames` caps capture. It must be strictly greater than the selected Movie frame range, with additional room for loading frames. Optional `endMovieFrame` selects the Movie endpoint. Full-run export captures the remaining interval from the current replay position and finishes automatically. Use Studio to prepare a complete-sequence or timeline-range export. `cancelVideoExport` takes that operation's `operationId`. Full-run export does not use a manual finish step.

Success means a request was accepted. Read `fullRunStatus` / `getStatus` and inspect actual frames, faults, playback stop reason and export state. The matching operation's `Completed` state means completion; `Failed` and `Cancelled` do not. Parse nested Runtime fields as returned.

After a timeout, query the original session and operation before taking further action. Do not resubmit input, restore or export blindly. If restarting invalidates a session, reload the bootstrap, reconnect and check session and operation identities. Resolve environment mismatches by restoring the matching environment, rather than rewriting fingerprints in a recording.

## In-game session interfaces

When the capability catalog exposes in-game control commands, available tools include `hktas_get_state`, `hktas_step_with_input`, `hktas_queue_input_batch`, input transactions, branch editing and replay saves. They use `expectedRuntimeMode` and `expectedMovieTick`; input submission also checks `expectedSceneEpoch`. These fields do not replace full-run `expectedNativeFrame` preconditions.

An accepted in-game save request is not yet a completed save. Match its request ID, terminal status and `getReplaySaves` entry. Track restore using the returned `operationId` in `coldRestore`, and reconnect after completion. Cancellation must carry the same operation ID. A save-overwrite request requires authorization for the specific files and target. Initial-save packages, Studio timelines and in-game replay saves are different data objects; do not interchange their IDs or file formats.

Definitions are in `src/HollowKnightTAS.Core/Automation/AutomationCommandIds.cs`, `src/HollowKnightTAS.Companion/Automation/AutomationCapabilityCatalog.cs`, `src/HollowKnightTAS.AgentBridge/McpCatalog.cs` and `src/HollowKnightTAS.Automation.Client/AutomationClient.cs`.

[Installation and player guide](USER-MANUAL.en.md)
