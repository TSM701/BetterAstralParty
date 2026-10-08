# Third-party runtime components

The whole package includes 224 clean loader/runtime files from BepInEx build
788, commit 5b766a3b7f6c164d4798924a93f3acf4db769d06. The DLLs are unchanged.
Only doorstop_config.ini has enabled=false for vanilla launch.

component-matrix.json binds each packaged loader file to its SHA-256, component,
version, license text and source. The mod's root MIT LICENSE covers only
BetterAstralParty-owned code. Each upstream license and copyright notice remains
applicable. MonoMod.Backports and MonoMod.ILHelpers package copyright is
Copyright 2024 0x0ade, DaNike; the upstream MIT license is also supplied.

THIRD-PARTY-SOURCE supplies source archives for the bundled LGPL components:
BepInEx, Unity Doorstop and Il2CppInterop. Their exact revisions and archive
hashes are recorded in the matrix. Source code and upstream build instructions
are unchanged. BepInEx's three Unity reference DLLs and Il2CppInterop's generated
Il2Cppmscorlib.dll were omitted from those archives. Those proprietary reference
binaries are not part of this distribution.

To rebuild, extract the matching source archive and follow its upstream README
and project/build instructions. Supply the missing references locally from a
legally installed matching Unity/game environment at the upstream project
reference paths. BepInEx source includes build/Program.cs and its build project;
Il2CppInterop includes its solution/project files; Unity Doorstop includes its
build scripts. Install the SDK and dependency versions specified by those
projects. This package is not a self-contained replacement for a licensed game
or Unity development environment. The source URLs in the matrix also give access
to unchanged upstream source and instructions. Upstream libraries may be
modified, rebuilt, replaced and debugged subject to their own licenses;
BetterAstralParty adds no restriction on doing so.

Dobby is the unmodified x64 DLL from BepInEx/Dobby v1.0.5, commit
888d971214900374edbca6206fad6ded8a2c1311, licensed under Apache-2.0.
Its matching source archive contains no separate NOTICE file.
The .NET runtime is 6.0.7; its license and full third-party notices are included.
No .NET SDK, game binaries, generated game interop DLLs, fonts or Steam artwork
are included in this distribution.

This matrix is a concrete review input. Independent review and exact bundle
approval remain required before a release is installable or published.

## Application source and replacement procedure

The accompanying MIT BetterAstralParty source includes the plugin, launcher,
helper and their build scripts. See docs/BUILD.md for the verified compiler,
.NET target pack and authorized local references. Rebuild the upstream LGPL
component from its corresponding source revision and follow that project's
instructions for its required external dependencies. Replace the component in
a separate test loader installation, rebuild BetterAstralParty against that
loader if its API changed, and test it in your authorized game environment.
The managed plugin dynamically references the loader; the Doorstop library is
a replaceable native loading component. This source distribution preserves the
ability to rebuild/relink the application and replace or debug those libraries.

Operational production signatures and compatibility hashes intentionally bind
approved install/update packages. Locally modified libraries or application
builds require your own appropriate test/development trust and package metadata;
they are not authenticated as the original signed production release. No private
production key is needed to study, rebuild, modify or debug the source. Do not
reset an existing installation's ownership receipt or security high-water state
to make a modified binary appear approved. Preserve a separate test installation.
