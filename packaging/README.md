# Companion packaging

`HollowKnightTAS.BundleTool` generates a local RSA-3072 release key, signs the
canonical Companion manifest, and verifies the staged bundle. Private keys and
published/staged binaries are ignored by Git.

The normal release flow is automated by `scripts/Build-CompanionBundle.ps1`.
It publishes the WPF project as untrimmed, self-contained `win-x64`, stages it
under the fixed `Companion/win-x64` path, signs every file, verifies the result,
and copies the verified bundle into the Mod install directory.

Before installing, save your Studio edits and close both Studio and the game.
The install script refuses to proceed while either application is running,
and checks again before replacing installed files. `-StageOnly` can run while
the applications are open because it does not change the installed Mod.

Runtime embeds only the matching public modulus/exponent. There is no release
switch that ignores manifest signature or file hashes.

To build and verify only the Companion staging bundle, without changing the
installed Mod, run from the repository root:

```powershell
./scripts/Build-CompanionBundle.ps1 -StageOnly -Configuration Release
```

Use `-GameExecutable 'D:\path\to\hollow_knight.exe'` when targeting another
installation. ClockStartup hashes are generated against that executable and its
adjacent Unity/game assemblies. The eight ClockStartup files are covered by the
outer signed Companion manifest.

`-StageOnly` refreshes the fixed `packaging/publish` and `packaging/staging/bundle`
build outputs. It requires the existing local signing keys. It does not build or
install the Runtime package, and signature verification is not an in-game TAS
acceptance result. Omit `-StageOnly` for the existing build-and-install flow.
