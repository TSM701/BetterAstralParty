# Private GA 1.0.0 자동 업데이트 준비 현황

2026-10-02. 부모의 최신 지시에 따라 매칭 포커스는 다음 마이너 패치로 미뤘다. 이번 작업은 비밀정보 없는 코드·격리 검증·GA 준비까지이며, 실제 설치와 원격 배포는 실행하지 않았다.

## 현재 상태와 확인 범위

- 등록 작업 루트는 `E:\SteamLibrary\steamapps\common\Astral Party`, 실제 소스와 확인한 실행 cwd는 그 아래 `BetterAstralParty`다. 앱 프로젝트 이름·ID는 위임에서 전달받았다. 지원되는 프로젝트 UI 소속 조회나 세션 대화 도구가 없어 UI 소속과 메인 개발 세션 실행 상태를 독립적으로 확인하지 못했다. 내부 DB/UI 우회나 잠금 파일만으로 실행 상태를 판단하지 않았다.
- GitHub 읽기 도구로 `TSM701/BetterAstralParty` 저장소 ID `1401226962`, visibility `private`, size `0`, 기본 브랜치 `main`, 접근 가능 및 릴리스 목록 `[]`를 확인했다. 예전 빈 접근 배열과 달라진 현재 관측이다. 소스 디렉터리는 여전히 Git 저장소가 아니다. 커넥터 접근은 모드가 사용할 인증정보를 제공하지 않는다.
- 16:58:52 UTC 읽기 관측에서 실제 INT 설치 DLL은 `0.39.2`, SHA-256 `9C97EFAD1F23ED381E62AA80D42BCD21104BCEF47972C6536BD834E6C15A8865`였다. 대상 이름의 게임/런처 프로세스는 관측되지 않았고 loader enabled는 false였다. 이후 설치 직전에 종료 상태를 다시 확인해야 한다.
- 새 소스·빌드는 `1.0.0-dev`다. 운영 인증 공급자 0개, production trust root는 빈 배열이다. 자동 다운로드/종료 후 적용 경로는 준비되어 있으나 운영 설정이 없어 활성화되지 않는다. GA 완료·설치 가능 릴리스로 표시하지 않았다.

## 이번 변경

`src/BetterAstralParty.csproj`에서 `MatchFocusState.cs`, `WindowsMatchFocus.cs`를 플러그인 컴파일 대상에서 제외했다. 두 원본 파일과 focus-tests, 조사·거절·검증 기록은 보존했다. 매칭 toggle/native reader/event bridge/window 동작은 추가하지 않았다. 이전 TP02 후보에 있던 매칭 관련 타입 7개(중첩 타입 포함)가 새 DLL에는 0개임을 Cecil로 확인했다.

`ReleaseHttpClient`는 릴리스 조회와 파일 다운로드의 공통 생성 경계다. 자동 리디렉션, 쿠키, OS/default/preauthenticated 자격증명, 자동 압축 해제를 끈다. 인증은 요청별이며 asset CDN 수동 이동에는 기존 정확한 host allowlist와 인증 제거 정책을 유지한다. GitHub asset API가 200 스트림 또는 302 응답을 반환하는 것은 공식 문서에 명시되어 있다. 현재 API 버전 `2026-03-10`도 공식 문서와 일치한다. [GitHub release asset API](https://docs.github.com/en/rest/releases/assets), [API versions](https://docs.github.com/en/rest/about-the-rest-api/api-versions).

`ReleaseUpdates.Production`은 이 클라이언트를 소유·정리하고 임의 클라이언트 교체를 거부한다. 미래 인증 연결용 `ConfigureAuthentication`은 기존 요청 취소·세대 폐기·개인 캐시 초기화를 거친다. 캐시 식별자는 다운로드와 같은 256자 한도를 적용하며 무효 세션이면 개인 결과와 알림을 지운다. 공급자 선택·보안 저장소 읽기·토큰 생성·갱신은 구현하거나 실행하지 않았다. worker의 HTTP 작업은 기존 방식대로 메인 UI 스레드 밖에서 실행된다.

테스트 프로젝트는 루트 `*.cs`만 명시적으로 컴파일하여 생성된 중간 파일이 소스로 섞이지 않도록 했다. 전체 UI 번역·KR/EN 문자열은 이번 변경에서 추가할 필요가 없었고 기존 자체 검증을 유지했다.

## 이번에 실행한 검증

| 검증 | 결과와 한계 |
| --- | --- |
| NET6 플러그인 | 경고·오류 0, `1.0.0-dev`; 매칭 0 / 운영 auth 공급자 0 / production trust empty를 컴파일 결과에서 확인 |
| 전체 자체 검증 NET8 | exit 0; 릴리스 조회 모의 HTTP 399 assertions, 공통 실제 HTTP 핸들러 14 checks(127.0.0.1 대체 서버 및 미설정 production) 포함 |
| HTTP·세션 격리 NET6 | exit 0; 같은 실제 제품 코드 및 tests로 399 assertions + 14 checks 통과. 전체 자체 검증을 NET6에서 통과한 것은 아님 |
| HTTP 실제 핸들러 | 302 target 요청 0, 다음 명시적 요청에 inert placeholder Authorization·Set-Cookie 재사용 0. 운영 토큰이나 실제 GitHub 요청 없음 |
| Helper/UI NET8·NET6 | 각 958 parent assertions; receipt 경계 child 94 assertions는 별도 보고. actual Framework fixture helper / ConfigEntry / fake NTFS 설치·정상 종료만 사용 |
| 서명·ZIP NET8·NET6 | 각 1,029 assertions, 일회성 fixture RSA 키만 사용 |
| 다운로드 NET8·NET6 | 각 105 assertions, fake HTTPS handlers; 인증 격리·host 이동·크기/시간 제한·취소·컨텍스트/캐시 검증 |
| 알림 UI 메타데이터 | presenter/native literal text/예외 경계/비차단 호출·home 알림 읽음 처리 통과. 실제 게임 렌더링 증거는 아님 |
| CardUi 표면 경계 | 이전 direct Surface 문자열 검사 대신 11개 카드 경계와 컴파일된 CardUi → AdviceBadge → NativeUi.Surface 소유 경로를 검증해 exit 0. 제품 CardUi/AdviceBadge/NativeUi 코드는 변경하지 않음 |

SR-01~04, TP-01~03의 수정은 보존했고 이번 helper regression에서도 관련 경계가 통과했다. 이전 TP02의 3개 runtime transaction 각 1,985 assertions 및 NTFS/process 각 65 assertions는 변경 없이 보존한 과거 증거다. 이번에 재실행했다고 표시하지 않는다. 메타데이터·mock·fixture 성공을 native 안정성으로 확대하지 않는다.

최초 자체 검증 wrapper 실행은 상대 출력 경로로 로그 저장에 실패했다. 다음 빌드는 그 생성된 중간 파일을 재컴파일하여 중복 assembly attributes로 실패했다. 두 실패 기록과 잘못 배치된 새 중간 파일은 삭제하지 않았고, 명시적 테스트 소스 목록·새 절대 출력 폴더로 다시 실행하여 exit 0을 확인했다.

추가 전체 NET6 retarget 시도는 기존 Program.cs의 NET8 전용 `Directory.CreateTempSubdirectory`로 컴파일에 실패했다. 기존 엔트리를 수정하지 않고 별도의 HTTP·세션 시험 프로젝트를 준비했다. 그 프로젝트의 최초 4개 빌드 시도는 누락된 번역/메뉴 의존성 또는 관련 없는 game substitute 의존성으로 실패했고 tests는 실행되지 않았다. 정확한 제품 의존성을 명시한 `transport-net6-b5`가 경고·오류 0, exit 0, 399+14 결과를 냈다. 실패 프로젝트·로그를 모두 보존했다.

CodeGraph 동기화는 automatic approval review에서 비공개 소스의 외부 인덱싱 서비스 전송 가능성과 목적지/payload 명시 승인 부재를 이유로 거부했다. 우회하거나 재시도하지 않았다. 로컬 파일 및 Cecil 분석으로 대체했으며 CodeGraph 인덱스 최신성은 이번 결과로 보장하지 않는다. 이 부가 동기화의 재개에는 별도 전송 승인이 필요하다.

새 DLL SHA-256: `03A2F3D46606DA89156DAC6025C2C84A7CBD42AC9D34E3FE48F3F8E8ED3BE96A`. 고정 checkpoint는 `artifacts/automatic-update-development/helper-ui-recovery-ga-20261002`다. 기존 beta/GA ZIP과 TP02 후보 ZIP은 수정하지 않았고, 새 GA 배포 ZIP·태그·릴리스·업로드는 만들지 않았다.

## 개인용 private GA의 최소 경로

부모의 인증·서명 검토 권장안을 반영해 **메모리 보관을 기본으로 하고 지속 저장은 별도 opt-in**으로 수정한다. 아래 보안 저장소 관련 경로는 기본 자동 저장을 뜻하지 않는다. 마스킹된 로컬 입력/실행 메모리 공급자와 Windows Credential Manager target `BetterAstralParty/GitHub/TSM701/BetterAstralParty`의 선택적 저장/삭제 경로는 실제 인증 설계 승인 후 구현한다. live auth 전에 기존 dump/export 경로를 감사하며 managed 문자열/header의 즉시 완전 삭제나 crash dump의 비밀 부재를 보장하지 않는다.

본인 한 명이 자신의 저장소를 읽는 첫 GA에는 만료일이 있는 fine-grained PAT를 선택한 저장소 한 개의 `Contents: read`(자동 Metadata read 포함)에 한정하는 안을 권한다. 릴리스 목록과 asset 다운로드에 이 권한이 지원된다. 배포·업로드용 write 권한은 모드에 주지 않는다. 사용자 직접 설정한 Windows Credential Manager 항목을 읽는 공급자는 승인 후 구현한다. 토큰은 채팅·cfg·소스·로그·manifest에 넣지 않으며 커넥터 자격증명을 재사용하지 않는다. [List releases permissions](https://docs.github.com/en/rest/releases/releases#list-releases), [Release asset permissions](https://docs.github.com/en/rest/releases/assets#get-a-release-asset), [PAT 제한·만료](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens).

현재 구현에는 실제 credential reader와 session coordinator가 없다. 승인 후 coordinator는 안전한 저장소 읽기를 worker에서 수행하고, 성공한 비밀정보를 메모리에 보관하며 토큰 자체 대신 새 비밀 아닌 session generation을 캐시 식별자로 사용해야 한다. 요청 인증은 정확한 repository의 GitHub HTTPS API GET에만 적용한다. logout/만료/401/권한 상실/계정 교체 시 조회와 다운로드를 함께 취소·무효화하고, UI thread에서 token 갱신·파일/저장소 조회를 하지 않는다. 두 컴포넌트의 공유 backoff와 기존 세대 폐기를 보존하는 통합 검증이 남아 있다. 토큰 값은 제품 문구나 진단 예외에 노출하지 않는다.

다른 사용자까지 확대할 때는 공통 PAT를 배포하지 않는다. 저장소 접근권과 사용자의 조직/협력자 유형을 먼저 정하고, GitHub App 사용자/설치 토큰과 제한된 broker 또는 별도 지원 로그인 방식을 설계한다. GitHub App 등록, backend, OAuth/device 권한, 신규 지속 접근은 별도 승인 대상이다. fine-grained PAT의 협력자 제한 때문에 개인용 설계를 그대로 확대할 수 있다고 약속하지 않는다. [Fine-grained PAT limitations](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens#fine-grained-personal-access-tokens-limitations).

## 부모가 사용자에게 한 번에 조율할 최소 결정·승인 묶음

다음은 권장안을 묶은 구체적인 범위이며 아직 승인된 운영 설정이 아니다. 비밀값을 답변으로 요구하지 않는다.

1. **개인용 인증**: 본인 전용 `TSM701/BetterAstralParty`(ID `1401226962`), 선택 저장소 한 개 `Contents read`, PAT 만료 30일, 사용자 직접 발급·마스킹된 로컬 입력·메모리 보관 기본. Windows Credential Manager 지속 저장은 별도 선택이며 항목 이름은 `BetterAstralParty/GitHub/TSM701/BetterAstralParty`로 제안한다. 승인 후 공급자·coordinator·입력/설정/해제 UX(KR/EN)와 오류 흐름을 구현한다. 토큰 생성/보관은 사용자 직접 또는 지원되는 보안 경로로만 진행한다.
2. **서명과 bootstrap 신뢰**: 사용자 소유의 소스·게임·배포 파일 밖 외부 signer의 RSA-3072/SHA256-PSS 운영 signing key 하나, exponent 65537, SHA256 길이 PSS salt, 공개키만 제품 trust에 넣는 최소 안. 현재 코드는 같은 trusted key map으로 서로 다른 payload/helper descriptor 형식을 검증하며 두 descriptor를 각각 서명한다. 키 관리 주체·OS 보호 저장소 또는 암호화 오프라인 백업·별도 unlock material·분실/복구 계획을 확정한 뒤 승인받는다. 제안 key ID는 `bap-ga-1`, fingerprint 정의는 공개키 DER SubjectPublicKeyInfo의 SHA-256이다. 비수출 키의 백업 가능성을 약속하지 않는다. 정확한 공개키 bytes/fingerprint와 최종 helper EXE SHA-256은 실제 build 후 기록해 독립 경로로 확인한다. 다운로드에 동봉된 해시만으로 첫 helper를 신뢰하지 않는다. 현재 자동 키 rotation/revocation은 없으므로 분실·침해 시 자동 수용을 중단하고 독립적으로 신뢰된 수동 verifier/helper 갱신·rebootstrap을 사용한다.
3. **GA 전환·로컬 포장**: 매칭 제외 상태에서 신규 `1.0.0` build, 새 출력 폴더, Stable channel, 기본 자동 다운로드/종료 후 적용 설정을 사용한다. 기존 0.39.2/GA 1.0.0 기록과 ZIP은 보존한다. 개인용 초기 전달은 기존 검증된 loader를 재사용하고 mod 소유 8개 파일과 별도 signed helper 전달 경로를 사용하여 기존 전체 loader/banner ZIP 재배포를 피하는 안이다. 새 helper 포함 full bundle이면 기존 GA 재배포 검토 항목도 다시 처리해야 한다.
4. **실제 검증**: 최종 인증·공개 trust·signed 산출물의 버전/해시를 검토한 뒤, 종료된 검증 INT 경로에 backup/receipt/rollback을 사용하는 `install-bootstrap.ps1 -UpdateOnly`로 적용하는 별도 동의를 조율한다. 그 후 GA 실행, KR/EN 알림/상세/설정, Stable/Beta, 인증 해제/만료/네트워크 실패, signed staged update의 정상 종료 후 적용·재실행·복구와 기본 vanilla loader OFF를 확인한다. 강제 종료·Steam 재시작·매칭 창 동작은 이 범위에 없다.

이 묶음은 운영 준비와 실제 설치/검증의 선택을 한 번에 받기 위한 제안이다. **원격 최초 README commit·v1.0.0 tag·private draft/release 생성·파일 업로드는 최종 파일 목록/해시가 나온 뒤 별도의 명시적 배포 지시까지 대기한다.** 저장소 size 0 관측만으로 branch 존재를 가정하지 않는다. release-ID와 ZIP asset-ID를 signature에 묶으므로 draft/asset ID 확보 후 descriptor 서명·finalize 검증·비공개 게시의 순서를 계획해야 한다. 공개 전환·소스 업로드는 포함하지 않는다.

서명 후 예상 update assets는 `BetterAstralParty-1.0.0-update.zip`, `BetterAstralParty.update.manifest`, `BetterAstralParty.update.signature`다. 초기 helper 전달은 `BetterAstralParty-UpdateHelper.exe`, `helper.descriptor`, `helper.signature`와 독립 fingerprint 검증이 필요하다. 업로드 허용 전에는 로컬 검토 대상만 준비한다. 알림-only fallback을 GA 자동 업데이트 완료로 대체하지 않는다.

## 남은 순서와 미실행

병렬 검토 입력은 checkpoint의 `independent-review-inputs`에 원본/hash로 고정했다. 원본은 [인증·서명 검토](../artifacts/parallel-review/security-recovery/private-ga-auth-signing-review-20261002.md), [GA 검증 체크리스트](../artifacts/parallel-review/test-package/private-ga-validation-checklist.md)다. 해당 검토는 pre-edit 기준이므로 최종 `source-after`와 diff는 부모의 별도 재검토 대상이다. CardUi 표면 검사 요구는 정적 검사 수정으로 처리했고 제품 UI를 변경하지 않았다.

개인 소유 private repo에는 collaborator read-only 초대가 없어 다운로드 이용자를 단순 초대하면 쓰기 권한도 늘어난다. 확장 시 조직 배포 repo의 명시적 Read 역할 또는 entitlement backend의 signed artifact 전달을 별도 선택한다. 설치 토큰·App private key는 backend에 두고 소비자에게 전달하지 않는다. Contents read는 릴리스 전용 권한이 아니며, 사용자 token은 없는 repo 접근권을 만들지 못한다. 이 내용은 현재 개인용 GA의 등록/권한 확대 승인이 아니다. [개인 repo 권한](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/repository-access-and-collaboration/permission-levels-for-a-personal-account-repository), [GitHub App installation token](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-an-installation-access-token-for-a-github-app), [Credential security](https://docs.github.com/en/rest/authentication/keeping-your-api-credentials-secure).

온라인 재개 이후 완료된 tests를 중복 실행하거나 산출물을 덮어쓰지 않았다. dotnet 프로세스 하나는 관측되었지만 Win32_Process 상세 조회가 access denied여서 실행 목적을 단정하지 않았다. 이 작업의 모든 검증 세션은 종료 결과를 받은 상태다. 초기 199-file pre-edit 목록에서 updater-tests 프로젝트가 누락된 것을 최종 diff 확인 중 발견했다. 그 파일의 세 가지 추가 요소는 최초 읽기 기록으로 preimage를 재구성하고 한계를 별도 기록했다. 원래 byte-level hash/EOL을 독립 캡처했다고 주장하지 않는다. 수정 전 test-surfaces.ps1과 두 검토 원본은 실제 bytes/hash로 고정했다.

인증·signing/trust/bootstrap 선택 → credential/session 구현과 모의 통합 검증 → 운영 공개키 적용·1.0.0 신규 빌드 → unsigned 입력 검토 → 명시적 private 배포 지시와 실제 release/asset ID 확보 → 오프라인 서명·finalize·hash 검토 → 설치/실게임 검증 → 허용된 비공개 배포 순서로 부모와 조율한다. 실제 private asset redirect·권한 만료·game UI/native/종료 후 교체는 아직 미검증이다. 자동 업데이트는 첫 legacy 설치를 임의로 인수하지 않으며 receipt 부족은 수동 설치 안내로 처리한다.

최종 beta의 UI/Steam checkout 표시/스크린샷 연결/플레이타임 사용자 확인은 기존 `ga-review-1.0.0.md` 기록을 유지한다. 이 세션은 새 updater GA 실게임 확인을 하지 않았으며 과거 FlowSDK native crash 원인도 해결했다고 주장하지 않는다.
