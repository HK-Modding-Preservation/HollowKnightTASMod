# Studio scenario harness

This is an opt-in local integration test, not an ordinary unit test. It runs the real Studio App and ViewModel command paths, the verified launcher, and Runtime IPC. It does not use a desktop automation or UIA client.

Requirements: this checkout's signed installed bundle, matching ClockStartup, Steam ready, no running game or Studio. Uses the configured local game path in Program.cs. The game remains under the normal protected-save/session rules. It adds test timelines to the existing library and leaves them for audit; do not delete existing user history.

Build with `dotnet build scripts/StudioScenarioHarness -c Release -p:SkipHKTASInstall=true -o artifacts/studio-simplification/harness`. Copy the installed Companion/win-x64/ClockStartup directory into that output's ClockStartup directory. Require the harness Companion DLL hash to match the installed Companion DLL before final acceptance.

Run `StudioScenarioHarness.exe --headless --scenario-output=<absolute-output-directory>`. `--reopen-only` exercises a fresh Studio process loading the active stored tree without launching a game. Standard mode launches and restarts the controlled game, follows a short neutral title-menu recording, tests seek/rebuild/FPS edits/branches/file switching, and invokes the actual window Closing save handler.

The independent watchdog checks every second: maximum duration 4 minutes, host private memory 768 MiB, game private memory 3 GiB, at least 2 GiB physical and commit headroom. Logs include each boundary and resource sample. Failures end the test. No screenshots or external accessibility-tree traversal occur. Do not run Companion unit tests concurrently: some legacy broker tests touch the shared automation descriptor.

Offline visual checks are in StudioThemeTests and render the application's own WPF content to PNG. They do not capture the desktop or change monitor settings.
