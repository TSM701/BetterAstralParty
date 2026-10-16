# 비공개 운영 시험 준비 — 2026-10-03

사용자 승인 «좋아 그럼 플랜대로 비공개 테스트 계속 진행해줘»를 부모가 전달했다. 범위는 `TSM701/BetterAstralParty`의 private `1.0.0-rc.1`(Beta), CurrentUser RSA 3072 nonexportable 키 하나, 새 개인 Publisher 폴더, 공개 pin 반영, 메모리 전용 입력 준비이다. 공개 배포·소스 업로드·토큰 검색·저장은 허용하지 않는다.

근거는 `artifacts/automatic-update-development/operational-private-trial-20261003/`에 있다. 이전 `session-security-followup-20261002` 결과는 그대로 보존하며 새 운영 검증의 대용으로 표시하지 않는다.

## 완료한 범위

- 키 이름 `BetterAstralParty.PrivateTrial.20261002`, key ID `bap-trial-20261002`. Microsoft Software Key Storage Provider, CurrentUser, RSA 3072/65537, Signing, ExportPolicy None. 개인 키를 내보내거나 실제 키 파일을 읽지 않았다. 두 번째 생성은 `AlreadyExistsNoChanges`/exit 10으로 거부됐다.
- 공개 SPKI SHA-256: `99ACE415879EF2FCA89C741F4F93838D8469953A4787C47C0C1A16BEF0B2A2E7`. BCL DER와 기존 DER 구현이 일치한다. dev·RC·helper·packager·draft·독립 signer의 공개 pin 하나를 메타데이터로 대조했다.
- 소스의 `PrivateUpdateApproval.CredentialInputEnabled`를 승인된 ON으로 바꿨다. 실제 설치나 실행은 하지 않았다. 저장된 게임 선택을 바꾸지 않았다. dev=`1.0.0-dev`, RC=`1.0.0-rc.1`; RC 파생본의 의미 변경은 버전 하나이며 줄바꿈 정규화가 있다.
- 제품 6종 빌드 성공. NET8 전체/OFF·NET6 focus/OFF는 각각 session 139, release 399, HTTP 14 검증을 통과했다. 기존 suite가 native 입력을 호출하므로 승인 gate와 pin factory만 OFF/empty로 바꾼 별도 시험 복사본의 IL을 확인한 뒤 실행했다. ON 제품의 native 동작을 통과했다는 뜻은 아니다. 정확한 8시간 경계·시계 회귀는 가짜 단조 시계로 검사했다.
- 현재 RC 8-file 데이터와 실제 helper로 독립 signer 순수 검증 27개 통과. ephemeral TEST RSA만 사용했으며 운영 키 서명은 하지 않았다. dev/RC의 기존 UI 메서드 42개·속성·IL 유지, 직접 Stage delegate 등록 0. PDB와 현재 소스 일치를 검증했다.
- 현재 RC ZIP과 helper를 업로드 후보로 준비했다. ZIP SHA-256 `00FE955770CC351EF197D051861AAF9C2A2232D80CFA10CF1015B8F16C1E0A31`, 630253 bytes. helper SHA-256 `3876BDA8C8C2FBDE523F92AC2042B7362FF7CF112ECDF894B6B731232F47A4F4`. 아직 서명이 없으므로 설치 패키지가 아니다.

## Publisher 저장 위치와 보호 범위

논리 경로는 `%LOCALAPPDATA%\BetterAstralParty-Publisher`다. 이번 프로세스의 brokered 파일 시스템은 이를 `C:\Users\mincom\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\BetterAstralParty-Publisher`에 매핑했다. 엄격한 기존 fence는 논리 alias를 거부했다. OS가 새로 만든 실제 폴더의 receipt 해시·NTFS 식별자·소유 SID를 고정하고 실제 경로의 fence와 private ACL을 검증한 뒤에만 키 생성을 재개했다. 게임 updater의 경로 방어는 완화하지 않았다. 폴더 식별자 `FC36ADBE:00360000000A344C`; 새 `tool-v1`은 검증된 독립 도구 네 파일만 포함한다. 새 폴더들의 상속 없는 ACL은 현재 사용자와 SYSTEM만 허용한다.

키 저장 디렉터리는 KSP의 논리 기본 `%APPDATA%\Microsoft\Crypto\Keys`이며 실제 개인 파일의 물리 위치를 조사하지 않았다. 같은 사용자 프로세스 사이의 완전한 격리는 제공하지 않는다. 키를 잃으면 수동 재부트스트랩이 필요하다. 폴더 ACL이 같은 사용자 악성 프로세스를 막는다고 주장하지 않는다.

독립 `tools/OperationalPublisher.cs`는 원격 요청을 하지 않는다. 실제 release/ZIP asset ID, rc.1/Beta/current dev, 요청 폴더 식별자, 공개 key hash, descriptor/helper/ZIP/image hash를 묶은 검토 plan을 먼저 검증한다. 기존 packager로 canonical 8-file ZIP과 두 descriptor를 재구성해 일치해야 기존 키를 연다. 두 descriptor만 RSA-PSS/SHA-256으로 서명하고 기존 verifier로 검증한 뒤 새 출력에 쓴다. plan hash는 검토 바이트를 묶으며 같은 사용자 프로세스와의 권한 격리를 제공하지 않는다.

## 실패와 미실행

native fixture를 두 차례 실행했지만 KO/EN 모두 허용된 PID/caption 범위에서 창을 식별하지 못했다. placeholder를 주입하지 않았고 마스킹·취소·포커스·invalid 표시·스크린샷을 검증하지 못했다. own inert fixture만 종료했다. 실제 PAT는 읽거나 입력 요청하지 않았다. 부모의 최소 검증 문서에 따라 사용자가 placeholder 화면을 직접 확인한 뒤 실제 인증으로 진행해야 한다.

초기 signer nullable 빌드 오류, MSIX 논리 경로/패키지 식별 실패, OFF suite 중복 Compile 경고, payload 도구의 slash 경로 오류, PowerShell reflection 배치 인수 오류를 보존했다. 최종 후보와 수정된 시험은 별도 새 출력에 만들었다. 중간 publisher b3/b4의 정확한 소스 snapshot은 없어 해당 PDB의 정확한 재구성을 주장하지 않는다.

게임·Steam·dev 설치·helper bootstrap·실제 PAT/API 조회·운영 키 서명·다운로드/종료 후 교체·RC 재실행·GitHub 쓰기는 미실행이다. 등록 cwd/source 경로는 확인했지만 이 세션의 앱 UI 프로젝트 소속을 확인할 지원 도구는 없었다. 기존 메인 세션에 직접 질문할 대화 도구도 없었다. 잠금 파일을 실행 상태 증거로 사용하지 않았다.

## 부모와 이어갈 순서

1. 독립 검토자가 새 공개 키·최종 소스·제품·실제 폴더 receipt/ACL과 보존 증거를 확인한다.
2. GitHub read-only 결과는 private=true, repository ID 1401226962, size 0, releases=[]다. 현재 커넥터에는 release 생성/asset 업로드 도구가 없다. repo-only read PAT의 쓰기 권한을 확대하지 않는다. 부모/사용자의 지원되는 GitHub 웹 경로로 private draft prerelease `1.0.0-rc.1`를 만들고 먼저 `BetterAstralParty-1.0.0-rc.1-update.zip`만 업로드한다. 빈 저장소의 초기 ref가 필요하면 소스 대신 최소 배포 설명만 별도로 조율한다.
3. 실제 release ID/ZIP asset ID·크기·제공되는 digest를 read-only로 확인한다. 업로드 전 ZIP과 현재 helper 바이트를 기존 packager로 exact ID-bound 요청으로 재구성하고 canonical ZIP hash가 위와 같은지 확인한다. 부모가 정확한 plan/hash를 리뷰한 후 기존 운영 키로 두 descriptor만 서명한다. ID가 없는 후보의 synthetic descriptor는 모두 버렸으며 운영 키로 dummy ID를 서명하지 않는다.
4. 기존 finalize로 6개 자산을 검증한다: ZIP, `BetterAstralParty.update.manifest`, `BetterAstralParty.update.signature`, `helper.descriptor`, `helper.signature`, `BetterAstralParty-UpdateHelper.exe`. 나머지 5개를 private draft에 올린 후 private prerelease를 게시한다. 내부 증거·소스·TEST key/fixture는 배포하지 않는다. 서버 digest가 없고 binary read도 불가하면 원격 파일 hash 검증은 미완료로 보고한다.
5. 서명된 helper bootstrap, 현재 게임/호환성/receipt/loader 상태를 부모가 검증하고 승인된 dev 시험 설치를 준비한다. 기존 `package-update-development.ps1`은 OFF/empty 개발 전용이므로 ON 후보에 그대로 사용하지 않는다. 새로운 ON 설치 패키지·bootstrap 검증은 아직 준비되지 않았다.
6. 최소 시험 문서대로 사용자가 KO placeholder 취소, EN placeholder invalid를 확인한 뒤 실제 PAT를 native 마스킹 창에 직접 입력한다. 토큰을 채팅·명령행·로그·파일로 전달하지 않는다. 실제 private 조회/다운로드 취소/종료 교체/RC 재실행의 성공·실패·미실행을 각각 기록한다. 이 단계 전 GA/실게임 검증 완료를 선언하지 않는다.
