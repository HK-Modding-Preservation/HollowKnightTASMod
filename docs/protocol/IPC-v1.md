# HollowKnightTAS IPC v1

## Scope

IPC v1 is the private, local Runtime-to-Companion transport. It is not the
public T15 automation endpoint. The Runtime owns a randomly named Windows
named pipe for each game session; the pipe is restricted to the current user
and additionally authenticated with a 256-bit session token.

## Framing

Every message is:

```text
uint32 big-endian JSON byte length
canonical UTF-8 JSON envelope
```

- The length must be in `[1, 1048576]`.
- UTF-8 is strict: BOM, invalid byte sequences and trailing content fail.
- The JSON representation must equal the canonical reserialization byte for
  byte. Duplicate properties, comments, alternate property order, whitespace,
  non-string payload values and unknown envelope fields fail.
- A movie is uploaded in chunks. The aggregate canonical movie limit is
  32 MiB even though each envelope is limited to 1 MiB.

The envelope is:

```json
{"protocolVersion":1,"sessionId":"<safe-id>","sequence":1,"messageType":"ping","payload":{"requestId":"r1"}}
```

`payload` is a canonical object with ordinally sorted safe field names and
string values. Typed commands parse each string with invariant culture and
reject missing, extra, out-of-range or ambiguous fields.

## Authentication and ordering

The first session message is `hello` at sequence `0`. Its payload binds:

- product and Companion version;
- Runtime protocol range;
- game PID and Runtime session ID;
- game-process UTC creation ticks, preventing PID reuse;
- environment-manifest, Runtime assembly, and Core assembly SHA-256;
- the explicit Runtime setting that requests native capabilities;
- Companion instance ID;
- 256-bit client nonce;
- the 256-bit session token.

Runtime replies with `helloAck`, the negotiated protocol and a fresh server
nonce. Every later inbound sequence must be exactly previous + 1; duplicate,
out-of-order and gap messages are rejected. Authentication failures never log
the token, bootstrap payload or sensitive environment values.

For a newly launched process, Runtime passes the token through a one-use,
randomly named bootstrap pipe whose DACL is created current-user-only by
Win32 before the child starts. This compatibility path is required because
the Unity Mono runtime does not implement managed named-pipe server or
anonymous-pipe constructors. Only the random bootstrap pipe name appears in
the fixed child argument; the token does not. A compatible already-running
Companion receives session registration through its current-user control
pipe. Command/movie text never participates in executable, working-directory
or argument construction.

## Runtime commands

```text
uploadMovieBegin / uploadMovieChunk / uploadMovieEnd
startReplay / stopReplay
pause / step / resume
subscribe / unsubscribe / requestSnapshot
createReplaySave / listReplaySaves / restoreReplaySave
approveReplaySaveOverwrite / cancelReplaySaveRestore
resumeReplaySaveRestore / setAutoSavePolicy
requestCapabilityCatalog
reportNativeEvidence
ping
```

No command names a process, DLL, shell, URL, arbitrary file, reflection path,
memory address or native capability enable operation.
`reportNativeEvidence` is accepted only for a session whose authenticated
registration requested native capabilities. Its exact-field parser accepts
only the started, verified, and faulted forms documented by Native Capability
Protocol v1.

## Runtime events

```text
helloAck
commandAccepted / commandRejected
runtimeModeChanged / runtimeStatus
tickLedger / watchFrame / milestone / desync
replaySaveCreated / replaySaveCatalog / replaySaveRestoreProgress
restoreAccelerationStatus / capabilityCatalog
nativeCapabilityEvidence
backpressure / fault / pong
```

Background IO validates framing, authentication, sequence and command schema,
then enqueues a bounded typed command. Unity consumes commands at a safe point
with a default 2 ms per-frame budget. Queue pressure emits `backpressure`;
it never blocks the Unity main thread.

## Disconnect

If the authenticated session disconnects during replay, pause or step, Runtime
enters Stopping, releases all injected actions, restores original bindings and
time settings, records `companion-disconnected`, and leaves the in-game Runtime
and T09 replay-save service available. v1 never continues unattended replay
after Companion loss.
