# Native Capability Protocol v1

## Scope

Native Capability Protocol v1 is the local, opt-in bridge between the
authenticated Runtime session, the signed Companion, and the one-shot
`HollowKnightTAS.NativeHost.exe` sidecar. It exposes fixed capability IDs; it
does not expose process selection, addresses, DLL paths, shell commands, raw
memory reads, or raw memory writes.

`EnableNativeCapabilities=false` is the fail-closed default. Runtime/T09
continues to provide recording, replay, arbitrary manual replay saves,
periodic replay saves, and cross-start restore when native support is disabled
or unavailable.

## Launch and trust binding

1. Runtime creates a random current-user pipe and 256-bit session token.
2. The authenticated Companion registration binds the Runtime session ID,
   game PID, game process creation time, environment-manifest hash, Runtime
   and Core assembly hashes, protocol range, and the explicit
   native-capability request bit.
3. Companion verifies the signed bundle, every declared file hash, RID, and
   protocol range.
4. Companion launches only the fixed signed NativeHost path with no command
   line arguments, redirected standard input/output, and a kill-on-close Job
   Object.
5. NativeHost resolves its parent PID through the Windows process snapshot
   API and requires the parent image to be the exact sibling
   `HollowKnightTAS.Companion.exe`.
6. NativeHost binds the target PID to the authenticated creation time,
   canonical image path hash, image hash, AMD64 PE machine, and the signed
   native build whitelist.

The random per-request credential and HMAC bind the response to one Companion
request and detect response substitution. The credential is not treated as
caller authentication by itself; parent-process verification is mandatory
before NativeHost reads the request.

## Capability descriptor

Every descriptor contains:

- stable ID and semantic version;
- state: `unavailable`, `experimental-disabled`, `experimental-enabled`,
  `verified`, `rejected-build`, `faulted`, or `unsupported`;
- required permissions;
- NativeHost, NativeBridge, and safe-barrier requirements;
- supported OS IDs, architecture IDs, and build-whitelist IDs;
- time, memory, and disk budgets;
- mutual exclusions and fallback;
- evidence SHA-256, `pending`, or `not-applicable`.

The v1 catalog is:

| Capability | State before evidence | Permission | Bridge | Fallback |
| --- | --- | --- | --- | --- |
| `native.process.observe.v1` | disabled or explicitly requested | `ProcessQuery` | no | Runtime/T09 |
| `native.input.override.experimental.v1` | unsupported | none | no implementation | pure Runtime |
| `native.clock.trace.experimental.v1` | unsupported | none | no implementation | pure Runtime |
| `native.capture.experimental.v1` | unsupported | none | no implementation | Runtime capture |
| `native.checkpoint.experimental.v1` | unsupported | none | no implementation | T09 and T14 ReplayOnly |

Unsupported IDs are deliberately present so clients cannot infer support from
an absent or stale UI entry.

## Native observe request

NativeHost accepts exactly one canonical UTF-8 JSON string map on inherited
stdin and accepts no arguments. Required fields are:

```text
attachCycles
credential
expectedImagePathSha256
expectedImageSha256
protocolVersion
requestId
sessionId
targetPid
targetStartTimeUtcTicks
```

`attachCycles` is fixed to `100` for the N1 gate. Each cycle opens the
authenticated process, rechecks creation time and canonical path, obtains a
query handle, and disposes it. The full observation then exports only:

- image and `Assembly-CSharp.dll` SHA-256;
- authenticated environment-manifest, Runtime assembly, and Core assembly
  SHA-256;
- build-whitelist ID and AMD64 result;
- module-name/hash aggregate and module count;
- thread-ID aggregate and thread count;
- committed/reserved virtual-memory totals;
- PSS API availability;
- fixed capability states;
- `rawPagesPersisted=false`;
- HMAC-SHA-256 proof over the canonical response.

No module path, memory page, token, credential, command line, keyboard input,
or user document content is returned or persisted.

## Runtime evidence

Runtime persists two strict evidence records:

1. `status=started`, capability ID, request ID, and
   `fallback=runtime-t09`.
2. `status=verified` with capability/evidence versions, whitelist and target
   hashes, 100-cycle proof, PSS availability, parent verification,
   `checkpointStatus=unsupported`, `rawPagesPersisted=false`, and
   `fallback=none`.

A failure produces only an error type, request ID,
`status=faulted`, and `fallback=runtime-t09`. Free-form exception detail and
paths are rejected by the Runtime evidence parser.

## Lifecycle and failure

- NativeHost has a 10-second deadline and is killed when its Job Object closes.
- NativeHost is one-shot and cannot outlive a completed observation.
- A missing, tampered, incompatible, timed-out, or crashed host changes only
  that capability to unavailable/faulted.
- Runtime never enters a native checkpoint barrier in v1.
- Companion or NativeHost failure does not terminate Hollow Knight and does
  not remove or mutate T09 replay saves.
