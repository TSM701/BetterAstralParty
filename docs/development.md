# Development

[Back to README](../README.md)

## Current C# workflows and game-update review

Run `Developer.cmd` from the canonical source root. Active development, tests,
packaging and maintenance use precompiled C# entry points; PowerShell commands
in dated records below are historical, not current instructions. Builds do not
install, launch the game, sign a package or publish a release.

```cmd
Developer.cmd test sdk-path-safety
Developer.cmd build --release --compatibility-path "<reviewed compatibility JSON>" --native-assembly "<reviewed current AstralParty.Runtime.dll>"
```

Without `--native-assembly`, tests use the preserved historical runtime snapshot
for regression checks. That result does not establish compatibility with an
updated game. An explicit input must exist and is hash-pinned throughout the
checks; an invalid input never falls back to the historical snapshot. The build
records the selected native input separately from its source manifest. Do not
include extracted game assemblies in public source archives.

After a game update, inspect actual installer failures, Steam build identity,
changed game files and catalog inventory first. Capture a fresh observation with
`Developer.cmd compatibility-snapshot`; it remains unapproved until the current
game logic, generated interop and affected native contracts have been reviewed
and checked. Game-content and catalog drift is advisory, not a launch or update
veto. Keep executable payload authentication, ownership, path safety and recovery
guards separate from observations of game hotfixes. Do not label changed game
files as verified compatible merely because launching is permitted. Static
and isolated checks do not prove in-game rendering or native hook safety.

The 3.3.0 runtime uses named `UIStringType` values and quantity-aware card pings;
do not reuse the historical numeric localization values or card-message arity.
Its PvE classification reads public `GameModeInfoConfigure` rows. Runtime
classification must reject missing or mismatched rows instead of inheriting the
game's permissive missing-row fallback. Pure historical math fixtures do not
establish the current game's map configuration.

Ordinary removal must remain possible after game files change, subject to the
existing ownership, lock and recovery checks. Installation must stop before new
installation history or helper changes when essential prerequisites or package
integrity fail, not simply because a game's hash changed. Explicitly
consented restoration of an earlier interrupted operation is separate from a new
installation. Signed package preparation and the development maintenance binding
remain separate from an unsigned build; report pending signing or runtime checks
without changing a previously approved package.

## SDK PATH safety — 2026-10-14

Development/test SDK invocations disable global-tool PATH registration in their
process before execution and restore the previous flag in `finally`. New CLI
homes alone do not isolate SDK first-use writes to Windows user PATH. Use
`Developer.cmd`, which guards the caller and child SDK processes. Do not copy
historical PowerShell or unguarded direct `dotnet` commands below. Do not edit
User/Machine PATH as part of a build. The SDK PATH safety test checks entrypoint
guards and uses two fresh offline restores to verify persistent PATH invariance.

## Local field zoom smoothing — 2026-10-09

Source/build candidate: `1.1.0-rc.1.local.2`, continuing the unpublished 1.1.0 feature line. Current/previous detailed logs contained occasional broad UI/frame spikes but no isolated wheel timestamps or zoom duration, so they do not establish zoom causation. Code inspection and substitutes reproduced wheel events being dropped while the rendered camera damped toward the previous offset.

Wheel input now accumulates against a separate requested pose, with frame-rate-independent easing and a bounded long-frame step. Whole-field limits use the hypothetical requested pose instead of requiring the rendered camera to have settled. Renderer discovery is reused for a wheel burst and refreshed on scene/dictionary/count changes or after one second of wheel activity; live renderer bounds, visibility and lifetime are checked for each wheel calculation. Point storage is reused. Native movement, drag, follow, FOV, game input, PvP and ownership guards remain unchanged.

The source-bound Release build passed with zero warnings/errors. Zoom substitutes cover continuous/reverse input during damping, 30/60/120 FPS interpolation, stalled/invalid frame duration, whole-field limits, live bounds, membership refresh, UI/drag/transition/PvP gates and owned restoration. Runtime getter/argument, PvP, grouped-hand and target-button regression scripts also passed. These checks do not establish actual game feel or runtime frame-time improvement.

The requested persistent free-camera mode and reset button are not part of this build. The user approved a small reset button below the native top-centre location pin. Native free-view activation and explicit return APIs can be reused, but the vanilla free-camera setting skips only the non-local-turn inactivity timeout. Local action-start synchronously cancels and closes free view before a managed tick can intervene; the hot-loaded camera types have no generated wrapper for existing Harmony hooks. No unsupported detour, persistent native setting, forced per-frame camera reacquisition or placeholder reset button was added. Native audit evidence: `artifacts/field-zoom-native-transition-audit-20261009.md`.

Independent exact signing and whole-package reviews passed. The final 282-file ZIP passed package checks on PowerShell 5.1 and 7; offline local-install fixtures passed 123 assertions. The development Install.cmd/Uninstall.cmd binding selects `artifacts/automatic-update-development/local-zoom-smooth-20261009-r1/final/payload`, package-manifest SHA-256 `5DAB9EF9B4FB4174C3CDBAD41028B4A5C16B3262D86D171242BEBCE090159FC6`. One ordinary UpdateOnly installation completed. All eight owned hashes, native helper ownership, receipt/update state and SameBuild checks matched. Three settings hashes, original loader baseline, high-water state and vanilla loader-OFF were preserved; all 11 backups and the completed journal were validated, with no pending journal. Post-install development Install/Uninstall CheckOnly passed without removal or Steam changes. Evidence: `artifacts/automatic-update-development/local-zoom-smooth-20261009-r1/result.json`. No remote publication or game/Steam launch was performed; actual in-game acceptance and full removal/reinstallation remain unverified.

## Local PvE match focus and field wheel zoom — 2026-10-09

The canonical source, development Install.cmd/Uninstall.cmd binding and installed plugin are now `1.1.0-rc.1.local.1`. These are new features, so the local candidate advances MINOR; no GitHub release or published archive was changed. The two independent settings default ON: General → Focus on Match Found, and Mod Features → Field Wheel Zoom. Korean/English labels and current ON/OFF descriptions are included.

Match focus observes native public matchmaking state before the gameplay gate, but independently requires an observed live queue followed by a fresh verified PvE choosing room. Existing rooms, reconnects, PvP, unknown modes and OFF/ON replay do not activate windows. It targets only this process's unique Unity window and makes one ordinary restore/foreground attempt, with taskbar fallback if Windows refuses. No input automation, foreground-lock changes or hot-loaded native event delegate was added. A match without any observed queue frame is intentionally skipped.

Wheel up reduces the active native field camera's WorldSpace follow offset; wheel down stops at a perspective whole-board fit calculated from public land renderer bounds, viewport and near/far planes. Native camera transforms, FOV, follow movement, drag and game input remain untouched. UI, touch/drag, combat, PvP, blends, unsettled motion and unsupported camera rigs are excluded. Cleanup restores only unchanged mod-owned offsets, and idle frames do not traverse field renderers. The close-up limit is half the captured native distance with a near-plane floor; actor clearance and framing still require in-game calibration.

The source-bound Release build passed with zero warnings/errors; all 121 inputs matched. Focus polling/window substitutes and native metadata passed 4,688 assertions. Zoom tests cover wheel direction, 16:9/narrow perspective fit, float boundaries, clipping, invalid bounds, UI/drag/transitions/PvP, owned restoration, foreign changes and disposal, including the production Vector3 bridge. Settings UI passed 41 assertions and focused LanguageCheck; hand, target-button, ping, PvP, runtime argument/getter and updated-receipt uninstall regressions passed. Offline local-install, clean-reinstall and old-package removal-plan fixtures passed 123, 66 and 168 assertions respectively. These are not runtime rendering/foreground or frame-time measurements.

Exact independent signing and whole-package reviews passed. The final 282-file ZIP passed hash/extraction/parser/bootstrap checks on PowerShell 5.1 and 7. The binding selects `artifacts/automatic-update-development/local-focus-zoom-20261009-r1/final/payload`, package-manifest SHA-256 `23450D0119A9476A74D64694AA44491CE4E67C29F3CAB63B78E1C953CDEE7238`. One ordinary UpdateOnly installation completed after checking the game/launcher were closed. All eight owned hashes, installed version, native helper ownership, receipt/update state and SameBuild checks matched. Three settings hashes, original loader baseline, retained high-water state and vanilla loader-OFF were preserved. All 11 backups and completed journal were verified; no pending journal remains. Install/Uninstall CheckOnly and installed Uninstall WhatIf passed without real removal or Steam changes. Evidence: `artifacts/automatic-update-development/local-focus-zoom-20261009-r1/result.json`. In-game acceptance and actual full removal/reinstallation remain unverified.

## Local hand-input and target-button correction — 2026-10-09

The canonical source, development maintenance binding and installed plugin are now `1.0.2-rc.1.local.1`. This is a local candidate, not a public release. The published 1.0.1 assets remain unchanged.

The 1.0.1 logs showed temporary native pending-use/input locks returning to normal, but did not record the exact speech-bubble click target, so the reported interruption cannot be reconstructed from logs alone. Source/native-IL inspection identified two concrete stale-hit paths: pooled message hit surfaces were revalidated only on the 0.2-second scan, and `PointerPath(refresh: true)` rebuilt ancestry from Stage's still-cached per-frame target. Card preview now revalidates owned ping surfaces on left-button edges; explicit pointer refresh on input edges performs the native current-order hit test and maps the display object through GRoot. It does not overwrite Stage touch state or add idle-frame hit tests. Genuine front overlays, native pending-use/CardWindow locks, focus gates, card handlers and drag thresholds are retained. Opt-in down/up tracing records only hit type and state booleans, never message text or player/card identity.

Player-target Cancel/Confirm buttons keep their native coordinates, dimensions, parent and handlers. The former same-window early return skipped later native sorting resets and late hand discovery. Ordering is now checked continuously, applied only when needed and restored using owned sort/sibling snapshots. The CardWindow is raised over the hand and its buttons over the shown card before card-preview/hand input consumers run. No target-window shrinking, reparenting or positional offset was introduced.

The source-bound Release build passed with zero warnings/errors and all 118 build inputs still matched. The full hand-layout suite (including the new production-pointer/ping fixture), target-button, ping-popup and PvP suites passed. Tests cover pooled target removal/reuse/resize, fresh versus cached hits, native overlay blocking, drag/use/focus gates, late hand discovery, ongoing sorting resets, idle no-write and ownership cleanup. Native Cecil signature/hierarchy checks passed; HD/FHD/4K target geometry uses substitutes, not an in-game renderer. Updated-receipt uninstall fixtures and the 282-file package extraction/hash/parser/bootstrap checks passed on PowerShell 5.1 and 7.

Independent exact signing and whole-package reviews passed before binding `artifacts/automatic-update-development/local-hand-input-20261009-r1/final/payload`, package-manifest SHA-256 `4F6F582F8FB1178C240F6EFA33794D741BAF78A1608EEC2694C33D00CF388A9D`. After the user closed the game, one ordinary UpdateOnly install completed. All eight owned files, native helper ownership/update state/receipt and same-build checks passed. All three settings hashes, original loaderBefore, Beta history and vanilla loader-OFF were preserved. The completed ordinary transaction retains all 11 hash-verified backups; Install/Uninstall CheckOnly and the post-install Uninstall WhatIf passed without real removal or Steam changes. Evidence is `artifacts/automatic-update-development/local-hand-input-20261009-r1/result.json`. Actual in-game speech-bubble clicking, target rendering and full removal/reinstallation remain unverified. No remote publication or game/Steam launch was performed.

## Public stable 1.0.1 — 2026-10-09

The user authorized public stable publication after confirming the download-page correction in game. [BetterAstralParty 1.0.1](https://github.com/TSM701/BetterAstralParty/releases/tag/1.0.1) is published as Latest, not a prerelease. It preserves the 1.0.0 public README style and Korean/English parity. The exact reviewed source commit is `742693e0b32ca2c7da79d713fe49960aa159bf2a`; all 160 public source files were compared with the remote Git tree, and a fresh source-export build reproduced all three product hashes. Existing 1.0.0 assets were not changed.

Nine custom assets were downloaded again anonymously after publication; every size/hash and both RSA-PSS signatures matched. Personal settings, logs, credential state, private signing tools and dummy artwork are excluded by the package/source allowlists. Recursive token/private-key/personal-path checks passed. No actual credential vault was read or exported; users' own optional Beta sessions remain separate Windows-vault state. The precise release, source, signing, asset and validation identities are in `artifacts/automatic-update-development/stable-1.0.1-20261009/result.json`.

The final source-bound build and focused suites passed: 603 release assertions plus 14 transport checks, and 376 persistent-session assertions on each of .NET 6/8. The complete 282-file package passed Windows PowerShell 5.1 and PowerShell 7 extraction/hash/parser/bootstrap tests. A developer-only test correction copies the package manifest into the missing-bootstrap fixture, selecting the packaged CMD route rather than the development route. Updated-receipt uninstall fixtures passed 6 scenarios/12 runs. The separate legacy full-uninstall harness still lacks a package manifest and stops at the source guard; its run is not claimed to pass.

The maintenance binding and installed plugin are now 1.0.1. One ordinary UpdateOnly install completed, all eight owned hashes and native helper ownership matched, the mod settings hash was preserved, and vanilla loader-OFF remained in place. Both CheckOnly routes and the installed Uninstall WhatIf passed without removal or Steam changes. No actual removal/reinstallation or in-game 1.0.0-to-1.0.1 automatic-update run was performed. Prior local-build entries below are historical and superseded by this stable binding.

## Local download-page correction — 2026-10-09

The canonical source and installed plugin are now `1.0.1-rc.1.local.2`. Download Page opens the selected channel's official GitHub releases listing: Stable uses `TSM701/BetterAstralParty/releases`, Beta uses `TSM701/BetterAstralPartyBeta/releases`. This fixed navigation is independent of release discovery, authentication, request cooldowns and whether an update is available. It reads no credentials and sends no token to the browser. The existing discovered-release URL validator, metadata privacy cleanup, Update Check styling and automatic download verification are unchanged. Download Page artwork and Korean/English help now describe the listing instead of an available individual release.

The source-bound Release build passed with zero warnings/errors, including source-mode, settings-input and settings-scroll checks. The focused release suite passed 603 assertions and 14 transport checks, covering repeated channel selection, unconfigured/signed-out/latest/available/failed states and absence of authentication, HTTP or update-state side effects when resolving the listing. The small managed diagnostic fixture API was kept compatible. No native UI or browser rendering test was performed.

The current Install.cmd/Uninstall.cmd binding selects `artifacts/automatic-update-development/local-download-page-20261009-r1/final/payload`, package-manifest SHA-256 `5EF8564C9AFD709D32983509AAE560D8099428626EE3EC74D949F9C6FF951246`. Exact bounded signing and whole-package reviews passed before one successful Windows PowerShell 5.1 UpdateOnly installation. All eight owned files match the intended build, helper ownership/update-state and same-build checks pass, all three configuration hashes and loader-OFF/Beta history remain unchanged, and all 11 completed-transaction backups are retained. Install/Uninstall CheckOnly and the post-install Uninstall WhatIf route passed without real removal. Evidence is `artifacts/automatic-update-development/local-download-page-20261009-r1/result.json`. No game/Steam launch or remote publication was performed; actual in-game clicking and the Windows browser handler remain unverified.

## Local channel-refresh correction — 2026-10-09

The preceding local build was `1.0.1-rc.1.local.1`. Discovery keeps two channel-bound verified snapshots and independent local cooldowns. A first visit to another channel checks immediately; rapid returns show the verified result with its original check time and current generation without flooding HTTP. A queued channel refresh runs at its own 60-second boundary rather than being lost to six-hour home freshness. Server retry limits remain shared, even when an old cancelled worker receives them. Authentication changes retire private Beta snapshots; failed/interrupted newer scans cannot revive an older success, and acknowledged notices do not repeat.

The final Release build has zero plugin warnings/errors. The focused release suite passed 552 assertions plus 14 transport checks; persistent-session checks passed 376 assertions on each of .NET 6 and .NET 8. The old self-test project references and inert authorization fixture were aligned with the current source. Run `.dotnet/dotnet.exe run --project self-test/BetterAstralParty.SelfTest.csproj -c Release -- --release-updates-only` for the focused suite; the unrelated complete legacy self-test suite is not claimed to pass. Evidence and exact source/build hashes are in `artifacts/immediate-channel-refresh-20261008-r1/result.json`.

That signed local Stable candidate was prepared and installed as `1.0.1-rc.1.local.1`. Its 123 offline contract checks cover the exact `local-install/Stable` purpose, repository identity, zero remote IDs, version suffix and remote-proof rejection. Remote Stable prerelease rules are unchanged. The metadata-only ordinary-journal namespace binding also passed isolated rollback/completion checks in both ordinary and current Codex host namespaces; final completion still requires strict native file identity and content checks. This shared correction does not reuse the earlier incident-specific recovery tool or discover journals belonging to a different host namespace.

At that completion, the Install.cmd/Uninstall.cmd binding selected `artifacts/automatic-update-development/local-stable-refresh-20261008-r1/final/payload`, package-manifest SHA-256 `F82D08FA69BFCDA6470F49E4C54EC12AAF4B64C99110372B42218BB0B568DC6F`. Independent exact-package review passed before the single successful UpdateOnly installation. All eight installed owned files and the helper matched the source-bound build; native ownership/update-state and same-build preflight checks passed. The ordinary journal completed with all 11 backups retained. Mod/BepInEx/loader configuration hashes, loader-OFF state, original loaderBefore and Beta channel history were preserved. Windows PowerShell 5.1 CheckOnly passed for both development entry points; removal compatibility was checked without uninstalling the real game. Evidence is `artifacts/automatic-update-development/local-stable-refresh-20261008-r1/result.json`; this binding is superseded by the download-page correction above. No game/Steam launch, in-game acceptance or remote publication was performed.

## Current GA status — 2026-10-08

The public [BetterAstralParty 1.0.0 release](https://github.com/TSM701/BetterAstralParty/releases/tag/1.0.0) is published in `TSM701/BetterAstralParty` (repository ID `1410226758`, release ID `406781524`). The application-source commit is `998d7e51925f9c2539cb367ddf0c5e8ebcefd5ad`; the approved whole, automatic-update and matching source archives are recorded in [GA preparation](ga-preparation.md).

At GA publication, the development maintenance pointer selected `1.0.0` at `artifacts/automatic-update-development/ga-release-20261008/final-package/payload`, with package-manifest SHA-256 `39D004932B81C6D7C2FA24DBD0E7BC74CF1F6B46B3807918AA34F187A3C1CAC0`. Both Windows PowerShell 5.1 CheckOnly routes passed. The later local binding above supersedes this maintenance state; the published GA assets are unchanged.

The real UpdateOnly synchronization initially stopped during ordinary-journal completion with native error `4395` (`LinkedFile`), after copying the eight GA-owned files and Stable receipt. That dated failure remains recorded in `artifacts/automatic-update-development/ga-release-20261008/local-installation-incomplete.json`. The user then directly authorized the exact local recovery. Its single execution completed successfully: the original journal was moved unchanged to its backup leaf's `completed.json`, all 11 backups were preserved, and the signed GA helper transition completed. Installed plugin metadata and the helper now match `1.0.0`; native ownership/update-state checks passed. Mod settings, loader configuration, the loader-OFF state and channel history are unchanged. Evidence is `artifacts/ga-local-recovery-20261008-r1/result.json`; the game and Steam were not started, so this is not new in-game acceptance. The separately retired dummy journal and its recoverable copy are preserved history, not this completed transaction.

That private, fixed-incident recovery did not change the general installer. The observed Codex execution namespace exposed a logical backup path differently from the user-confirmed physical path; routine maintenance from the user's normal `Install.cmd`/PowerShell context should not be conflated with that execution context. The later local candidate above contains the separately reviewed shared ordinary-journal namespace correction. Preserve the historical recovery tool's execution reservation and evidence; it is not a reusable maintenance entry point.

The isolated native file-fence/transition fixture passed 102 checks. Its result is `artifacts/gnw-89a722c13a9e47a89ef4b54b889ff218/result.json`, SHA-256 `D5D13C76652EA6AD35BA2C33A8A5AC233529CAEEEF9D037F9E2FE5F82922C88D`. This fixture did not run a full installation, uninstall/reinstall, the real game or Steam. Dated pending/private-candidate entries below are historical, not the current GA release status.

## Historical prerequisites

Follow [AGENTS.md](../AGENTS.md). This describes the existing Windows development workspace, not a self-contained source checkout bootstrap. The build script expects `.dotnet/dotnet.exe` and dependencies referenced by the project files. Installation requires a complete verified bundle with its compatibility manifest, signed updater proofs and loader files.

## Historical PowerShell build and install

From the project root in PowerShell:

```powershell
.\tools\build.ps1 -Release
```

This builds the plugin, launcher and update helper and runs the source-mode, settings-input and settings-scroll checks. Each run writes a fresh directory under `artifacts/development-build-*`, including `build-result.json`. It does not install the result, create a release or approve a new compatibility baseline. Run feature-specific tests separately.

With the game closed, use the development entry point:

```powershell
.\Install.cmd
```

In the canonical source folder, this delegates to the exact verified whole bundle pinned by version, path and manifest SHA-256 in `artifacts/development-maintenance.json`. It does not install loose build outputs or rebuild from source. `Uninstall.cmd` uses the same binding and preserves native removal confirmation and current receipt ownership checks.

The current binding is the approved `1.0.2-rc.1.local.1` local Stable candidate at `artifacts/automatic-update-development/local-hand-input-20261009-r1/final/payload`, package-manifest SHA-256 `4F6F582F8FB1178C240F6EFA33794D741BAF78A1608EEC2694C33D00CF388A9D`. It includes the hand-input and target-button corrections above and retains the earlier update/maintenance behavior. Preparing a binding alone does not install it; this installed copy was subsequently synchronized by the ordinary UpdateOnly workflow and completed without incident-specific recovery. Repeating installation is not required to apply this same build.

The standing development workflow is: implement and test, prepare a complete signed local candidate, validate installation/removal compatibility, then update that binding. Compare the pinned plugin hash with the intended build, not just its version. A different build at the same version is not an ordinary update. Each local build uses an increasing prerelease on a base newer than the installed release; for example, `1.0.1-rc.1.local.1` is newer than its base rc.1 and older than a subsequent rc.2. Do not use `1.0.0.local.N` to update stable `1.0.0`: that prerelease is older than the stable version. The installer does not select files by timestamp, build or sign on demand. If validation or key-provider confirmation is unavailable, report the currently pinned version and the unfinished step.

Local preparation does not need GitHub access or upload. It uses the existing build/whole-package tools plus `prepare-local-install.ps1`, `prepare-ga-update-descriptor.ps1 -Scope LocalInstall`, `sign-local-install.ps1` and `prepare-local-signed-inputs.ps1`; `package-ga.ps1 -Scope LocalInstall` retains complete inventory, signature and approval validation. Local whole proofs have the separate `local-install/<channel>` purpose, exact Stable/Beta repository identity and zero remote release/asset IDs. Stable local prereleases require the exact positive `.local.N` suffix without build metadata, on an eligible version base; ordinary Stable prereleases remain rejected. Remote update verification rejects local proofs. Sign only under the exact reviewed policy, preserve provider confirmation and never repeat an uncertain signing run.

Publish, push, create a remote release/draft/tag or upload assets only when the user explicitly requests publication. A local candidate is not a publishable release: public packaging needs its own version and actual GitHub identities. Do not copy a local archive into a remote release. Preparation alone never installs or removes the user's game files.

Complete user-facing installation ZIPs use `BetterAstralParty-<version>.zip`, for example `BetterAstralParty-1.0.0.zip`. Automatic-update and source archives retain `-update.zip` and `-source.zip`. Internal unsigned candidates retain the `-unsigned-candidate.zip` suffix; existing immutable packages and internal whole-package contract names are unchanged.

For routine synchronization of an existing installation, use `Install.cmd -UpdateOnly -GameRoot <verified INT path>`. This uses the same pinned bundle without registering shortcuts or restarting Steam. Normal installation handles first installation and shortcut repair. Preserve backups, settings, translation patches, unrelated mods and the vanilla loader-OFF default. Do not force-close the game without fresh interactive consent.

## Historical focused checks

```powershell
.\tools\test-compatibility.ps1
.\tools\test-steam-shortcut.ps1
.\tools\test-launchers.ps1
```

The launcher check compares source files with an installed copy. Other checks are available in `tools/`. Static and stub-based tests do not replace in-game verification. Do not enter public matchmaking for testing; use a password-protected private room with bots if a match is needed.

## Compatibility and open verification

Completed public GA preparation and publication are tracked in [GA preparation](ga-preparation.md). The dated records below and the automatic-update status at the end describe their original candidates, not the current maintenance binding or release. The root LICENSE supplies MIT terms for BetterAstralParty-owned code; third-party terms remain separate. The final public file scope, dummy-artwork exclusion, source/notice delivery and exact GA package approval were verified for the published `1.0.0` assets. Future publication still requires an explicit user request.

### 2026-10-01 UTC — Private GA preparation (1.0.0)

The user approved promoting the final 0.39.2 beta without feature or behavior changes. Version 1.0.0 changes the plugin version metadata and distribution documentation only. The existing packager already supports stable version names, so its implementation is unchanged. The existing installed 0.39.2 copy is preserved during this preparation. See [GA preparation](ga-preparation.md) for candidate hashes and validation results.

### 2026-10-01 UTC — User verification and private repository authorization

The user subsequently confirmed completing the UI, Steam checkout-window, screenshot-association and playtime checks for the final beta. These checks are user verification, not an agent-run test of the 1.0.0 package. The historical entries below retain the verification state at the time they were written; their pending wording does not describe the current status of these four checks. Payment completion was not separately claimed, and the agent performed no purchase. The FlowSDK native-crash root cause remains unconfirmed; the GA designation does not resolve it. Unsupported special combat rules and exact-overkill limitations remain as documented.

After these records are updated, the user authorizes creating only the private `TSM701/BetterAstralParty` repository with existing authorized access, after confirming the owner and checking for an existing repository of that name. This does not authorize pushing files, creating a remote tag/release, uploading assets, choosing a license or making anything public. New OAuth/PAT or persistent authorization requires separate approval. The frozen 1.0.0 ZIP is not regenerated: its internal README still contains the superseded pending-check wording. A documentation-only replacement candidate and its new hash require coordination before any upload.

The user subsequently authorized preparing that documentation-only candidate in a separate output location. It is now prepared and package-checked in PowerShell 5.1/7; README.md and package-manifest.json are the only changed entries, with all 249 other files byte-identical. See [GA preparation](ga-preparation.md) for both original and refreshed hashes. Neither candidate has been uploaded.

### 2026-10-02 — Steam checkout launch-context correction (0.39.2)

The user reports no Steam checkout window under the mod launch. The latest local overlay renderer/UI logs identify both GameID and OverlayGameID as the non-Steam shortcut ID, while the launcher sets SteamAppId to the owned game (2622000). This proves a launch-context mismatch, not a recorded transaction/server error. Steam's [microtransaction guide](https://partner.steamgames.com/doc/features/microtransactions/implementation) routes in-game authorization through the overlay; [Facepunch's SDK initialization](https://github.com/Facepunch/Facepunch.Steamworks/blob/master/Facepunch.Steamworks/SteamClient.cs) sets AppId and GameId together, but that occurs after the renderer has already attached in this route.

Set SteamAppId, SteamGameId and SteamOverlayGameId consistently before creating AstralParty_INT.exe, overriding any inherited shortcut overlay ID. This is process-local only: the registered shortcut, original Steam launch options, accounts, loader defaults and native payment/authentication code are untouched. Do not reinstate the old deliberate shortcut GameId split. The new inert executable test records the child environment at startup and exercises inherited-overlay override, missing Steam context, already-running-game and failed-compatibility guards without starting Steam, the real game or a payment. Actual checkout display/completion and screenshot/playtime association remain unverified until a user-run check; no payment was attempted by the agent.

### 2026-10-19 — Mod launch through the owned Steam app (1.4.1)

The user reported that the Steam overlay never appeared under the Mod shortcut. The overlay renderer was loaded in the game process, but Steam had started the non-Steam shortcut, so the overlay bound to the shortcut identity. Overriding SteamOverlayGameId hid the overlay; keeping only SteamAppId showed the overlay but the in-game checkout window did not open. Launching app 2622000 itself through Steam, with the loader arguments in its launch options, gave mod, overlay and checkout together. `steam://run` with arguments asks the user to confirm them on every launch, so the Mod entry instead writes an empty single-use request file in the per-user runtime folder and opens `steam://rungameid/2622000`. The plugin deletes the request on every load and activates only when the request is at most three minutes old. This relies on the shared loader being ON in doorstop_config.ini; when it is OFF, Mod.cmd falls back to `steam://run` with arguments and the confirmation dialog. A launch from the Steam client within three minutes of an unused Mod request would also activate BAP once.

On 2026-09-25, Steam content_log.txt confirmed verification on build 23905731 found all 38 chunks of `8vJXnINT/AstralParty_INT_Data/data.unity3d` different and replaced exactly one file with depot 2622001 / manifest 1145304994797959214. The prior hash was `728E822D5A80E642FD42F8C9BF68E927E8FC4ADA5BC5C13A895C79C7BFB512EE`; the restored hash is `948F75DCB07D7AEA83D0EC239A5B5B8A2D70F2B4BC8D8889D7B20C9915173CC5`. History records a local Afacad-Regular font restoration on September 20; that is the likely local modification Steam undid, not proof of a new game update or damaged disk. The old game-file backup is no longer available at its recorded path, so an asset-by-asset comparison could not be repeated. Korean text remaining visible does not imply that this bundle remained unchanged.

The launcher/installer now treats only this exact resource path's hash mismatch as advisory. Existence, a minimal size and UnityFS signature are still required. These are envelope checks, not full corruption or semantic validation. Executable/engine/loader/metadata and catalog mismatches remain blocking, as do catalog changes during play and per-feature runtime API failures. No baseline hashes are rewritten. The current restored bundle opened in the existing AssetsTools inspector; compatibility fixtures and the actual installation check passed with one resource warning. Live gameplay after this policy change has not been verified.

Review game, translation, runtime and catalog changes before producing a manifest with `tools/new-compatibility-baseline.ps1`. Never regenerate hashes merely to allow an unknown installation. Document actual verification separately from intended behavior.

On 2026-09-23, the non-Steam route passed INT startup, BetterAstralParty 0.4.66 loading, Korean title-screen, Steam overlay and screenshot-file checks. The final live vanilla check was handed to the user; original-game playtime accumulation is unverified.

Version 0.4.67 replaces the field nickname's separate Han-only font loader with the live sidebar `UICom_PlayerInfo.txt_PlayerName` font and renderer settings. Only mod-owned nickname labels change; their colour, size, native display-name privacy rules, HP and buffs remain unchanged. Missing UI is retried on subsequent scans; successful application is cached per plate. Release build, typography metadata/path checks and field-buff regression checks passed. Actual Chinese-character rendering still requires an in-game check; historical font implementation notes do not prove resolution.

## Records

- [Archived README and development history](development-history.md): original notes, including superseded designs and verification limits.
- [Performance investigation, 2026-09-23](performance-2026-09-23.md)
- [Performance audit, 0.4.12](performance-audit-0.4.12.md)

Put future detailed investigations in `docs/`, not the user-facing README. Keep the supported Korean and English user instructions aligned. The earlier public-packaging and license decisions below are historical; they do not supersede the current root LICENSE or authorize publication.


## Historical automatic-update development status — 2026-10-02

The 1.0.0-dev final-failure OFF policy is integrated; see [failure policy](update-failure-policy.md) and its linked evidence. Development packaging verifies production metadata, rejects fixture/older policy inputs and remains local-only/non-installable. Operational authentication/trust/helper fingerprint and actual private delivery/native validation are separate pending work. Do not run the ordinary installation commands for this development candidate.
