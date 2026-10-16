# 비공개 업데이트 인증 세션 준비 — 2026-10-02

자격증명 없이 구현·빌드·격리 검증을 완료한 리뷰 단계다. 운영 PAT, 서명 키, 공개 키 pin 및 helper fingerprint는 설정되지 않았다. 이 체크포인트의 DLL과 리뷰 ZIP은 실제 설치·게시용이 아니다. 새 인증 코드의 독립 리뷰와 아래 보안 결정을 마친 뒤 운영 산출물을 다시 빌드해야 한다.

## 현재 구현

- `PrivateReleaseSession`이 조회·다운로드·종료 후 적용 예약의 인증 수명을 함께 관리한다. 명시적 로그아웃, 인증 실패(401), 접근 불가(403/404), 정책 해제, 8시간 만료 때 세 파이프라인을 정리한다. 자연스러운 게임 종료는 메모리 자격증명만 버리고 이미 검증된 offline helper 적용 예약은 유지한다.
- `MemoryReleaseAuthentication`은 fine-grained PAT 형식만 받고 메모리에만 보관한다. 정확한 `https://api.github.com/repos/TSM701/BetterAstralParty/releases` 조회·페이지 이동 및 release asset GET에만 Bearer를 붙인다. 다른 저장소, CDN, 쓰기 요청, 기존 Authorization, 비정규 URI에는 붙이지 않는다. 캐시 구분자는 토큰과 무관한 임의 값이다.
- 일반 설정의 연결/로그아웃 버튼과 상태 안내를 한국어·영어로 연결했다. 기존 관리 입력 드라이버를 사용하며 Stage에 새 managed delegate를 등록하지 않는다. 연결 상태 문구는 입력 수락과 서버 인증 성공을 구분한다.
- `PrivateUpdateApproval.CredentialInputEnabled`는 컴파일된 `false`다. 운영 trust도 비어 있어 입력 호출과 실제 전송은 활성화되지 않는다. cfg, 환경 변수, 파일로 활성화할 수 없다.
- `ProductionUpdateKeys`는 공개 RSA 3072/4096 파라미터와 canonical SPKI SHA-256 검증 지점을 제공한다. 운영 키 목록은 빈 배열이며 실행 중 외부 파일·cfg에서 pin을 가져오지 않는다.
- `PublisherDraft`는 설정/경로 설명과 공개 pin 확인만 제공한다. 키 생성·서명·비밀 키 내보내기·게시 명령은 파일 접근 전에 exit 3으로 거부한다. 운영 CNG signer는 아직 구현하지 않았다.

## 입력 화면 선택은 미확정

이전 인게임 마스킹 입력 제안을 대체할 후보로 Windows `CredUIPromptForWindowsCredentialsW` 어댑터를 작성했다. 사용자 선택을 받은 것으로 간주하지 않았다. native 호출은 승인과 운영 pin이 모두 갖춰져야 도달할 수 있으며 이번 검증에서 창을 띄우지 않았다.

입력 버퍼/저장 체크박스/자동 입력을 제공하지 않고 Credential Manager 읽기·저장을 호출하지 않는다. Windows API 자체는 자격증명을 저장하지 않는다. 네이티브 결과 버퍼는 덮어쓰고 해제한다. .NET 불변 문자열의 모든 복사본 삭제는 보장할 수 없으며 같은 사용자 프로세스와 crash dump로부터의 비밀 격리는 제공하지 않는다. [Microsoft API 문서](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-creduipromptforwindowscredentialsw), [버퍼 해제/추출 문서](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credunpackauthenticationbufferw).

창은 worker에서 실행되지만 취소 토큰으로 강제 종료할 수 없다. 사용자가 닫기 전까지 새 창은 차단하며 늦게 돌아온 입력은 폐기한다. 게임 포커스·전체 화면·마스킹·닫기 동작은 미검증이다. 승인 후 비밀이 아닌 임시 문자열로 화면부터 확인해야 한다. 실제 PAT를 채팅·로그·명령 인자·환경 변수·cfg에 넣지 않는다.

## 검증 범위

최종 NET8 전체 self-test와 NET6 인증/조회 focus에서 각 세션 92, 조회 399, HTTP transport 14 assertion이 통과했다. NET8·NET6 dev→`1.0.0-rc.1`/Beta 및 unsigned in-memory 준비 42 assertion도 각각 통과했다. 테스트는 inert 문자열, fake HTTP, 공개 파라미터만 사용했다.

실제 dev·RC DLL, Framework x64 helper, packager, 게시 도구 초안은 경고·오류 없이 빌드했다. 메타데이터에서 Matching 타입 0, fixture 값 0, 운영 pin 0, 입력 승인 false를 확인했다. 각 DLL의 CardUi/AdviceBadge/NativeUi 42 메서드 IL/attribute는 이전 GA와 동일했다. 최종 DLL·테스트 PDB의 source checksum도 현재 바이트와 대조한다.

개발용 패키지 검사기의 정상 후보 및 승인 활성화/키 목록 변경/허용하지 않은 제공자/fixture 값/RC 버전 변형 5개의 거부를 검증했다. 변형 DLL은 실행하지 않았다. 생성된 개발 리뷰 ZIP은 signed update ZIP이 아니다.

초기 NET6 focus 누락 참조 실패, 기존 src/obj의 implicit compile에 따른 assembly attribute 중복 실패와 출력 준비 권한 실패는 기록을 보존했다. 최종 빌드는 명시적 source allowlist의 격리 프로젝트로 해결했다. NET6 전체 self-test, 실제 인증 API, native 입력창, 실게임 종료·재시작·파일 적용은 실행하지 않았다. 이전 GA helper/trust/NTFS 검증 결과는 이전 체크포인트 근거이며 이번에 재실행한 결과로 합산하지 않는다.

## 사용자 복귀 후 결정 및 순서

1. 부모 독립 리뷰에서 새 세션·입력·pin·패키지 검사기 변경을 확인하고 수정 요구를 반영한다. 이전 GA 리뷰는 새 인증 구현의 승인으로 대체하지 않는다.
2. 단일 저장소 read-only fine-grained PAT, 제안 만료 30일, 게임 세션 최대 8시간 메모리 사용과 입력 화면을 선택한다. Windows dialog 또는 추가 인게임 UI 조사 중 선택이 필요하다. Credential Manager 지속 보관은 구현하지 않았고 별도 opt-in 없이는 추가하지 않는다. [GitHub PAT 관리 문서](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens).
3. 서명 키 보관 방식(CNG CurrentUser nonexportable 또는 오프라인 signer), 생성 권한·설정·실행 경로를 확정한다. 제안은 Microsoft Software Key Storage Provider / RSA3072 / exponent65537 / signing only / export policy 0 / RSA-PSS SHA256 salt32다. CurrentUser 키는 같은 사용자에 대한 격리가 아니며 nonexportable 설정은 복구 가능한 백업을 보장하지 않는다. [CNG 보관 문서](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-and-retrieval).
4. 실제 환경에서 설명만 조회한 경로는 키 디렉터리 `C:\Users\mincom\AppData\Roaming\Microsoft\Crypto\Keys`, 게시 도구 제안 디렉터리 `C:\Users\mincom\AppData\Local\BetterAstralParty-Publisher`다. 디렉터리·키를 생성하거나 키 저장소 내용을 읽지 않았다. 실제 private key 파일명은 OS가 정하며 이 작업의 운영 키는 생성하지 않았다. 승인 후 운영 signer 구현·생성은 별도 단계다.
5. 공개 modulus/key id/SPKI fingerprint만 독립 검토해 모드·helper·packager pin에 연결한다. 자격증명 없이 먼저 native 화면을 검증하고 실제 운영 산출물을 재빌드·검증한다.
6. GitHub owner TSM701의 정식 로그인된 게시 경로와 권한을 확인한다. 로컬 `gh`와 세션 메시지 도구가 없고 connector의 Release 쓰기 도구도 없다. 기존 읽기 접근을 인증 추출·UI 자동화·DB 수정으로 우회하지 않는다. 기록상 비공개 저장소 ID1401226962 및 releases=[]였으며 이번 작업은 원격을 변경하지 않았다.
7. exact bare tag `1.0.0-rc.1`, prerelease/Beta로 실제 Release/asset ID를 확보한 뒤 8개 owned payload와 helper, 서명 descriptor를 준비한다. 서명한 dev 설치 receipt와 helper bootstrap, 비공개 RC Release/6 assets를 검증 후 진행한다. `v` prefix의 조회/packager 불일치와 `rc1` 대 `rc.1` 차이는 기존 제한으로 유지한다.
8. 사용자가 실제 게임에서 Beta 조회·다운로드·자연 종료·재실행·새 버전/변경 사항/다운로드 표시를 확인한다. 에이전트는 게임·Steam·창을 실행/종료/조작하지 않았다. 공개 전환·공개 릴리스·전체 소스 업로드는 허가하지 않는다.

## 리뷰 자료

`artifacts/automatic-update-development/private-session-preparation-20261002/REVIEW-READINESS.md`, source-before/source-after, changes.diff, 빌드·테스트 로그와 evidence.json을 사용한다. 이전 `private-trial-plan-20261002`와 `helper-ui-recovery-ga-20261002`는 변경 없이 보존한다. 프로젝트 UI 소속과 실행 상태는 cwd나 lock 존재로 추정하지 않는다.
