# Combat diagnostics — 0.17.0

## 2026-10-01 — 0.39.0 native-crash follow-up

The retained 2026-10-01 01:24 dump reports a read access violation at
FlowSDK.dll+0x22fd68. The same offset also occurs before the dropdown change;
module-address candidates found in raw stack memory are not a symbolized unwind
and do not establish that the mod caused the crash. Its root cause is unresolved.
No FlowSDK hooks, patches, forced crashes or native input-handler changes were
added. No claim of a native-crash fix is made.

Opt-in settings diagnostics now record dropdown open/close begin/completion and
selection/application steps using setting IDs/values only. WER summaries use
the report's actual EventTime (UTC), reject old copied reports, and isolate
locked/rotated report files. These changes improve evidence collection, not
native access-violation containment. A reproducible next occurrence needs its
fresh WER/dump and session log; a controlled vanilla/mod comparison or native
symbolized unwind is still required to attribute the fault.

한국어: FlowSDK 접근 위반의 근본 원인은 아직 특정되지 않았습니다. 진단 ON
일 때 드롭다운 처리 단계와 실제 WER 발생 시각을 남기며, 접근 불가 보고서는
다른 보고서 수집을 중단시키지 않습니다. SDK나 원본 입력 동작을 변경하지
않았고 크래시 해결로 간주하지 않습니다. 재현 시 새 로그·덤프 확인이 필요합니다.

## 한국어

진단 ON일 때만 전투 번호(무작위 세션 번호 + 순번)로 공개된 입력과 결과를 연결합니다.
계정 ID·닉네임·채팅·손패·미공개 주사위·패킷·객체 전체는 수집하지 않습니다.
캐릭터 데이터 ID, 공개 능력 ID, 버프 중첩/지연/남은 턴, 적용한 피해 보정,
공개 공격력/방어력 범위, 공개 공격 주사위, 최종 수치, 계산 순서와 예상/실제 HP를 기록합니다.
카드 효과는 공개된 스탯/버프와 계산 입력으로 남습니다. 이 기능은 공개 카드 ID를
별도 추출하지 않으며, 손패를 검사해 사용 카드를 알아내지 않습니다.

일반 상태는 바뀔 때만 기록하고 성능 수치는 기존처럼 10초마다 집계합니다.
공격자 쪽 추가 스냅샷은 전투 단계가 바뀔 때 읽습니다. 파일은 약 1초 또는
16 KiB마다 묶어 반영하며 오류·전투 결과·OFF/정상 종료 시 즉시 반영합니다.
강제 종료 시 마지막 미반영 기록이 유실될 수 있습니다. 쓰기는 동기식이며,
디스크 지연까지 없어진다는 뜻은 아닙니다. 실제 게임 성능은 별도 측정이 필요합니다.

저장 위치: INT 게임 폴더의 `BepInEx/BetterAstralParty-Diagnostics/`.

- `current.log`, `previous.log`: 각 1 MiB 이하. 용량 도달 시 중단하지 않고 순환합니다.
- `combat-mismatch.log`, `combat-mismatch-previous.log`: 최근 HP 불일치 2건.
  각 기록은 최근 전투 이벤트 최대 128개/64 KiB와 최신 상태 최대 64 KiB를 담습니다.
  헤더를 포함해 약 132 KiB 이하이며, 잘린 기록은 표시됩니다.

불일치는 게임 결과 HP와 계산 HP가 다르다는 뜻이지 계산 오류의 확증은 아닙니다.
0.30.8부터 몬스터 공격으로 패배한 공개 결과에서는 원본 연출의 HP 최소 1 표시를 별도로 비교합니다. `expectedHP`는 계산값, `expectedDisplayHP`는 연출 보정값이며, 이때 `observedHP=1`은 생존이나 정확한 피해량의 증거가 아닙니다. 피해/KO 공식에는 이 표시 보정을 적용하지 않습니다.
회복/부활/다단계 처리와 과잉 피해는 여전히 구분이 필요합니다. 중간에 ON한 기록은
일부 입력이 빠질 수 있으며, 결과 수치를 읽지 못하면 불일치 파일도 생성되지 않습니다.
진단 OFF는 수집을 중단하지만 기존 파일을 삭제하지 않습니다. 공유 전 직접 검토하세요.
자동 업로드·게임 상태 수정은 없습니다. 제거기는 이 전용 로그도 백업 후 제거합니다.

## English

Opt-in only. A random session ID and sequential battle number correlate public
character/config IDs, passive skills, visible effects (stacks, delay, duration),
resolved modifiers, ATK/DEF ranges, revealed attack dice, final totals, operation
order and predicted/observed HP. No account IDs, names, chat, hands, hidden rolls,
network packets or object dumps. Card effects appear through public stats/buffs;
this feature does not extract played-card IDs or inspect hands to infer them.

Changed states only; performance totals every 10 seconds. Additional actor
snapshots are phase-boundary reads. Writes are batched at approximately one second
or 16 KiB, with immediate flush on errors/results/OFF/normal exit. Abrupt crashes
may lose pending lines. This is bounded synchronous I/O, not a guarantee of zero
frame-time overhead; in-game performance still needs measurement.

The game's `BepInEx/BetterAstralParty-Diagnostics/` holds two rotating ordinary
logs of at most 1 MiB each and two mismatch reports of at most approximately
132 KiB each. Each report retains at most 128 events/64 KiB plus up to 64 KiB of
latest combat states; truncation is explicit. OFF clears in-memory battle evidence,
not files. The uninstaller backs up and removes these mod-owned logs only.

HP differences may include healing/revival; zero HP cannot verify overkill.
Since 0.30.8, a visibly announced defeat by a monster compares against the native
minimum-1 HP display. `expectedDisplayHP` is separate from calculated `expectedHP`;
displayed HP 1 in this branch cannot prove survival or exact damage. Damage and KO
formulas are unchanged; no future result fields are read.
Mid-battle activation may lack earlier inputs, and an unreadable result cannot
produce a mismatch report. Review before sharing. No automatic upload or gameplay
mutation. Automated checks use native substitutes, not live gameplay verification.
