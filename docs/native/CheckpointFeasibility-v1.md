# Process Checkpoint Feasibility v1

## Verdict

`native.checkpoint.experimental.v1` is `unsupported` in v1. No process-level
capture is advertised as a savestate, no restore API is exposed, and the N3
restore matrix is intentionally not run. Cross-start and user-selectable
savestates remain T09 replay saves; T14 may add versioned semantic keyframes
only as an optional short-tail acceleration.

## Why PSS is not restore

Windows Process Snapshotting can capture and query a VA clone, thread/context,
handle, and address-space metadata. The documented API does not provide an
operation that replaces the live target with an earlier whole-process state.
A successful `PssCaptureSnapshot` availability check therefore proves only
that a diagnostic primitive exists.

A real same-process restore would additionally need a cooperative barrier and
correct restoration of:

- every managed and native thread plus pending callbacks;
- Mono GC/runtime internals and object identity;
- Unity native objects, physics broadphase, animation, rendering, audio, and
  input subsystems;
- PlayMaker FSM actions, event queues, coroutines, and scene-loading state;
- handles, file offsets, Steam integration, GPU/driver resources, and other
  external state;
- Runtime movie/ledger/RNG cursors and all Mod-owned state.

Partial rollback can look correct for one frame while diverging immediately
afterward, so a snapshot cannot pass merely by matching one semantic hash.

## Celeste comparison

Celeste TAS tooling benefits from a mature, game-specific integration and a
comparatively tractable managed state model. Hollow Knight combines Unity
native state with extensive PlayMaker FSMs, coroutines, asynchronous scene
transitions, physics, and arbitrary Mod state. Copying a Celeste-facing
savestate technique without an HK-specific state contract would omit live
native and FSM state.

The transferable Celeste idea is the workflow: deterministic input,
frame/tick control, state inspection, repeatable replay, and fast iteration.
It is not evidence that a generic Hollow Knight process image can be safely
restored.

## Lighter alternatives

1. **T09 baseline plus full deterministic replay — implemented.** A replay
   save stores a durable baseline bundle, canonical input journal, manifest,
   and integrity chain. It is selectable across launches and supports
   periodic creation.
2. **T14 semantic keyframe plus short-tail replay — optional.** A
   build-specific, versioned DTO may restore only a proven safe room-entry
   anchor, then replay the remaining input. Any unsupported object or hash
   mismatch downgrades to full T09 replay.
3. **Read-only process observation — implemented here.** Module/thread/VA
   aggregates help diagnose determinism without changing state.
4. **Whole-process checkpoint — rejected for v1.** It remains research-only
   until capture, restore, 600-tick oracle parity, fault cleanup, and every N3
   scenario pass on an exact build.

This ordering provides the user-visible “save at any time / periodic save /
start next launch from any save” contract without pretending that an
unrestorable memory image is a savestate.

