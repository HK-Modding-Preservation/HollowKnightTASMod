# Semantic Keyframe Protocol v1

## Status

The protocol and fail-closed store are implemented. The current Hollow Knight
runtime advertises only `ReplayOnly`. No `RoomEntry` gate is enabled, no
keyframe is captured, and T09 full replay remains the sole persistent truth.

This is an explicit safety downgrade, not a claim that semantic restore has
passed in-game parity.

## Product contract

- A T09 replay save is complete without T14 files.
- T14 may associate a compatible keyframe at tick `k <= t` with a T09 save at
  tick `t`; it never edits the T09 descriptor.
- A successful accelerator must restore `K@k`, replay `J(k,t]`, match the T09
  target semantic SHA-256, and leave the next input cursor at `t + 1`.
- Any missing object, noncanonical document, build/manifest/baseline/journal
  mismatch, unsupported required adapter, apply fault, or semantic mismatch
  discards the candidate and requires a clean T09 baseline replay.
- Neither a keyframe failure nor removal may delete or rewrite a T09 entry,
  baseline, journal, ordinary save slot, or expected hash.

## Support tiers

| Tier | Meaning | v1 runtime status |
|---|---|---|
| `ReplayOnly` | No proven safe semantic restore boundary. | Enabled and enforced. |
| `RoomEntry` | A specifically whitelisted room-entry lifecycle plus short-tail replay. | Protocol only; zero gates whitelisted. |
| `RegisteredRuntime` | Explicit object/FSM adapters. | Reserved; not enabled. |

`ReplayOnlyRestoreAccelerator` implements the T09 optional accelerator
interface but always returns `Unavailable`. Its restore method is also
defensive and never applies state if called directly.

## Canonical files

The descriptor conforms to
`schemas/semantic-keyframe-v1.schema.json`; the adapter registry conforms to
`schemas/keyframe-adapter-manifest-v1.schema.json`.

Canonical JSON rules are stricter than JSON Schema:

1. UTF-8 without BOM.
2. Exact field order emitted by the Core codec.
3. No insignificant whitespace, unknown property, duplicate property, or
   trailing byte.
4. Lowercase 64-hex SHA-256 values.
5. Adapter arrays sorted by ordinal `adapterId`, with unique IDs.
6. Enum spelling and case are exact.
7. Parsing is followed by canonical reserialization and byte-for-byte
   comparison.

Consequently an otherwise valid but reordered JSON document fails closed.

## Descriptor bindings

`semantic-keyframe-v1` binds:

- keyframe ID, tier, status, capture movie tick, scene, entry gate and epoch;
- exact game build, environment manifest and T09 baseline hashes;
- journal head, adapter manifest, semantic snapshot and RNG hashes;
- each adapter ID, schema, required flag, payload hash and payload length.

A persisted descriptor cannot claim `ReplayOnly`; that tier means there is no
keyframe artifact to persist.

Compatibility additionally requires:

- descriptor status `Ready`;
- `captureMovieTick <= targetMovieTick`;
- descriptor tier no higher than the current runtime tier;
- exact game, manifest, baseline and adapter-manifest hashes;
- the keyframe journal head is a verified ancestor in the target journal chain;
- every required runtime adapter for the tier is present;
- every required descriptor adapter is known with the exact schema,
  required flag and minimum tier;
- no unregistered runtime-mod profile.

Unknown required adapters fail with `UnsupportedAdapter`. Unknown optional
adapters may be ignored only after every hash binding above matches.

## Content-addressed store

Given the T09 root:

```text
replay-saves/v1/
  keyframes/
    entries/<keyframeId>.json
    index.json
    transactions/<transactionId>/state.json
    quarantine/
  objects/sha256/<first-two-hex>/<sha256>
```

Keyframe payloads share the T09 content-addressed object namespace. Commit
order is:

1. transaction state;
2. all verified objects;
3. prepared canonical descriptor;
4. atomically published descriptor;
5. atomically published independent accelerator index.

The index is the visibility boundary for restore planning. A crash before its
publication may leave reusable objects or a descriptor, but never a candidate
for a T09 save. Recovery ignores temporary files, reports incomplete
transactions, filters orphaned associations, and quarantines a corrupt index.

Removing an association or an unreferenced descriptor deliberately does not
delete any object. Automatic object GC is disabled in `ReplayOnly`; this
prevents an accelerator from deleting data owned by T09. A future GC must
first implement and verify a unified T09/T14 reference graph and grace period.

## Runtime capability report

The session event `semantic-keyframe-tier` contains:

- `enabled`;
- `tier`;
- `acceleratorRegistered`;
- `captureAllowed`;
- stable comma-separated `reasonCodes`;
- `restorePlan=FullReplay`.

The IPC capability catalog reports
`restore.keyframe-tail=replay-only` when the setting is requested, or
`disabled` otherwise. `restoreAccelerationStatus` repeats the tier, reasons
and `FullReplay` plan for nonvisual external clients.

Current stable downgrade reasons include:

- `no-verified-room-entry-gates`;
- `rng-coverage-not-complete`;
- `unregistered-runtime-mod-profile` when applicable.

## Promotion gate

`RoomEntry` remains unreachable until each proposed gate has all of the
following retained evidence:

1. safe committed-tick, scene lifecycle, Hero-ready and save-write exclusion;
2. complete versioned adapters for persistent data, scene data, Hero, RNG and
   input/tick cursor;
3. three rooms by five target classes, with full replay and accelerated replay
   each passing 10/10 exact target hashes;
4. exact cursor `t + 1` and a matching 600-tick continuation oracle;
5. corrupt, incompatible, missing-adapter, unsafe-capture and partial-apply
   cases all cleanly reloading baseline and passing T09;
6. measured tick and wall-clock data without an unverified speed claim.

Until then, the correct result is a transparent `ReplayOnly` downgrade.
