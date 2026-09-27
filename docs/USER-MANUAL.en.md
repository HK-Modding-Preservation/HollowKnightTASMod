# Installation and player guide

[中文](USER-MANUAL.md)

HollowKnightTAS Studio records, edits and replays frame-by-frame input for Hollow Knight. Its four tabs are Input Editor, Timeline, Manual and Setting. The interface supports Chinese and English.

## Install and start

You need Windows x64, Hollow Knight and an installed Modding API. Use a complete HollowKnightTAS release package and preserve the companion directory structure. The Studio release includes its .NET runtime and FFmpeg video encoder.

1. Save your edits and close the game and Studio.
2. Extract the package into `hollow_knight_Data/Managed/Mods/HollowKnightTAS` under the game directory. If the archive already has a top-level `HollowKnightTAS` folder, extract it into `Mods`. Avoid nesting two folders with the same name.
3. The Mod directory must directly contain `HollowKnightTAS.dll`, `HollowKnightTAS.Core.dll`, `companion.manifest.json` and `Companion/win-x64/HollowKnightTAS.Companion.exe`. When updating, use all companion files from the same release.
4. Start the game. From the title screen, open Options → Modding → HollowKnightTAS and select the entry that opens Studio and restarts the game. Wait for the controlled game to pause at startup frame 0.

A normal game launch leaves TAS control, recording, external interfaces and collider display inactive.

Other Mods can remain enabled; you are responsible for checking compatibility. Replay checks the actual environment, including game and Mod identities and display settings. Use a matching environment when replay reports a mismatch.

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

Default FPS is 50, with integer values from 1 to 1000. The default setting affects only new and appended frames. Change existing rows through the selection FPS context-menu action. The game mouse setting applies to new sequences; an existing sequence retains its recorded mouse mode. With game mouse input enabled, keep the same resolution for recording and replay.

Enter a positive Insert count, then insert empty frames to add that many rows at the start of the selection. Context-menu insertion uses the same count. One Undo removes the entire insertion.

## Save sequences and starting saves

Ctrl+S or Save sequence chooses a path on the first save, then overwrites that file. Save sequence to... chooses another path. New sequences use `.hktaspack`, containing all input and the four-slot starting-save snapshot fixed at startup frame 0, including empty slots and slot-associated Mod files. Sharing the package carries these starting conditions with it.

Manual saves, saving to another path and autosave retain the same starting snapshot; they do not capture progress made after recording began. Playback, replay, restore and video export create independent shadow saves from the package snapshot. In-game save/load operations during controlled play affect only that shadow copy. Save binding does not replace environment compatibility checks.

A text `.hktas` sequence is marked as having no bound initial saves and depends on local starting saves. It remains in that format when saved. A damaged package or missing starting-save data is rejected.

The default sequence directory is `HollowKnightTAS/Sequences` inside the Windows Documents folder. By default, autosave checks every 300 seconds (5 minutes) of wall-clock time and writes changed drafts into its `Autosave` subdirectory. It does not overwrite manually saved files or advance the game. Change the folder and interval in Setting; 0 disables autosave and the maximum is 86400 seconds. Existing saved settings take precedence.

## Timeline and restore

The timeline holds input branches and restore nodes. A branch leaf records the furthest frame actually executed and retains all future input. Clicking a leaf loads its branch; selecting and restoring a node changes the game position. Restoring an ancestor retains the selected branch's later input.

Saving the current frame as a node, saving from the grid context menu, and Shift+F1–F10 record a restore target without playing or seeking to it. Saving during playback pauses first. Future targets and edited targets that have not been verified are marked unverified and do not count toward actual furthest progress. F1–F10 restores a slot; existing nodes can also be bound to slots from the timeline.

The context menu can play forward to a future frame and pause, or restore a past frame and pause, without a pre-existing node. Restore uses a protected restart and input replay, so it takes time. The game image, grid and selection remain frozen, editing is disabled, and a progress bar tracks replayed frames relative to the target. Completion reveals the target state; failure releases the freeze and reports the reason.

Timelines, checkpoints and F1–F10 bindings exist only in memory for the current Studio session. Reopening Studio clears them; restarting the game for restore does not. Save a sequence file to retain input across sessions. Sequence files and `Autosave` copies remain on disk. Deleting a node and its subtree requires confirmation.

Opening another file validates it first, then pauses and records the current branch before replacing the draft. Failed validation keeps the current sequence. After switching, playback starts from the new sequence's beginning; you can also replay to a selected row. Before closing Studio, save the sequence with Ctrl+S. Normal closing ends an active video export, processes the current branch and exits the controlled game it owns. This does not replace saving a sequence file. If closing fails, the window remains open and reports the error. Closing discards session timeline and slot bindings; edits not written to a sequence file or Autosave may be lost.

## Export MP4

Choose a `.mp4` output path that does not already exist. Studio uses its bundled encoder. Video preserves the actual duration of each game frame, including changes in frame rate.

- The Input Editor MP4 button exports the entire current sequence, including future draft input.
- Set a start and end node in Timeline to export `(start frame,end frame]` on one ancestor path. Reverse selections are reordered automatically; sibling branches cannot form a range.

Export restores and replays automatically. Preparation is excluded from the video; loading frames during capture are included, so video frame count can exceed Movie input frame count. Pause and resume continue the same capture. Cancelling preparation or capture does not publish a final MP4. Wait for the export status to report completion; request acceptance or the presence of a temporary file is not completion.

## Settings and troubleshooting

Setting includes language, FPS, mouse mode, sequence directory, autosave interval, shortcuts and collider display. Collider display is off by default and only draws outlines; it does not change collision behavior.

- Studio is disconnected: start through the game's TAS entry and wait for Runtime readiness. Normal game sessions do not expose TAS control.
- Companion verification fails: reinstall the complete matching release and preserve its directory structure.
- A sequence reports an environment mismatch: restore the game, Mods and display settings used for recording. Do not edit environment fingerprints to bypass validation.
- Restore or export fails: retain the error and check the current operation status before issuing another request.
- For logs, check `ModLog.txt` and `Player.log` in the game user directory, normally `%USERPROFILE%/AppData/LocalLow/Team Cherry/Hollow Knight` on Windows.

See the [AI control guide](AI-CONTROL.en.md) for automation and the [build guide](BUILD.en.md) for compiling the project.
