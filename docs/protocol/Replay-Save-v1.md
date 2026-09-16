# HollowKnightTAS Replay Save v1

## 1. Scope and terminology

Replay Save v1 is the durable savestate protocol used by HollowKnightTAS. A
replay save is not a process-memory snapshot. It is an integrity-checked tuple:

```text
desktop baseline bundle
+ contiguous input/command journal from movie tick 0
+ target semantic snapshot
+ environment manifest and ledger summary
+ a cursor whose next value is effectiveMovieTick + 1
```

The Chinese UI term “存档点” and the API term `replay save` refer to this tuple.
An optional accelerator may shorten restoration, but the tuple above remains
the portable source of truth and full replay remains the correctness fallback.

Schema version `1` is intentionally fail-closed. Unknown schema versions,
missing objects, wrong hashes, journal gaps, or environment mismatches must not
be treated as a usable save.

## 2. User-visible contract

### 2.1 Save request

A manual request may be issued during gameplay, pause, movement, combat, or a
scene transition. The request records:

- `requestedAtUtc`: wall-clock time at which the caller requested the save;
- `requestedAtMovieTick`: latest committed movie tick visible to the caller,
  or `-1` if no tick has committed yet;
- `effectiveMovieTick`: the next safe, fully committed tick at which the
  journal and semantic snapshot were frozen.

The request remains `Pending` until that safe point. If the process exits first,
the request becomes an explicit failure; no entry is published.

If a request is received outside the safe gameplay phase, its minimum eligible
tick is `requestedAtMovieTick + 1`; it can never be retroactively attached to
the tick that was already visible when the unsafe request arrived. A request
received from the safe committed-tick callback may use that current tick.

### 2.2 Automatic saves

Automatic time is measured only in committed movie ticks. The default policy is
an interval of `18,000` ticks and retention of the newest `20` automatic
entries. Paused time, loading time, and any period in which the movie cursor
does not advance produce no automatic request.

Retention never removes manual or movie-command entries. Pinning converts an
automatic entry to manual before the next retention pass.

### 2.3 Restore

Restore validates all objects before changing game state. It then:

1. obtains a lease for the configured dedicated TAS save slot;
2. requires explicit approval when the slot bytes differ;
3. creates an exact byte backup before an approved overwrite;
4. if gameplay is active, returns to `Menu_Title` with
   `ReturnToMainMenu(DontSave)` and waits for the menu lifecycle to stabilize;
5. loads the stored baseline through `GameManager.LoadGame` and
   `ContinueGame`;
6. replays the canonical prefix without manual input;
7. pauses after `effectiveMovieTick`;
8. compares the live semantic hash with `semanticSnapshotSha256`;
9. exposes `nextMovieTick = effectiveMovieTick + 1` only after exact equality.

A mismatch is `RestoreDesync`; a nearby state is never success. Cancellation or
failure releases injected input, restores time/bindings, and rolls back an
uncommitted slot lease.

An accelerator result is accepted only when both its resume cursor equals
`effectiveMovieTick + 1` and a semantic snapshot captured at a real
`LateUpdateEnd` equals the target hash. Runtime pauses before that capture so
the candidate state cannot advance between restore and verification. A thrown
exception, null result, refusal, wrong cursor, or hash mismatch restores the
temporary time-state lease and starts the normal baseline load plus full replay
from a clean lifecycle.

The equality gate above is the SHA-256 of the exact canonical Semantic Snapshot
v1 bytes. The `v1-float32-decimal-4` projection may be emitted as a diagnostic
to explain sub-ULP differences, but projected equality cannot authorize a
restore.

### 2.4 Original-runtime clock boundary

The former `unity-capture-delta-equals-fixed-v1` profile is retired. Replay
Save does not write `Time.captureDeltaTime`, `Time.fixedDeltaTime`,
`Time.timeScale`, `Application.targetFrameRate`, or
`QualitySettings.vSyncCount`. The legacy configuration flag is accepted only
for compatibility and is ignored by Runtime.

The movie journal and restore cursor advance only while the observed vanilla
`Time.timeScale` is bit-exactly `1.0f`; hit-stop, pause, loading, and slow time
remain vanilla-owned. Reference and candidate verification may use a common
external clock/phase/RNG environment, but it must act symmetrically on both
processes and is not a Runtime replay patch.

Target replay-save snapshots use position source
`hero-transform-position-v1`: `hero.position.x/y` are captured as the raw
Float32 `Transform.position`, while velocity comes from `Rigidbody2D` and Hero
state from `HeroController`. Capture and target verification use the same
source. Coordinates are not rounded or quantized; only the exact canonical
Semantic Snapshot v1 SHA-256 can authorize a restore.

## 3. Store layout

The store root is:

```text
Application.persistentDataPath/HollowKnightTAS/replay-saves/v1/
  catalog.json
  entries/<replaySaveId>.json
  objects/sha256/<first-two-hex>/<lowercase-sha256>
  transactions/<transactionId>/
    entry.json
    state.json
  trash/
  slot-backups/
```

Object names are the lowercase SHA-256 of their exact bytes. Existing objects
are reused only after their bytes hash to their name.

`catalog.json` is a derived index. Recovery rebuilds it from published entries;
an entry does not depend on a stale or missing catalog to remain recoverable.
Catalog ordering is stable by effective movie tick, creation time, then ID.

## 4. Descriptor and entry envelope

`entries/<replaySaveId>.json` is canonical UTF-8 without BOM:

```json
{
  "descriptor": {
    "autoRetentionCount": 0,
    "baselineId": "slot-2-semantic-0001",
    "baselineObjectSha256": "<sha256>",
    "baselineSemanticSha256": "<sha256>",
    "createdAtUtc": "2026-07-28T00:00:00.0000000Z",
    "effectiveMovieTick": 142,
    "journalHeadSha256": "<sha256>",
    "journalSegmentObjectSha256s": ["<sha256>"],
    "label": "manual-combat",
    "ledgerSummarySha256": "<sha256>",
    "manifestSha256": "<sha256>",
    "movieObjectSha256": "<sha256>",
    "reason": "Manual",
    "replaySaveId": "save-...",
    "requestedAtUtc": "2026-07-28T00:00:00.0000000Z",
    "requestedAtMovieTick": 142,
    "sceneEpoch": 0,
    "sceneName": "GG_Workshop",
    "schemaVersion": 1,
    "semanticSnapshotSha256": "<sha256>"
  },
  "descriptorSha256": "<sha256-of-canonical-descriptor>"
}
```

The supported reasons are `Manual`, `AutomaticInterval`, and
`MovieCheckpointCommand`. Only `AutomaticInterval` carries a positive
`autoRetentionCount`; the other reasons carry `0`.

The descriptor references these objects:

| Field | Object bytes |
|---|---|
| `manifestSha256` | canonical environment manifest |
| `baselineObjectSha256` | Baseline Bundle v1 or v2 |
| `movieObjectSha256` | canonical HK TAS Movie v1 prefix |
| `journalSegmentObjectSha256s` | ordered Journal Segment v1 chain |
| `semanticSnapshotSha256` | target Semantic Snapshot v1 |
| `ledgerSummarySha256` | canonical JSON tick-range summary |

`journalHeadSha256` must equal the final segment object hash. The concatenated
segments must cover exactly movie ticks `0..effectiveMovieTick`.

## 5. Binary objects

All integers are little-endian. Length-prefixed byte/string values use a signed
32-bit byte length. Strings are strict UTF-8 without BOM.

### 5.1 Baseline Bundle v1 / v2

Magic: ASCII `HKTB`.

```text
magic[4]
schemaVersion:int32
baselineId:utf8
baselineSemanticSha256:utf8
originalSlot:int32
capturedAtUtcTicks:int64
saveData:bytes
hasModdedSaveData:uint8
moddedSaveData:bytes              # present only when flag = 1
semanticSnapshot:bytes
```

The semantic snapshot must hash to `baselineSemanticSha256`. Limits are
64 MiB for the desktop save, 16 MiB for modded JSON, and 1 MiB for the semantic
snapshot.

Bundle schema 2 appends the following after `semanticSnapshot`. Descriptor and
catalog versions are unchanged. Schema 1 objects retain their exact encoding.

```text
hasRecordingOrigin:uint8         # 0 or 1; following fields only when 1
profileId:utf8                   # at most 256 UTF-8 bytes
rootBoundarySecondsBits:int64    # IEEE 754 double bits
framesAfterRootBoundary:int32
gameTimeSecondsBits:int64        # IEEE 754 double bits
fixedTimeSecondsBits:int64       # IEEE 754 double bits
```

Times must be finite, nonnegative (negative zero is rejected), and journal game
and fixed times cannot precede the root. Frame offset must be nonnegative.
The origin is included in the bundle object hash. Absent metadata is unknown,
not an inferred verified origin. These observations do not prove deterministic
restoration; the target must independently match the persisted origin before
releasing replay input. The target checks the persisted frame offset and exact
clock bits, and rejects unsupported profile/root settings before requesting the
root. Interactive sources project an integer number of fixedDeltaTime steps
approximately 30 game seconds ahead from the current double clock and persist
the resulting float-representable target. Whole-second rounding is not used.
Targets configure ClockPayload with that exact persisted value and verify
readback. Configuration is immutable and must precede root preparation. A target
that is too late rejects the restore rather than selecting a replacement root.
Dynamic boundary reachability and in-game equivalence remain unverified.

### 5.2 Journal Segment v1

Magic: ASCII `HKTJ`. A segment contains 1 to 4,096 records.

```text
magic[4]
schemaVersion:int32
sequence:int32
previousObjectSha256:utf8
recordCount:int32
repeat recordCount:
  movieTick:int64
  inputTick:uint64
  visualTick:int64
  fixedTick:int64
  sceneEpoch:int32
  phase:uint8
  held:uint16
  pressed:uint16
  released:uint16
  axisX:int16
  axisY:int16
```

Sequence `0` uses 64 lowercase zeroes as `previousObjectSha256`. Every later
segment references the SHA-256 of the immediately preceding segment. Movie
ticks are contiguous both inside and across segments.

## 6. Commands and scene transitions

Input alone drives ordinary transitions. An explicit transition initiated by a
script or external controller is journaled as an HK TAS Movie v1 checkpoint
immediately before the next committed movie tick. Its identifier starts with:

```text
hktas.force-scene.
```

The suffix is canonical base64url-encoded transition data. Restore validates it
before calling the normal `GameManager.BeginSceneTransition` lifecycle. An
unknown or malformed reserved command is corruption, not a no-op.

## 7. Atomic commit and recovery

Commit stages are:

```text
TransactionCreated
ObjectsPublished
DescriptorPrepared
EntryPublished
RetentionApplied
CatalogPublished
```

Each file is written to a same-directory temporary file, flushed to durable
storage, then replaced or moved atomically. The catalog is published last.

After a crash:

- a valid published entry is visible even if catalog publication did not run;
- a transaction without `CatalogPublished` is reported as incomplete;
- a transaction that never published an entry is not visible;
- a corrupt entry is retained as evidence and surfaced with a concrete status;
- automatic retention moves victim descriptors to `trash`; content objects are
  not eagerly deleted.

## 8. Validation order and statuses

Validation performs, in order:

1. descriptor schema and entry-envelope integrity;
2. current manifest equality;
3. existence and SHA-256 of every referenced object;
4. ordered, contiguous journal chain and exact terminal tick;
5. baseline identity and semantic hash;
6. target Semantic Snapshot v1 and scene consistency;
7. Movie v1 parse/static validation and expanded tick count;
8. byte-exact input parity between movie expansion and journal records.

The public status is one of:

```text
Pending, Ready, Restoring, Corrupt, Incompatible, JournalGap,
RestoreDesync, Failed, Cancelled
```

`Ready` is the only loadable catalog state.

## 9. Privacy and compatibility

Baseline objects and slot backups contain real save bytes. They remain under
the game persistent-data directory and must not be copied into repository
fixtures, diagnostic archives, logs, or IPC responses. External APIs may expose
IDs, hashes, sizes, statuses, progress, and semantic data, but never raw baseline
or backup bytes unless a future protocol adds a separate explicit export
capability.

Implementations may add accelerators or garbage collection. They must not:

- change canonical v1 bytes;
- weaken exact target-hash verification;
- silently overwrite a differing user slot;
- make portable restore depend on an accelerator;
- delete manual entries or normal-slot backups as background retention.
