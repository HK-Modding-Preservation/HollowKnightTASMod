# Sequence package v1

Implemented in Companion `SequencePackage` / `InitialSaveSnapshot`. Runtime Movie v2 and native save guard protocols remain unchanged.

## File contract

`.hktaspack` is a ZIP containing exactly `manifest.json`, `movie.hktas`, and the declared `initial-saves/user[1-4].*` files. All entries are validated in memory before any launch or draft change; archive paths are never extracted directly. Manifest JSON uses the C# property names:

- `Format`: `HK-TAS-Sequence`; `Version`: `1`.
- `Slots`: exactly `[1,2,3,4]`. The file list is complete; absent files are absent in replay, including empty slots. Local files are never merged.
- `InitialSavesId`: SHA-256 of UTF-8 JSON for the filename-to-SHA-256 map, lowercase filenames sorted ordinally, compact serialization.
- `MovieSha256`: SHA-256 of the exact UTF-8 movie bytes.
- `Files`: records with `Name`, `Length`, `Sha256`, including primary saves and all supported slot-side files.

Limits: 16 MiB movie, 128 KiB manifest, 32 MiB per save file, 128 MiB total saves, 128 save files. Reject duplicate names (case-insensitive), invalid slot paths, missing/extra entries, unsupported versions and mismatched lengths/hashes. Save writes a sibling temporary file, flushes it and replaces the destination only after completion.

## Lifetime and isolation

`ProtectedSaveSession.Prepare` captures the actual original file set for auditing and creates an immutable startup snapshot. New Studio sequences bind that snapshot at native frame zero, before any gameplay. Later saves reuse the immutable snapshot. Shadow writes never modify it.

When a bound sequence is selected, `FullRunMovieCoordinator.SequenceInitialSaves` supplies the seed for the next protected launch. `SessionInitialSaves` identifies the seed of the process already running. A changed binding requires a cold restart; `ArmReplay` refuses a mismatched active seed. Each restart creates a new shadow directory containing only the bound file set.

The existing `ProtectedSaveDescriptor.OriginalFileSha256` field now identifies the shadow seed for Runtime verification. `ProtectedSaveSession.OriginalSha256` and `OriginalLengths` independently identify this machine's real files. `VerifyOriginalSavesUnchanged` uses only that audit, including when replaying saves from another machine. Native write protection and Runtime redirection remain active.

Timeline trees persist `InitialSavesId`, referencing an immutable local cache under `studio-frame-saves/initial-saves/`. Moving the exported package does not break existing timeline restores. Switching trees resets the manual-save destination; it cannot overwrite another sequence's file. Video export temporarily selects the source tree's baseline and restores the draft's binding afterward.

Legacy text movies remain unbound. Legacy timeline hashes may reuse a captured startup snapshot only after exact file-set/hash equality proves it is the recorded baseline; no unrelated current saves are silently substituted. Missing bound timeline caches fail explicitly.

## Verification

`SequencePackageTests`, `SequenceBindingTests`, `ProtectedSaveSessionTests`, sequence-saving, timeline and video-plan regressions cover codec failures, immutable origin, independent audit/seed, empty slots, offline open, frame-zero switching, repeated saves, autosave, save-as and moved-package timeline recovery.

`StudioScenarioHarness --headless --sequence-binding --scenario-output=<absolute directory>` exercises actual new/save/autosave/open/restart paths at frames 120 and 20. It changes only a disposable shadow while paused, then restarts; this synthetic isolation test is not a gameplay TAS fixture. It compares real saves through the coordinator audit and exercises a four-empty-slot package against a nonempty local save set. Test timelines and caches are isolated under the scenario output.
