# BetterAstralParty 자동 다운로드·종료 후 적용 설계

이 문서는 1.0.0-dev 당시의 설계·검증 기록이며 아래 본문은 당시 상태를 보존합니다. 현재 사용 절차는 [README](../README.md), C# 빌드·패키징 절차는 [BUILD](BUILD.md)를 따릅니다.

This is a historical 1.0.0-dev design and verification record; the original text is preserved below. Follow the [README](../README.md) for current usage and [BUILD](BUILD.md) for C# builds and packaging.

2026-10-02 UTC. 개발 소스와 새 후보는 1.0.0-dev입니다. 새 기본 ON/저장된 OFF 보존 및 최종 실패 자동 OFF 정책을 coordinator·helper·설정·재활성화 UI에 통합했습니다. 운영 인증·trust·helper fingerprint 미설정으로 실제 자동 실행은 차단됩니다. 최신 정책과 검증은 [실패 정책](update-failure-policy.md) 및 [검증 보고](../artifacts/automatic-update-development/helper-ui-recovery-policy-20261002-1203/verification.md)를 따릅니다. 아래 이전 체크포인트는 당시의 기록입니다.

## 요청 범위와 현재 근거

사용자가 자동 다운로드와 자동 적용 구현을 요청했습니다. 자동 다운로드와 종료 후 적용은 별도 설정으로 제공하고 기본 OFF로 시작합니다. 설정은 사용자의 동의를 표현하며 자동 적용이 켜져 있어도 실행 중인 게임·런처를 종료하거나 파일을 교체하지 않습니다.

현재 지침의 unattended updater 금지는 이번 명시적 사용자 요청 범위에서 대체됩니다. 기존 GA ZIP, 설치된 DLL, 기본 바닐라 실행, 개인 설정과 사용자 데이터는 계속 보존합니다. 인증·서명용 실제 자격증명 생성, 영구 접근·저장, GitHub 게시·업로드·태그·public 전환은 승인 범위에 없습니다.

- CodeGraph CLI status/sync는 complete, 변경 대기 0입니다. CodeGraph MCP는 노출되지 않아 CLI로 조회했습니다.
- 프로젝트 및 등록 루트의 .agents/.codex와 memory_summary.md를 확인했으며 관련 로컬 자료는 없었습니다. executor 스킬 목록도 비어 있습니다.
- ReleaseUpdates는 net6.0 플러그인에 포함된 비동기 체크·계정/채널 캐시·취소·버전 비교 코어입니다. 실제 인증 제공자와 HTTP 클라이언트는 연결하지 않았습니다.
- tools/SteamLauncher.cs는 Windows .NET Framework 기반 창 없는 부모 실행기입니다. tools/BetterAstralParty-Steam.ps1는 실제 INT 게임을 시작하고 종료까지 기다립니다.
- tools/install.ps1의 UpdateOnly도 BepInEx.cfg, doorstop_config.ini, 설치 영수증을 변경합니다. 자동 적용에서 이 일반 설치기를 호출하면 설정 보존·무종료 요구를 그대로 만족하지 않습니다.
- tools/install-backup.ps1는 대상 경로·reparse point 검사, 쓰기·공간 검사, 파일 백업, 중단된 작업 journal 및 복구를 제공합니다. 자동 업데이트에서는 그 정책을 재사용하되 별도 제한된 트랜잭션으로 구현합니다. 현재 복구 journal의 절대 백업 경로를 그대로 신뢰하는 구현은 자동 경로에 복사하지 않습니다.
- 영수증 BetterAstralParty.install.json은 schema=1, owner=kr.betterastralparty.mod, loaderBefore와 path/SHA-256 목록을 보관합니다.
- 기존 전체 GA ZIP은 공용 로더도 포함합니다. 자동 업데이트에는 모드 전용 payload를 사용하며 전체 GA ZIP을 곧바로 자동 설치하지 않습니다.

## 구조와 실행 순서

1. 기존 알림 코어가 채널에 맞는 새 릴리스를 선택합니다. 자동 다운로드 OFF면 기존 알림과 수동 다운로드 페이지 동작을 유지합니다.
2. 자동 다운로드 ON이고 인증·신뢰 공개키·호환 helper가 준비됐으면 정확한 저장소/릴리스의 descriptor와 detached signature를 제한된 크기로 받습니다.
3. 설치 시 고정한 신뢰 공개키로 descriptor의 서명을 검증하고 저장소·릴리스 ID·asset ID·버전·채널·파일 정책을 확인합니다.
4. 서명에 묶인 ZIP asset을 별도 .part 파일로 제한된 크기/시간 안에 받습니다. 전체 ZIP 해시·길이·각 파일 해시와 구조를 검증한 뒤 Ready 상태로 승격합니다.
5. 종료 후 적용 ON이면 설치된 신뢰 helper를 숨김 실행합니다. 명령행에는 제한된 로컬 plan 경로만 전달하며 토큰·URL query·임의 명령은 전달하지 않습니다. helper는 네트워크를 사용하지 않습니다.
6. helper가 현재 게임의 PID/시작 시간/정확한 실행 경로를 확인한 process handle로 자연 종료를 기다립니다. 재시작된 동일 설치본의 게임이나 런처가 있으면 계속 대기하거나 보류합니다.
7. 모든 검증을 다시 한 뒤 소유 파일을 백업하고 트랜잭션으로 적용합니다. 결과·복구 상태를 남기며 게임은 자동 실행하지 않습니다.
8. 다음 실행은 미완료 트랜잭션을 먼저 확인합니다. 복구가 필요한 상태에서 모드 로드를 허용하지 않으며 기존 백업을 이용해 복구하거나 오류를 표시합니다.

상태: NotConfigured / Idle / Downloading / Verifying / Ready / WaitingForExit / Applying / Applied / Cancelled / Failed / RecoveryRequired. UI 갱신은 기존 main-thread polling과 예외 격리를 유지합니다. 작업자는 Unity/FairyGUI를 호출하지 않습니다.

설정: 자동 다운로드(Auto download), 종료 후 적용(Apply after exit), 취소(Cancel), 준비 파일 폐기(Discard). 한국어·영어가 같은 동작을 나타냅니다. 적용 OFF면 검증된 파일은 Ready로 남으며 임의 설치가 시작되지 않습니다. 인증 또는 공개키가 미설정이면 네트워크·helper 실행 모두 0입니다.

## 신뢰와 서명 프로토콜

SHA-256은 바이트 무결성 확인에만 사용합니다. 출처 인증은 코드/최초 신뢰 설치에 고정한 배포자의 공개키와 RSA-PSS/SHA-256 서명으로 수행합니다. 기본 배포 키 목록은 비어 있게 두며 실제 키 설정 전에는 동작하지 않습니다. 다운로드된 공개키, manifest의 임의 키, 같은 릴리스의 .sha256만으로 신뢰를 추가하지 않습니다. PSS salt 길이는 SHA-256 해시 길이인 32바이트로 고정합니다. [RFC 8017 §8.1](https://www.rfc-editor.org/rfc/rfc8017.html#section-8.1)

교차 런타임에서 동일하게 검증할 수 있는 고정 순서 UTF-8/LF descriptor를 사용합니다. 필드는 중복·누락·미지 필드·잘못된 형식이면 거부합니다. JSON의 중복 키나 직렬화 차이에 의존하지 않습니다. 서명 입력은 아래 descriptor 전체 원본 바이트이며 첫 줄이 용도 구분자입니다. 아래 예시는 형식 예시이고 유효 서명 패키지가 아닙니다.

~~~text
BetterAstralParty.UpdateDescriptor/v1
repository=TSM701/BetterAstralParty
version=1.2.0
channel=Stable
platform=windows-x64-int
release-id=123
asset-id=456
asset-name=BetterAstralParty-1.2.0-update.zip
zip-bytes=12345
zip-sha256=<64 uppercase hex characters>
file-count=1
file=BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll<TAB>12345<TAB><64 uppercase hex characters>
~~~

file 행은 정확한 허용 경로를 ordinal 순서로 나열합니다. 파일 이름은 허용 ASCII 문자만 사용하고 URL·명령·인코딩된 경로를 받지 않습니다. 숫자 범위·길이·버전은 엄격하게 검사합니다. signature 파일은 제한된 key ID와 RSA-PSS 서명만 담으며 key ID는 고정된 허용 키 선택에만 사용합니다.

버전은 현재 설치 버전보다 엄격하게 새로워야 합니다. 기존 SemVer 정책(선택적 v 하나, 숫자 prerelease 비교, build metadata 무시, stable/beta 구분)을 유지합니다. helper에는 .NET Standard 외부 종속성을 추가하지 않고 작은 공통 파서로 구현한 뒤 기존 SemanticVersioning 2.0.2 결과와 교차 검증합니다. 저장소/릴리스/tag/선택된 채널/asset 이름·ID와 descriptor가 어긋나거나 구버전 서명을 재사용하면 거부합니다.

실제 서명 키 생성·보관·배포자 등록은 부모/사용자 결정 후 별도 승인 단계입니다. 구현 검증에는 테스트 프로세스에서만 메모리에 생성한 fixture 키를 사용하고 비밀 부분을 저장하거나 제품에 넣지 않습니다. production helper에는 fixture 신뢰 키나 검증 우회 플래그를 넣지 않습니다. 공개키 교체·폐기는 기존 신뢰 설치를 통한 별도 전환 정책이 필요하며 이 단계에서 임의 자동 키 등록은 구현하지 않습니다.

## 다운로드 경계

GitHub asset 다운로드는 정확한 API endpoint와 asset ID로 요청합니다. Accept: application/octet-stream 요청은 200 본문 또는 302를 받을 수 있으며, private asset에는 Contents read 권한이 필요합니다. [GitHub release asset API](https://docs.github.com/en/rest/releases/assets#get-a-release-asset)

- 클라이언트 생성 시 AllowAutoRedirect=false를 고정합니다. HTTPS, 기본 포트, exact api.github.com/정확한 owner/repo/asset endpoint만 인증 헤더를 받습니다.
- 302는 수동 검증하며 1회만 따릅니다. CDN의 exact host release-assets.githubusercontent.com과 허용 경로만 대상으로 하고 Authorization·cookie·API 전용 헤더는 전달하지 않습니다. 다른 host, userinfo, fragment, 포트, HTTP 또는 추가 redirect는 거부합니다.
- CDN의 query는 메모리에서만 사용하고 로그·journal·명령행에는 남기지 않습니다. 이 host 정책은 현재 제안이며 실제 private API의 redirect 경로는 아직 확인하지 않았습니다. 다른 경로를 만나면 접근 범위를 자동 확대하지 않고 지원되지 않는 대상으로 중단합니다.
- descriptor <=64 KiB, signature <=1 KiB, ZIP <=64 MiB, 전체 다운로드 <=3분, 읽기 정체 <=15초로 제한합니다. Content-Length만 믿지 않고 실제 스트림 바이트를 센 뒤 signed length와 일치시킵니다.
- 계정·채널·릴리스 변경은 generation을 바꿔 진행 중 작업을 취소하고 늦은 결과를 폐기합니다. single flight와 기존 cooldown/Retry-After 정책을 공유합니다.
- 취소·단절·시간 초과 시 .part는 Ready가 될 수 없습니다. 첫 구현은 부분 이어받기를 하지 않고 재시도 시 처음부터 받습니다. 재시작 후 Ready도 서명·길이·해시를 다시 검증합니다.
- staging은 현재 사용자 전용 로컬 캐시 아래 설치 경로 해시별로 나누고 디렉터리 ACL·링크·소유 파일 정책을 검증합니다. private 응답 내용, 인증 토큰과 presigned query는 진단 로그에 쓰지 않습니다.
- 인증 컨텍스트가 바뀌거나 로그아웃하면 예약을 취소하고 해당 계정의 준비 파일을 정리합니다. 계정 범위가 맞지 않는 캐시를 자동 적용하지 않습니다.

## ZIP·파일 소유권 정책

v1 payload는 아래 목록의 파일만 담을 수 있습니다. 필수 플러그인 외 파일은 해당 설치 영수증에 같은 경로가 이미 존재하고 현재 해시가 일치하는 경우에만 교체할 수 있습니다.

| 허용 대상 | 설치 경로 |
| --- | --- |
| 필수 모드 DLL | BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll |
| 모드 실행기 | BetterAstralParty-Launcher.exe |
| 모드 호환성 기준 | BetterAstralParty.compatibility.json |
| 소유 실행 스크립트 | BetterAstralParty-Mod.cmd, AstralParty-Vanilla.cmd, BetterAstralParty-Steam.ps1, check-compatibility.ps1, steam-shortcut.ps1 |

설정·세이브·로그·번역 패치, 다른 모드, BepInEx 공용 DLL/runtime, winhttp.dll, BepInEx.cfg와 doorstop_config.ini는 payload/적용 대상에서 제외합니다. 자동 helper 자체는 실행 중 교체하지 않습니다. 첫 신뢰 helper의 설치 및 helper 신뢰 정책 교체는 전체 패키지의 명시적 설치 경로에서 수행하며 현재 설치에는 손대지 않습니다.

ZIP의 모든 항목을 추출 전에 검사하고 signed file set과 정확히 같아야 합니다. <=32개 항목, 개별 압축 해제 <=64 MiB, 전체 <=128 MiB, 압축 비율 <=100:1이며 실제 읽은 양도 제한합니다. 절대/UNC/device 경로, . 및 .., backslash, ADS의 :, 빈 이름, trailing dot/space, 예약 장치명, 중복·대소문자 충돌, symlink/reparse·암호화 항목과 예상하지 않은 디렉터리를 거부합니다. 파일 길이와 SHA-256이 서명된 값과 일치해야 합니다. 일반 ExtractToDirectory를 바로 호출하지 않습니다.

대상 설치는 helper의 설치 위치에서 직접 결정합니다. plan/descriptor의 임의 GameRoot를 따르지 않습니다. root부터 대상/캐시/백업까지 reparse point와 링크를 확인하며 hard link도 거부합니다. 디렉터리 handle과 실제 file ID/volume을 유지·재확인해 경로 검사와 쓰기 사이의 변경을 막습니다. 소유 파일이 사용자에 의해 수정됐거나 영수증/파일 식별이 불확실하면 덮어쓰지 않습니다.

## 종료 확인·트랜잭션·복구

확인한 게임의 PID, 시작 시간, 실행 이미지 경로 및 Windows session을 바인딩합니다. PID 재사용, 경로 접근 거부 또는 다른 설치의 같은 프로세스 이름을 동일 게임 종료로 간주하지 않습니다. 실행 중에는 대기만 하며 프로세스를 종료하지 않습니다.

종료 확인 이후 설치별 lock을 얻고 정확한 INT/CN 게임 및 모드 런처 경로를 다시 검사합니다. 적용 중 게임 재실행을 막기 위해 게임 EXE의 배타적 읽기 handle을 유지하고 각 모드 대상도 배타적 file lease로 잠급니다. 이 Windows 제어가 새 CreateProcess를 실제로 막는지는 inert EXE fixture로 검증할 필수 항목이며 아직 통과로 주장하지 않습니다. 검증을 만족하지 못하면 적용 기능을 활성화하지 않고 launcher gating과 별도 OS 경계를 보완합니다.

기존 버전 installer/uninstaller와의 동시 변경도 파일 잠금·원본 해시 재검사로 차단합니다. 이후 개발 버전의 installer/uninstaller/launcher에는 같은 설치 lock과 recovery 상태 검사도 추가합니다. Steam 자체는 종료하지 않습니다.

모든 서명·경로·영수증·현재 해시·공간·쓰기 검사를 완료한 뒤에만 백업합니다. 백업 파일은 좁은 관리 디렉터리의 index 기반 이름이며 임의 절대 경로를 journal에서 받지 않습니다. 각 백업의 길이·해시를 확인하고 write-through journal을 먼저 보관합니다.

모드 파일은 잠금을 유지하는 열린 handle로 교체하고 flush합니다. 다중 파일의 일괄 원자성을 주장하지 않습니다. Prepared → BackedUp → Applying → Committing → Applied 상태와 파일별 이전/신규 해시를 기록해 중단을 복구합니다. journal 갱신 자체는 같은 볼륨의 임시 파일과 원자적 rename/replace를 사용합니다. 설치 영수증은 같은 트랜잭션에서 대상 해시만 갱신하며 owner/schema/loaderBefore와 다른 파일 기록을 유지합니다.

실패·취소 시 변경된 항목만 역순으로 복구합니다. 취소는 복구를 중단시키지 않습니다. 강제 종료·재부팅 후에는 원본/신규 해시로 완료된 쓰기를 판별합니다. 현재 파일이 둘 중 어느 해시에도 맞지 않으면 사용자 편집을 덮어쓰지 않고 RecoveryRequired로 보류합니다. 손상된 journal/백업, 재시작된 게임, 부분 권한 실패에서도 성공으로 기록하지 않습니다. 완료·복구된 백업은 보존합니다.

모드 launcher는 미완료 트랜잭션에서 게임을 시작하지 않고 helper 복구를 먼저 요청합니다. 자동 네트워크 인증이 없어도 로컬 백업 복구는 가능해야 합니다. 기본 바닐라 및 Steam 설정은 변경하지 않습니다.

## 빌드·파일 계획

공통 신뢰/파일/트랜잭션 코드와 독립 helper를 분리합니다. helper는 Windows .NET Framework의 BCL만 사용하여 게임의 BepInEx runtime을 실행 환경으로 재사용하지 않습니다. 현재 컴퓨터에서 PS 5.1/CLR v4의 RSACng.VerifyData/PSS 및 ZipArchive 타입·메서드는 확인했지만 서명·추출·적용 실행은 아직 검증하지 않았습니다.

현 개발 SDK 6.0.428/8.0.425 및 Roslyn csc.dll은 있습니다. loader의 SemanticVersioning.dll은 .NETStandard 2.0이며 확인한 Framework Facades 경로에는 netstandard.dll이 없었습니다. helper에 해당 종속성을 추가하지 않는 이유입니다.

예정 파일:
- updater/UpdateTrust.cs: 제한 descriptor, 공개키 pin/PSS, 공통 버전·파일 정책.
- updater/UpdatePackage.cs, updater/UpdateTransaction.cs: 검증·소유 파일 lease·백업·journal·복구.
- updater/Program.cs 및 tools/build-updater.ps1: credential-free offline helper, 별도 개발 출력.
- src/UpdateDownload.cs: 제한 transport, 취소·중복·인증 generation, 상태 machine.
- ReleaseUpdates/Plugin/ModUi/ModText/MenuLayout: asset 연결, 설정과 KR/EN UI.
- self-test/AutomaticUpdateCheck.cs 및 Windows fixture 도구: 생산 로직·실제 helper 독립 검증.
- installer/uninstaller/launcher: helper 등록·공유 lock/recovery 진입점. 기존 설치·GA 산출물은 변경하지 않음.

버전은 현재 미배포 1.0.0-dev 작업을 확장합니다. 빌드·fixture는 artifacts/automatic-update-development 아래 분리합니다. 기존 build.ps1/build-launcher.ps1의 GA 출력 덮어쓰기 경로는 사용하지 않습니다. 배포 README는 일반 안내를 유지하고 기술·검증 기록은 개발일지에 기록합니다.

## 구현 순서와 검증 관문

1. 신뢰 descriptor·서명·ZIP·경로·소유권 공통 코어. fixture 서명 정상/오류/미설정, 잘못된 key ID, descriptor 변조·누락·중복·구버전, ZIP traversal/ADS/장치명/link/중복/폭탄/손상/내용 해시를 검증.
2. 비동기 다운로드·재시작 상태. fake transport의 200/302, 인증 비전달, off-host/추가 redirect, 크기/시간/정체, 단절·취소·Retry-After·single-flight·늦은 계정 응답 및 Ready 재검증.
3. Windows offline helper·트랜잭션. 실제 inert 프로세스의 종료 전 무변경, PID 재사용·경로 식별 실패, 재실행 guard, 동시 실행, 원본 편집·hard/reparse links, 공간/잠금/쓰기 실패, 파일별 fault injection과 중단·재시작·rollback·journal 변조.
4. UI/설치 연결. 자동 다운로드와 종료 후 적용의 독립 OFF/ON, auth/key/helper 미설정, 취소, KR/EN 및 프레임 비차단, 생산 metadata 검사와 기존 회귀 검사.
5. 부모 리뷰 후 실제 private 인증·서명·릴리스 접근과 실게임 검증. 이 단계에는 별도 접근 결정과 설치 검증 계획이 필요함.

각 단계에서 실제 통과/실패/미실행과 보호 파일 해시를 보고합니다. 기존 surface 검사 기준 입력과 개발판의 동일 실패는 그대로 보존하며 검사를 약화하지 않습니다. 이전 393개 알림 검증 통과 기록을 새 자동 적용의 검증 완료로 사용하지 않습니다.

## 부모가 결정할 인증·배포 경계

| 선택지 | 접근·저장 범위 | 현재 차단 지점 |
| --- | --- | --- |
| 기존 개인 fine-grained PAT, 해당 private repo의 Contents read만, 세션 메모리 사용 | 토큰 내장·영구 저장 없음. 개인이 이미 권한이 있는 저장소만 접근 | 저장소 접근 확정과 안전한 개인 입력 경로. 새 PAT 생성은 별도 승인 |
| GitHub App의 사용자 device flow | 등록된 공개 client ID 및 사용자별 동의. repo read 범위를 제한 | App 등록·device flow 활성화 승인. refresh token/영구 저장은 추가 승인 |
| 인증 제공자 미설정 유지 | 실제 네트워크 0, mock/fixture만 검증 | 실제 private 자동 배포 사용은 불가 |

GitHub App device flow에는 App 설정에서 활성화와 등록된 client ID가 필요합니다. shared App private key/client secret을 모드에 넣지 않습니다. [GitHub App user-token device flow](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app#using-the-device-flow-to-generate-a-user-access-token)

현재 모드 인증은 미설정입니다. 부모가 TSM701/BetterAstralParty 비공개 빈 저장소 생성과 id 1401226962, visibility private, size 0 확인을 전달했습니다. 이는 모드 인증·운영 서명 키 준비 완료를 뜻하지 않습니다. 소스·태그·릴리스를 업로드하지 않습니다. 인증과 무관한 1~4단계는 구현·빌드·격리 검증할 수 있으며 실제 signed update asset 제공과 키 운영은 아직 준비되지 않았습니다.

## 구현 상태 — 신뢰 코어 리뷰 단계

updater/UpdateTrust.cs, UpdateVersion.cs, UpdatePackage.cs를 추가하고 net6.0 플러그인에 링크했습니다. 기존 게임·UI·인증 로직은 수정하지 않았습니다. updater-tests는 독립 net8.0 실행 프로젝트이며 같은 생산 소스를 Framework용 fixture에도 직접 컴파일했습니다.

서명 대상은 고정 순서 ASCII/UTF-8 LF descriptor 전체입니다. 저장소 slug, 채널, 정확한 release tag, windows-x64-int 플랫폼, release/asset ID, asset 이름, ZIP 길이·SHA-256, 정렬된 파일 목록 및 각 길이·SHA-256을 결합합니다. pin은 RSA 3072/4096비트, exponent 65537의 공개 부분만 받아 복사하며 PSS/SHA-256 서명 검증 후 파싱합니다. 운영 factory의 키 목록은 비어 있어 NotConfigured로 거부합니다. 테스트 키는 fixture 프로세스의 메모리에만 생성하고 운영 DLL에 키·fixture 타입·서명 생성 함수는 포함하지 않습니다.

현재 버전과 호출자가 제공한 highest-applied 버전보다 엄격하게 새로워야 합니다. 저장·실제 설치 버전 재확인은 후속 적용/복구 단계입니다. 21개 버전의 441쌍을 기존 알림 SemVer 라이브러리와 비교한 882개 assertion이 일치했습니다.

ZIP 길이·전체 해시, 중앙/로컬 header, 파일 set, CRC32 및 실제 해제 길이·SHA-256을 검사합니다. v1은 single-disk ZIP32의 stored/deflate 및 명시된 ZIP32 data descriptor만 허용하며 ZIP64, encryption, extra/comment, SFX/preamble, 숨김/중첩 local header를 거부합니다. exact 모드 소유 경로 allowlist와 64 MiB ZIP, 개별 64 MiB/전체 128 MiB 해제, 100:1 압축 비율 상한을 적용합니다. traversal/ADS/장치명/case alias, symlink/reparse/directory 속성, 중복 항목을 거부합니다. private byte snapshot을 검증하며 추출·실행·파일 쓰기는 하지 않습니다.

최종 결과: .NET 8 fixture 1,029개 assertion, Framework fixture 147개 공통 assertion 통과. 플러그인·net8 fixture·Framework library/fixture 최종 빌드 경고/오류 0. 기존 전체 자체 검사도 별도 출력에서 빌드·실행해 알림 393개 및 전투/입력/기하 등 검사가 통과했습니다. 제품 DLL의 운영 키 공백, fixture 미포함, 코어의 서명 생성·추출·실행·쓰기 부재와 인증 제공자 0을 메타데이터로 확인했습니다. 이는 실게임 시작·렌더링 검증이 아닙니다.

초기 결과도 보존합니다. Framework 응답 파일의 /noconfig 위치에서 CS2023 경고가 발생해 명령행으로 옮겼습니다. streaming fixture는 Framework ZIP 작성기가 non-seekable stream의 Position을 조회해 실패했고 테스트 대역의 쓰기 위치 getter를 제공한 뒤 통과했습니다. 생산 코어 검사를 약화하지 않았습니다.

다운로드 transport/HTTPS 인증·redirect, staging/extraction, 실제 filesystem reparse/hard-link/TOCTOU 및 receipt 검사, high-water 저장/설치 버전 재확인, 프로세스 종료·재실행 guard, 적용·backup/journal/rollback, UI/launcher 연결은 미구현·미실행입니다. 취소는 미리 취소된 token 진입점만 실행했으며 진행 중 네트워크·파일 취소는 후속 단계입니다. 이전 수동 surface 검사 실패 기록은 보존하고 이번에는 재실행하지 않았습니다.

결과·diff·보존 근거: artifacts/automatic-update-development/trust-core-20261002-061201/verification.md, evidence.json, changes.diff, product-metadata.json. 일반 README, 기존 GA/안내 갱신 ZIP·체크섬 및 설치 파일은 보존합니다.


## 2026-10-02 자동 업데이트 다운로드 코어 — 다운로드 단계 완료

부모의 단계 분리 허용에 따라 제한 HTTPS 다운로드와 검증 바이트 보관 경계를 먼저 완료했습니다. 파일 staging·적용 helper·UI/런처 연결은 다음 단계입니다. 사용자 합의에 따라 자동화/격리 테스트와 도구로 가능한 실환경 검증을 먼저 수행하며, 실제 설치 변경 승인은 아직 없습니다.

src/UpdateDownload.cs는 TSM701/BetterAstralParty의 자산 ID API만 요청합니다. 생산 HttpClientHandler는 자동 리디렉션·쿠키·기본 인증·자동 압축 해제를 끕니다. 인증 제공자는 메모리의 비밀 아닌 세션 ID와 API bearer 헤더만 제공하는 계약이며 아직 구현하지 않았습니다. 임시 probe의 승인 결과에서 Authorization만 복사하고 URI/메서드 변경은 거부합니다. 외부 CDN에는 인증·쿠키·provider 헤더·API 버전 헤더를 전달하지 않습니다. 현재 운영 trust factory는 빈 키 목록이므로 네트워크 요청 없이 NotConfigured입니다.

GitHub 공식 API의 200/302 규칙을 확인했습니다. 단 한 번의 HTTPS 리디렉션을 정확한 release-assets.githubusercontent.com 호스트와 /github-production-release-asset/1401226962/<GUID> 경로에만 허용합니다. query는 메모리에만 두고 로그에 남기지 않습니다. 저장소 ID는 부모가 커넥터로 확인한 값이며 이 세션은 실제 private API/CDN 경로를 검증하지 않았습니다. 이 경로 정책은 GitHub의 영구 API 보장을 가정하지 않으며 예상과 다르면 다운로드를 거부합니다. 다른 호스트·HTTP·userinfo·포트·fragment·다른 저장소·경로 별칭·추가 redirect를 거부합니다.

descriptor <=64 KiB, 서명 envelope <=1 KiB, ZIP <=64 MiB와 signed ZIP의 정확한 길이를 헤더와 실제 수신 바이트에서 검사합니다. 총 3분, 헤더/stream 무진행 15초 제한이며 cancellation을 무시하는 작업도 await를 중단하고 늦은 응답/stream을 폐기합니다. 네트워크 예외 본문/URL/자격증명을 표시하거나 기록하지 않습니다. RSA-PSS descriptor 검증을 통과해야 ZIP을 요청합니다. HTTP 401/403/404/429, Retry-After와 rate reset, 크기·서명·ZIP·시간 초과·사용자 취소를 분리합니다. 전송 자체의 중단은 Failed이며 사용자 취소와 구별합니다.

Start/Poll은 메인 스레드에서 대기하지 않으며 worker 한 개만 시작합니다. 동일 Ready 결과를 재사용하고 공유 60초 cooldown 및 서버 backoff를 계정/채널 변경 후에도 유지합니다. 취소·로그아웃·계정/채널/현재 버전/release/asset 변경 시 이전 비공개 결과와 worker를 폐기합니다. 취소 후 늦은 서버 제한 응답도 재시도 제한에 반영하되 이전 payload를 되살리지 않습니다.

updater/UpdatePackage.cs의 VerifiedUpdatePayload는 검증한 ZIP의 private snapshot을 소유하고 쓰기/배열 노출이 불가능한 read stream만 반환합니다. 호출자 원본 변조는 영향을 주지 않습니다. DownloadedUpdate는 원본 descriptor/서명도 private clone으로 보관합니다. 이는 메모리 바이트의 동일성 보장입니다. 실제 staged 파일의 TOCTOU·reparse/hard-link·file ID·exclusive lease 및 적용 시 재검증은 아직 구현/검증하지 않았습니다.

최종 .NET 8과 실제 로컬 .NET 6 런타임에서 각각 다운로드 105개·신뢰 1,029개 assertion이 통과했습니다. Framework 최종 공통 fixture는 147개 assertion 통과입니다. 플러그인·fixture·Framework 컴파일 모두 경고/오류 0입니다. 제품 메타데이터에서 안전한 HTTP 설정 4개, 운영 trust 빈 목록, fixture/placeholder 부재, 인증 제공자 0, 업데이트 영역의 signing/extraction/process/file-write 호출 부재를 확인했습니다. 실제 GitHub 요청·게임 실행·설치 검증은 아닙니다.

초기 실패 기록도 보존합니다. NETSDK1004는 없는 기존 restore 메타데이터를 --no-restore로 참조한 빌드 준비 실패였습니다. 이후 로컬 .deps만 지정한 복원과 독립 intermediate 폴더를 사용했습니다. 두 번의 CS0579 제품 빌드 실패는 외부 intermediate 설정으로 기존 src/obj 생성 파일이 compile 항목에 포함된 결과입니다. 첫 제외 설정의 %3B는 목록 구분자로 적용되지 않았으며, 실제 MSBuild compile 항목 조회 후 따옴표로 감싼 obj/**;bin/** 목록으로 수정했습니다. 제품 코드나 검증 조건을 완화하지 않았습니다. 최종 제품 빌드는 성공입니다. 초기 94/104개 assertion 결과와 로그를 최종 105개 결과와 함께 유지합니다.

보호 파일 267개(기존 GA/안내 갱신 ZIP·체크섬, 추출 GA, 설치 DLL/doorstop, 이전 개발 DLL, 일반 README/INSTALL)는 전후 해시가 같습니다. 기존 코드/도구 130개 중 변경은 UpdatePackage.cs, updater-tests csproj/Program.cs 세 개이며 나머지 127개는 같습니다. 기존 UI/알림/게임/런처/설치 코드는 변경하지 않았습니다. 이전 단계의 전체 self-test 통과와 bounded surface 실패는 과거 근거이며 이번 단계에서 재실행하지 않았습니다.

실제 staged ZIP 재검증·경로/receipt/고수위 영속화·게임/런처 종료 및 재시작/PID 경계·exclusive lease·journal/backup/rollback·UI/런처 연결이 남았습니다. 다음 단계도 inert 실행 파일과 임시 설치 경로에서 먼저 검증합니다. 운영 키·인증·GitHub 업로드·실제 설치 변경을 하지 않았습니다. GitHub 빈 private 저장소가 존재한다는 부모의 확인은 모드 인증과 서명 키 준비를 뜻하지 않습니다.

지금 사용자에게 설치나 플레이 검증을 요청하지 않습니다. 기능 연결과 별도 설치 승인 후 사용자에게 남길 항목은 KR/EN UI 자연스러움·실제 플레이 감각·Steam/다른 모드 호환성입니다. 다운로드·파일 적용 안전성은 우리가 먼저 도구/격리 테스트로 확인합니다.

근거: artifacts/automatic-update-development/download-20261002-065912/verification.md, evidence.json, changes.diff, product-metadata.json. 일반 README에 개발 기록을 추가하지 않았습니다.


## 2026-10-02 적용/복구 선행 경계 체크포인트 — Windows 파일·프로세스 primitive

updater/WindowsFileFence.cs와 WindowsProcessGuard.cs, 독립 updater-safety-tests를 작성했습니다. 이 코드는 아직 플러그인/런처/실제 설치에 연결하지 않았습니다. 전체 staging·journal·교체·복구 helper·high-water 갱신·UI 연결은 작성 전이며 다음 단계로 이어갑니다.

WindowsFileFence는 로컬 x64 NTFS만 지원하며 모든 디렉터리 ancestor 핸들, volume/file ID, reparse 속성과 case-sensitive flag를 확인/유지합니다. 경로 별칭·UNC/device·ADS·예약 장치명·trailing dot/space·case-sensitive 디렉터리는 거부합니다. 파일은 독점 핸들로 열고 hard-link 수 1과 file ID/최종 경로를 재확인합니다. 파일 생성·열기는 NtCreateFile의 고정 부모 디렉터리 핸들, 단일 leaf 이름, OBJ_DONT_REPARSE 및 FILE_OPEN_REPARSE_POINT를 사용합니다. 생성은 FILE_CREATE이며 기존 파일을 덮어쓰지 않습니다. rename도 NtSetInformationFile의 고정 부모 핸들과 ReplaceIfExists=false를 사용합니다. 다른 볼륨 이동은 거부합니다. 기존 파일·디렉터리를 검사한 뒤 핸들을 닫는 방식으로 적용하지 않습니다.

WindowsProcessGuard는 정확한 실행 경로와 PID/생성 시각/Windows session을 묶은 process handle을 유지하고 자연 종료만 조회합니다. snapshot에서 같은 이름의 프로세스를 발견하면 경로를 확인하고, 확인 불가능한 일치 이름 프로세스는 안전 정지합니다. 같은 이름의 다른 설치 경로는 구분합니다. 파일 lease 획득 전/후 재검사로 scan/open 경합을 줄이고 game/launcher EXE의 독점 lease를 유지합니다. 생산 코드에 강제 종료나 process start 호출은 없습니다. 실제 PID를 강제로 재사용시키지는 않았으며 생성 시각/이미지/session 불일치 거부를 검증했습니다.

최종 .NET 8·실제 로컬 .NET 6·Framework에서 각각 65개 assertion 통과, 경고/오류 0입니다. 가짜 실행 파일만 최대 5초 후 자연 종료하도록 만들었습니다. 독점 EXE lease 중 게임 재실행 10회 및 런처 실행이 Windows에서 차단되고 해제 후 정상 실행됩니다. exact live game/launcher, 잘못된 생성 시각/session/이미지, 다른 설치의 같은 이름 프로세스, 독점 파일 쓰기/rename 거부, pinned directory rename 거부, hard link/junction/broken junction 거부, 동일 볼륨 fence 간 이동의 정확한 바이트와 ID를 확인했습니다.

중요한 실제 실패와 수정도 유지합니다. 최초 Win32 SetFileInformationByHandle relative-root rename이 오류 87로 두 번 실패해, 공식 문서의 user-mode NtSetInformationFile로 수정했습니다. 디렉터리 share-write 제한만으로 재파싱 변경용 write 핸들이 차단된다는 확장 assertion도 실패했습니다. 이 가정을 제거하고 고정 디렉터리 핸들 기반 NtCreateFile로 생성/열기를 보강했습니다. 검사 직후 실제 in-place junction을 설정하는 create와 rename 경합을 주입했고 외부 목표 파일이 생성/이동되지 않으며 안전 정지했습니다. 초기 53/63개 성공 기록과 실패 로그를 모두 보존합니다. 조건을 느슨하게 바꿔 통과시키지 않았습니다.

기존 설치/GA/README/다운로드 개발 DLL 등 보호 파일 269개와 기존 코드/도구 132개는 전후 해시가 동일합니다. 이번 체크포인트에서 기존 소스는 변경하지 않고 다섯 개 새 소스/프로젝트를 추가했습니다. 일반 README/INSTALL은 그대로입니다. 실제 게임·강제 종료·운영 키/인증·GitHub 업로드·실제 설치 변경은 실행하지 않았습니다.

다음은 locked ZIP의 helper 재검증과 index 파일 extraction, 설치 receipt/실제 DLL metadata·고수위 확인, 전용 operation lock, 같은 볼륨 작업 디렉터리, 파일별 rename 및 상태 기록의 실패 주입, commit/rollback/restart 복구입니다. 모호한 file ID/해시/상태에서는 자동 덮어쓰기하지 않습니다. UI/런처/installer gating은 이 코어 검증 뒤 연결합니다. 현재 사용자 확인 요청은 없습니다.

근거: artifacts/automatic-update-development/apply-boundary-20261002-074417/boundary-checkpoint.md, boundary-evidence.json, boundary-changes.diff. Microsoft 문서: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntcreatefile , https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntsetinformationfile , https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information . 문서의 계약은 실제 fixture 결과와 구분합니다.


## 2026-10-02 비공개 staging·잠금 재검증 체크포인트

updater/UpdateStaging.cs와 WindowsPrivateSecurity.cs를 추가하고 WindowsFileFence에 신규 private 디렉터리 생성·ACL 조회·디렉터리 ID 조회를 연결했습니다. 신뢰 루트가 비어 있거나 descriptor 서명/ZIP 검증이 실패하면 디스크 쓰기 전에 중단합니다. 검증된 descriptor·signature·key ID·ZIP만 무작위 bap-stage 디렉터리에 저장하며 CREATE 전용 핸들, 실제 바이트 한도, 취소, flush를 적용합니다. 취소된 부분 staging은 private 상태로 남지만 Ready 후보로 반환하지 않습니다. 자동 삭제는 구현하지 않았습니다.

신규 staging 디렉터리는 현재 사용자 소유, 상속 차단 DACL, 현재 사용자와 SYSTEM만 상속 가능한 full access를 갖도록 생성합니다. 기존 디렉터리 권한을 고치는 코드는 없습니다. 권한 설정과 고의 ACL 변조 테스트는 이번에 새로 만든 fixture 디렉터리에만 실행했습니다. 기존 설치·Discord·오디오·게임/Steam 설정을 변경하지 않았습니다. 부모가 요청한 오디오 확인 중에도 읽기 전용 보존 검증만 수행했습니다.

VerifyLocked는 정확한 디렉터리 ID와 private ACL을 확인하고 고정된 네 파일을 share=0으로 열어 유지합니다. 잠긴 실제 바이트로 RSA-PSS 서명, context, descriptor hash, ZIP 구조와 payload를 다시 검증하며 반환 직전 재확인합니다. 재검증 실패·취소는 모든 파일 핸들을 해제합니다. 잠금 유지 중 파일 쓰기와 두 번째 verifier 접근, ACL 변조, 디렉터리 교체, hard link, 네 파일 변조·누락·한도 초과, 채널/최고 적용 버전 변경을 거부했습니다. Dispose 후 Recheck도 거부합니다. 이 구현의 StagedUpdate는 현재 프로세스 내부 후보이며 프로세스 간 지속 plan/receipt는 아직 없습니다.

최신 .NET 8·6·Framework 각각 staging 39개 assertion, 공통 경계 회귀 65개 assertion을 통과했습니다. SDK 빌드 경고/오류 0, Framework /warnaserror 빌드 진단 0입니다. 초기 staging 35개 테스트 결과도 보존합니다. 첫 최종 빌드 wrapper는 출력 부모 디렉터리 생성 누락으로 테스트 실행 전에 실패했고 수정했습니다. 첫 경계 회귀 fixture는 긴 중첩 출력 경로가 기존 240자 제한에 걸려 실패했습니다. 해당 로그를 보존하고 더 짧은 새 격리 경로로 검증했으며 경로 제한을 완화하지 않았습니다.

공통 Framework 라이브러리 메타데이터에서 운영 신뢰 루트가 비어 있고 fixture 키/타입, 기존 ACL setter, 서명 생성, HTTP, process start/kill, 경로 기반 File 쓰기/이동/삭제 호출이 없음을 확인했습니다. 라이브러리에는 기존 handle-relative rename primitive가 포함되어 있지만 staging 진입점은 설치·압축 해제·교체를 실행하지 않습니다. UI/플러그인/런처 연결이나 운영 helper 실행 파일은 아직 없습니다.

보호 파일 270개와 기존 코드/도구 137개 중 변경 대상 두 파일을 제외한 135개가 시작 해시와 동일합니다. 일반 README/INSTALL, GA ZIP/추출 파일, 기존 개발 DLL, 실제 설치 DLL/doorstop이 보존되었습니다. 변경은 신규 네 소스/프로젝트와 기존 fence·회귀 csproj, 개발 문서 두 개에 한정합니다. CodeGraph sync도 성공했습니다.

다음 단계는 실제 설치 DLL의 비실행 metadata 및 receipt에서 context를 독립적으로 도출하고 재검증하는 일, 프로세스 간 candidate 전달, operation lock, 같은 볼륨 private 작업 디렉터리의 검증된 extraction, journal/backup/rename, commit·rollback·재시작 복구, receipt·최고 적용 버전의 마지막 commit입니다. 현재 VerifyLocked는 호출자가 제공한 context를 검증하며 설치 metadata/receipt를 직접 읽지 않습니다. 이후 helper/UI/런처 gating을 연결합니다. 실제 설치·실게임·운영 인증·GitHub 배포는 미실행입니다. 기존 bounded surface 비교 실패 기록도 그대로 남아 있으므로 모든 프로젝트 검사가 통과했다고 주장하지 않습니다.

근거: artifacts/automatic-update-development/staging-20261002-084826/verification.md, evidence.json, changes.diff, product-metadata.json, review-snapshot/. 최종 staging 로그는 f/, 경계 회귀 로그는 artifacts/automatic-update-development/apply-boundary-r0914/에 있습니다.


## 2026-10-02 설치 context·교체·복구 트랜잭션 코어 체크포인트

InstalledPluginMetadata.cs, InstallReceipt.cs, UpdateTransaction.cs와 격리 TransactionTests를 추가했습니다. 기존 소스는 수정하지 않았습니다. 운영 helper/플러그인/UI/런처는 아직 연결하지 않았습니다. 실제 설치·GA·Discord/오디오·GitHub 배포·운영 인증/키 생성은 변경하지 않았습니다.

InstalledPluginMetadata는 BCL만으로 PE/CLI #~ metadata의 TypeDef·Field·Constant를 한도 내에서 읽고 BetterAstralParty.Plugin의 literal Guid/Version을 확인합니다. Assembly.Load나 설치 DLL 실행이 없습니다. 실제 GA DLL 1.0.0도 읽기 전용으로 검증했습니다. fixture DLL은 실행되면 예외를 내는 initializer를 가지며 metadata만 읽었습니다. ECMA-335 II.22/24/25 및 Microsoft PE format을 기준으로 구현했습니다.

receipt는 중복 key·잘못된 schema/owner·허용 목록 밖/중복/case-alias 경로·잘못된 SHA를 거부합니다. 자동 교체 허용 대상은 고정된 모드 소유 8개뿐입니다. 기존 파일이 존재하면 receipt 해시와 실제 독점 핸들의 해시가 일치해야 합니다. 원래 없던 소유 파일은 absent 상태로 기록하고 충돌한 외부 파일을 덮어쓰지 않습니다. plugin은 반드시 기존 metadata를 확인합니다. UI에서 제공한 current-version은 사용하지 않고 실제 설치 metadata와 최고 적용 버전으로 verification context를 다시 만듭니다.

서명된 descriptor/ZIP을 다시 검증하고 동일 볼륨의 신규 private work에 index 이름으로 8개 payload와 새 receipt/high-water를 준비합니다. ZIP 경로를 extraction 경로로 사용하지 않습니다. 새 plugin Guid/Version과 실제 준비 파일 길이·SHA도 확인합니다. immutable plan은 설치 root·work·staging directory ID, descriptor SHA, 각 고정 대상의 old/new file ID·SHA를 기록하고 flush한 후에만 기존 파일을 이동합니다. plan 자체에 서명을 생성하지 않으며 복구 시 저장된 signed descriptor/ZIP을 운영 trust로 다시 검증합니다.

operation file의 share=0 잠금으로 동시 코어 실행을 차단합니다. game EXE와 모든 교체 대상의 독점 핸들을 유지하고 game/launcher exact path 프로세스를 스캔합니다. 강제 종료/실행은 없습니다. original→index backup, prepared→target rename은 고정 parent handle과 ReplaceIfExists=false를 사용합니다. original absent rollback은 설치된 새 파일을 work로 되돌려 대상의 부재를 복원합니다. 알 수 없는 occupant·ID·SHA·journal·ACL 상태에서는 어떤 파일도 추정해서 덮어쓰거나 삭제하지 않습니다.

8개 설치 파일과 receipt 교체를 마친 뒤 immutable commit-intent marker를 flush하고 최고 적용 버전을 마지막에 교체합니다. 마지막 committed marker가 검증되어야 성공입니다. intent 이전 복구는 역순 롤백하고, intent 이후에는 앞선 9개 파일이 모두 정확한 새 상태인지 확인한 후 high-water/commit을 마칩니다. 원래 최고 버전은 백업으로 남기며 새 버전은 기존 최고/실제 설치 버전보다 커야 합니다. pending journal이 있거나 marker가 부분/불일치 상태이면 다음 Apply도 차단합니다. terminal work·backups·staging은 자동 삭제하지 않습니다.

최신 .NET 8·6·Framework 각각 1,985개 assertion 통과, 빌드 진단 0입니다. 설치/receipt/high-water/commit mutation 전·후 44개, rollback 36개, forward recovery 6개 경계 각각에 실패를 주입하고 새 fence/plan으로 복구했으며 재복구의 동일 결과를 검증했습니다. 별도 fixture 프로세스에서 rollback과 forward commit도 통과했습니다. fixture에는 RSA public modulus만 저장했고 private signing key는 메모리에서만 사용했습니다. unknown bytes, receipt/hash/소유 경로 변조, root ID/journal 경로 변조, future high-water, originally absent 파일에 외부 occupant가 들어오는 흐름, pending journal 우회, 부분 commit marker를 검증했습니다. 초기 1,783개와 중간 1,979개 결과도 그대로 보존합니다.

보호 파일 271개와 기존 코드/도구 141개 모두 시작 SHA와 동일합니다. 개발 문서 두 개는 기존 바이트를 보존하고 이 기록만 덧붙였습니다. 일반 README/INSTALL은 그대로입니다. Framework 공통 라이브러리 metadata에서 빈 운영 trust, fixture/운영 자격증명/서명 생성/기존 ACL setter/Process.Start 또는 kill 부재를 확인했습니다. 새 ACL 생성은 가짜 설치의 신규 private 디렉터리 안에서만 실행했습니다.

범위/잔여: 테스트는 Windows NTFS에서 exception 경계 및 자연 종료한 별도 프로세스 재시작을 검증했습니다. 실제 전원 차단, 저장 장치의 rename/flush 내구성, 준비 중 모든 부분 write/공간 부족은 실행하지 않았으며 손상된 plan/marker는 RecoveryRequired로 중단합니다. 현재 receipt parser는 정확한 8개 목록만 지원합니다. shared loader까지 기록한 최초 설치/legacy receipt는 거부하므로 원본 항목을 보존하는 안전한 migration 정책을 후속 검토해야 합니다. 완료 work·backup 정리/128개 한도 관리, helper CLI/후보 전달·재시작 UX, UI/런처 gating, 운영 서명/비공개 인증, 실제 설치·실게임 검증은 남았습니다. 자동 권한 변경은 실제 설치 경로에 실행하지 않았습니다. 기존 bounded surface 실패는 미해결 기록으로 유지합니다.

근거: artifacts/automatic-update-development/transaction-20261002-0924/verification.md, evidence.json, changes.diff, product-metadata.json, review-snapshot/, g/*-fixture.log. 사양: https://ecma-international.org/wp-content/uploads/ECMA-335_6th_edition_june_2012.pdf ; https://learn.microsoft.com/en-us/windows/win32/debug/pe-format .


## 2026-10-02 helper/UI/launcher 개발 체크포인트 (1.0.0-dev)

현재 미출시 1.0.0에 포함할 기능이라는 사용자 정정을 반영하여 Plugin.Version, 현재 문서 표기와 새 제품 출력을 1.0.0-dev로 맞췄습니다. 과거 1.1.0-dev 출력·보고서와 미래 버전 비교 fixture는 보존했습니다. 엄격한 SemVer에서 1.0.0-dev < 1.0.0 및 정식판 승격을 검증했습니다.

자동 다운로드와 종료 후 적용은 별도 설정이며 기본 OFF입니다. 기존 릴리스 알림에 고정 이름의 ZIP/descriptor/signature asset ID를 연결하고 한국어/영어 설정·상태·취소 UI를 추가했습니다. main-thread tick은 완료된 작업만 관찰하고 준비/큐 등록은 worker로 수행합니다. launcher는 자신 PID/생성 시각/session을 자식 환경에만 전달합니다. helper는 서명·ZIP·receipt·설치 metadata를 다시 검증하고 최초 게임/launcher의 정확한 identity와 자연 종료를 기다립니다. 강제 종료나 자동 재실행은 구현하지 않았습니다.

정확한 8파일 소유권 receipt를 충족하지 못하는 legacy/shared-loader 설치는 ManualUpgradeRequired로 중단합니다. receipt를 다시 써서 다른 모드/사용자 파일을 독점 소유로 간주하지 않습니다. root/stage identity 및 descriptor/context를 묶은 ticket, claim, cancel, result를 사용합니다. helper launch image는 anchored read handle과 hash로 검증하도록 연결했습니다. 운영 plugin은 인증 provider 0개, 빈 trust 및 빈 helper fingerprint이므로 다운로드/실행은 차단됩니다. production helper도 빈 trust를 디스크 접근 전에 확인합니다.

최종 .NET 8 및 .NET 6 helper/UI fixture는 각각 46 assertion을 통과했습니다. 기본 OFF, 독립 opt-in, 반복 클릭, 취소/자동 재큐 방지, 계정/채널 변경, 늦게 완료되는 작업, 언어 표기, fresh coordinator 재시작, legacy receipt 중단, SemVer 승격을 포함합니다. 별도의 .NET Framework helper와 inert 게임/launcher의 자연 종료를 사용하는 가짜 설치에서 1.0.0-dev→1.0.0 적용 및 취소를 검증했습니다. 8파일 hash와 unrelated mod 보존을 확인했습니다. 실제 UpdateHandoff backend의 Prepare/Queue 전체를 실행한 것은 아니며 UI fixture는 mock handoff, helper E2E는 검증 ticket을 직접 구성합니다.

최종 plugin(.NET 6), production/fixture helper(.NET Framework), launcher(.NET Framework) 빌드에 compiler diagnostic은 없습니다. 기존 mock selftest도 종료 코드 0으로 완료했고 릴리스 알림 HTTP fixture의 393 assertion이 통과했습니다. mock audio API만 사용했으며 실게임/오디오 장치를 조작하지 않았습니다. production helper는 NotConfigured/exit 2를 반환하고 설치 디렉터리를 만들지 않았습니다. 컴파일된 제품 metadata에서 1.0.0-dev, 빈 운영 trust, concrete auth provider 0개, fixture/private key/강제 프로세스 조작 부재를 확인했습니다.

중간 실패도 남깁니다. a는 fixture의 미초기화 필드 CS0649로 빌드 실패했고 명시적 null 초기화로 수정했습니다. b/c/d의 helper E2E는 FileBusy 실패였습니다. 종료 관찰 handle을 해제하는 변경만으로는 해결되지 않았고, d의 fixture 진단에서 cancel marker를 읽을 때 native sharing violation 32가 발생함을 확인했습니다. 이 경계는 보수적으로 취소로 처리하여 적용을 진행하지 않도록 수정했습니다. e는 43 assertion, 지연된 Queue 중 ApplyAfterExit OFF 취소 검증을 더한 최종 f는 46 assertion입니다. 실패 로그/출력은 삭제하지 않았습니다.

남은 작업: 실제 backend Prepare/Queue와 새 launch-image share 경로의 격리 integration 실행, 정상/누락/중복/잘못된 릴리스 asset 계약 fixture, native 인게임 화면/클리핑/입력/언어 전환, helper 결과 poll의 예외/손상 흐름, 취소 marker가 디스크에 flush되기 전 UI가 Cancelled가 되는 비동기 경계와 즉시 종료 상황, recovery CLI/UX 및 배포 packaging을 검토해야 합니다. 현재 recovery transaction 코어는 있으나 helper CLI가 복구 진입점을 제공하지 않습니다. 65 filesystem boundary 및 1,985 transaction assertion 전체는 이번 턴에 재실행하지 않았으며 이전 체크포인트 근거와 구분합니다. 기존 bounded CardUi surface 실패도 전체 통과로 표시하지 않습니다.

실게임/실설치 자동 적용, GitHub 통신/업로드/태그/릴리스/public 전환, 운영 인증/키 생성 및 지속 접근 설정은 하지 않았습니다. 운영 trust/인증/helper fingerprint 및 private 배포 대상 결정은 부모 조율과 필요한 사용자 승인 후 작업입니다. baseline의 보호 파일 272개와 이번 변경 외 기존 코드/도구 136개는 SHA-256이 일치합니다. GA ZIP/DLL/launcher, 설치 beta DLL/doorstop, README/INSTALL을 보존했습니다.

근거: artifacts/automatic-update-development/helper-ui-20261002-0954/verification.md, evidence.json, changes.diff, product-metadata.json, review-snapshot/, f/net80-fixture.log, f/net60-fixture.log, compat/selftest.log. 장시간 실행 중인 명령 없이 부모 리뷰를 위한 체크포인트로 종료합니다.


## 2026-10-02 취소/복구/backend 및 기본 ON 정책 체크포인트

현재 미출시 소스/제품은 1.0.0-dev입니다. named cancel event를 helper가 먼저 열고 ready를 기록한 뒤 Queue가 완료되도록 연결했습니다. 취소는 게임 스레드에서 kernel event만 즉시 설정하고 디스크 기록은 worker로 처리합니다. 기록 완료 전에는 Cancelling, 실패 시 CancelFailed이며 새 다운로드를 차단합니다. 채널 변경으로 확인되지 않은 취소를 우회하지 않습니다. helper가 신호 handle을 유지하여 취소 파일을 기록하기 전에 가짜 게임이 자연 종료해도 적용을 중단하는 full backend 흐름을 검증했습니다. 반복 실행은 독점 파일 경합(FilesBusy, 실행 여부를 단정하지 않음)과 이미 소비된 ticket(AlreadyClaimed)을 구분합니다.

복구 CLI --recover는 서명/ZIP/context/metadata/journal을 다시 검증하고 commit-intent 전 rollback, intent 후 forward 완료를 수행합니다. fresh startup 및 helper 관찰 실패에는 RecoveryRequired와 확인 가능한 stage 명령을 안내합니다. 원클릭 복구 실행은 구현하지 않았고 명시적 CLI를 사용합니다. 가짜 설치에서 정상 적용/취소, 복구 재실행, 손상된 plan 거부, 실행 중인 정확한 fake game 차단 및 unrelated mod/8파일 hash 보존을 검증했습니다. Beta feed가 안정판을 선택한 경우 descriptor context는 선택된 릴리스의 Stable 채널을 사용하도록 수정했습니다. 양 feed의 정상/누락/중복/음수/잘못된 타입/이름 asset 계약을 검증했습니다.

후속 사용자 요청을 반영해 Config.Bind와 fresh coordinator의 자동 다운로드/종료 후 적용 기본값을 ON으로 바꿨고 저장된 OFF는 유지합니다. UpdateFaults는 아직 연결 전 primitive입니다. immutable failure record, operation guard, 설정 저장 실패 증거, exact operation completion, 명시적 archive 및 두 writer 경쟁을 검증했습니다. 실제 BepInEx ConfigFile로 새 ON, 저장된 OFF, 실제 OFF 저장 실패 후 독립 고장 기록 유지도 확인했습니다. 최종 실패에 따른 자동 OFF, 제한 재시도, helper/coordinator의 기록·설정 저장 연결, 프로세스 liveness를 고려한 restart 분류와 재활성화 UI는 **미구현/미검증**입니다. 단순 guard 존재를 살아있는 게임/launcher 대기 실패로 취급하지 않도록 후속 설계가 필요합니다. 운영 키/인증/helper fingerprint는 미설정이므로 실제 자동 실행은 차단됩니다.

정책 변경 전 j는 .NET 8·6 각각 200 assertion PASS, 최종 l은 각각 217 PASS입니다. 추가 17개는 primitive/config 검증이며 최종 실패 OFF end-to-end가 아닙니다. 최종 plugin(.NET 6), production/fixture helper와 launcher(Framework) 빌드는 compiler diagnostic 0입니다. 이전 compat mock selftest는 exit 0 및 release HTTP 393 assertion PASS이며 default-ON 변경 후 전체 selftest를 반복하지 않았습니다. 65 filesystem/1,985 transaction 전체도 이번 턴에 반복하지 않았고 기존 CardUi bounded surface 실패를 전체 통과로 표시하지 않습니다. native 인게임 화면·입력·실제 private GitHub 통신은 미실행입니다.

중간 실패는 보존했습니다. a는 실제 backend 프로젝트 링크 누락, b는 시작 inspection을 기다리지 않은 fixture, c는 transaction이 만드는 두 번째 stage를 고려하지 않은 Single(), d는 fixture 명령 인용 오류였습니다. e는 160 PASS, f/g 183, h 184, i 198, j 200입니다. k는 fault archive 이름이 긴 fixture root에서 기존 absolute path fence의 제한을 넘었고 짧은 GUID 이름으로 수정한 l이 217 PASS입니다. 검증 엄격도를 낮추지 않았습니다.

리뷰용 로컬 ZIP은 plugin/helper/launcher와 manifest/DEVELOPMENT.md 5개만 포함하고 payload SHA를 재검증했습니다. 해시 불일치와 fixture helper 입력은 출력 생성 전에 거부됩니다. manifest는 기본 ON과 failurePolicyIntegrated=false, installableRelease=false를 명시합니다. 실제 배포/설치 후보로 간주하지 않습니다. 소스/태그/릴리스/파일 업로드/public 전환, 운영 키/인증 생성, 실제 게임/Steam/오디오 조작을 수행하지 않았습니다.

코드 잔여: 최종 실패 OFF 정책 전체 연결/제한 재시도/영속 기록 우선순위/설정 경쟁 및 재시작 분류/재활성화 UX, helper 첫 배포와 서명 update 패키징, legacy ownership migration, 완료 work/staging 정리 및 한도 도달 안내. 운영/승인 잔여: private 인증 방식·signing trust·helper fingerprint 설정 결정과 필요한 승인, private GitHub 배포/접근, 실제 설치/실게임 UI 검증. 이후 구현에는 추가 키/자격증명 없이 가능한 부분부터 이어서 진행할 수 있습니다.

근거: artifacts/automatic-update-development/helper-ui-recovery-20261002-1051/verification.md, evidence.json, changes.diff, cumulative-helper-ui.diff, full-feature-review-snapshot/, product-metadata-final.json, package-verification.json, l/*-fixture.log. 정책 변경 전 sources/log는 before-policy/, verification-before-policy.md와 j/에 보존했습니다.


## 최종 실패 정책 통합 후속

새 기본 ON 정책과 최종 실패 OFF, 유한 재시도, process-aware guard, 기록·복구·설정 저장 순서, 실패 이벤트, 다시 사용 UI를 구현했습니다. [update-failure-policy.md](update-failure-policy.md)에 현재 동작과 코드/운영 잔여를 분리했습니다. 이전 primitive/checkpoint 미완료 표시는 당시 상태이며 최신 후보는 failurePolicyIntegrated=true, installableRelease=false입니다.


배포·최초 설치·legacy 수동 차단·정리 및 보안 리뷰 수정의 최신 경로는 [update-delivery.md](update-delivery.md)를 따릅니다. 과거 체크포인트의 코드 잔여 항목 표기는 당시 기록입니다. 현재 운영 trust/auth와 실제 배포·설치·실게임 검증은 계속 미실행입니다.
