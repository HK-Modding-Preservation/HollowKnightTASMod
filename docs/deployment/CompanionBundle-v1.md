# Companion Bundle v1

## Fixed layout

```text
HollowKnightTAS/
  HollowKnightTAS.dll
  companion.manifest.json
  Companion/
    win-x64/
      HollowKnightTAS.Companion.exe
      <self-contained pinned files>
      Native/
        HollowKnightTAS.NativeHost.exe
        native-build-whitelist-v1.json
```

Runtime only reads this offline bundle. It does not search PATH, the registry,
Desktop, arbitrary Steam folders, URLs or user-selected executables.

## Signed manifest

The canonical manifest contains schema/product/version/RID/protocol range,
one canonical relative entrypoint and the complete declared file list. Each
file has a lowercase SHA-256. `signature` is an RSA-3072/SHA-256 PKCS#1 v1.5
signature over the canonical manifest with the signature property removed.
Runtime embeds only the release public key.

Verification order is:

1. strict size/UTF-8/canonical schema;
2. schema, product, version, RID and protocol intersection;
3. manifest signature;
4. relative path containment and reparse-point rejection;
5. existence and exact SHA-256 for every declared file;
6. entrypoint must be one of the declared verified files.

Absolute paths, backslashes, drive prefixes, `.`/`..`, empty segments,
duplicate paths, reparse points and undeclared entrypoints fail before process
creation.

## Build and launch boundary

- Release publish is `win-x64`, self-contained, WPF, and not trimmed.
- Runtime uses the verified absolute entrypoint, its containing directory as
  `WorkingDirectory`, and `UseShellExecute=false`.
- Arguments contain only the fixed `--bootstrap-pipe=` option and a
  Runtime-generated random one-use pipe name. Unity Mono cannot construct a
  managed anonymous pipe server, so Runtime creates this bootstrap pipe
  directly with Win32 and an initial current-user-only DACL.
- Movie content and IPC fields cannot alter the executable, directory or
  arguments.
- NativeHost and its build whitelist are members of the same signed file
  list. Companion launches only the fixed host path with no arguments after
  an authenticated Runtime request. NativeHost requires the exact sibling
  Companion as its OS parent; direct launch fails before stdin is read.
- Missing, invalid or incompatible bundles enter a structured degraded state;
  they never prevent Runtime/T09 from loading.

Debug may trust a separately configured local development public key. Release
has no ignore-signature or ignore-hash switch. Private signing material is not
stored in the repository or release bundle.
