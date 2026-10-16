# BetterAstralParty public GA preparation

## Current GA completion — 2026-10-08

BetterAstralParty 1.0.0 is published as the public stable release at [TSM701/BetterAstralParty — 1.0.0](https://github.com/TSM701/BetterAstralParty/releases/tag/1.0.0). Repository ID is `1410226758`, release ID is `406781524`, and the published application-source commit is `998d7e51925f9c2539cb367ddf0c5e8ebcefd5ad`. The final release uses the canonical development resources; it is not the archived October 1 candidate.

- `BetterAstralParty-1.0.0.zip` is the approved complete installation bundle, SHA-256 `F800F943251A8EAD15FD659F372E49FBD1693BF7DF7EA514297CC171E21BD2BE`. Its payload is `artifacts/automatic-update-development/ga-release-20261008/final-package/payload`, with package-manifest SHA-256 `39D004932B81C6D7C2FA24DBD0E7BC74CF1F6B46B3807918AA34F187A3C1CAC0`.
- `artifacts/development-maintenance.json` now pins that exact `1.0.0` payload and manifest. Both development CheckOnly routes passed. The subsequent real UpdateOnly run initially failed ordinary-journal completion with native error `4395` (`LinkedFile`); that snapshot remains in `artifacts/automatic-update-development/ga-release-20261008/local-installation-incomplete.json`. The directly authorized, single exact-incident recovery then completed successfully. The installed eight owned files, Stable receipt and signed helper now match GA; native ownership/update-state checks passed. The original journal is preserved unchanged as `completed.json`, all 11 backups and settings are retained, and loader-OFF and channel history are unchanged. See `artifacts/ga-local-recovery-20261008-r1/result.json`. This local completion did not change published assets or run the game/Steam. The separately retired dummy journal remains recoverable history.
- The three missing build-input records were added before the final build. The final product build-source SHA-256 is `78527827498ED2460E468D23F1C3AA8CC39D883112CCB5BB6EFDAC662E773132`. The exported application source reproduced the plugin, launcher and helper bytes; the final installer-only corrections do not alter those three binaries.
- The matching `BetterAstralParty-1.0.0-source.zip` is preserved under `artifacts/ga-public-source-20261008-r3/`, SHA-256 `55F1030D617E90B200B492B70D60974E28E2C996307114F82FE4FCAA9BB083EA`. Its 159-file manifest, plus the manifest itself, exactly matches the 160 published source files. Application MIT terms, third-party notices and three corresponding-source archives are included; dummy Steam artwork and private operational records are excluded.
- Stable repository identity, release-purpose helper/update signatures, exact whole-package approval and the Beta/local-to-GA transition checks are complete. The isolated native file-fence/transition fixture passed 102 checks: `artifacts/gnw-89a722c13a9e47a89ef4b54b889ff218/result.json`, SHA-256 `D5D13C76652EA6AD35BA2C33A8A5AC233529CAEEEF9D037F9E2FE5F82922C88D`. That fixture alone did not run the full installer, uninstall/reinstall, the real game or Steam. The installed-copy completion is separately evidenced by the exact-incident recovery above; neither result claims new in-game acceptance.
- Exact independent whole-package and technical notice review passed. Evidence is `artifacts/automatic-update-development/ga-release-20261008/whole-independent-audit.json` and `artifacts/ga-public-source-20261008-r3/technical-license-closure.json`. The immutable packaging state's older `independentReviewPending` field predates this separate review and does not describe its completed result.

The dated preparation records below are preserved history. Their local `.local.1` binding, pending decisions and narrower publication permissions describe the earlier review stages, not the current release or current maintenance binding. Existing packages, source exports and signing/recovery evidence remain unchanged.

## Archived source-only review — 2026-10-08, before final GA

At this stage the review used the canonical development source and resources, not the October 1 candidate. It updated documentation and prepared/compiled a local source-only snapshot. That review step itself did not create a repository, change a product version, sign, replace an installable package, install, uninstall or upload anything.

The maintenance binding at the start of this review was `1.0.0-rc.6.local.1`, at `artifacts/automatic-update-development/li1/payload`, with package-manifest SHA-256 `55B570CD49B1D7684C4C55940A523D65C32535F2F99B99104C6E63BF86885570`. That immutable local-development candidate is retained as history and is superseded by the current `1.0.0` binding above.

### Step 1 — Public documentation and distribution inventory

Completed checks and corrections:

- README.md and docs/INSTALL.md now have identical Korean/English user instructions. Removed development-only binding details and stale rc.2/rc.6 claims from those instructions. They explicitly identify public GA as being prepared, not already published, and distinguish MIT application source from third-party materials.
- The subsequent README cleanup keeps mod introduction, features and practical user procedures, removes generic disclaimers and internal validation details, and records this ongoing writing policy in AGENTS.md. The immutable source-review snapshot and ZIP below predate this cleanup; regenerate the final GA source export from the current canonical documentation without altering those historical artifacts.
- User-facing complete installation ZIPs now follow `BetterAstralParty-<version>.zip`; internal unsigned candidates remain visibly marked. Packaging-expression tests cover release/prerelease/build-metadata versions and retain the `-update.zip` contract. README/INSTALL now describe the shared PvP convenience-feature suspension, resumption and continuing updater/diagnostic processing. These changes do not alter runtime PvP behavior or regenerate the bound installable package.
- Both instructions describe the same supported older-uninstaller and clean-reinstall policy, while retaining downgrade, ownership and recovery checks. docs/BUILD.md now gives explicit public-source compilation commands without private maintenance/research dependencies; local maintenance stays in docs/development.md. Historical candidate records remain separate from current status.
- Rechecked all 281 inventoried files against the pinned manifest: hashes and sizes matched. The payload has 282 files including package-manifest.json, with no unlisted files. A bounded path/type check found no PDBs, dumps, user CFGs, font files, game interop directory, CodeGraph/Git directory or development maintenance binding. This is not a full secret or binary metadata audit.
- The independent dependency inventory review matched all 224 loader/runtime files to 18 components, checked 19 notice references and three LGPL corresponding-source archives, and found no hash/size mismatches or unmapped loader files. The source archives include license/build material and no tested vendor binaries. This does not certify legal compliance or successful upstream rebuilds.
- A source-only review snapshot is at `artifacts/ga-public-source-review-20261008-r1/source`, with 159 inventoried files plus source-manifest.json. Its sibling BetterAstralParty-source-review.zip has SHA-256 `2616804EDEF9E6B92F885DA0D8EAA8CD90C1877F8DE660DC469A4ED47AA3476F`; the source manifest SHA-256 is `895AD3DC0DE4247D46120D7EB09001D57B60A89400BF02EF5EF5021FD48F6E7D`. This is an unpublished current-source review, not the final 1.0.0 source release or a replacement installer.
- Explicit compilation from that source snapshot passed for plugin, launcher and helper, using separately supplied local SDK/game/loader references. Plugin compilation reported zero warnings/errors. Compile outputs remain outside the source snapshot in its sibling compile-check directory; no research DLLs, internal harnesses, game process or installed files were used as outputs. The final GA build still needs a corrected complete input binding and fresh acceptance checks.
- Independent read-only review verified the ZIP and source manifest hashes, all 159 files plus the manifest, safe/unique paths and the compilation structure. It found no missing/extra/changed entries, directly included user paths, tested secret patterns, private operational histories or game/Steam artwork. Three upstream source archives retain their own upstream logo PNGs; these are not the excluded game/Steam banner. Pattern checks are bounded, not proof against every possible secret format.

Historical open decisions and findings at the source-only review stage:

- The user selected open-source publication. Prepare an allowlisted application-source export under the existing MIT license as well as release assets and user guidance. Never upload the development/game directory wholesale.
- The current root LICENSE already supplies MIT terms for BetterAstralParty-owned code. Do not silently change it or extend it to third-party components, game content or artwork.
- THIRD-PARTY-NOTICES/SOURCES.md describes accompanying application source and docs/BUILD.md, but neither the complete application source nor that build document is in the pinned whole package. The new source-review ZIP supplies these for review; deliver a matching final source archive alongside the exact GA build rather than treating the current review as final. Do not infer that a public source repository is the only possible licensing route: the relevant shared-library/relinking conditions require separate verification against the [LGPLv3 text](https://www.gnu.org/licences/lgpl.html) and [LGPLv2.1 text](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html).
- The user confirmed that the developer Steam hero PNG is unused dummy material and must not be included. Exclude game/Steam artwork from both public source and distribution; preserve the local original and do not alter unrelated upstream source archives.
- The user excludes Defender incidents, development permission/approval denials, internal session histories and their diagnostic records from public documentation and source exports. Preserve those records locally; do not remove product permission handling or validation code.
- The prior build-source.json lists 115 files that still match the root, but omits the compiled diagnostics/DiagnosticBundle.cs, Directory.Build.props and global.json. Include these in source review snapshots and correct the build-input binding before the final GA build. Do not retroactively claim the old manifest bound the omitted files.
- Final public download links are intentionally not added until repository identity and publication are verified. Native runtime and modified-library rebuild/replacement checks have not been executed in this review; application compilation is a separate completed check.

### Historical remaining sequence — completed by the final GA work above

1. Finish the public-file scope and exact source/notice delivery decision above. Keep current candidates, published archives, signing reservations and recovery evidence unchanged.
2. Verify the actual public Stable repository identity before configuring it. Current StableRepositoryId is zero; anonymous Stable requests and private Beta credential isolation must be checked against the final feeds.
3. Establish and test the existing Beta/local-to-GA migration path. WholeInstallBridge currently rejects a different active channel; a higher version alone does not make a cross-channel whole installation valid. Preserve settings, receipts and channel-specific replay protection.
4. Prepare a source-bound GA build with matching whole/update bytes, current notices and completed checks. Public descriptors need the actual repository/release/asset identities and release-purpose signatures; local-install proofs with zero remote IDs are not publishable substitutes.
5. Complete user-run in-game acceptance, then publish only under an explicit publication request. Replace the preparation notice with verified download/migration guidance as part of final publication preparation.

## Archived private GA candidate — 2026-10-01

Prepared on 2026-10-01 UTC under the user's instruction to promote the final beta without adding or changing features. Remote deployment has not been performed.

## Scope

- The plugin version changes from 0.39.2 to 1.0.0. The rest of Plugin.cs is byte-for-byte equivalent after replacing that one constant. No gameplay, UI, launcher, installer or packaging implementation changes were made in this preparation.
- README, installation documentation and AGENTS.md record the approved stable transition and preserve the known runtime limitations. Historical beta archives remain unchanged.
- The existing packager already supports stable versions. The new name is `BetterAstralParty-1.0.0.zip`, without the historical beta suffix. This naming change does not establish additional runtime validation.
- The installed game copy remains 0.39.2. No game launch, real installation/uninstallation, Steam modification, payment or external log upload was performed.
- The GitHub update notification is a subsequent task and is not included in this candidate. There is no automatic file replacement.

## Candidate assets

| Asset | Size | SHA-256 |
| --- | --- | --- |
| `releases/BetterAstralParty-1.0.0.zip` | 37,716,918 bytes; 251 files | `59836E137F2A84B72C1200B59F7E4D8CF7A8E10D18F813DCE77826E5B13EDEEE` |
| `artifacts/BetterAstralParty.dll` | Built plugin, version 1.0.0 | `2C6524A88482BFB6E394FBE0A9F6A0E3EE3E42FE15ED62577E33308184BDAD24` |

After the subsequent user confirmation and authorization to prepare a separate documentation-only output, a refreshed candidate was created at `artifacts/ga-preparation-1.0.0-20261001-181419/documentation-refresh/BetterAstralParty-1.0.0.zip`: 36,613,484 bytes, 251 files, SHA-256 `EBF6F6BC5A9CA399A3C179111401E6744271BF6B9BC627E69A6641E367B7F702`. Its sibling checksum, `comparison.json` and PowerShell 5.1/7 package-check logs are in the same directory. Both package checks exited 0. Only README.md and package-manifest.json entries differ from the original; all other 249 entries, including every binary, are byte-identical. Rebuilding ZIP compression changes container size, without changing those file contents. The original ZIP, checksum and extracted release tree are preserved.

The refreshed ZIP and its sibling `.zip.sha256` are the current local assets proposed for later review/upload. The internal `package-manifest.json` describes the individual file hashes. Upload is still unapproved. Build logs, comparison evidence, backups, private diagnostics and game files are not proposed uploads.

## Executed validation

- `tools/build.ps1 -Release` completed successfully with zero warnings and zero errors, including its self-test and existing regression scripts.
- Independent combat enumeration covered 4,224 scenarios / 152,064 dice pairs. Retained combat replays passed 72 exact HP-loss checks and 19 lethal/display-censored checks; exact overkill remains unverified.
- `tools/test-beta-package.ps1` passed in independent PowerShell 7.6.6 and Windows PowerShell 5.1.26100.9444 processes. Both exited 0. These checks cover extraction, all package hashes, script parsing, invalid-game-path failure logging and the missing-bootstrap CMD fallback, using temporary fixtures.
- The first package-test wrapper mistakenly interpreted the intentionally retained fixture exit code 1 as a failure after the test printed PASS. No code or ZIP change was needed; the independent process runs above establish the actual test results.
- A separate ZIP inventory check found no duplicate/unsafe paths or manifest mismatches. It found none of the tested game binaries, generated interop assemblies, fonts, translation patches, developer C# sources, PDBs, logs, dumps, backups, user configuration or account directories.
- Tested GitHub-token, AWS-access-key and private-key marker patterns had no matches. Packaged text had no development-machine user path. This is a bounded pattern check, not proof that every possible secret format is absent.
- BepInEx, Doorstop and .NET license/source notices are included unchanged. The package uses the original upstream loader archive rather than a played-in game tree. The bundled Steam hero PNG is retained from the existing installer.

Evidence: `artifacts/ga-preparation-1.0.0-20261001-181419/build.log` and `package-checks.log`. Pre-change `src/Plugin.cs`, `AGENTS.md`, `README.md`, `docs/INSTALL.md` and the previous DLL/launcher are backed up under the same directory. These local records are not release assets.

## User verification, known limitations and unexecuted checks

- The user subsequently confirmed the final beta's UI, Steam checkout-window, screenshot-association and playtime checks. These are user-confirmed results. This preparation session did not run the 1.0.0 package in-game, and substitute/geometry tests alone do not certify native rendering, clipping, glyphs, hit testing, animations or runtime interop.
- FlowSDK native-crash root cause remains unconfirmed. Existing diagnostics retain five historical `c0000005` events at `FlowSDK.dll+0x22fd68`, latest 2026-09-30 16:24:01 UTC. A 0.39.2 log rereading those reports is not a new crash. Existing records associate the latest event with a completed hand-layout dropdown creation; repetition and a controlled vanilla comparison are not established.
- WER/dump file metadata and the retained diagnostic summaries are accessible, but the current session could not read the WER/dump contents. Prior local analysis lists raw stack module candidates, not an unwound call stack; this does not establish causal attribution.
- The unsafe 0.22.x injection/focus hooks and 0.28.1 render-delegate registration already have separate removal records. Do not reintroduce or re-fix those paths based solely on the FlowSDK signature.
- Checkout-window display, screenshot association and playtime after the 0.39.2 launch-context correction are user-verified. Payment completion was not separately claimed. This preparation session performed no purchase.
- Unsupported special combat rules, unknown dice-rule precedence/distributions and exact lethal overkill remain documented limitations.

The user explicitly requested preparing this feature-preserving GA candidate with those limits retained. Further crash investigation is not an added gate to local candidate preparation. Optional follow-up diagnostics can compare the verified vanilla INT route and the mod route, recording timestamps and existing dropdown open/close/apply breadcrumbs without adding native hooks or bypassing compatibility. A private bot room is sufficient if battle validation is later authorized; public matchmaking is unnecessary.

## Frozen archive and current documentation

The original ZIP and its extracted release directory remain unchanged. That ZIP's internal `README.md` still says UI, checkout-window, screenshot-association and playtime checks are pending. This is superseded by the user confirmation above. Current source documentation and staged GitHub README/release text have been corrected. The separately authorized refreshed candidate updates README.md, regenerates its hash in package-manifest.json, and has its own new ZIP checksum. Coordinate which candidate is reviewed before uploading; do not silently overwrite the original or reuse its hash for changed bytes.

## Private GitHub authorization and proposed later uploads

- Account and proposed repository: `TSM701/BetterAstralParty`, private, with no added collaborators or access expansion.
- The user's subsequent authorization permits repository creation only, after updating the records and confirming the owner/existing-name status through existing authorized access. File pushes, tags, releases, uploads, public visibility and a mod-license choice remain unapproved. New OAuth/PAT or persistent authorization requires separate approval.
- Proposed repository contents: the installation `README.md` and `RELEASE-NOTES.md` staged in `artifacts/ga-preparation-1.0.0-20261001-181419/github/`. Do not push the development workspace or game tree.
- Proposed tag/title: `v1.0.0` / `BetterAstralParty 1.0.0 — private GA`. A stable release in a private repository is the proposed end state; no remote tag or release exists from this preparation.
- Proposed assets: exactly the documentation-refresh directory's `BetterAstralParty-1.0.0.zip` and `BetterAstralParty-1.0.0.zip.sha256`, using refreshed ZIP hash `EBF6F6BC5A9CA399A3C179111401E6744271BF6B9BC627E69A6641E367B7F702`. The staged release notes disclose the validation limits. No asset upload is currently authorized.
- No project-wide mod license has been selected. Do not silently add an open-source license; third-party notices retain their independent terms. A public distribution or license change is a separate decision.
- The GitHub connector identifies TSM701 but returns no accessible repositories. The proposed repository lookup returns 404, which does not distinguish absence from inaccessible private content. No repository creation, push, remote tag, release publication or asset upload was attempted.
- The account check was repeated after creation-only authorization: login `TSM701`, user ID `204223406`; the owner repository list is still empty and the exact-name lookup is still 404. Creation is blocked by the available execution capabilities: no repository-create connector action, no `gh` on PATH or at the standard Program Files location, and no callable `node_repl`/browser automation tool. The Computer Use skill was read, but its required JavaScript runtime cannot be initialized in this session. No alternate helper, credential extraction or authentication flow was attempted. This is a capability blocker, not an automatic approval rejection.
- Available connector tools do not expose repository creation, release publication or release-asset upload, and `gh` is not installed on PATH. After approval, use an authorized GitHub browser session or the supported [GitHub CLI browser login](https://cli.github.com/manual/gh_auth_login), for example `gh auth login --hostname github.com --git-protocol https --web`. Do not request a token in chat, extract another app's credentials, generate SSH keys or embed long-lived credentials in the mod. A new login/persistent authorization needs approval before execution. Repository [private visibility](https://docs.github.com/en/repositories/creating-and-managing-repositories/about-repositories#about-repository-visibility) must be checked before uploading anything.
