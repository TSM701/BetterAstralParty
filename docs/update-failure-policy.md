# 업데이트 최종 실패 자동 OFF 정책

2026-10-02 UTC. 개발 버전은 **1.0.0-dev**입니다. 새 설정의 자동 다운로드/종료 후 적용 기본값은 ON이며 저장된 OFF는 유지합니다. 이 문서는 기존 primitive 체크포인트 이후의 통합 구현을 설명합니다. 운영 인증, 서명 trust, helper fingerprint는 미설정입니다.

## 실행과 실패 순서

네트워크 요청 전에 root identity, 작업 nonce, 선택한 release context, 원래 게임 PID/생성 시각/session을 담은 operation guard를 CREATE_NEW와 flush로 저장합니다. 저장할 수 없으면 요청을 시작하지 않습니다. 준비가 끝나면 자신의 nonce와 일치하는 다운로드 guard를 archive하고, helper를 시작하기 전 별도의 Apply guard를 ticket에 묶어 저장합니다. 실패/operation/설정 저장 실패 기록은 덮어쓰거나 삭제하지 않습니다.

다운로드의 일시적인 네트워크 오류, timeout, rate limit은 **최대 두 번** 시도합니다. 다음 시도는 최소 60초 또는 서버 Retry-After 이후이며 대기가 15분을 넘으면 최종 실패로 처리합니다. 검증/크기 오류는 즉시 최종 실패입니다. 기다리는 동안 네트워크 요청은 없습니다. 자동 취소 후 같은 요청을 자동으로 다시 시작하지 않습니다.

최종 실패 시 coordinator는 즉시 두 실행 옵션을 OFF로 고정하고 실패 신호를 보냅니다. worker는 영속 실패 기록을 먼저 저장한 뒤 자신의 guard를 archive하고 두 BepInEx 설정을 OFF로 저장합니다. 실패 표지를 저장할 수 없으면 guard를 남기고 차단합니다. 설정 저장 실패는 별도의 표지에 기록하며, 설정 파일의 ON 값이 실패 latch를 우회할 수 없습니다. 채널/버전/인증 계정을 변경해도 latch는 유지됩니다.

helper는 BepInEx 설정 파일을 쓰지 않습니다. 적용 실패 시 영속 실패 latch를 먼저 저장하고, 독립적으로 재검증한 같은 descriptor의 transaction을 복구합니다. commit-intent 이전에는 rollback, 이후에는 forward commit을 완료합니다. 복구된 경우에도 실패 latch는 남습니다. 손상된 plan, 알 수 없는 identity, 복구 실패, 실패 표지 저장 불가는 RecoveryRequired로 차단하고 guard/백업을 보존합니다. OFF 표시만으로 복구 완료를 선언하지 않습니다.

## 실패와 구분하는 상태

| 상황 | 동작 |
| --- | --- |
| 운영 auth/trust/fingerprint 미설정 | 요청/실행 0; 옵션을 실패로 취급하지 않음 |
| HTTP 401/403 접근·인증 선행 조건 | guard 완료; 옵션 보존; 같은 context 자동 반복 없음 |
| 새 버전 없음/필수 asset 없음 | 다운로드 안 함; 옵션 보존 |
| 정상 사용자 취소 | cancel 이벤트 및 marker; guard archive; 실패 latch 없음 |
| 정확한 원래 게임/launcher/helper 생존 | 기다리고 새 작업 차단; 실패 기록/설정 OFF 저장 안 함 |
| 정상 종료 대기 한도 만료 | FilesBusy로 종료; guard archive; 실패 latch 없음 |
| final download/verify/prepare/apply 오류 | 실패 latch 우선; 두 옵션 OFF |
| journal 손상/복구 실패/owner 불확실 | 안전 차단·수동 확인; 자동 재시도 없음 |

lock 존재로 생존을 단정하지 않습니다. game/launcher는 exact image path + PID + creation FILETIME + session으로 검증합니다. helper.owner는 guard SHA에 묶인 별도 PID/생성 시각/session 기록이며 실제 설치 root의 helper image를 확인합니다. PID 재사용이나 확인 권한 부족은 생존으로 추정하지 않습니다.

cancel 이벤트와 failure 이벤트는 분리되어 있습니다. failure는 cancel도 함께 set하지만 helper는 일반 취소와 구별해 latch를 남깁니다. helper가 두 handle을 보유한 뒤 ready를 기록하므로 부모가 취소 marker를 쓰기 전에 자연 종료하거나 creator handle이 닫혀도 신호를 읽을 수 있습니다.

## 설정 경쟁과 다시 사용

worker가 두 ConfigEntry를 바꾸고 저장하는 동안 SettingChanged는 main-thread polling용 flag만 남깁니다. coordinator/native UI는 worker에서 호출하지 않습니다. ON 변경이 OFF 저장 완료 직전에 들어오면 그 변경을 기억하고 저장을 다시 보정합니다. helper와 설정 writer의 경쟁은 helper의 설정 쓰기를 없애 해결합니다.

기존 UpdateGet 버튼을 상태에 따라 **다시 사용 / Re-enable Updates** 또는 **상태 다시 확인 / Recheck Installation**으로 재사용합니다. 새 native panel/delegate는 추가하지 않았습니다. 다시 사용은 worker에서 기록·journal을 다시 검사하고 두 옵션의 ON 저장이 성공한 후 기록을 archive합니다. failure latch는 마지막에 archive합니다. ON 저장/ack 실패 시 latch를 유지하고 OFF 저장을 시도합니다. 진행 중인 helper가 ticket lease를 보유하거나 pending/corrupt journal이 있으면 재활성화를 완료하지 않습니다. 정상 재활성화만으로 같은 release를 즉시 자동 재다운로드하지 않으며, 명시적 다운로드나 새 release context를 기다립니다.

복구가 필요한 경우 게임/모드 launcher를 닫고 운영 trust가 설정된 helper의 안내된 --recover를 사용합니다. 성공 후 상태 다시 확인, 다시 사용 순서로 진행합니다. journal/실패 표지를 임의 삭제해 차단을 우회하지 마세요. 서명/복구 상태를 확인할 수 없거나 저장 위치가 막히면 수동 진단이 필요합니다.

## 검증과 남은 작업

[최신 검증 보고](../artifacts/automatic-update-development/helper-ui-recovery-policy-20261002-1203/verification.md)와 [전체 diff](../artifacts/automatic-update-development/helper-ui-recovery-policy-20261002-1203/changes.diff)에 근거를 기록합니다. 가짜 설치, ephemeral RSA fixture, 별도 fake game/launcher와 Framework helper로 검증하며 운영 개인 키/토큰은 생성하지 않습니다. native KR/EN 렌더링·입력과 실제 private GitHub는 미실행입니다.

코드 잔여: 첫 helper의 신뢰된 배포/설치 경로와 완전한 서명 update payload 포장, legacy/shared-loader ownership migration, 완료된 work/stage 보존·정리 도구와 한도 UX. 현재 정확한 8파일 receipt만 지원하고 완료 기록/백업은 보존합니다. discovery는 work 128개, stage 256개를 초과하면 안전 차단합니다.

운영/승인 잔여: private 인증 위치와 짧은 수명/사용자 인증 방식 선택, 운영 공개 trust key와 helper fingerprint, 실제 비공개 대상·접근 확인, 첫 설치·실게임 검증. 장기 토큰/개인 키를 모드에 넣지 않습니다. 추가 자격증명이나 지속 접근 확대는 별도 승인이 필요합니다. 로컬 후보 manifest의 failurePolicyIntegrated=true는 위 정책 통합에 대한 표시이며 **installableRelease=false**입니다.

English: Fresh defaults ON, stored OFF preserved. At most two transient download attempts. Persist failure evidence before recovery/configuration OFF; preserve an operation guard when evidence cannot be written. Helper never edits BepInEx preferences. Live owner waits, missing prerequisites and ordinary cancellation are distinguished from failures. Corrupt journals and failed recovery remain blocked. Explicit reactivation verifies safety, saves both ON, then archives failure last. Operational trust/authentication/fingerprint and live-game/private-GitHub validation remain pending.



배포·최초 설치·legacy 수동 차단·정리 및 보안 리뷰 수정의 최신 경로는 [update-delivery.md](update-delivery.md)를 따릅니다. 과거 체크포인트의 코드 잔여 항목 표기는 당시 기록입니다. 현재 운영 trust/auth와 실제 배포·설치·실게임 검증은 계속 미실행입니다.
