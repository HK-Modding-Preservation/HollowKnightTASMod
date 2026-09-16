# Native Capability Threat Model v1

## Protected assets

- the active Hollow Knight process and its input/time state;
- user save slots and the T09 content-addressed replay-save store;
- Runtime session tokens and one-shot native credentials;
- the signed Companion/NativeHost bundle;
- structured evidence integrity and build identity.

## Trust boundaries

The Runtime Mod is authoritative for Hollow Knight semantics, safe ticks,
movie cursors, and fallback. The signed Companion is authoritative for local
tool UX and sidecar lifecycle. NativeHost is trusted only for the fixed
read-only capability compiled into it. Movies, TAS text, public automation
clients, network peers, arbitrary executables, and arbitrary PIDs are
untrusted.

The operating-system boundary is the current Windows user. v1 does not claim
to resist an administrator, kernel code, or malicious same-user code that can
debug or inject into the already trusted Companion. It does prevent accidental
or untrusted direct use of NativeHost as a general process tool.

## Threats and controls

| Threat | Control |
| --- | --- |
| Directly run NativeHost | Exact sibling Companion parent image is required before stdin is read |
| Replace Companion or NativeHost | RSA-signed manifest plus SHA-256 of every declared file |
| Path traversal or alternate executable | Fixed canonical relative paths; no user/movie path input; reparse points fail closed |
| PID reuse | Runtime registration and NativeHost request bind PID plus UTC creation ticks |
| Target another process | Signed Companion derives PID only from the authenticated Runtime registration; NativeHost also requires exact Hollow Knight filename/build hashes |
| Modified/unknown game build | AMD64, executable hash, `Assembly-CSharp.dll` hash, and whitelist ID must all match |
| Forge a successful response | One-shot 256-bit credential and HMAC over canonical response |
| Leak session token | Token stays inside current-user bootstrap/control pipes and is never logged |
| Arbitrary memory access | No address, length, read, write, allocation, DLL, or shell field exists |
| Persist raw pages | Observe exports aggregates/hashes only and asserts `rawPagesPersisted=false`; schema rejects extra page fields |
| NativeHost hang/crash | 10-second timeout and kill-on-close Job Object; Runtime/T09 fallback |
| Companion crash | Job close terminates NativeHost without terminating Hollow Knight |
| Movie enables native code | Native setting is outside movie grammar and is copied into authenticated session registration |
| Unsupported checkpoint overclaim | Catalog and evidence must say `unsupported`; no capture/restore command is implemented |

## Residual risk

- Reading module files can fail if another product denies access; the
  capability then faults and falls back.
- Thread sets and memory totals are observations, not deterministic state
  restoration data.
- Current-user malware can tamper with any user process and is outside this
  application-level trust model.
- Bundle signing establishes publisher integrity, not Windows Authenticode
  reputation.
- The build whitelist intentionally rejects a legitimate Hollow Knight update
  until a new fingerprint is researched, tested, and signed.

## Audit invariants

- standard user only; no elevation prompt, service, driver, or global hook;
- no network listener or telemetry;
- no raw pages or dumps in evidence directories;
- no user save or T09 store mutation during N0/N1 tests;
- direct launch is a structured rejection;
- native-disabled produces no NativeHost process and no native evidence;
- every verified record names the exact whitelist and preserves T09 fallback.

