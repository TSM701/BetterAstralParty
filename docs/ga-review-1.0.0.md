# Private GA review evidence — 1.0.0

Local review record, 2026-10-01 UTC. This is not a release upload. The frozen candidate and installed game copy are preserved. See [GA preparation](ga-preparation.md) for the exact authorization and candidate hashes.

## Verification wording and its provenance

The user has now confirmed UI, Steam checkout-window, screenshot-association and playtime checks on the final beta. This session did not run 1.0.0 in-game or make a purchase. Payment completion was not separately claimed. Of those previously listed runtime checks, only the FlowSDK native-crash root cause remains unconfirmed. Existing unsupported special combat rules and exact-overkill limits remain documented separately.

| Origin | Status of the original wording | Current record |
| --- | --- | --- |
| README.md:13, 0.39.2 changelog | Existing final-beta record: checkout display/completion needed a user check after the launch-context correction. | Retained as history; README.md:9 records the later user confirmation. |
| README.md:17, 0.39.0 changelog | Existing record: FlowSDK cause unconfirmed, runtime check needed. | Cause remains unconfirmed. |
| docs/development.md:55 | Existing main-development launch-context record; was line 47 before this update. Agent-run checkout/payment and screenshot/playtime checks were not performed. | Preserved; the new user-verification section at line 43 supersedes the four pending checks. |
| docs/development.md:63 | Existing September 23 record; was line 55 before this update. Overlay and screenshot files had been checked, playtime was pending at that time. | Preserved as dated history; latest user confirmation is separate. |
| README.md:285 and :341 | Existing current user guide previously repeated checkout/screenshots/playtime as pending. | Corrected to user-confirmed results. |
| README.md:9; docs/INSTALL.md:3; docs/ga-preparation.md | GA wording introduced by this preparation session, based on the previous records. | Corrected to distinguish user verification from this session's unexecuted 1.0.0 runtime test. |
| Staged GitHub README.md:3 and RELEASE-NOTES.md:7/:15 | New preparation copies, not prior source comments. | Corrected locally; not uploaded. |

The pending checklist did not originate in code comments. `src/Plugin.cs:88` says the native-crash scan reads historical reports and is not a live crash handler. `src/NativeCrashReport.cs:3` limits it to existing Windows evidence. `tools/BetterAstralParty-Steam.ps1:22` explains matching Steamworks and the overlay before the rendering device is created; it does not declare all four UI/Steam checks failed.

The checkout window not appearing was an actual user-reported pre-0.39.2 issue. The later user confirmation supersedes the post-fix pending-check record. The other pending statements were unexecuted verification, not proven defects. Five retained FlowSDK `c0000005` events at offset `0x22fd68` are actual historical crashes, latest 2026-09-30 16:24:01 UTC; root cause and a controlled reproduction are not established. A later log replaying those records does not prove a new crash.

## Behavior preservation

The backed-up Plugin.cs becomes byte-identical to current source by changing only the version literal `0.39.2` to `1.0.0`. No launcher, installer, package implementation, gameplay or UI feature implementation changed.

The backed-up beta DLL and GA DLL were compared using the already available Mono.Cecil. Both contain 1,016 methods. After normalizing the expected inlined version string, there were zero unexpected method changes and zero added methods. The comparison included method attributes, local initialization, stack size, local types, instructions and exception handlers. This supports feature-preserving promotion; it does not prove absence of pre-existing defects or certify native runtime behavior.

## File inventory and upstream provenance

The frozen ZIP has 251 files, 37,716,918 bytes. SHA-256: `59836E137F2A84B72C1200B59F7E4D8CF7A8E10D18F813DCE77826E5B13EDEEE`.

| Category | Count | Contents |
| --- | ---: | --- |
| Mod distribution | 22 | Five CMD files, eleven PowerShell files, plugin DLL, launcher EXE, compatibility manifest, package manifest, README and Steam hero PNG. |
| Third-party notices | 5 | BepInEx, Doorstop and .NET licenses, .NET third-party notices and source/version links. |
| Upstream loader root | 4 | winhttp.dll, doorstop_config.ini, .doorstop_version and changelog.txt. |
| Upstream .NET runtime | 187 | Original loader archive's dotnet directory. |
| Upstream loader core | 33 | Original loader archive's BepInEx/core directory and dependency DLLs/config. |

All 224 files under `.deps/bepinex/` were compared with the original archive `.cache/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip`, SHA-256 `F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A`. The only difference is `doorstop_config.ini` changing `enabled=true` to `enabled=false`; upstream binaries are unchanged.

The hero PNG is 1920×620, 2,076,856 bytes, SHA-256 `6CA539F1A68C53AFE7BD2B7FBF296D8F643DC31BA2AF00DDD3AD2598FD36746B`. It matches the existing banner session's final image byte for byte. That session records image generation and edits, not a license to the depicted game's logo or characters. No personal photos or account data were identified in it.

No tested game binaries, interop output, developer C# sources, PDBs, logs, dumps, backups, user configuration, fonts, translation patches or account directories were found. Tested GitHub-token/AWS-key/private-key patterns and development-machine user paths had no matches. A SteamID-shaped match in `tools/steam-shortcut.ps1:27`–`:29` is the public SteamID64 arithmetic base and bounds, not a user's SteamID. These are bounded inspections, not a claim that every possible secret format was detected.

## Redistribution review still needed before uploading files

BepInEx and Doorstop are LGPL 2.1 components. Including their license and source links alone does not establish that the binary distribution's corresponding-source/equivalent-access requirements have been satisfied. This review has not completed individual transitive dependency license/notice mapping. The bundled .NET MIT license and .NET third-party notices are retained. No mod-wide license has been selected, and no permission for redistributing the banner's game logo/characters has been established. No unrequested license was added and the frozen ZIP was not silently modified.

Primary references: [BepInEx license at the packaged commit](https://raw.githubusercontent.com/BepInEx/BepInEx/5b766a3/LICENSE), [Doorstop 4.5.0 license](https://raw.githubusercontent.com/NeighTools/UnityDoorstop/v4.5.0/LICENSE), [.NET 6.0.7 license](https://raw.githubusercontent.com/dotnet/runtime/v6.0.7/LICENSE.TXT). These are unresolved review items; the package is not described as fully cleared for redistribution.

## Executed build checks and their limits

The release build completed with zero warnings/errors. Its self-test independently enumerated 4,224 combat scenarios / 152,064 dice pairs, and replayed 72 exact HP-loss and 19 lethal/display-censored outcomes. The latter cannot verify exact overkill. The following 31 direct build regression scripts completed without aborting the successful build. Most native/UI cases use metadata, geometry or inert substitutes. User-confirmed runtime results are a separate evidence source.

| Script under tools/ | Main coverage | Execution limit |
| --- | --- | --- |
| test-compatibility.ps1 | Advisory resource versus blocking engine/catalog changes, invalid/missing fixtures | Fixture checks |
| test-find-game.ps1 | Case/slashes/quoted parents, duplicate/multiple/missing paths, CN rejection | Path fixtures |
| test-install-log.ps1 | Version/error/fallback logs and write failures | Temporary files |
| test-release-version.ps1 | Beta/stable/RC, malformed/unsafe versions, naming and manifest | Parser/fixture checks |
| test-install-notice.ps1 | Fresh silent install and existing-user notice | Fixture checks |
| test-install-transaction.ps1 | Backup, transaction, rollback and interrupted recovery | Fixture checks |
| test-install-safety.ps1 | Settings/unrelated files, consent and UpdateOnly guards | Fixture checks |
| test-steam-session.ps1 | Process identity, PID reuse, list recheck and force-close consent | Inert processes |
| test-steam-context.ps1 | Owned child App/Game/Overlay IDs, inherited override, one-shot flags, guards and parent environment | No real Steam, game or payment |
| test-uninstall.ps1 | Selective removal/restore, unrelated files, malicious receipts, repeat and junction rejection | Temporary install tree |
| test-runtime-arguments.ps1 | Target/type guards and all four Quaternion values | Native invocation substitute |
| test-runtime-getters.ps1 | Typed getters/fields, live reads, guards and allocation paths | Native substitute |
| test-character-names.ps1 | Favorite-template-only edits, missing data and OFF restoration | UI substitute |
| test-character-guide.ps1 | Roles, unknown tags, KO/EN and hover-only isolation | Bindings, no renderer |
| test-field-search.ps1 | Fewer native calls with equivalent lookup results | Native substitute |
| test-typography.ps1 | Native-font and OS whole-label fallback policy/bindings | No actual glyph render |
| test-native-crash.ps1 | Signature, executable/time isolation, malformed timestamps and locked report | No induced crash or root-cause fix |
| test-remaining-hp-projection.ps1 | Spectator settings, revealed rolls and hand/public-data isolation | Native substitute |
| test-fight-readiness.ps1 | Delayed/disposed controller/content, reentry and ready gates | Native substitute |
| test-card-target-buttons.ps1 | Coordinates/order/resolutions, no-write idle, close and foreign-change restore | Geometry/native substitute |
| test-battle-status.ps1 | Public-data filter, HP Vector3 binding, phase/layout and extra-request guards | Static/binding checks |
| test-shushu-shield.ps1 | Actors/skins, restrictions, local lock/restore, recycled effect ownership and failure cleanup | Native substitute |
| test-input-attention.ps1 | 29 input timer routes, focus/identity and cleanup | No actual OS alert display |
| test-ping-focus.ps1 | Alt/Tab/focus pause, held modifiers and cancelled pending targets | No real game execution |
| test-pvp-safety.ps1 | Queue/room/transitions, stale fail-closed and PvE recovery | Native substitute |
| test-startup-notice.ps1 | Automatic-gate suppression with manual NOTICE preserved | Static and compiled-hook metadata |
| test-relic-background.ps1 | 0/44/88% alpha, live refresh/cache/resize and relic-only text | Native substitute, no visual check |
| test-hand-layout.ps1 | Stable piles, lifecycle, hover/click/world press, zoom/tweens/locks, cleanup and foreign callbacks | Geometry/native substitute |
| test-settings-scroll.ps1 | Clipping, drag-click guards, content sizing and fixed footer | Bindings, no live rendering |
| test-settings-input.ps1 | Diagnostic/phase/caption/coordinate/settings guards | No crash reproduction |
| test-windowless-launcher.ps1 | Windowless launch, wait/exit, 200 KB stdout/stderr, parser/log/migration and hero ownership | Inert compiled executable and Steam registration fixtures |

The separate extracted-package check passed in PowerShell 7.6.6 and Windows PowerShell 5.1.26100.9444, both exit 0. It checks 251 hashes, parsing, failure logging and missing-bootstrap fallback; it does not install into the live game. Evidence is in the local preparation directory's `build.log` and `package-checks.log`; neither is an upload asset.

## Remaining actions

Only creation of an empty private `TSM701/BetterAstralParty` repository is currently authorized remotely. It has not been executed because a supported creation/authenticated browser capability is unavailable in this session. Owner login is verified; empty connector lists and a 404 do not prove that the name is absent. No creation retry, credential extraction, new authorization, push, tag, release or asset upload was attempted.

The frozen archive's README:3/:25 is stale relative to the new user confirmation. A separately authorized documentation-only candidate is now prepared under the preparation directory's `documentation-refresh/`, SHA-256 `EBF6F6BC5A9CA399A3C179111401E6744271BF6B9BC627E69A6641E367B7F702`, 36,613,484 bytes. README.md and package-manifest.json are the only changed entries; all other 249 files are byte-identical. All inventory/hash/path checks passed, and both PowerShell 5.1/7 package checks exited 0. Runtime implementation and the installed 0.39.2 copy are preserved. The original ZIP/checksum/release tree are unchanged. The planned update feature is described separately in [Update notification design](update-notification-design.md); it is not implemented or present in 1.0.0.
