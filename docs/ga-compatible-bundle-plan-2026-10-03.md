# GA 호환 설치 묶음 재빌드 계획 — 2026-10-03

이 문서는 복구 후 새 설치 안전장치의 독립 리뷰용 계획이다. 새 GA/RC 릴리스, 실제 설치 동기화, 게임 실행 또는 원격 업로드를 완료하거나 승인했다고 표시하지 않는다. 구현·검사 근거는 artifacts/automatic-update-development/installer-bundle-guard-20261003-b1/에 고정한다. 현재 작업은 소스 수정과 격리 fixture에 한정한다.

## 기준 소스와 산출물

| 구분 | 현재 기준과 의미 |
| --- | --- |
| 활성 소스 | src/Plugin.cs의 1.0.0-dev, 설치 묶음 protocol 1. 현재 루트 소스의 고정 source-after/를 다음 빌드의 기준으로 삼는다. |
| 격리 개발 빌드 | 이번 루트 소스의 플러그인·실제 Framework 런처 빌드. 전체 설치 패키지/승인된 배포 후보가 아니다. 실제 설치나 루트 artifacts에 복사하지 않는다. |
| 실제 복구 설치 | 1.0.0-rc.1 / GS-20261003-B1 DLL SHA 2CB1F9C3F2E105E4593359E8367BBBA4DE25AF448BFCE9EFF2371C37CACC33E6; 런처 SHA C1361ED8598CB3067E84CD1C89125A8AC722EA8C3CEB4F7D694A4707AB5FC283. 복구 시점의 파일/API 검증이며 새 안전장치는 아직 설치하지 않았다. |
| 깨끗한 과거 RC 비교본 | on-dev-install-fix-20261003/source/, rc-source/Plugin.cs와 products/rc1-b2/BetterAstralParty.dll SHA 69A1BEBBE4C50AD1FA79024B7F4D52C55029C29B816B0E9CF1E2DCDCDBC0AFD5. 변경 비교용 보존 자료다. |
| 루트 artifacts/과거 GA | DLL SHA 2C6524A88482BFB6E394FBE0A9F6A0E3EE3E42FE15ED62577E33308184BDAD24, 구형 런처 SHA 25CAF5F38626545670BD34A5FB39C8193827499D204276003158630A24039C69. 새 GA 입력으로 사용하지 않는다. |

과거 1.0.0 바이트는 SemVer상 RC보다 높아도 승인된 새 배포가 아니다. 첫 안정판 목표는 사용자가 지정한 미출시 1.0.0이다. 중간 private 검증 후보가 필요하면 1.0.0-rc.2를 제안하며 부모와 조율한 뒤 별도 고정 빌드에서 변경한다. 이번 작업은 버전을 올리지 않았다. 같은 RC 원본/GS DLL은 서로 다른 바이트이므로 일반 재설치로 교환하지 않는다.

## 순서와 빌드 입력

1. 부모가 고정 diff의 DI/UN 항목과 제약을 독립 리뷰한다. 이후 변경은 새 revision으로 고정한다.
2. 현재 고정 소스를 새 private 출력 경로에 복사하고 승인된 후보 버전만 바꾼다. BepInPlugin Guid/Name/Version, protocol과 manifest version을 일치시킨다.
3. 플러그인은 이번 current-product-build-inputs.json의 75개 소스와 검증된 16개 참조를 기준으로 빌드한다. game interop은 검증된 기존 참조 사본을 읽었다. 런처는 tools/build-launcher.ps1 -OutputDirectory <새 격리 경로>로 Roslyn와 전체 updater closure를 Framework에 포함한다. 소스·참조·컴파일러·출력 hash와 진단을 남긴다.
4. DLL을 실행하지 않고 metadata를 확인한다. SettingsTraceProbe, GS-20261003-B1 및 임시 General write/flush 계측은 제외한다. 기존 DiagnosticLog, NativeCrashReport, 설정·업데이트 기능은 유지한다. 과거 G stage를 수정하거나 GA 소스에 섞지 않는다.
5. 공유 로더/게임 compatibility manifest와 catalog 기준을 감사한다. upstream loader archive를 사용하고 실제 플레이 폴더, 타 모드, 사용자 설정을 배포 입력으로 쓰지 않는다.
6. owned8뿐 아니라 실제 호출되는 installer/helper/transitive scripts, Mono.Cecil, 필요한 공용 loader·Steam 이미지·고지문·안내를 모두 inventory에 결합한다. PDB/fixture/테스트 소스/로그/백업/서명 도구 입력/자격증명/dump는 배포에서 제외한다.
7. schema/version/kind/경로/bytes/SHA와 build identity를 계산한 전체 묶음을 독립 리뷰한다. 승인된 정확한 identity만 tools/approved-install-bundles.json에 등록하고 catalog hash를 manifest에 넣는다. installer가 catalog를 재생성하거나 현재 receipt를 승인 증거로 채택하지 않는다.
8. 새 private descriptor/signature는 최종 버전·release/asset ID·ZIP 바이트에 바인딩한다. 과거 서명을 새 바이트에 재사용하지 않는다. 새 서명/자격증명은 별도 승인 범위에서만 진행하며 이번에는 수행하지 않는다.
9. 새 전체 묶음의 독립 fixture를 마친 후 실제 설치 동기화와 수동 실행 검증을 조율한다. 현재 게임 폴더는 그대로 둔다. private 원격 대상/접근권한/업로드 파일도 부모와 조율한다.

catalog는 신뢰된 전체 패키지에 포함되는 검토 기록이며 암호학적 인증이 아니다. hash·API·JSON까지 다시 쓸 수 있는 악의적 패키지를 이 script만으로 인증하지 않는다. 기존 updater의 공개 신뢰 pin과 메모리 인증을 유지하고 장기 토큰/비밀키를 모드에 넣지 않는다.

## 설치·제거 규칙과 예방 범위

- cmd/bootstrap/direct PS 및 normal/UpdateOnly의 실제 쓰기 경로는 공통 검사를 통과한다. source csproj 예외를 제거했다. 루트에는 승인된 manifest가 없고 catalog가 비어 있어 구형 artifacts 재설치가 실패한다.
- FirstInstall, Upgrade, 동일 owned8 SameBuild, exact plan + fresh y의 ReviewedRecovery를 구분한다. current/high-water 아래 일반 설치 및 같은 버전의 다른 owned8은 거부한다. 빈 입력·계획 불일치·손상은 기존 파일을 보존한다.
- 전체 source inventory의 읽기 lease를 유지한다. 필수 helper 누락·목록 밖 fresh loader·경로 alias/중복/reparse/hardlink·현재 상태 변경을 거부하고 같은 updater maintenance lease 취득 후 대상 hash를 재확인한다.
- receipt는 schema1/4속성/owned8을 유지한다. 새 build 필드를 넣지 않는다. first loaderBefore, 저장 OFF, high-water/복구 자료, 공유 loader/타 모드는 유지한다.
- 최신 receipt hash가 맞으면 source artifacts 버전이 달라도 정상 제거한다. 변경 DLL을 assembly 이름만으로 삭제하지 않고 잔존 파일·receipt를 알린다. 전용 cfg/log는 기존 정책에 따라 백업 후 제거한다.
- automatic rollback은 transaction이 만든 현재 상태만 되돌린다. 외부 바이트를 보존하고 안전한 나머지를 복구하며 pending journal/backup과 recovery-required를 남긴다.
- running prerequisite는 maintenance 전에 재확인/취소를 제공한다. force는 fresh y와 검증 PID/path/start/session이 필요하다. active helper는 기다림/재확인/취소만 제공한다.
- helper는 기존 공개 서명·root/file identity·receipt/committed journal로 확인된 pair만 선택 제거한다. 정상 구형 런처에 helper 검사 API가 없으면 검증된 외부 전체 묶음의 검사기를 사용하며, 그 입력을 확인할 수 없으면 helper를 보존한다. 변경/unknown helper는 보존한다. 파일 없는 shortcut-only + 비활성 update evidence는 검증된 외부 묶음 gate를 쓰고 진짜 pending은 거부한다.

새 런처는 표준 -File install-bootstrap.ps1/install.ps1/uninstall.ps1에서 과거 GuardModLaunch API로 유지보수를 시도하면 쓰기 전에 현재 패키지로 안내한다. 이는 새 런처가 설치되어 있고 해당 API/표준 파일명을 사용하는 과거 도구에 한정된다. 현재 실제 C136 런처에는 이 검사 자체가 없다. renamed script/-Command/gate 없는 옛 도구/전체 직접 복사는 우회할 수 있다. 보호 런처 이전에 실제 보존 old installer가 구형 DLL/런처를 덮어쓰는 한계도 fixture에서 확인했다. 모든 historical writer의 물리적 쓰기 차단을 주장하지 않는다. ACL·상주 서비스·Steam 원본 라우팅은 변경하지 않는다.

## GA 잔여와 미실행

- 기존 tools/package-beta.ps1 출력은 새 schema/kind/bytes/build identity/catalog/critical closure를 만들지 않는다. 그대로 포장하면 새 안전장치와 호환되지 않는다. 승인된 manifest를 생산/검증하는 포장 경로를 후보 단계에서 별도 구현·리뷰해야 한다. 이번에는 packaging을 실행하지 않았다.
- 실제 production catalog 항목은 0개다. 새 private 전체 묶음 등록과 metadata/compatibility 리뷰가 필요하다. fixture catalog를 production에 넣지 않는다.
- 사용자 General 비재현 보고로 추가 crash 추적은 일시 중지한다. FlowSDK 원인 해결/GS 안정성은 확정하지 않는다. 복구 후 실제 시작 및 한영 native 렌더링·글리프·클리핑·클릭·스크롤·업데이트 UI는 이번 작업에서 미실행이다.
- 새 후보에서 private GitHub Stable/Beta, 비동기 요청·캐시·offline/timeout/권한 만료·변경 사항/다운로드·현재 버전 비교·재시도/OFF 정책을 재검증한다. 과거 HTTP/transaction 수치를 이번 통과로 재사용하지 않는다.
- 자동 다운로드/종료 후 적용은 기존 별도 사용자 요청 범위를 유지한다. installer 결함 수정으로 자동 교체 범위를 새로 추가하지 않는다.
- 기존 bounded CardUi surface 실패와 matching focus 통합/실게임 보류는 남는다. native Unity Stage delegate injection을 넣지 않는다.
- disk full/전원 차단 및 모든 cfg/receipt/commit write 실패 경계는 이번 installer fixture에서 전수 검사하지 않았다. PowerShell rollback의 개별 검사와 다음 write 사이의 비협조적 writer까지 완전히 막는 OS 강제 경계는 없다. 불명확 상태는 백업을 보존하며 추가 리뷰한다.
- project UI 소속과 다른 세션 진행 상태를 직접 확인할 지원 대화 도구가 없어 로컬 근거로 감사했다. 실제 source cwd는 확인했지만 project UI 연결과 같다고 단정하지 않았다.
- 실제 install/uninstall/게임/Steam profile 변경, GitHub/Library 업로드, public 전환, 태그, 새 릴리스, token/signing-key 접근은 이번 작업에서 하지 않는다.

## 이번 검사

최종 b10은 58 scenario × 2 harness 환경의 116개 기대 결과를 통과했다. b8의 106개와 JSON root/catalog/API signature를 추가한 b9의 114개도 통과한 중간 결과로 보존했다. 실제 Framework/native gate와 파일 copy/delete/receipt/backup/rollback을 사용했다. plugin metadata assemblies, compatibility/registry/Steam/process APIs는 격리 대체다. cmd는 양 harness에서 Windows PS5.1을 호출하므로 별도 PS7 cmd 구현으로 계산하지 않는다. old writer의 의도된 덮어쓰기/정상 제거 한계 사례도 포함하므로 모두를 사전 거부나 실게임 안정성으로 해석하지 않는다.

실제 steam-session의 합성 프로세스12×2, 경로보호6×2 및 release-version 검사는 PS5.1/7에서 통과했다. 기존 운영 서명 helper를 공개 키로 검증해 격리 install/선택 제거·변경 이미지/receipt 보존·pending 거부를 확인했다. helper를 실행하거나 signing key를 읽지 않았다. 중간 fixture 준비/문법/형식 실패와 PS7 정수 타입 발견도 보존한다. 이번 결과는 전체 기존 test suite 재실행이 아니다.


## 2026-10-03 b2 현재 규칙 (b1 유지보수/high-water 설명 갱신)

B1-S01: 모든 유지보수 경로는 승인된 전체 incoming bundle의 외부 검사기를 사용한다. uninstall의 updater 흔적 없는 상태와 WhatIf, install의 pending PowerShell journal 복구에서도 설치된 launcher assembly를 로드하거나 호출하지 않는다. manifest 전체 파일을 핀하고 catalog/API/build identity를 확인한 후 incoming 검사기의 SHA-256을 다시 확인한다. receipt가 선언한 런처가 없거나 변조되면 제거 전에 중단한다. receipt로 설명되지 않는 런처는 승인된 incoming 검사기와 동일한 해시일 때만 기존 파일 제거 계획에 포함한다. 정상 receipt에 기록된 구 C136 런처는 실행 없이 selective removal할 수 있다.

B1-S02: installer는 별도의 PowerShell high-water parser 대신 실제 UpdateTransaction.Water를 호출하는 ReadInstallerState API를 사용한다. 동일 파일 핸들에서 버전과 원본 해시를 읽고, 실제 NTFS root identity, 2,048 byte 제한, 정확한 LF 구분과 마지막 LF, descriptor SHA 형식을 그대로 적용한다. reviewed recovery plan의 gameRootIdentity와 preflight 이후 root를 다시 검사한다. foreign root, CRLF, 마지막 LF 누락, 빈/과대/손상 기록은 bytes를 보존한 채 중단한다.

IG-TP01: uninstall receipt의 object/4개 필드/owner/schema/loaderBefore, files 배열, 최대 owned8, 허용 경로, 중복, hash scalar를 검증한다. 부분 또는 빈 receipt에서 소유권을 확인할 수 없는 기존 파일이 남으면 원본 receipt도 보존하고 부분 제거 결과를 안내한다. malformed receipt 원본도 보존하며 고정 allowlist의 개별 source hash fallback만 사용한다.

격리 검증: PS5.1/7에서 86 scenario씩 총 172개 예상 결과를 만족했다. untrusted launcher probe 호출은 모두 0회였다. 실제 updater parser가 invalid high-water를 전후 모두 거부했고 정상 high-water를 받아들였다. synthetic process 12개씩, path 7개씩, release/version 및 PS parser 검사도 통과했다. net8 native transaction fixture는 44 forward mutation, 36 rollback, 6 forward recovery boundary와 separate-process recovery를 포함한 1,985 assertion을 통과했다. 현재 dev plugin과 전체 Framework launcher build는 경고/오류 0개다. plugin은 1.0.0-dev/protocol1이며 GS probe type/marker가 없다.

검증 중 실패와 한계: 초기 accessor 중복 compiler 실패, legacy fixture 입력 경로 누락, PS5 module path 누락, pending fixture가 updater lock까지 복구 대상으로 잡은 오류, metadata harness Cecil 미로드를 보존했다. r2는 144/148 예상 결과만 만족했으며 최종 r4와 혼동하지 않는다. native 기존 empty-trust 검사는 production 공개 pin 도입 후 가정이 맞지 않아 명시적 빈 trust 집합을 쓰도록 고쳤다. ephemeral fixture private key는 메모리에서만 생성했고 operational private key/credential에 접근하지 않았다. native UI/실게임/실제 Steam/운영 HTTP/auth/cache/UI, 모든 installer cfg/receipt/commit write 실패, 비협조적 OS writer race는 이 검증으로 통과했다고 주장하지 않는다.

운영 제약: 승인 catalog는 계속 빈 배열이다. 실제 packager/catalog/whole candidate는 미완성이며 이 변경은 GA 완료나 실설치 배포가 아니다. 기존 b1 1,201 파일, 이전 8 stage 2,662 파일, 실제 설치52 파일을 보존한다. 실제 설치/제거/Steam, GitHub/Library 업로드/릴리스/태그, 자격증명/운영 signing key 작업을 수행하지 않았다. 새 ReadInstallerState API가 없는 protocol0 입력은 외부 검사기로 사용할 수 없으며 별도의 검토된 최신 전체 입력이 필요하다. 구 PowerShell pending journal의 path 기반 사용자 확인 복구는 기존 범위이며 root identity를 journal 자체에 소급 추가하지 않았다. 부모의 b2 독립 재검토와 배포 후보 승인 작업이 남아 있다.


## 2026-10-03 GA 1.0.0 unsigned 전체 후보 준비

b2 독립 보안/테스트 재검토가 기존 세 P2를 닫은 후 부모 요청에 따라 GA 1.0.0 격리 DLL·런처·helper와 호환 packager를 준비했다. root catalog는 빈 배열이며 후보도 self-approval하지 않는다. helper 서명 원문·해시를 고정하고 signature 추가 뒤 새 identity의 독립 승인, 실제 release/asset ID를 사용한 update descriptor 서명 순서를 분리했다. 기존 source/README 설명과 자료를 보존했다.

정확한 계약·성공/실패/미실행·소유자 단계는 docs/ga-whole-candidate-contract-2026-10-03.md와 artifacts/automatic-update-development/ga-whole-candidate-20261003-b1의 고정 인계에서 확인한다. source/build는 1.0.0/protocol1이나 실제 설치는 RC.1 GS 진단본 그대로다. 실제 설치/게임/Steam/원격 게시 및 운영 key/token 접근을 하지 않았다. 배포 가능 또는 GA 완료를 뜻하지 않는다.
