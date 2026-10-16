# Stable/Beta 분리와 완본체·자동 업데이트 통합 계획 — 2026-10-04

이 문서는 사용자가 승인한 후속 구현의 설계와 파일 소유권 인계다. 현재 서명 턴은 기존 고정 helper 한 건의 서명·검증과 중간 입력 고정까지 수행한다. rename/새 저장소 생성/게시/실제 설치/채널 전환 구현은 이 문서 작성으로 수행되지 않는다. 공개 범위(소스 전체 또는 배포 파일·일반 안내)는 부모가 사용자에게 확인 중이며 라이선스도 보안 검토 중이다.

## 사용자가 결정한 배포 구조

- 새 공개 Stable 저장소: TSM701/BetterAstralParty.
- 현재 비공개 저장소를 TSM701/BetterAstralPartyBeta로 rename하여 Beta용으로 유지한다. 기존 저장소를 public으로 전환하지 않는다.
- 두 저장소 모두 최초 설치용 whole 패키지 + 자동 업데이트용 owned8 ZIP/검증 manifest/signature를 동시에 제공한다. whole의 DLL/launcher/compatibility 및 update의 대응 bytes는 같은 source/version/platform/검토 묶음이어야 한다. source-only 또는 update-only ZIP으로 첫 설치를 대신하지 않는다.
- 전체 패키지에는 모드·clean loader·검증된 helper와 이후 자동 업데이트 기능이 모두 포함되어야 한다. 처음 설치 과정에서 helper bootstrap까지 실제로 연결하고 검증해야 한다.
- 메인 메뉴에 늦게 조회가 완료되어도 새 버전 팝업을 표시하고 동일 목표 버전은 실행당 한 번만 알린다. 플레이·매칭 중에는 표시하지 않는다. 팝업은 강제 종료·자동 재시작을 수행하지 않는다.
- Beta→Stable에서 최신 검증 Stable이 더 낮을 수 있으므로 명시적인 채널 전환에 한해 안전한 downgrade가 필요하다. 일반 업데이트의 anti-downgrade를 완화하지 않는다.

## 현행 바이트와 실제 충돌

2026-10-03 unsigned 후보의 helper descriptor SHA-256은 `1B1F7222398C195B004F3CA6803A3DE8E1CF754AB7E0170EBCF4B483B487D5C4`이며 repository는 당시 비공개 slug `TSM701/BetterAstralParty`다. descriptor에는 채널이 없다. 이번 승인된 CNG 서명 한 건은 이 바이트만 처리했으며 미래 repo 전환에 맞춰 고치지 않았다.

현행 `src/Plugin.cs:39`의 feed, `src/ReleaseUpdates.cs:36`의 UpdateContext, `src/PrivateReleaseSession.cs:51`의 bearer allowlist, `updater/HelperDelivery.cs:29`·`:44`의 helper canonical/verification 및 `tools/GaPackageContract.cs`가 원래 slug에 묶여 있다. 현재 UI의 Stable/Beta는 같은 저장소에서 release 필터를 바꾸는 동작이다. private 연결 없이는 production 조회가 구성되지 않아 새 공개 feed의 anonymous GET도 별도 구현이 필요하다.

rename은 원래 numeric repo ID를 유지할 것으로 기대되지만 작업 전에 API로 검증한다. 새 같은-name public repo는 별도 ID여야 한다. 로컬 기록의 기존 ID `1401226962`는 사전 기록이며 이번 턴에서 API로 재확인하지 않았다. 구버전의 이름 URL, rename redirect, bearer 경로, 서명 descriptor의 repo/channel/actual release/asset IDs, 캐시와 staged ticket을 감사한다. 무조건 redirect를 따르거나 예전 PAT을 새 feed에 넓게 보내지 않는다. 이름만 재사용하면 구버전이 다른 새 저장소를 볼 수 있으므로 안전한 수동 whole 업그레이드/마이그레이션 경로와 사용자 안내가 먼저 필요하다.

현재 whole은 loader와 helper-bootstrap 파일을 포함하지만 `Install.cmd → tools/install-bootstrap.ps1 → tools/install.ps1`이 helper 설치를 연결하지 않는다. 별도 trusted bootstrap 코드는 `tools/UpdateDistributionBootstrap.cs`에 있지만 그 개발 도구는 현 whole에 없다. 따라서 현재 서명 포함 중간본은 첫 일반 설치 한 번으로 자동 업데이트 준비까지 완료됨을 증명하지 못한다. 검토된 전체 manifest/pin으로 외부 helper를 실행 전 검증하고, exact owned8 receipt가 완성된 후 signed helper installation/receipt를 연결하는 절차를 구현·검증한다. 기존 unreceipted/다른 helper는 채택·덮어쓰지 않고 명시적 migration/recovery로 처리한다.

공개 검사에서 현 모드 DLL CodeView에 작업자 로컬 user 경로가 확인됐다. 최종 자체 product 빌드는 PathMap/검토된 debug 설정으로 제거하고 whole/update 둘 다 재검증한다. upstream clean loader에도 일부 공급자 build/debug 경로가 존재한다. 원본 loader byte/hash를 임의 patch하지 않고 공급자 metadata와 우리 로컬 경로를 구분해 보안 검토에 넘긴다. 키·토큰·개인 설정·로그·덤프·게임 독점 바이너리를 배포하지 않는다. 이번 스캔은 token/private-key 형태 탐지와 file allowlist 검사이며 모든 가능한 secret의 부재를 수학적으로 보증하는 것은 아니다.

## 명시적 채널 전환과 high-water

`src/ReleaseUpdates.cs:389` 선택은 installed 이하 버전을 제외한다. `updater/UpdateTrust.cs:178`도 target <= current 또는 target <= HighestAppliedVersion이면 NotNewer로 거부한다. `updater/UpdateTransaction.cs:75`의 HighWater/v1은 root/version/descriptor를 담으며 repository/channel별 이력을 구분하지 않는다. UI 채널 문자열 변경만으로 낮은 Stable을 선택하거나 apply할 수 없다. 기존 global high-water를 삭제·낮추면 이후 signed replay를 허용할 수 있으므로 그런 shortcut은 쓰지 않는다.

후속 설계 검토 항목:
1. 일반 update와 명시적 channel-transition을 별도 목적의 operation으로 구분한다. UI가 source/destination repo ID·channel·현재/목표 version·설정 호환성·백업 범위를 보여주고 사용자 의도를 특정 검증 release/hash에 결합한다.
2. destination의 최신 verified release를 완전한 인증/익명 허용 feed 조회로 선택한다. 불완전/오래된 cache·네트워크 실패·권한 실패 시 임의의 이전 버전을 선택하지 않는다. 정상 anti-downgrade는 유지한다.
3. repo/platform/channel별 검증 이력과 transition generation을 root-bound receipt/high-water에 결합하는 schema migration을 설계한다. 현재 v1 root identity와 가장 높은 관측값을 보존한다. 목적별 replay policy, 이전 channel에서 이미 설치한 같은 target으로 돌아가는 경우, 서로 다른 repo의 동일 version도 검토한다. 단순 floor reset이나 bool force 플래그는 충분하지 않다.
4. 설정 schema 호환성을 먼저 확인하고 값의 손실/마이그레이션을 명시한다. 전체 owned files/receipt/high-water/설정의 일관된 backup·commit·실패 rollback/recovery를 검증한다. 설정이 호환되지 않으면 승인 없는 초기화 대신 전환을 중단한다.
5. 게임·launcher 종료와 서명·호환성·경로 lease·unknown-file 보존은 전환에도 적용한다. 중단/재시작/전원 손실/채널 전환 중 추가 요청·staged cancellation/recovery·구버전 receipt migration을 fixture로 검증한다. 채널 변경으로 자동 게임 종료나 재시작을 추가하지 않는다.

## 메인 메뉴 팝업 연결과 소유권

현재 메인 스레드 entry는 `src/ModUi.cs:47` Tick이다. updater poll은 `:53–57`에서 worker 결과를 수용하고, `:99` GameUi.Tick으로 현재 home/visibility를 갱신한다. 설정 창이 닫혀도 동작하도록 새 notice tick을 CoreUi/PvP gate 이후, 최신 GameUi/언어 갱신 이후와 `:138` `if (!IsOpen) return` 이전에 둔다. Stage의 unmanaged/managed delegate 재등록은 하지 않는다.

`src/GameUi.cs:136` UpdateHome/`:237` RefreshReleaseNotification이 기존 '모드 설정 • 새 업데이트' 버튼을 다룬다. HomeAvailable은 UIHomePanel 존재/visibility이며 매칭 overlay까지 제외하는 근거로 충분하지 않다. 테스트 세션 01a0fccd의 읽기 전용 감사가 완료됐다. report.md/test-cases.md의 native 메뉴·매칭·준비 panel/controller·modal stack 및 재사용 popup 근거를 후속 설계에 반영한다. 제안한 30개 기준은 아직 실행되지 않았다. MatchFocusState는 별도 포커스 policy이며 실제 match transport가 연결되지 않은 코드라 popup gate로 추정하지 않는다.

팝업의 once-per-run 소비 상태는 언어 변경·메뉴 재생성·auth reconnect에도 유지한다. ReleaseUpdates의 `_notifiedVersions`는 auth identity 변경에서 clear되므로 UI의 실행당 once 요구를 이것만으로 보장하지 못한다. 표시 성공 전에 알림을 소비하지 않는다. Available의 release/notes/version과 AutomaticUpdates 실제 상태를 사용한다. Downloading/Preparing은 진행 안내, Ready는 준비 완료와 적용 설정 확인, 실제 Queued는 '종료 후 적용·직접 재실행' 안내로 구분한다. Ready를 곧바로 종료 후 적용으로 표시하지 않는다. modal/scene 종료 시 mod-owned view만 dispose하고 원래 input/transition을 보존한다. KO/EN·폰트·화면 크기·늦은 완료·중복·matching/PvP·장애/접속 해제·queued 상태를 검증하고 실게임 rendering은 별도로 확인한다.

권장 파일 소유권(부모가 최종 배정):
| 담당 범위 | 파일 |
|---|---|
| UI 구현 | 새 UpdateNotificationState/UpdateNotificationUi, `src/ModUi.cs`, `src/GameUi.cs`, `src/ModText.cs`. Plugin 연결 변경은 core 담당과 한 번에 조율 |
| repo/auth/feed | `src/ReleaseUpdates.cs`, `src/ReleaseHttpClient.cs`, `src/PrivateReleaseSession.cs`, 새 feed policy, `src/Plugin.cs`의 production/bootstrap config |
| transition/receipt/recovery | `updater/UpdateTrust.cs`, UpdateContext/UpdateTicket, UpdateTransaction, InstallReceipt, UpdateRecovery, HelperDelivery와 `src/UpdateHandoff.cs`. shared updater 파일은 한 담당자가 소유하고 다른 작업과 직렬 통합 |
| 최초 설치/helper 연결 | `tools/install.ps1`, install-bootstrap/install-policy/install-backup, UpdateDistributionBootstrap. helper trust/API 변경은 transition 담당과 먼저 합의 |
| package/build/sign/release | ga package contract/policy, build scripts/PathMap, whole·owned8 매칭과 서명, 실제 repo/release/asset IDs. 운영 signer는 product에 포함하지 않음 |
| 문서 | 이번 턴은 이 설계와 development-history appendix만 수정. 원본 일반 README/INSTALL과 고정 payload는 보존 |

## 통합·검토·배포 순서

1. 현재 signed-helper 중간본과 old/new identity·review hash를 고정하여 독립 검토로 넘긴다. catalog는 비워 두고 이 후보를 최종 배포하지 않는다.
2. UI·repo/auth·transition·최초 설치/helper를 위 소유권으로 나눠 구현하고 공통 파일은 직렬 통합한다. 공개 범위·라이선스·rename migration 검토를 부모가 조율한다.
3. 자체 product 경로를 제거한 고정 빌드에서 두 channel whole/update를 각각 같은 source/version/compatibility로 준비한다. 최종 helper image/descriptor가 이번 서명과 동일한 경우에만 재사용 가능하다. 바뀌면 새 exact signing input을 review한 뒤 기존 승인 범위의 절차로 처리하며 현재 서명을 다른 byte에 붙이지 않는다.
4. signed-helper 포함 final identity/review inventory를 독립 검토한 exact tuple로만 catalog 등록한다. 변경된 popup/bootstrap/문서도 모두 final inventory에 포함한다.
5. 승인된 rename/새 public repo를 수행하고 실제 repo IDs/private ACL/public visibility를 확인한다. whole/update를 둘 다 draft에 준비하고 실제 update release/asset IDs로 descriptor를 생성·검증·서명한다. Beta는 prerelease 표시와 Beta descriptor가 일치해야 한다. 현재 consumer는 user-selected Beta에서도 stable 표시 release이면 Stable context를 만들기 때문이다.
6. private와 public 각각 fresh install→helper receipt/fingerprint→업데이트 및 명시적 채널 전환을 실제 허용된 환경에서 검증한 뒤 릴리즈한다. owner가 요청한 공개 범위가 확정되기 전 외부 공개하지 않는다. 토큰/키/개인 경로/독점 게임파일/실험·로그·자기승인 fixture를 검사한다. 실제 설치와 in-game loading/visual correctness는 서로 구분하여 보고한다.


## 완료된 팝업 읽기 전용 리뷰 반영

근거는 `artifacts/parallel-review/test-package/update-notification-design-review-20261004-b1/report.md`와 `test-cases.md`다. 이 리뷰는 현재 signed-helper 후보의 독립 배포 승인이 아니며 UI 구현도 아니다. 30개 기준과 최소 실게임 체크는 모두 미실행이다.

- 실제 관리형 entry는 ModUi.cs:823 EventSystemUpdatePatch.Postfix이며 기존 frame guard를 재사용한다. 새 Stage delegate를 넣지 않는다.
- 표시 eligibility는 UIManager.currentPanel이 실제 HomePanel이고 home instance/visible/input anchor가 맞으며 connectRoomId/roomState/현재 유효 MatchData가 비활동인지 읽는다. TeamId=0으로 Dispose된 stale MatchStatus를 활성 매칭으로 오인하지 않는다. 필수 schema/null/unknown은 표시만 보류한다. 새로운 enum은 추측값이 아니라 검증된 native structured 정의를 사용한다.
- Common/Com_PopUpWindow_Bottom의 별도 복제본과 native title/button/font를 재사용한다. btnsState/gears는 검증 없이 selectedIndex를 추정하지 않는다. 기존 설정·native 창·OS credential prompt가 있으면 겹치지 않게 보류한다.
- HomePanel.OnKeyDown은 home.touchable을 검사하지 않고 Enter로 game start를 실행할 수 있다. 새 알림이 GameUi.SetSettingsModal을 두 번째 소유자로 사용하지 않는다. 최소 알림은 원래 홈 키보드 입력을 보존하고 native start/다른 panel 전환 시 닫으며 완전 modal이라고 표현하지 않는다. 완전 modal이 필요하면 별도 소유권·focus 검증이 선행되어야 한다.
- once-per-run 이력은 기존 홈 버튼 읽음 marker와 분리한다. Result.Release를 입력으로 받고 실제 visible/onStage 성공 이후 정규화 SemVer를 Shown 집합에 추가한다. v 접두사/build metadata의 동일 precedence와 다른 prerelease를 구별한다. language/menu/auth/channel 변화는 표시 완료 집합을 초기화하지 않는다. private context 손실 시 pending/display 자료는 버리되 공개된 version 문자열의 표시 이력만 실행 동안 유지한다.
- 새 알림의 오류 breaker는 알림만 정리한다. ReleaseUpdates.FrameFailed, Automatic.Disable, config 저장, helper cancel 또는 다른 CoreUi 전체 정리를 호출하지 않는다. 표시/닫기/상태 refresh는 다운로드·queue·옵션·auth·채널을 바꾸지 않는다.
- Available을 트리거로 창을 열고 이미 열린 창의 상태를 갱신한다. Automatic의 Ready가 같은 Tick에 queue Preparing으로 넘어갈 수 있다. 작업 identity/generation을 read-only snapshot으로 연결할 수 없으면 특정 release의 staging 완료라고 단정하지 않고 전역 업데이트 상태로 표시한다.
- UI action은 기존 일반 업데이트 설정으로 이동하거나 알림만 닫는 두 가지로 시작한다. 동시 F8/마우스 down-up/Escape/scene transition의 입력 재소비를 막는다. Popup close는 update cancel이 아니다.
- 기존 test-update-notification-ui.ps1의 no-provider/모든 popup 금지 가정은 역사 범위를 유지하고 새 presenter 검증을 별도로 준비한다. 새 기능의 후속 버전은 AGENTS의 MINOR 정책에 맞춰 부모가 통합 범위에서 결정한다. 현재 1.0.0 서명 input은 변경하지 않는다.

권장 UI 소유권은 UpdateNotificationState/UpdateNotificationUi와 ModUi/GameUi/ModText이며, read-only target snapshot이 필요하면 core 담당이 AutomaticUpdates의 제한된 공개 계약을 제공한다. source 공유 파일을 두 담당자가 동시에 수정하지 않도록 부모가 조율한다. N01–N30과 최소 실게임 결과가 나오기 전 새 popup 배포 준비 완료로 보고하지 않는다.

## 사용자 안내와 스타일의 후속 변경

사용자가 설치 방법 보완과 Markdown bold 금지를 요청했다. 기존 프로젝트 AGENTS.md에 영어 스타일 지침을 append하고, README.md와 실제 패키지 README 원본 docs/INSTALL.md에 whole 다운로드·전체 압축 해제·실제 설치 명령·실행·업데이트 조건·안전한 제거를 보완했다. 아직 연결되지 않은 helper bootstrap은 준비 완료로 설명하지 않는다. 새 일반 안내에는 bold·개발일지·크래시 이력·버전변경점을 넣지 않는다. AI 활용 문구나 허위의 전부 인간 작성 표현도 추가하지 않았다. 필수 third-party 고지를 유지하고 소스 라이선스는 부모의 보안 검토에 따른다.

이 문서 변경은 고정 whole README 바이트에 덮어쓰지 않았으며 다음 통합 후보를 새 input manifest로 준비할 때 포함해야 한다. 지금의 signed-helper identity는 popup/bootstrap/새 안내가 통합된 최종 identity가 아니다.


## 공개 전 보안 감사 반영

근거는 `artifacts/parallel-review/security-recovery/public-stable-distribution-mit-rename-audit-20261004.md`다. 감사는 unsigned 고정 후보를 대상으로 하며 현재 signed-helper 중간본이나 미래 통합본이 아래 항목을 해결했다고 판단한 것은 아니다.

- PUB-01/P1: 기존 이름을 새 public에 재사용하면 미전환 Beta client가 public GitHub API에도 bearer를 붙일 수 있다. 현재 UpdateDownload는 기존 numeric repo ID를 CDN 302 경로에서 검사하지만 metadata/asset API 인증 전에는 새 repository identity를 확인하지 않는다. 의도치 않은 Stable 적용은 응답 방식과 기존 pin/repo/channel/version/hash/helper 조건에 따른 위험이며 항상 가능하다고 단정하지 않는다. rename만으로 이미 배포된 code가 바뀌지 않는다.
- 이전 private 경로가 살아 있을 때 먼저 Beta 전환용 client와 검증된 helper migration을 제공한다. 알려진 사용자 전환·메모리 인증 종료와 구버전 재로그인 금지 안내를 완료한 뒤 rename을 조율한다. 미전환 구 client의 bearer 전송을 절대로 막아야 한다면 원래 이름 재사용을 미룬다. 새 코드에 repoID를 넣는 것만으로 구 코드까지 보호됐다고 말하지 않는다.
- Stable은 anonymous GET만 사용하고 연결된 Beta session이 있어도 Authorization/Cookie/OS credentials를 보내지 않는다. Beta bearer는 정확한 BetterAstralPartyBeta HTTPS API release/asset 경로에만 허용한다. 자동 redirect 금지와 CDN 새 요청에서 인증 제거를 유지한다. 인증 실패 후 public fallback으로 자동 전환하지 않는다.
- metadata/selected release/descriptor/ZIP/ticket/helper/fault/cache/ETag를 numeric repo identity·channel·purpose·인증 모드에 결합한다. 전환의 늦은 응답/prepared ticket은 폐기하고 shared cooldown 우회는 허용하지 않는다.
- Stable signing pin 또는 purpose는 구 Beta verifier가 수용할 수 없어야 한다. 같은 RSA pin에 envelope key ID만 바꾸는 것만으로는 서명된 descriptor의 의미 분리가 충분하다고 추정하지 않는다. 구 verifier가 새 Stable signature/descriptor를 거부하는 실제 fixture를 acceptance로 둔다. 이번 private helper 서명을 public stable에 그대로 게시하지 않는다. 새 trust contract는 별도 검토이며 이번 턴에서 새 key 생성/교체를 수행하지 않는다.
- PUB-02/P2: 모드 DLL의 실제 RSDS 개인 PDB 경로를 PathMap/재현 빌드로 제거하고 whole/update bytes를 새로 검증한다. upstream loader의 제작자 debug metadata는 출처와 scope를 기록하며 원본 binary를 임의 patch하지 않는다. README 업데이트 명령의 특정 drive 경로는 사용자가 Steam에서 읽어 입력하는 명령으로 중립화했다.
- PUB-03/P2: Il2CppInterop 1.5.3/dbda1cb 계열은 LGPL-3.0-only 및 포함 GPLv3 원문과 대응 소스 제공 조건을 정확한 revision에 연결한다. 기존 LGPL-2.1 두 고지로 대체하지 않는다. HarmonyX/Cecil/MonoMod/AsmResolver/AssetRipper/Cpp2IL/Disarm/Capstone/Iced/SemanticVersioning/Dobby와 transitive components도 실제 version/hash/license/notice/source matrix를 갖추고 원래 copyright/permission을 보존한다. 사용자 자체 MIT가 제3자 고지를 대신하지 않는다. 이 worker는 라이선스를 임의로 부여·변경하지 않았다.
- PUB-04: hero artwork의 제작/배포권을 확인하거나 권리가 확실한 대체를 택한다. 그림을 자동으로 모드 MIT 대상에 넣지 않는다. source 공개는 자체 source allowlist로 준비하고 게임 SDK/interop·decompiled source·게임 독점파일·dump·운영/계정/내부 기록·private history를 제외한다. .gitignore만으로 과거 history/secret가 없어졌다고 주장하지 않는다.
- 현재 scan에서는 후보 내 실제 secret로 확정된 값이 발견되지 않았다는 기존 감사 결과와 이번 token/key 형태 검사가 일치한다. 이는 metadata PII·license·artwork·미확인 형식·source history까지 공개 준비 완료라는 뜻이 아니다.

배포 순서는 private identity read → 원래 경로에서 Beta migration 준비/검토 → 사용자 전환 확인 → private rename 및 같은 ID/private 검증 → 별도 public create 및 다른 ID/public 검증 → old verifier가 수용 못 하는 Stable 서명 scope와 실제 IDs → whole/update·고지·source allowlist 최종 검증 → 게시다. 각 step은 부모가 현재 사용자 승인 범위 안에서 조율한다. 실제 rename/create/외부 공개는 이 서명 턴에서 수행하지 않는다.

## 보완 감사와 배너 기능 제거 범위

보완 근거는 `artifacts/parallel-review/security-recovery/public-stable-artwork-bearer-clarifications-20261004.md`다. 현재 `src/UpdateDownload.cs:53`의 repository ID `1401226962`와 `:221–229`의 CDN prefix 검사는 새 저장소의 다른 ID로 보내는 302 다운로드를 거부한다. asset binary가 직접 200으로 반환되는 경로에는 CDN prefix 검사가 적용되지 않는다. 다음 CDN hop은 인증 없는 새 request다. 구 이름 재사용으로 public GitHub API에 bearer가 전송되는 정책 위반을 고쳐야 하며, 외부 호스트·CDN·저장소 관리자에게 token 값이 유출됐다는 검증 결과로 확대하지 않는다. 이번 worker는 실제 repository ID나 운영 HTTP를 새로 조회하지 않았다. 현재 활성 Beta 사용자를 한 명이라고 단정할 근거도 없다.

사용자는 hero가 원본 Steam 배너를 바탕으로 AI로 생성된 이미지임을 밝힌 뒤 아예 빼는 방향을 승인했다. 이 설명은 게임 로고·캐릭터 재배포 허락이나 MIT 적용 근거가 아니다. 후속 통합에서는 배포 이미지와 신규 설치의 배너 적용 기능을 제거하고 모드 Steam 바로가기는 유지한다. 이 서명 턴은 제거를 구현하지 않으며, 기존 고정 후보·백업·개인 Steam 설치·source PNG를 보존한다. 원본을 영구삭제하지 않고 후속 배포 manifest/source allowlist에서 제외한다. 개발 tree에서 이동이 필요하면 공개 대상 밖의 복구 가능한 보관 위치와 원래 hash를 기록한다.

현재 image SHA-256은 `6CA539F1A68C53AFE7BD2B7FBF296D8F643DC31BA2AF00DDD3AD2598FD36746B`이다. `tools/steam-shortcut.ps1`의 배너 적용 실패는 이미 바로가기를 유지하지만 `PlanRegister`는 이미지 source 존재와 무관하게 `Test-HeroNeeded`를 호출한다. 이미지만 빼면 재설치 시 불필요한 Steam 유지보수 계획이 반복될 수 있다. `tools/package-ga.ps1:47`도 hero를 필수 input으로 요구한다. 따라서 이미지가 없을 때 예외만 숨기는 변경으로 완료하지 않는다.

후속 변경 소유권과 계약:
- 최초 설치/Steam 담당이 `tools/steam-shortcut.ps1`의 신규 배너 설치 함수·호출·배너 필요 여부 계획과 새 hero receipt 생성을 제거한다. 바로가기 탐색·등록·갱신·실행·제거와 계정 선택·VDF 보존·동시변경/Steam 실행 검사·복구 기능은 유지한다. Install.cmd/Uninstall.cmd와 install-bootstrap/install/uninstall 경로에서 불필요해진 배너 의존성을 확인한다.
- 이전 버전 호환 제거 담당이 기존 `Remove-Hero`와 `tools/uninstall.ps1:217–220`의 artwork rollback 계약을 최소한으로 유지하거나 명확한 legacy helper로 격리한다. 현재 receipt의 Owner/GameRoot/AppId/hash 검증은 정확히 유지한다. 이미지가 hash와 다르면 사용자가 바꾼 그림을 보존한다. 알 수 없는 receipt/외부 경로/손상 backup은 변경 없이 보류한다. 기존 원래 artwork backup이 있는 경우 그 계약을 검사하고 확인된 backup만 복원한다. 현재 Install-Hero는 사용자 그림을 덮어쓰지 않으며 original artwork backup을 만드는 기능은 확인되지 않았다.
- package 담당이 hero 필수항목과 후속 input 생성/manifest/source 공개 목록에서 이미지를 제외한다. 일반 README/INSTALL은 앞으로 제공할 기능과 일치하게 수정하며 과거 기록·고정 후보의 안내 bytes는 그대로 보존한다. `steam-shortcut.ps1`은 owned8 업데이트 파일에 포함되므로 기능 제거 뒤 whole/update 양쪽 script bytes와 descriptor/hash를 함께 새로 고정한다.
- 독립 검토 담당이 이미지 없는 최초 설치/이미 등록된 바로가기/no-op 재설치, legacy 소유 배너 제거, 변경된 사용자 그림·외부 JPEG·다른 profile 보존, receipt 손상/누락, backup hash 불일치, 제거 도중 실패와 rollback을 fixture로 확인한다. 실제 Steam profile과 개인 그림을 검증 fixture로 사용하지 않는다. 별도 실게임/Steam 검증은 실제 실행 여부를 기록한다.

새 배너 제거 코드와 fixture 결과를 popup/bootstrap/feed/transition 변경에 통합한 뒤 최종 build·패키지 재현·독립 검토를 진행한다. 지금 서명한 중간 whole에는 기존 그림과 기존 배너 코드가 남아 있으므로 최종 공개나 설치 입력으로 승격하지 않는다.
