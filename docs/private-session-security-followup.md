# 비공개 세션 보안·통합 회귀 보완 — 2026-10-02

보안 리뷰 PS-01/02/03과 test/package 리뷰 PS-TP-01/02를 수정하고, G1/G2의 실제 제품·예약 helper 검증을 격리 환경에서 보완했다. 개발용 산출물과 검증 근거를 `artifacts/automatic-update-development/session-security-followup-20261002/`에 보존한다. 운영 인증·키·설치·게시를 승인하거나 완료한 체크포인트는 아니다. 기존 GA/세션 체크포인트와 설치 파일을 보존하며, 새 소스는 부모의 추가 독립 리뷰 대상이다.

## 수정 내용

| 리뷰 항목 | 변경과 확인 |
| --- | --- |
| PS-01 | 메모리 인증의 최대 8시간 수명을 `Stopwatch` 기반 경과 시간으로 계산한다. UTC의 앞뒤 변경은 수명을 연장하지 않는다. 경과 시간이 음수가 되거나 제한에 도달하면 자격증명과 캐시 식별자를 폐기하고 되살리지 않는다. |
| PS-02 | 개발 후보 포장에 기존 `WindowsFileFence`를 재사용한다. 루트·모든 조상 디렉터리 handle을 고정하고 reparse 경로를 거부하며, 새 출력은 같은 경계 안에서 상대 handle/CreateNew로 만든다. |
| PS-03 | 입력 파일 lease를 포장 종료까지 유지하고 한 번 캡처한 private bytes를 해시·IL 검토·복사·ZIP에 사용한다. 검토용 배열은 clone이다. 원본과 출력 identity를 재확인한다. |
| PS-TP-01 | native `ERROR_CANCELLED`만 취소로 분류한다. 사용자 이름/domain 불일치는 InvalidInput, API·unpack·buffer 오류는 InputFailed로 분리하며 예외 내용을 UI에 노출하지 않는다. |
| PS-TP-02 | native 입력 시작·종료 revision을 main-thread Poll에서 반영한다. 로그아웃 뒤 창이 닫힌 경우에도 연결 버튼 상태를 다시 갱신한다. |

`ProcessExiting`과 인증 게시를 같은 gate로 직렬화하고 종료를 먼저 불가역으로 표시한다. 자연 종료는 이미 검증·예약된 offline helper를 취소하지 않는다. 실험 중 새로 발견한 취소 실패 경로도 수정했다. 로그아웃 후 request context가 지워지거나 동시에 FilesBusy가 관찰되어도 취소 실패가 두 업데이트 옵션 OFF 저장과 durable fault로 남도록 폐기 중인 context/stage를 유지한다.

운영 approval은 컴파일 상수 false, 운영 trust는 빈 배열이다. cfg·환경 변수로 켤 수 없다. 운영 signer, PAT, helper fingerprint를 새로 구성하지 않았다. `DevelopmentPackageFiles.cs`는 포장 도구의 facade이며 제품 DLL/helper에 포함하지 않는다.

## 이번 체크포인트에서 실행한 검증

- 최종 NET8 전체 self-test와 NET6 focus 각각: 세션 139 assertions, 릴리스 조회 399 assertions, HTTP transport 14 checks 통과. 전체 NET6 self-test는 실행하지 않았다.
- dev·RC·Framework helper·NET8 packager·publisher 5종 최종 빌드: 오류/경고 0. dev/RC는 각각 `1.0.0-dev`, `1.0.0-rc.1`이며 제품의 Matching type, fixture marker, 개인키 서명·프로세스 종료 호출 검사 결과는 0이다. 42개 기존 UI 표면 method의 IL/attribute가 GA 기준과 같다. 실제 UI 표시를 검증한 뜻은 아니다.
- Windows NTFS 경계 회귀: 실제 assertion 24개 통과. 증거 record 32개에는 예외 유형 8건이 포함된다. 입력 쓰기/삭제, 출력/조상 교체, 경로 이탈과 새 inert junction을 시험했고 외부 inert sentinel은 보존했다. 기존 링크나 사용자 파일을 수정하지 않았다.
- 실제 dev DLL/helper/검토된 launcher로 로컬 개발 후보 포장 1건 통과. approval/pins/provider/fixture/RC-version 변조 5건은 출력 생성 전에 거부됐고 변조 제품을 실행하지 않았다. ZIP은 바이너리 3개, 안내, unsigned manifest의 5 entries이며 `installableRelease=false`이다.
- 최종 Portable PDB 8종을 각 DLL과 함께 읽고 모든 sequence-point document의 SHA-256이 실제 소스 bytes와 일치함을 확인했다. Framework helper는 Portable PDB가 없으므로 response file과 메타데이터가 근거다.
- 실제 제품 기반 통합 회귀는 NET8·NET6 각각 부모 assertion 60개, 7개 child 시나리오의 assertion 합계 60개를 통과했다. 실제 `UpdateHandoff.Prepare/Queue/Cancel`, staging/ticket/Windows signal/파일 안전 코드와 helper engine을 사용했다. 자연 종료 적용, 로그아웃, 정확히 8시간 만료, 정책 해제, 늦은 Prepare/Queue 정리, 취소 실패 OFF/fault 저장 및 재시작을 확인했다. 자연 종료의 receipt와 파일 해시, 취소 뒤 기존 dev 보존, loader OFF와 inert 저장 설정 보존도 확인했다.

통합 회귀에서 사용하는 키는 프로세스 메모리 안의 **TEST 전용 ephemeral RSA3072**다. 개인키를 이름 있는 저장소에 만들거나 export/persist하지 않았다. BCL의 `ExportSubjectPublicKeyInfo`와 제품의 SPKI 인코딩을 독립 비교했으며, 테스트 공개 modulus만 새 fixture 파일에 저장했다. 실제 운영 public pin provenance 검토와는 별도다.

`actual-payload-fixture-only/`는 최종 실제 RC DLL과 기존 검증된 7개 owned supporting 파일의 정확한 8-entry ZIP, 실제 raw production helper를 TEST 서명으로 포장한 6 assets다. synthetic Release/asset IDs를 사용하고 `DO-NOT-INSTALL.txt`를 둔다. `protocol-payload-fixture-only/`는 inert apphost/game/loader/catalog, fixture compatibility, TEST helper entrypoint/public trust를 대체한 별도 fixture다. plugin DLL을 실제 game plugin으로 로드하지 않는다. 실제 raw production helper는 isolated fixture의 존재하지 않는 stage에 대한 `--apply`를 실행하여 `NotConfigured`/exit 2로 차단됨만 확인했다. 운영 신뢰가 설정된 실제 helper 적용을 검증하지 않았다.

## 실패와 근거의 한계

- integration 시도 1은 fixture cache routing define이 빠져 ManualUpgradeRequired로 실패했다. 기존 캐시 경로를 읽기 전용으로 조회했을 가능성이 있으며 사용자 경로에 쓰지 않았다.
- 시도 2는 첫 실행의 assembly 파일명 오류 후 올바른 파일로 재실행했고, 취소 실패가 OFF/fault 저장으로 이어지지 않는 코드 결함을 재현했다. 수정 전 이미지·로그·소스를 보존했다.
- 시도 3은 늦은 Prepare의 비동기 정리가 끝나기 전에 test child를 종료해 restart assertion이 실패했다. 최종 시도 4는 main-thread Tick에서 retirement task의 종료를 관찰한 뒤 검증한다. 시도 3 소스 복원본은 당시 PDB 체크섬과 다르므로 byte-exact 원본으로 인정하지 않는다. 실패한 DLL/PDB/로그와 이 한계를 보존했고, 최종 시도 4의 소스/PDB는 일치한다.
- NTFS 첫 harness는 PowerShell의 자동 `$input` 변수로 인한 신뢰 제한이 있다. b2에서 변수와 예외 유형 확인을 고쳤고, b3에서 32 records를 24 assertions로 정확히 구분했다. 이전 결과를 최종 성공 수에 포함하지 않는다.
- 이전 42개 metadata-only trial fixture, 기존 GA/helper/trust/NTFS 전 suite는 이번 후속 체크포인트에서 다시 실행하지 않았다. 과거 성공은 새 인증·취소 변경의 승인이 아니다.
- RC 파생 소스는 Version literal만 의미상 변경했지만 준비 스크립트가 CRLF→LF를 정규화했다. byte-exact 단순 치환이라고 주장하지 않으며 비교 근거와 RC PDB를 보존한다.

## 남은 운영 단계

native 창의 실제 마스킹·취소·포커스·전체 화면, 한/영 게임 메뉴와 해상도·스크롤·클릭 영역, 실게임 종료/적용/재시작은 미실행이다. GitHub 실인증/feed/CDN과 private 게시도 미실행이다. 기존 설치와 공개/비공개 원격 파일·태그를 바꾸지 않았다.

부모의 독립 리뷰 뒤 입력 방식 선택, 운영 키/독립 public pin, signer, helper fingerprint/bootstrap, 제한된 인증, 설치 ownership/receipt, exact private RC Release/asset IDs와 최종 6 assets를 순서대로 확정해야 한다. 사용자가 부재 중인 현재 단계에서는 이를 생성하거나 활성화하지 않는다. 게시 순서는 draft와 immutable product hash 확정, ZIP upload 후 exact asset ID 확인, ID/hash를 바인딩한 descriptor와 서명, 나머지 assets 검증, bare `1.0.0-rc.1` prerelease/Beta publish로 조율한다. ZIP 교체·재빌드·asset ID 변경에는 descriptor/서명을 다시 검토해야 한다. 이 순서는 검토할 운영 계획이며 실행 결과가 아니다.

기본 cwd가 Astral Party 루트이고 소스가 BetterAstralParty 하위 폴더임은 확인했지만 앱 프로젝트 UI 소속은 확인하지 못했다. 허용된 다른 세션 대화 도구가 없어 메인 세션에 직접 질문하지 못했다. lock 존재만으로 실행 상태를 추정하지 않았다. Git 저장소 부재와 부모가 전한 GitHub 접근 현황은 운영 대상 확정 전 다시 확인해야 한다.
