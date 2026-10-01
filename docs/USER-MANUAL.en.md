# Installation and player guide

[中文](USER-MANUAL.md)

HollowKnightTAS Studio records, edits and replays frame-by-frame input for Hollow Knight. Its six tabs are Input Editor, Timeline, Manual, Information overlay, Boss FSMs and Setting. The interface supports Chinese and English.

## Install and start

You need Windows x64, Hollow Knight and an installed Modding API. Use a complete HollowKnightTAS release package and preserve the companion directory structure. The Studio release includes its .NET runtime and FFmpeg video encoder.

1. Save your edits and close the game and Studio.
2. Extract the package into `hollow_knight_Data/Managed/Mods/HollowKnightTAS` under the game directory. If the archive already has a top-level `HollowKnightTAS` folder, extract it into `Mods`. Avoid nesting two folders with the same name.
3. The Mod directory must directly contain `HollowKnightTAS.dll`, `HollowKnightTAS.Core.dll`, `companion.manifest.json` and `Companion/win-x64/HollowKnightTAS.Companion.exe`. When updating, use all companion files from the same release.
4. Start the game. From the title screen, open Options → Modding → HollowKnightTAS and select the entry that opens Studio and restarts the game. Wait for the controlled game to pause at startup frame 0.

A normal game launch leaves TAS control, recording, external interfaces, collider display and information overlay inactive.

Other Mods can remain enabled; you are responsible for checking compatibility. Replay checks the actual environment, including game and Mod identities and display settings. Use a matching environment when replay reports a mismatch.

## Information overlay

The Information overlay tab enables a panel over the game by default: Movie frame, internal room name, position, velocity, dash and shade dash cooldowns, health and soul. It appears after Runtime connects, updates during playback and stepping, and remains visible while paused. Unavailable hero values show `—`. Ready means the cooldown timer ended; other action restrictions still apply.

Search to add fields, remove rows or uncheck them to hide them. Drag rows to reorder, or use Move up/down. Select a row to edit its name, decimal places (0–6), unit and color. A blank name or color uses the default. Colors use `#RRGGBB`. Settings save automatically; Restore defaults resets them.

The panel defaults to the top-right corner and stays aligned while moving the game window. Choose a corner, X/Y margins, font size and background opacity. Enable Drag position to drag the panel over the game; disable it afterwards to restore mouse passthrough. F11 toggles visibility by default, with F12 or no shortcut also available. In-game shortcuts require global hotkeys. The panel follows the game window and is not included in MP4 exports.

### Custom read-only information

Select Add custom field and edit a sample in Read-only field query. Names are case-sensitive. Roots are `hero` (HeroController), `player` (PlayerData), `game` (GameManager), `position` and `velocity` (the hero's vectors).

| Query | Value |
| --- | --- |
| `hero.dashCooldownTimer` | Raw dash cooldown timer |
| `hero.cState.wallSliding` | Wall slide flag |
| `player.geo` | Geo |
| `player.equippedCharms[0]` | First equipped charm; unavailable for an empty list |
| `position.x` | Hero X coordinate |
| `component("/Knight", "HeroController").jump_steps` | A field on an explicit component |
| `fsm("/Knight", "Spell Control", "MP Cost")` | An explicit FSM variable |

Component queries support instance fields in the game and other Mods. Use a full namespace if needed. Object paths must begin with `/` and identify an active object by its full hierarchy path; ambiguous components report an error. FSM/variable names must match exactly. Paths support nested fields and zero-based array/List indices.

Queries read public/private fields and automatic property backing fields, returning numbers, booleans, strings or enums. They do not execute methods, property getters, assignments or arbitrary C# scripts. Raw timers retain their actual values, including negatives; only preset cooldown rows show Ready. Missing targets show `—` with details below the settings; other rows continue updating. Limits: 32 rows, 512 characters per query and 16 field segments.

## Boss FSMs

Open Boss FSMs, select the boss in the current scene, then choose Add object and children. Each PlayMaker FSM gets its own graph with the current state highlighted. Search the list to add FSMs on other objects, including independent scene controllers. Instance numbers distinguish objects with the same name; inactive templates are labeled.

Use the wheel to zoom and drag with the left mouse button to pan. Fit shows the whole graph; Locate current state centers the active node. You can search states, collapse cards or expand a graph. Click a node to inspect the types of its loaded actions. Local and global transitions retain the game's event names.

Select up to 32 FSMs. Observation does not advance a paused game; highlighting updates after stepping and during playback, with the sampled frame shown below. Brief intermediate states may be missed. After a reconstruction, only targets with unique paths and names rebind automatically; ambiguous instances need a new selection. Leaving the tab stops polling. Behaviors written in C# or coroutines need a separate adapter.

## Record or replay

Open a `.hktaspack` sequence in Input Editor, then play or step. With no sequence open, playing or stepping starts a new recording. Recording covers menus and gameplay. During loading, native loops advance without consuming Movie input frames.

The top playback button toggles play and pause; Step advances one frame. The resume shortcut P only resumes paused playback. Frame N means input `[0,N)` has executed and row N is next. Native frame counts native loops and is not interchangeable with an input row number.

Default shortcuts:

| Action | Key |
| --- | --- |
| Toggle play/pause | Pause |
| Resume playback | P |
| Step; hold to repeat | V |
| Open / save sequence | Ctrl+O / Ctrl+S |
| Copy / paste | Ctrl+C / Ctrl+V |
| Undo / redo | Ctrl+Z / Ctrl+Y |
| Insert / delete frames | Insert / Delete |
| Restore quick slot | F1–F10 |
| Save quick slot | Shift+F1–F10 |

Change control shortcuts in Setting. With global hotkeys enabled, playback, resume, stepping and quick slots work while the game or another window has focus. File and grid-editing shortcuts remain local to Studio. Registration conflicts appear in Setting. Text fields take priority for typing.

## Edit input

Each row is one frame. Action headers show the game's bound keys; hover to see action names. Click a cell to toggle input, or drag vertically to edit a range in one column. Shift selects consecutive rows; Ctrl selects separate rows. The context menu provides copy, paste, insert, delete and selection FPS. Scrolling down beyond the last row appends empty frames.

Future edits synchronize before the next play or step. After changing input that has already executed, select a target row and click Replay to current frame. Here, “current frame” means the selected grid row. Follow playback is enabled by default; disable it to keep browsing at a chosen position.

Default FPS is 50. Values from 1 to 1000 may have up to 6 decimal places, such as 99.999. The default setting affects only new and appended frames. Change existing rows through the selection FPS context-menu action. The game mouse setting applies to new sequences; an existing sequence retains its recorded mouse mode. With game mouse input enabled, keep the same resolution for recording and replay.

Enter a positive Insert count, then insert empty frames to add that many rows at the start of the selection. Context-menu insertion uses the same count. One Undo removes the entire insertion.

Double-click an `RNG seed` cell or choose “Edit this frame RNG seed…” from the context menu. Enter an integer from -2147483648 to 2147483647; `0` is valid and a blank value clears the setting. The seed resets RNG once before that Movie frame, after any scene synchronization. Random values then evolve normally, and scene changes retain their existing RNG reset behavior. Seeds follow their frames through copy, insertion and deletion, support Undo/Redo, and changing an executed frame branches the worldline and requires replay just like an input edit.

## Custom keys

Choose a key to the right of Delete frames and click Add custom key, then check or drag across frames in the new column. Consecutive checked frames hold it; insert a released frame before another press. Add combination keys separately and check them on the same frame. Configuration and input are saved with the sequence and support undo/redo. Remove custom key removes that key and its input throughout the sequence. Reapply or replay after adding or removing keys.

Custom keys control other Mods separately from Studio playback shortcuts. They support Unity GetKey, GetKeyDown and GetKeyUp; other input paths require compatibility checks. Adjust or disable conflicting Studio global hotkeys in Settings.

## Save sequences and starting saves

Ctrl+S or Save sequence chooses a path on the first save, then overwrites that file. Save sequence to... chooses another path. New sequences use `.hktaspack`, containing all input and the four-slot starting-save snapshot fixed at startup frame 0, including empty slots and slot-associated Mod files. Sharing the package carries these starting conditions with it.

Manual saves, saving to another path and autosave retain the same starting snapshot; they do not capture progress made after recording began. Playback, replay, restore and video export create independent shadow saves from the package snapshot. In-game save/load operations during controlled play affect only that shadow copy. Save binding does not replace environment compatibility checks.

A text `.hktas` sequence is marked as having no bound initial saves and depends on local starting saves. It remains in that format when saved. A damaged package or missing starting-save data is rejected.

The default sequence directory is `HollowKnightTAS/Sequences` inside the Windows Documents folder. By default, autosave checks every 300 seconds (5 minutes) of wall-clock time and writes changed drafts into its `Autosave` subdirectory. It does not overwrite manually saved files or advance the game. Change the folder and interval in Setting; 0 disables autosave and the maximum is 86400 seconds. Existing saved settings take precedence.


Click Edit saves in the Input Editor to select a file and edit its JSON. Apply changes to sequence creates a new starting point and preserves the old timeline; save a new package afterwards. Export saves writes all saves and Mod companion files to a new folder, preserving originals.

## Timeline and restore

The timeline holds input branches and restore nodes. A branch leaf records the furthest frame actually executed and retains all future input. Clicking a leaf loads its branch; selecting and restoring a node changes the game position. Restoring an ancestor retains the selected branch's later input.

Saving the current frame as a node, saving from the grid context menu, and Shift+F1–F10 record a restore target without playing or seeking to it. Saving during playback pauses first. Future targets and edited targets that have not been verified are marked unverified and do not count toward actual furthest progress. F1–F10 restores a slot; existing nodes can also be bound to slots from the timeline.

The context menu can play forward to a future frame and pause, or restore a past frame and pause, without a pre-existing node. Restore uses a protected restart and input replay, so it takes time. The game image, grid and selection remain frozen, editing is disabled, and a progress bar tracks replayed frames relative to the target. Completion reveals the target state; failure releases the freeze and reports the reason.

Timelines, checkpoints and F1–F10 bindings exist only in memory for the current Studio session. Reopening Studio clears them; restarting the game for restore does not. Save a sequence file to retain input across sessions. Sequence files and `Autosave` copies remain on disk. Deleting a node and its subtree requires confirmation.

Opening another file validates it first, then pauses and records the current branch before replacing the draft. Failed validation keeps the current sequence. After switching, playback starts from the new sequence's beginning; you can also replay to a selected row. Before closing Studio, save the sequence with Ctrl+S. Normal closing ends an active video export, processes the current branch and exits the controlled game it owns. This does not replace saving a sequence file. If closing fails, the window remains open and reports the error. Closing discards session timeline and slot bindings; edits not written to a sequence file or Autosave may be lost.

Older sequences can be opened, edited and replayed. Different execution rules produce compatibility warnings, and some input frames may need adjustment. Manual saving updates the execution profile while retaining inputs, key configuration and initial saves; autosave retains the original profile.

## Export MP4

Choose a `.mp4` output path that does not already exist. Studio uses its bundled encoder.

- The Input Editor MP4 button exports the entire current sequence, including future draft input.
- Set a start and end node in Timeline to export `(start frame,end frame]` on one ancestor path. Reverse selections are reordered automatically; sibling branches cannot form a range.

Export restores and replays automatically. Preparation is excluded from the video; loading frames during capture are included, so video frame count can exceed Movie input frame count. Pause and resume continue the same capture. Cancelling preparation or capture does not publish a final MP4. Wait for the export status to report completion; request acceptance or the presence of a temporary file is not completion.

## Settings and troubleshooting

Setting includes language, FPS, mouse mode, sequence directory, autosave interval, shortcuts and collider display. Collider display is off by default and only draws outlines; it does not change collision behavior.

- Studio is disconnected: start through the game's TAS entry and wait for Runtime readiness. Normal game sessions do not expose TAS control.
- Companion verification fails: reinstall the complete matching release and preserve its directory structure.
- A sequence reports an environment mismatch: restore the game, Mods and display settings used for recording. Do not edit environment fingerprints to bypass validation.
- Restore or export fails: retain the error and check the current operation status before issuing another request.
- To report a problem, click **Export diagnostic logs** at the bottom of Settings, choose a folder, and send the ZIP with reproduction steps and the affected frame. The ZIP includes Studio application logs and retained logs from previous runs. Studio log copies and timing history are retained for 7 days, up to 256 MiB combined.

See the [AI control guide](AI-CONTROL.en.md) for automation.
