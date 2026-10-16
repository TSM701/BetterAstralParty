# GitHub release notification — implementation and remaining work

2026-10-02 UTC. The authentication-independent core and settings UI are implemented in **1.0.0-dev**, an unshipped development build. The production plugin has no concrete authentication provider or configured HTTP client, and sends no live HTTP requests. No credential, installation or release package was created. Builds and evidence are isolated under `artifacts/update-notification-followup/`. The original and documentation-refreshed 1.0.0 GA ZIPs/checksums, GA binaries and installed beta DLL/loader settings are unchanged.

## Implemented UI and core

The existing General tab has three native cloned controls: Stable/Beta, check status and Download page. Hover help shows current version, result/time, newer version and at most 700 UTF-16 units of notes. Korean/English labels reuse localization; untrusted notes bypass translation. Controls use the existing right scroll viewport and clipped, auto-scrolling help pane. The mod-owned `GTextField.UBBEnabled` is false. Native metadata confirms that the false branch reaches `TextField.set_text` without the markup parser. In-game rendering/input remain unexecuted.

Automatic nonblocking checks are wired through ModUi.Tick when the existing home menu is ready/re-entered, or authentication/channel context changes while it is ready. They call Check(manual:false), reuse six-hour freshness, and wait for the shared 60-second cooldown/server backoff. No periodic request loop or automatic popup is added. An unconfigured instance sends zero HTTP requests and creates no notice, including repeated menu entries. Available marks the mod-owned native home button title/tooltip; opening settings selects General, displays release details and acknowledges the marker. Same semantic versions, ignoring one leading v/build metadata, are notified once per in-memory account context across channels; account/sign-out changes clear private state and reset that context's deduplication. Download opens only a validated GitHub release page after a click. It never downloads/replaces files. Browser sign-in does not authenticate the API. Gameplay, payment and Steam startup paths remain unchanged.

Stable requires major >= 1, no SemVer prerelease suffix, `prerelease=false` and `draft=false`. Beta includes stable/beta/RC and historical 0.x versions, excluding drafts. Selection uses strict SemVer, one optional leading v, numeric prerelease comparison and strictly newer precedence, ignoring build metadata/publication order. The loader's existing `SemanticVersioning.dll` 2.0.2 is referenced without upgrade/download; see its [upstream project](https://github.com/adamreeve/semver.net) and [package metadata](https://www.nuget.org/packages/SemanticVersioning/2.0.2). Redistribution notices remain part of the GA license audit.

`src/ReleaseUpdates.cs` holds the provider interface, immutable results, bounded parsing/selection and one asynchronous job. `Plugin.cs` owns the unconfigured instance/channel. `ModUi.cs`, `MenuLayout.cs` and `ModText.cs` add native controls, polling and literal bilingual presentation. No new HTTP framework or native delegate registration was introduced.

State is main-thread-owned. `Task.Run` handles network/JSON work; workers never call native UI/Unity. Check returns immediately; Poll reads only a completed task. Cancellation first detaches pending work and increments its generation, then independently contains Cancel/Dispose exceptions. Unexpected completed-task exceptions become Failed/timestamped Stale. ModUi.Tick has a final updater-only exception boundary so updater failure cannot abort the remaining frame. Channel/provider/account changes, sign-out and disposal clear private cache/display. Identity getter failure or authorization refusal cannot send HTTP. CacheIdentity must be a nonsecret, nonblocking memory lookup; refresh/sign-in/network work is prohibited in that getter.

## Implemented budgets, cache and failures

- Read the GitHub release list with 100 items/page; validate pagination against the exact HTTPS API owner/repository/releases endpoint. Limit to four pages, 1 MiB aggregate body bytes (including reused cached pages), JSON depth 16 and 15 seconds including response-body reads. Incomplete pagination cannot claim Up to date.
- Keep one in-flight check and a shared 60-second cooldown. Automatic menu-entry checks reuse six-hour freshness. Page bodies/ETags are scoped by exact URL/account/channel; invalidating those data does not reset request timing. Cooldown and server Retry-After are conservatively shared across channel/provider/account changes in this repository client, including sign-out/sign-in, to avoid bypassing shared secondary/IP limits. A retained backoff remains visible after a configured context change and blocks both manual and automatic checks until expiry.
- Revalidate every page. A 304 requires the exact cached representation, otherwise retry once unconditionally or fail. Cached pagination survives a 304 without Link; updated Link/ETag metadata is retained for later scans. Sign-out/access failure clears private results. See [GitHub conditional-request/pagination guidance](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api).
- The future client creation boundary must enforce HttpClientHandler.AllowAutoRedirect=false before sending credentials; a post-response URI check cannot prevent an already-followed redirect. Configure currently accepts only mock clients in tests. No live factory/provider is supplied or tested, and no claim is made that arbitrary injected clients already enforce that setting.
- Distinguish Not configured, Not checked, Checking, Up to date, Available, Authentication required, Access unavailable, Rate limited, Failed, Stale, Incomplete and Cancelled. 401 requires authentication; 403/404 access failures clear private cache without interpreting 404 as proof of repository absence. Rate-limited 403/429 respect Retry-After/reset headers. Failed/incomplete checks can retain a visible stale result with its original timestamp and failure reason. Server bodies/credentials are not exposed in errors.
- Retain title <=160 and notes <=4,096 UTF-16 units; display <=700 notes units without cutting a surrogate pair at the boundary. Strip unsafe control/bidi override characters; markup remains literal and nonclickable. Download links require HTTPS github.com, the exact owner/repository/release-tag path, matching SemVer tag, default port and no userinfo/query/fragment.

## Required private authentication decision

The only production authentication code is `IReleaseAuthentication`; **no concrete provider or HTTP client is configured**. GitHub private release access requires authorization; supported fine-grained tokens need Contents read permission. Codex connector identity cannot be inherited by the installed mod. See [GitHub release API](https://docs.github.com/en/rest/releases/releases#list-releases).

Before live integration, agree with the parent/user on the supported per-user route, registration, scope, expiry, revocation, logout and storage. New OAuth/GitHub App registration, PAT creation, persistent access or access expansion requires approval before execution. Do not embed a shared token/client secret/private key, extract browser/connector credentials or make the repository public. None was done. Repository/release access and the private deployment target remain separate blockers; mock-tested code does not complete private update delivery.

## Executed validation

Isolated plugin and self-test builds succeeded with zero warnings/errors. The revised self-test reports **393 update assertions** against linked production code, fake `HttpMessageHandler` and nonsecret test-account identities. Mock requests contain no Authorization header; no live network was used. Existing self-tests passed, including 4,224 independently enumerated combat scenarios/152,064 dice pairs and 72 exact +19 lethal/display-censored replays. Exact overkill remains unverified. The first reviewed development revision (274 assertions) is preserved under review-v1.

| Area | Executed cases |
| --- | --- |
| Versions/channels | 1.10/1.9; rc.10/rc.2; equality/build metadata/downgrade; development/stable; leading v; 0.x; draft/conflicting flags; malformed tags |
| Auth/cache | Unconfigured/refused/broken identity sends HTTP zero; two-page 200/304 and per-page ETags; 304 without cache; changed Link/ETag across later 304; cooldown/freshness; account/channel/sign-out clearing |
| Failures/budgets | 401/403/404/429/500; backoff/expiry; mock offline/TLS exception; header/body timeout; malformed/oversized JSON; unsafe/cyclic/past-budget pagination |
| Lifecycle | 1,000 Check/Poll/automatic Tick iterations while I/O pending; single flight; throwing cancellation callbacks during cancel/channel/account/provider/disposal; unexpectedly faulted completed worker with/without prior success; late completion discard |
| Automatic notification | Home readiness/re-entry; six-hour reuse/expiry; channel/context changes; normalized same-version suppression; acknowledgment; sign-out/new account; removal clears marker; no-auth 1,000 menu entries with HTTP zero; disposed trigger |
| Retained limits | Channel round trip, new account and same-account provider replacement cannot bypass 60 seconds/Retry-After; automatic deferred check starts only after expiry |
| Content/links | Literal markup; controls/bidi; bounded text/surrogate cutoff; off-host/repository/tag/port/userinfo/query/fragment rejection; all KR/EN states; notes bypass localization |
| UI metadata/geometry | General/nonfixed controls and reachable last row; compiled presenter wiring; native Boolean UBB switch/literal branch; zero provider implementation; no direct wait/network in Check/Poll |

Sixteen existing focused native/UI regressions and the new tools/test-update-notification-ui.ps1 passed, including production automatic caller/final exception boundary, private home clone title and read acknowledgment metadata. One additional legacy manual test, test-surfaces.ps1 (outside the normal 31-script build list), failed its requirement for direct NativeUi.Surface construction in CardUi. Current CardUi delegates to AdviceBadge, which constructs that surface; all five CardUi method bodies are identical to the frozen GA DLL. The legacy script/CardUi were preserved and its failure log is separate. These checks do not certify live GitHub or in-game rendering/input/browser. See artifacts/update-notification-followup/review-fixes.md for the revised diff/results and remaining limits.

Initial outcomes are preserved separately: the first isolated build included old obj generated files and failed duplicate assembly attributes, resolved by excluding `**/obj/**`; an initial SemVer test exposed extra implicit v stripping in the dependency, fixed with a stricter guard; the first native UI script inspected EnsurePanel rather than Create, corrected and passed. Final passing checks do not erase these initial failures.

Reproduce without ordinary build/install/package scripts, which would overwrite GA artifacts:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$out = Join-Path (Get-Location) 'artifacts/update-notification-followup'
$pluginObj = Join-Path $out 'obj/plugin/'
$testObj = Join-Path $out 'obj/self-test/'
& './.dotnet/dotnet.exe' restore src/BetterAstralParty.csproj --configfile NuGet.Config "-p:BaseIntermediateOutputPath=$pluginObj"
& './.dotnet/dotnet.exe' restore self-test/BetterAstralParty.SelfTest.csproj --configfile NuGet.Config "-p:BaseIntermediateOutputPath=$testObj"
& './.dotnet/dotnet.exe' build src/BetterAstralParty.csproj -c Release --no-restore -o "$out/plugin" "-p:BaseIntermediateOutputPath=$pluginObj" '-p:DefaultItemExcludes=**/obj/**'
& './.dotnet/dotnet.exe' build self-test/BetterAstralParty.SelfTest.csproj -c Release --no-restore -o "$out/self-test" "-p:BaseIntermediateOutputPath=$testObj" '-p:DefaultItemExcludes=**/obj/**'
& './.dotnet/dotnet.exe' "$out/self-test/BetterAstralParty.SelfTest.dll"
& 'C:/Program Files/PowerShell/7/pwsh.exe' -NoProfile -File tools/test-update-notification-ui.ps1 -PluginPath "$out/plugin/BetterAstralParty.dll"
```

## Unexecuted work before shipping

The supported private provider/registration/access/storage design is unimplemented. Live private release detection, server ETag/pagination/rate behavior, native in-game labels/scroll/input/state clearing, clicked browser/sign-in/download page and runtime failure isolation remain unexecuted. No 1.1.0 release package, tag, upload, source push, public release or visibility change occurred. Parent review comes next; authentication/live testing and the GA distribution/license audit remain necessary for an actual private delivery.


2026-10-02 후속 체크포인트: 현재 1.0.0-dev의 helper/복구/backend 및 새 기본 ON과 저장된 OFF 보존을 검증했습니다. 최종 실패 OFF 정책은 primitive 단계이며 연결은 미완료입니다. 로컬 리뷰 ZIP은 installableRelease=false, failurePolicyIntegrated=false입니다. 상세/증거는 docs/update-recovery.md 및 artifacts/automatic-update-development/helper-ui-recovery-20261002-1051/verification.md를 참조하세요.

2026-10-02 최종 실패 정책 후속: [실패 정책](update-failure-policy.md)에 coordinator/helper 통합, 유한 재시도, 영속 OFF와 명시적 재활성화를 기록했습니다. 기존 primitive 체크포인트 이후의 최신 로컬 후보는 failurePolicyIntegrated=true, installableRelease=false입니다. 운영 private GitHub 인증/배포·실게임은 미실행입니다.
