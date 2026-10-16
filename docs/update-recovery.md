# Update recovery / 업데이트 복구 — 1.0.0-dev

이 문서는 1.0.0-dev 당시의 설계·검증 기록이며 아래 본문은 당시 상태를 보존합니다. 현재 사용 절차는 [README](../README.md), C# 빌드·패키징 절차는 [BUILD](BUILD.md)를 따릅니다.

This is a historical 1.0.0-dev design and verification record; the original text is preserved below. Follow the [README](../README.md) for current usage and [BUILD](BUILD.md) for C# builds and packaging.

현재 정책: [최종 실패 자동 OFF](update-failure-policy.md). coordinator/helper의 기록 우선·복구·OFF 저장·명시적 재활성화 통합을 검증했습니다. 운영 trust/auth/fingerprint는 미설정이며 실설치/실게임은 미실행입니다. 아래 체크포인트는 당시의 기록입니다.

This unshipped development build has no operational signing trust, authentication provider or helper fingerprint. `--apply` and `--recover` return `NotConfigured` before touching an installation. The local development candidate ZIP is for review and is not an installable release.

현재 미출시 개발 빌드는 운영 서명 신뢰·인증·helper fingerprint가 미설정입니다. 실제 설치에 적용하거나 복구할 수 없으며, 로컬 개발 후보 ZIP은 리뷰용입니다.

## Cancellation / 취소

Automatic download and apply after exit are independent settings, new configurations ON by default, with saved OFF values preserved. Cancel or disabling a queued apply synchronously sets a cancel-only event. The UI displays “Recording Cancellation” until its disk record completes; a failed record displays “Cancellation Record Failed” and blocks retry. Channel changes cannot bypass an unconfirmed cancellation. The helper opens the event before acknowledging readiness and keeps its own handle, so cancellation does not depend on the game staying alive long enough to flush the marker. An absent/pre-existing event does not authorize an apply. No process is force-closed.

자동 다운로드와 종료 후 적용은 별도 설정이며 새 설정의 기본값은 ON이며 저장된 OFF는 유지합니다. 취소 또는 큐에 등록된 적용을 끄면 즉시 취소 신호를 전달합니다. UI는 디스크 기록을 확인할 때까지 “취소 기록 중”을 표시하고, 기록 실패 시 “취소 기록 실패”로 재시도를 차단합니다. 채널 변경으로 이 대기를 우회하지 않습니다. helper가 신호를 연 뒤 준비 완료를 알리고 자체 handle을 유지하므로, 게임이 기록 전에 종료되어도 취소를 관찰할 수 있습니다. 신호가 없거나 이미 다른 객체가 존재하면 적용 권한으로 간주하지 않습니다. 프로세스를 강제 종료하지 않습니다.

## Guided recovery / 복구 안내

Once operational trust is deliberately configured, an unfinished transaction causes “Recovery Required” at startup. The UI shows a validated stage identifier when it can identify the handoff. Close Astral Party and the mod launcher first, then run:

```text
BetterAstralParty-UpdateHelper.exe --help
BetterAstralParty-UpdateHelper.exe --recover "<game INT folder>" bap-stage-<32 lowercase hex>
```

운영 신뢰를 별도로 설정한 뒤 사용할 복구 흐름입니다. 미완료 transaction이 있으면 시작 시 “복구 필요”를 표시하고, 확인 가능한 요청에는 검증된 stage 식별자를 안내합니다. 먼저 게임과 모드 런처를 종료하고, 위 명령의 경로와 stage를 해당 설치 값으로 바꿔 실행합니다. helper는 설치 root/파일 identity, journal, 원본/대체 파일 hash, 서명된 descriptor와 ZIP을 다시 검증합니다. commit-intent 전에는 원본으로 되돌리고, intent 후에는 확인된 새 버전으로 완료합니다. 백업과 journal은 삭제하지 않습니다. 게임이 실행 중이거나 plan/marker가 손상·변경된 경우 임의로 복구하지 않습니다.

| Result | Exit | Meaning / 의미 |
| --- | --- | --- |
| Committed / Recovered | 0 | Committed update or restored originals; distinct result text / 적용 완료 또는 원본 복원 |
| NotConfigured | 2 | Operational trust unavailable; no installation access / 운영 신뢰 없음 |
| Cancelled | 3 | Apply stopped / 적용 중단 |
| ManualUpgradeRequired | 4 | Ownership receipt unsupported / 소유권 receipt를 충족하지 못해 수동 업그레이드 필요 |
| Safety failure | 5 | Unsafe or uncertain state; no inferred overwrite / 안전 조건 불충족 |
| Failed | 6 | Unexpected failure / 예상하지 못한 실패 |
| FilesBusy | 7 | Exclusive file contention; does not establish helper liveness / 파일 사용 경합이며 helper 실행 여부를 단정하지 않음 |
| AlreadyClaimed | 8 | Consumed ticket cannot replay / 처리된 ticket 재사용 금지 |
| RecoveryRequired | 9 | Pending or invalid journal requires review / 미완료 또는 잘못된 journal |
| Abandoned | 11 | No matching prepared journal; no update performed / 해당 복구 journal 없음 |
| Invalid invocation | 64 | Command syntax / 명령 인수 오류 |

There is no in-game one-click recovery launch. Recovery uses this explicit CLI after the game exits. Legacy receipts that treat a shared loader as this mod's exclusive property are blocked; automatic ownership migration is not implemented. Completed work/staging cleanup and retention-limit guidance are also not implemented. Retained evidence has bounded limits; never remove unknown files or backups to bypass a failure.

인게임 복구는 상태와 명령 안내를 제공합니다. 원클릭 복구 실행은 구현하지 않았으며 게임 종료 후 명시적인 CLI를 사용합니다. shared loader를 모드 독점 소유로 취급하는 legacy receipt는 차단하고 자동 migration을 수행하지 않습니다. 완료 work/staging 정리와 보존 한도 도달 안내도 미구현입니다. 오류를 우회하려고 확인되지 않은 파일이나 백업을 삭제하지 마세요.

Verification uses fake installations, inert processes, mock HTTP and ephemeral fixture trust outside products. Native in-game appearance/input, real private GitHub access, full release packaging/signing and actual installation remain unexecuted. The latest evidence is `artifacts/automatic-update-development/helper-ui-recovery-20261002-1051/verification.md`.

The event protocol follows [Microsoft EventWaitHandle documentation](https://learn.microsoft.com/en-us/dotnet/standard/threading/eventwaithandle) and [CreateEventW handle lifetime](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createeventw). These API properties support the design; the fake-process tests provide the implementation evidence.

## Default-ON failure policy checkpoint / 기본 ON 실패 정책 체크포인트

The later user request supersedes the earlier default OFF policy. Config.Bind defaults and fresh coordinator preferences are ON; stored OFF remains OFF. `UpdateFaults` is a persistence prototype with immutable failure/operation records, independent configuration-save failure evidence and bounded explicit archival. Its primitive tests use real BepInEx config files only in fake installations.

The final-failure OFF behavior is **not connected** to the coordinator/helper yet. Bounded retries, failure-to-record-to-OFF sequencing, process-aware restart interpretation of active guards, cancellation/non-failure classification and explicit reactivation UI still need implementation and end-to-end tests. A live helper/game wait must not be inferred as a failure from a guard or lock alone. Automatic execution remains unavailable because operational trust/authentication/fingerprint are absent. The review candidate manifest records `failurePolicyIntegrated=false` and `installableRelease=false`.

후속 사용자 요청으로 새 기본값 ON을 반영하고 저장된 OFF를 보존했습니다. 별도 고장 기록/operation guard/설정 저장 실패 증거는 primitive 단계입니다. 최종 실패 자동 OFF와 제한 재시도, helper·coordinator 연결, 살아있는 프로세스 대기와 중단된 guard의 구별, 취소 비실패 분류, 명시적 재활성화 UI는 아직 연결·검증하지 않았습니다. 이 개발 후보를 설치·운영 가능한 완성품으로 취급하지 않습니다.


배포·최초 설치·legacy 수동 차단·정리 및 보안 리뷰 수정의 최신 경로는 [update-delivery.md](update-delivery.md)를 따릅니다. 과거 체크포인트의 코드 잔여 항목 표기는 당시 기록입니다. 현재 운영 trust/auth와 실제 배포·설치·실게임 검증은 계속 미실행입니다.
