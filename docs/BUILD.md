# Building BetterAstralParty

The root MIT LICENSE covers BetterAstralParty-owned code. Third-party libraries
retain their own licenses; see THIRD-PARTY-NOTICES/component-matrix.json and
THIRD-PARTY-NOTICES/SOURCES.md. Game assemblies, generated interop assemblies, Unity reference
DLLs, fonts and Steam artwork are supplied locally by their authorized owners.

## Local prerequisites

The toolchain is Windows x64, .NET SDK 8.0.425 and the
local Windows Framework64/v4.0.30319 reference assemblies. The plugin targets
.NET 6 and requires Microsoft.NETCore.App.Ref 6.0.36 locally; install the
corresponding .NET 6 SDK/targeting pack before an offline restore. The source
export contains neither an SDK nor proprietary references. global.json pins
the tested compiler. The helper also explicitly selects Roslyn 8.0.425.

Use Developer.cmd for the new C# development workflow. Its small Framework
bootstrap sets DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false before the first SDK process
and checks the raw User/Machine PATH values before and after execution. A fresh
DOTNET_CLI_HOME alone does not prevent first-use registration in the user PATH.
The driver stops on unexpected changes without rewriting the user's environment.
It uses the existing project-local .dotnet SDK; it does not download one.

Legally install the matching INT(Global) game and initialize its supported
BepInEx loader to generate game interop references. Obtain the unchanged
BepInEx Unity IL2CPP Windows x64 build 788 (6.0.0-be.788+5b766a3) from
https://builds.bepinex.dev/projects/bepinex_be . Match the original archive
SHA-256 F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A
and component versions/hashes recorded in component-matrix.json. Keep the
clean loader and the installed game's BepInEx/interop directory separate.
Do not substitute assemblies from an unrelated game or loader version.

## Plugin, launcher and helper

The developer host runs compilation, regression tests and package preparation
directly in C#. Developer.cmd compiles its small C# bootstrap using the Windows
Framework compiler, then uses the pinned SDK to build the developer host. This
development-only compilation is not used by an installed game's maintenance tools.
Use migration-status to inspect the complete required check inventory; listing a
check is not evidence that it passed. The full build writes build-result.json only
after every required check succeeds against the held source inputs. Missing ports
fail instead of falling back to an interpreter. Historical PS files remain as
migration reference, not as commands for the C# driver or distributed payload.

The 1.3.0 native maintenance path uses the precompiled launcher for installation,
removal and launch-time compatibility checks. It does not compile C# or start
PowerShell during those operations. The existing signed automatic update helper
and emergency update window remain separate delivery mechanisms with the same
signature, ownership, process and transaction boundaries.
Public update descriptors use the signed `release/Public` scope with a canonical
Stable channel field. Verification resolves the effective channel from the
selected public feed; GitHub's current `prerelease` flag controls eligibility.
A plain version tag can therefore move between Release and Pre-release without
rebuilding or re-signing any asset. Semantic `-rc` tags remain ineligible for
Release. Private Beta and local-install signatures retain their separate
purposes; old narrow public proofs remain strictly bound to their original channel.
Installed versions and saved channel preferences never change merely because
the publisher reclassifies a release. Explicit channel switches retain their
approval and per-channel history checks.
The current source targets a fresh 1.3.0 protocol-4 installation generation with
five owned payload files and no PowerShell payload. Protocol-3 clients must not
consume these updates; remove an older installation with its original package
before installing the new generation. Existing published packages and the
currently approved local installation binding remain unchanged.
Force-Uninstall.cmd is a separate irreversible removal mode, not an automatic
fallback. It previews exact targets and requires fresh consent before deleting
the scoped BetterAstralParty files and selected-game recovery backups without
quarantine copies. It preserves the shared loader, unrelated mods and unknown
extra files. Its regression tests use isolated fixtures, never a real installation.
Extract the full package into a short ordinary local folder. Windows CMD/CLR
startup can reject an excessively long executable or batch-script path before
the installer starts; the same limit affects older package CMD entry points.
Native maintenance still validates and handles long game/backup file paths.

The new 1.3.0 generation replaces the historical protocol-3 baseline; it is not
an in-place compatibility bridge. The launcher does not
embed a helper executable or a legacy bootstrap. Build the helper separately;
the complete installation package delivers it with its verified descriptor and
signature. Building the plugin, launcher and helper requires no private signing
key. Producing an approved installation or update package is a separate signing
and packaging step. The helper component version is independent of the plugin
version; supported maintenance preserves an authenticated newer helper.

The isolated native compilation commands below build the launcher and helper
without approving a package. The full plugin build and regression matrix also
require matching game/interop references and the locally extracted research
assembly used by ABI checks. The source archive is not an installation package.
Compilation does not install files, modify Steam,
start the game, sign a release or establish in-game correctness.

Run the C# entry point from the canonical source root:

```bat
Developer.cmd help
Developer.cmd migration-status
Developer.cmd packaging-self-check
Developer.cmd test sdk-path-safety
Developer.cmd test native-diagnostics-cli
Developer.cmd build-launcher --output-directory artifacts\fresh-launcher-output
Developer.cmd build-helper --output-directory artifacts\fresh-helper-output
Developer.cmd build --release --compatibility-path artifacts\reviewed-compatibility.json --output-directory artifacts\fresh-full-build
Developer.cmd build --release --version 1.3.0 --compatibility-path artifacts\reviewed-compatibility.json --output-directory artifacts\fresh-public130-build
```

Without `--version`, builds use the version in the canonical `src/Plugin.cs`,
including its local development suffix. A public build can use `--version 1.3.0`
without editing that source or the approved local installation binding. The
driver creates `version-override/Plugin.cs` under the fresh build output with only
the version literal changed and gives the plugin, launcher and helper that version.
`build-source.json` records the unchanged canonical inputs; `build-result.json`
binds their manifest hash, the override, the generated source's path/size/SHA-256
and the three compiled product hashes. Package preparation and source export
revalidate that exact version-only transformation. Source exports retain the
canonical source and include `SOURCE-MANIFEST.json` and `BUILD-RECORD.json`; use the
same `--version` value to reproduce the public build.

Output directories must be fresh and under artifacts. Isolated native compilation
does not create an approved build-result.json. A full build result is still not a
signed installation package: signed helper/descriptor evidence and complete
package validation are required before changing the development installation
binding. Package-integrity signing uses the approved signing and packaging
workflow; compilation alone does not approve or publish a package. Private signing
keys are not included in public source or installed payloads.

After a game update, create a new compatibility snapshot with explicit input
roots and a review note:

```bat
Developer.cmd compatibility-snapshot --game-root "<INT installation>" --cache-root "<reviewed addressables cache>" --review-note "<review reference>" --output-directory artifacts\fresh-compatibility-snapshot
```

This is a bounded, read-only fingerprint collection with create-new local output,
not compatibility approval. Review and test the game changes before using the
result as a build's --compatibility-path. It neither changes the game nor silently
accepts an unknown catalog.

Builds map source paths to /src/BetterAstralParty and are deterministic. Inspect
CodeView and embedded PDB document names before publication; do not patch debug
records in compiled images. All required BetterAstralParty compilation units
are included. Licensed game/Unity references remain external build inputs.

## Source and release packages

Public source contains BetterAstralParty-owned compilation units and build
instructions, not a prepared loader installation, game references or a private
signing key. Use a verified whole release package for ordinary installation.
The application source, third-party notices and corresponding source archives
must be delivered for the exact release they describe. A source review snapshot
does not establish that a GA release has been published or tested in game.

The C# package commands prepare local/release signing inputs, validate whole
packages and export the matching source. verify-staged-release checks the pinned
GitHub repository, release and uploaded update asset using bounded GET requests;
it never creates or edits remote objects. Optional authentication is read from
an explicitly named process environment variable, not a command-line token.
finalize-release-assets verifies the signed protocol-4 tuple and exact whole,
source and build hashes before creating a fresh flat asset directory and
SHA256SUMS. It neither signs files nor uploads them. Historical incident recovery
tools and obsolete version-specific signers are not public source inputs.

## LGPL component replacement

To rebuild or replace LGPL components, extract the matching THIRD-PARTY-SOURCE
archives and follow their upstream build instructions. SOURCES.md documents the
removed proprietary reference DLLs and replacement/relinking procedure. No game
license is supplied by this source export, and BetterAstralParty imposes no extra
restriction on modifying or debugging those libraries.
