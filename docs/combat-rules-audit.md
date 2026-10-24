# Combat rule audit

## 2026-10-24 — two Shooting Star relics

With both relics held, the server may resolve the release as one hit or as one hit per relic;
no release with both has been observed. Without incoming modifiers both give the same sum.
With them they differ (Weakness Mark +1: 1 + 2 + 1 = 4 as one hit, (1 + 1) + (2 + 1) = 5 as
two), so the indicator shows the span of both, and combat-audit names both as rules until a
logged release separates them.

Resolved the same day: the user confirmed from play that the two relics hit one after the
other, each its own hit. The indicator now shows that sum, and combat-audit keeps "one hit"
only as the alternative rule.

## 2026-10-23 — Shooting Star incoming modifiers, Powerful Attack, reverse inference

Observed in game: gold Shooting Star against a boss (HP 83) holding 15 Starlight, with
Weakness Mark 1140102 ("damage taken +1") on the boss, dealt 18; the indicator said 17.
2 + 15 + 1 = 18 fits; "own Starlight counted" (2 + 16) also fits this one release but
contradicts the 2026-10-22 observation. A players' guide (TapTap 星趴pve攻略 基础篇,
2025-12) states fixed damage ignores defense and takes vulnerability/reduction. Release
damage now takes the monster's incoming modifiers; a per-hit cap stays a range until seen.

Powerful Attack (10010) Params [6, 6, 50]: "final ATK +50% for this battle". Card advice
and the hover KO view now bound floor..ceiling of the boosted final ATK instead of
withholding the card (the guide says rounded up; not observed).

The client has no PvE rule code (rechecked in the 2026-10-15 runtime: damage names belong
to presentation handlers, the separate single-player mini-game and the scripted tutorial).
The server sends HeroHpChangeS2C (change, before/after HP, damage type, killer) and the
client keeps the applied change in BattleProperty.ChangeHP. Releases and KO hits now log
it, and combat-audit tests candidate release rules against the results.

## 2026-10-23 — boss targeting lock 10331101

The 深度改造 boss (hero 1033, skill 103311) shows buff 10331101, described as ignoring attacks,
being unselectable and taking 0 damage. In play it cannot be attacked until a set monster is
defeated three times, and the icon stays (progress 0) afterwards. combat-audit hit 19b0f3c7a197-145
had it as defender: ATK 35, DEF 5, die 2 against 9 HP was lethal, while the model predicted 0.
Any fight with it as defender happens only after the lock opens, so 10331101 no longer zeroes a
hit. Property stacks of every public property are now logged so 10009 (改造) can be audited.

## 2026-10-22 — Shooting Star stack order observed (1.5.3-dev.21)

Observed in game: Starlight granted by a Shooting Star release is not part of that
release's damage. The gold boss bonus is the Starlight held before letting go, so the
ranges shown since 1.5.3-dev.7 (including both relics together) are now exact values.

## 2026-10-21 — spectator perspective and Shooting Star (1.5.3-dev.7)

Spectated fights skip the card step: the logged step ladder is 1 -> 2 -> 4 -> 5 -> 6,
so spectators first saw the dice indicator at the revealed roll. Step 4 now shows
the pre-roll view for spectators. Spectators of a player defender now see that
player's survival view (KO view when the defender is a monster); titles became
"KO 주사위 조건" and "생존 주사위 조건". No threshold arithmetic changed.

Shooting Star (relics 50078/50079, #14): STRRelic describes release damage 1/2 and
one Starlight stack; Relic Params are [-1, 1] and [-2, 1], and gold adds the current
Starlight stack count against a boss. The client only plays the server's HP change
(BuffShow_5007801/5007901); no client formula exists. Boss = Hero.MonsterType Boss (2)
or PveBoss (10) from the protobuf enum. Unverified and shown as ranges: whether the
gold boss bonus counts this release's own stack, and whether both relics fire
together. The BWIKI page for 金色飞星 repeats the description without settling it.

## 2026-10-21 — uncounted effects and combat audit (1.5.3-dev.6)

Combat rules are resolved by the server; the client has no damage formula and
BuffInfoConfigure carries no effect semantics (type, tag, rounds and display
only). Exact agreement therefore rests on per-buff handlers plus observation.
Until now, any shown buff without a handler silently counted as zero.

`src/CombatBuffAudit.cs` now lists the 21 modeled buffs and 107 buffs audited
as neutral from the original STRBuff descriptions of the 159 shown buffs in the
2026-09-26 table snapshot: they change only public ATK/DEF/max HP (already in
the native totals), movement, coins, cards, healing or damage outside the hit,
combat avoidance or progress counts. The remaining shown buffs (for example
5007002 大铜锣 "received damage -1", 2002001 "cannot dodge", 1251201 "dodge +1",
1060101 lifesteal, HP links/floors and conditional ATK) and every buff added by
later game updates are "not counted". PublicCombatEffects collects them from
both combatants into `HitModifiers.Unknown` without changing any number; the UI
marks them and diagnostics log `combatEffects.unknown.<role>`.

`Developer.cmd combat-audit` replays logged hits through the current model and
groups outcomes by buff. It replaces the old `combatEffects.unmodeled` list,
which mixed neutral stat buffs with real gaps. No in-game verification of the
new markers was performed in this entry.

## 2026-10-02 — 0.39.1 observed exchanges and failed-dodge capture

The retained ed7103a231be session provides 91 final HP comparisons: 72 exact
nonlethal HP-loss checks and 19 lethal/display-censored results, including two
native monster-defeat HP1 displays. None has an HP mismatch. These public scalar
inputs and observed HP are now replayed in the existing build; lethal results
assert lethality/display projection, not exact overkill or permanent death.

Combats 3 and 96 expose a different fault: defense base 2, visibly rolled 1/2,
then displayed final defense 0, but the old diagnostic calls the choice Defend.
Extracted BattleShowDirector.Dice_Show confirms the hit+dodge branch publishes
showResult=2, then calls RefreshPoint_Defender(2, 0, false). That method resets
changeState to 0. IsDodge now recognizes the public result as well as the icon;
the final-number reader waits for the zero replacement. Zero in this final path
means failed dodge, not a new allowable pre-roll face. Final damage uses full
attack without the defense minimum, and defense-only KO conditions stay cleared.
Successful dodge, actual defend and pre-roll dice restrictions are unchanged.
No BattleRole result/hidden die is read by the mod.

Independent enumeration now includes HP floors and triggered on-hit additions,
including zero-clamp, immunity and cap interactions, across 4,224 scenarios /
152,064 dice pairs and all advisor paths. These checks establish model consistency;
they do not establish unaudited mixed-effect server ordering. The latest session
contains no restricted-dice cases. Unknown weights/overlapping special rules,
revival and multi-hit semantics remain outside a full GA calculation certification.

한국어: 최신 91건의 HP 결과는 일치했습니다(정확한 피해 72건, 과잉 피해를
확인할 수 없는 치명적 결과 19건). 다만 회피 실패 시 원본 UI가 방어 아이콘과
0 수치로 바뀌는 것을 방어 선택으로 오인했습니다. 공개된 회피 결과까지 함께
읽고 최종 0 수치를 기다리도록 수정했습니다. 성공 회피·실제 방어·최소 주사위
조건 정책은 유지합니다. 특수 주사위 실전 및 미검증 복합 효과는 GA 검증 완료로
간주하지 않습니다.

## 2026-10-01 — 0.39.0 public dice support

Current behavior supersedes the historical threshold-range presentation below:
KO/survival uses one best-case achievable pair, with a visibly revealed attack
die fixed for every role. Genuine impossible/certain outcomes still use all
public outcome bounds; unsupported rules remain unknown.

The shared snapshot now carries achievable attack, defense and dodge faces.
Live public passive 104011 fixes dodge to Skill.Params[1]; 106311 fixes attack
to Params[0] without active public Mind on the defender, otherwise permits
3..Params[0] (the lower bound is from original STRSkill106311). Active public
buff 1250101 limits its owner's combat die to Skill12501.Params[0]. Schema and
buff associations are checked against the live catalog. Delayed Mind and zero
public progress do not activate the conditional range. No hidden rolls or
private buff parameters are read.

Achievable faces do not prove their probability weights. Restricted multi-face
attack/defense rules therefore retain usable KO/survival bounds, but do not
produce invented exact card/encounter percentages. After public attack reveal,
defense advice bounds every allowed defense outcome and retains the existing
single worst-case recommendation. A fixed dodge face has an exact outcome.
Ordinary dice retain the existing equal-face model. Zero-roll buff 1250201 and
the overlapping 106311/1250101 priority remain unsupported; observed final
damage stays independent of pre-roll uncertainty. Existing additive/cap effect
ordering is unchanged and mixed-effect server semantics remain unaudited.

Native-shaped substitutes verify live parameter changes, schema rejection,
Mind expiry/progress, unknown weights/precedence and final-result independence.
Independent enumeration checks 72 restricted-face KO/survival scenarios.
These checks do not prove runtime rendering or all server combat rules.

한국어: 가능한 주사위 눈과 확률은 별개로 취급합니다. 제한 눈은 KO·생존
최소 조건에 반영하지만 분포를 모르면 카드·조우의 정확한 확률을 만들지
않습니다. 공개 공격 눈 이후 방어 추천은 가능한 결과의 범위를 사용합니다.
0 눈과 미검증 복합 우선순위는 미지원이며, 공개 최종 피해 계산과 분리됩니다.

## 2026-09-29 — 0.30.15 threshold endpoints and final refresh

The latest retained 0.30.14 sessions contain two final comparisons: HP10,
ATK15/DEF2 -> HP0 (exact overkill remains unverifiable), and HP6, ATK6/DEF6
-> HP5. Both match the existing damage resolver. The retained mismatch files
are from 0.30.7 and describe the already-audited monster defeat HP1 display;
they are not evidence of a new damage-formula discrepancy.

The first session exposes a threshold representation problem: public ATK4..9,
attack die6, DEF1 and HP10 previously displayed only the possible KO limit <=4.
Actual limits across that public range are <1 through <=4. The shared threshold
solver now retains both endpoints for the same displayed attack die; survival
uses >6 for an impossible endpoint rather than implying that six suffices.
Valid exact thresholds at one and six retain inclusive comparisons. No random
card distribution is invented. Existing global No KO/Certain KO handling stays.

Native Dice_Show changes showPoint to 2 before arithmetic, and later writes the
actual totals through RefreshPoint_Attacker/Defender. The KO view previously hid
and held its pre-total condition without incorporating those revealed totals.
It now receives computed final damage from RemainingHpUi only after the existing
public-number readiness gate; no future result fields or hidden rolls are read.
The held view then shows the resolved KO outcome instead of the old threshold.
Final HP diagnostics remain independent of optional KO rendering errors.

6,048 enumerated public-bound scenarios, sentinel/exact-endpoint checks and both
latest log replays pass, together with the existing build/regression checks.
Threshold diagnostics now include damage bounds, defender bounds, possibility
mode and resolved damage. No unsupported changes to hit-effect ordering were
made; live rendering and unmodeled combat effects still require verification.

## 2026-09-28 — 0.30.8 monster defeat presentation

The retained 0.30.7 session has 62 result comparisons: 43 matching nonzero HP,
16 zero-HP results (overkill unverified), and three displayed-HP mismatches.
Combats 41/59/61 predict lethal damage 7/10/12 against HP 7/9/12 but display HP 1.
Native `BattleShowDirector.PlayDefenderDead` calls `ShowResultTip(true)` and,
when attacker characterType is 2, passes `max(defenderHP, 1)` to `RefreshLife`.
Thus the displayed 1 is not evidence of a survival passive or a damage reduction.
Diagnostics now compare the native display projection only with a visible defeat
caption at phase 6 and a monster attacker. Raw calculated HP remains in the log;
the clamped branch is marked overkill-unverifiable. No result protocol fields are
read and the shared damage/KO formula is deliberately unchanged. Regressions replay
all three cases and retain detection of a genuine nonlethal difference.

## 2026-09-28 — 0.29.3 retained mismatches and possible thresholds

- Latest retained session has seven result comparisons, all HP-consistent (two
  lethal outcomes cannot verify exact overkill). Current session ends before its
  result. The two retained mismatch reports are from 0.22.4, not 0.29.2.
- Both are Sykes (passive 12912) against monster 1067: ATK/DEF 13/11,
  HP105 with mark +1 and three erosion stacks predicts 6 but loses 7;
  ATK/DEF 7/5, HP17 with mark +1 and no erosion icon predicts 3 but loses 4.
  Skill12912's public description applies one erosion stack when dealing damage;
  Params[4]=1, six parameters, two associated buffs (1291201/1291202).
  These independent nonlethal observations support including the triggered stack
  in that same hit. The shared resolver now includes a separate on-hit gain from
  this public source passive, rather than inflating existing icon stacks or
  assigning +1 to every mismatch. Successful dodge, immunity and zero damage
  do not trigger it. Caps and HP floors remain enforced.
- This is a log-backed ordering correction, not access to the server's damage
  implementation. New live validation, especially mixed reductions/caps, remains
  necessary. The earlier existing-stacks-only assumption is superseded here.
- If no guaranteed threshold exists but KO/survival is possible within public
  card/stat bounds, show the best-case numeric threshold explicitly titled
  Possible KO/Survival conditions. Never substitute a midpoint or hidden outcome.
  Impossible/certain outcomes and unsupported input remain distinct.

## 2026-09-27 — 0.17.3 revealed-roll and retained-log audit

- Current 0.17.2 session: two result records, zero HP mismatches. Both are
  lethal (10 HP / 14 predicted damage and 9 HP / 10 predicted damage), so
  exact overkill remains unverifiable. Previous 0.17.1 session has no result
  records. QAcurrent.log is still the earlier 0.15.0 session: 82 results and
  nine original mismatches, eight addressed by existing mark/culprit fixes;
  one HP-floor case remains unattributed, not evidence of a new regression.
- At 03:17:47 the attack die was visibly 2, but the attacker indicator still
  reported AttackDie=5, DefenseDie=1. The role-specific UI mapping replaced
  the revealed die with zero (pre-roll search). Removed that mapping for all
  roles and superseded the conflicting persistent instruction. Visibility,
  raw-die-versus-total guards and the two-second reading hold are unchanged.
- That encounter has public ATK 5..14, DEF0, HP9. Fixing roll2 yields no
  guaranteed KO threshold, but KO remains possible with the unresolved card
  gain. Do not replace the public range with its maximum or use later final
  totals early. Added replay checks for this distinction and higher-roll
  defense thresholds, plus independent enumeration of every revealed roll
  with random attack/defense card bounds, immunity, damage caps and bonuses.
- Reviewed shared final damage, visible defense/dodge, card, encounter and
  KO/survival paths. They already use HitDamage; no additional arithmetic
  change is justified by these retained results. Existing single-hit and
  unknown-effect/ordering limits still apply. No claim of exhaustive native
  mechanic coverage or in-game rendering verification.

## 2026-09-27 — 0.16.1 QA/local mismatch audit

Inputs: QAcurrent.log (0.15.0), local diagnostics current.log and previous.log.
QA and current contain the same nine mismatched battle outcomes, not eighteen
independent failures. Previous contains five more. Result HP cannot validate
exact overkill damage or distinguish post-hit healing without source evidence.
Each current log contains 82 result records; previous contains 84. Re-evaluating
the two confirmed modifier corrections removes 8 current and 1 previous
mismatches, with no newly mismatching result records. The remaining 1 current
and 4 previous results are the unattributed HP-floor cases below.

Verified corrections in the shared reader/resolver:

- Bonnie (character 127) applies Mark, public property 10006. STRBuff's original
  text is `受到伤害+1`, not per-layer damage. Positive stacks enable +1 once.
  Six independent nonlethal observations with 2/3/4 stacks now match: HP
  12→2, 121→101, 94→62, 62→30, 19→16 and previous-session 23→11.
  This supersedes the earlier per-stack assumption in this document.
- Culprit 10671302 says `受到伤害+2`. Read Skill 106713 Params[5], with the
  15-parameter / three-associated-buff schema checked. Progress=0 is active,
  not zero magnitude. Its native ATK/movement/card stats are not added again.
  Three observations match: HP 10→5, 11→7 and 12→9 (minimum defended hit).
- Skill 100312 says `攻击目标角色时，至少保留其1点生命值`. Read the source's
  RoomPlayer.GetBattlePassiveSkillId(), which routes through CharacterHandle
  and the native hero/monster/talent selection. For target CharacterType.Hero
  only, Params[0] limits actual HP loss after incoming modifiers/cap. Shared
  final damage, card previews, encounter risk, KO/survival and dodge failure
  use that same HP floor; successful dodge remains zero.

Five old HP=1 outcomes fit the nonlethal passive but remain **unattributed**:
source passive IDs were not recorded. Conditional replay tests are not proof
that this passive caused those events. New diagnostics record source passive IDs,
target type and HP floor to distinguish it from healing/revival on the next run.
No in-game replay or claim of universal effect coverage. Existing unsupported
effects/ordering limitations below remain; no released archives were changed.

Regression checks replay all nine confirmed outcomes through the production
effect reader plus five conditional nonlethal outcomes, and cover delayed buffs,
live culprit parameters, target types, HP floor, and shared advisor propagation.

## 2026-09-26 — 0.14.2 Shushu recommendation policy

User explicitly requires Dodge without exceptions when the defender has active
Shushu shield. Preserve buff 1071101 identity in the shared public snapshot,
independently of combined reduction magnitude. Delayed/absent buffs do not set
the flag. VisibleCombatAdvisor applies this policy before availability, ties or
numerical preference, for both local and spectator advice. Titles and summary
agree with the recommendation; unavailable-dodge hover remains truthful. Native
buttons, probability/damage formulas, encounter/card outcome enumeration and
KO/survival conditions are unchanged. This is a user-selected policy, not a
claim that Dodge is mathematically optimal or selectable. Reader and policy
tests cover delayed shields, combined effects, unavailable dodge, ranged stats,
ties, unfavorable outcomes and unchanged numerical outputs.

## 2026-09-26 — 0.14.1 retained-log audit and phase-input fixes

Inspected INT diagnostics current/previous, INT Player/Player-prev, loader logs,
and repository archived logs. Only the retained 0.13.1 diagnostic session contains
paired recent predictions and visible outcomes:

| Local time | Before HP | Final ATK/DEF | Modifier | Predicted damage | Result HP |
| --- | --- | --- | --- | --- | --- |
| 21:05:01 | 10 | 13/2 | +1 mark | 12 | 0 |
| 21:05:20 | 9 | 16/3 | 0 | 13 | 0 |

Both KO outcomes agree. Neither proves exact damage because overkill is not
recoverable from HP zero. Historical files do not retain a complete battle
ledger; no definite damage mismatch can be attributed to a formula from these
records. Do not tune constants or effect order to manufacture an explanation.

Code inspection found an independent input bug: GameUi's defense/dodge advice
accepted the point label after it became the final attack total. Totals 1–6
passed input validation and were added to base ATK again. Native Dice_Show IL
sets showPoint=2 for arithmetic; only showPoint=1 is a public raw roll. Reused
CardKoMinimum.RevealedDie in this sibling caller, with all-six-face regressions.

The final-damage view now refreshes public modifiers at its existing visible
final-number gate instead of retaining an earlier phase snapshot. It still
freezes once and never reads future result fields. Opt-in damage.result records
predicted damage, expected HP, visible result HP and hpMismatch once per result.
HP mismatches can also include result-phase healing/additional effects; they
are not automatically classified as arithmetic errors. A zero result cannot
verify exact overkill. Scene/actor/phase resets clear the per-hit observation.
No new UI wording or math ordering rule is introduced. Offline replay and wiring
tests are not in-game verification; all previously documented coverage limits
still apply.

## 2026-09-26 — 0.14.0 source-dependent erosion mark

Original STRBuff 1291202 says each stack adds +1 damage received from abyss
tentacles and Sykes. Buff 1291202 resolves name 12912021 and description 1291202;
Character 129 maps to Sykes with PvE skill 12912. That skill's Params[4] is the
number of stacks applied, not a damage multiplier, so it is deliberately NOT
reused as the per-stack +1. The narrow buff/source/+1 mapping comes from the
original description; stack counts and canonical CharacterHandle.Id are live.
RoomPlayer.characterConfig and CharacterHandle's constructor were verified in
runtime IL; cosmetic skins do not determine the damage source.

PublicCombatEffects.Read now requires the damage source. All four production
call sites pass it in the correct direction, including encounter counterdamage.
Existing public stacks affect the current hit; delayed effects and duplicate
IDs are excluded. Missing source identity with nonzero erosion fails closed.
Sykes dice-combat damage is covered; SummonShow_12922 delegates to the separate
12921 field-summon presentation and no field-summon prediction is added here.
Original client metadata/descriptions do not reveal server compound-effect
ordering; additive-before-cap remains the documented model, not server proof.

Production-reader substitute tests cover 0/1/2/6 stacks, other heroes/monsters,
missing/invalid source, delays, duplicates, reductions, cap, successful dodge,
KO and survival thresholds. Wiring checks enforce source direction for combat,
encounter and final damage. In-game result verification remains pending.

## 2026-09-26 — 0.13.2 conditions-only presentation

Removed the damage-title branch, damage/HP string generation, and hypothetical
6/1 or 1/6 dice substitution for all roles/phases. Titles depend only on role.
An absent threshold is —; a publicly observed die can still be shown as = N.
Damage bounds are retained internally only to distinguish an impossible outcome
from a missing guarantee: KO impossible requires best-case damage < HP;
survival impossible requires best-case damage >= HP. Unknown inputs remain
unknown. Tests reject reintroducing a damage title or damage/HP formatting here.
Historical sections below describe superseded displays, not current behavior.

## 2026-09-26 — 0.13.1 attacker threshold consistency

Local attackers now query all six hypothetical attack faces for the minimum
KO pair during both card selection and revealed-roll phases. Live public
post-card stats are still refreshed; the observed roll is a separate caption
and continues to control the reading hold. A required threshold is not a claim
that an already-lower roll can KO. Defender/spectator calculations still fix
the public roll. Damage fallback is retained only when the chosen calculation
has no valid pair. Shared role selection replaces duplicate native role reads;
outdated display-wiring assertions have been replaced. Regression checks cover
all roles, unrevealed/revealed faces and the reported 2-versus-required-6 case.

## 2026-09-26 — 0.13.0 local-defender survival conditions

Role 1 uses Survival rather than Calculate, both reusing DamageRange. Survival
requires maximum damage strictly below HP (not the complement of minimum-damage
KO). Select the lowest qualifying defense die and greatest allowed attack die,
with attack ≤ M and defense ≥ N. The bounds must hold together. Revealed rolls
fix attack to the public die. Hover includes lowest defense-card gain. Missing
survival pairs use numeric damage at attack 1 / defense 6 (actual attack once
revealed); unknown effects still fail closed. Attacker and spectator behavior
is unchanged. Role is included in the display cache key, including scene reuse.
The offline simulator independently checks all 36 pairs, each revealed roll,
ranged stats, both card roles, reductions, caps, immunity and lethal HP equality.
These checks do not verify native pixel rendering.

## 2026-09-26 — 0.12.0 numeric no-threshold display

The display no longer uses × or "KO not guaranteed". If no guaranteed pair
exists, it shows attacker =6 (or the publicly revealed die), defender =1,
and the damage interval for exactly those dice beside defender HP. The title
switches from KO conditions to damage. These are comparison dice, not a claim
that either unobserved die has occurred. DamageRange is shared with threshold
calculation, using the same monotone HitDamage rules (bonus, reduction, cap,
immunity), worst/best public stat and supported card endpoints. No probability
distribution is assumed and no uncertain KO is labelled impossible. Unsupported
data still fails closed. Native inner-diamond restoration and inset fixes are
included. Offline checks do not replace in-game rendering verification.

## 2026-09-26 — 0.11.0 KO display overhaul

Damage rules are unchanged. Standalone native discs no longer inherit the live
dice prefab's groups/controllers/transitions or disappear when no threshold is
found. Roles and discs share the same column centre, with ≥/≤ inside each disc.
The revealed attacker shows = N: an observed die, not a promise of KO. Missing
thresholds use × and a shared "KO not guaranteed" status; unsupported input uses
? and "Insufficient data". Both preserve discs. These states are presentation
of the same supported public bounds, not additional effect coverage.

Checks cover both columns, pre-roll and revealed eyes 1–6, KO/no-pair/unknown,
role routing, attack/defense hover paths, spectator hand isolation, new card-phase
observation reset, resolution/scale bounds, fades and reading hold. Choosing
dodge clears defense-only conditions immediately; normal arithmetic still fades
after the existing hold. Display tests use native substitutes and pure layout/
state checks, not proof that every in-game animation or texture lifecycle is
correct. In-game rendering verification remains pending.

## 2026-09-26 — 0.10.0 staged implementation

This release implements the agreed first stage after the investigation below,
not the complete all-effects overhaul. `HitModifiers` is an immutable value
snapshot shared by defense/dodge, card and encounter advice, KO conditions and
the final damage display. Effect-only changes participate in existing cache
keys. Native displayed ATK/DEF already include their stat effects and are not
adjusted again. Only public `GetShowBuffs` effects are read, after PvE gating;
no hidden parameters, unrevealed rolls or other players' hands are accessed.

### Newly mapped active effects

| Public buff | Original data source (zero-based Params) | Applied rule |
| --- | --- | --- |
| 10261101 | Skill 102611, Params[0] = -2 | Existing stacks × incoming adjustment |
| 10531201 | Skill 105312, Params[2] = -1 | Existing stacks × incoming adjustment |
| 1101101 | Skill 11011, Params[1] = +1 | Incoming damage; Params[0] is healing, not damage |
| 4000301 | Destiny 40003, Params[0] = -2 | Next-hit adjustment |
| 4000401 | Destiny 40004, Params[0] = +2 | Next-hit adjustment |
| 5006001 | Relic 50060, Params[0] = -2 | Existing stacks × incoming adjustment |
| 10331101 | Original STRBuff 10331101 | Targeting lock, no effect inside a hit (observed 2026-10-22; was immunity) |

Numeric values above document the inspected snapshot; runtime values come from
the active native catalog with parameter-count, range and buff-association
guards. Relic's scalar BuffId is checked separately from repeated Skill/Card/
Destiny buff IDs. Delayed effects are excluded and duplicate IDs are counted
once. Existing mark, shield, adjustment and one-damage-cap mappings remain.

The user's reduction example is now identified: **Ren's Shushu shield** is
Skill 10712's PvE **next received damage -99**, not 50% reduction and not immunity.
Its PvP counterpart is -3 and remains outside this PvE-only feature. The -99
mapping already existed; this release tests it explicitly in the common path.

### Verification and remaining limits

`self-test/CombatSimulationCheck.cs` independently enumerates 3,456 effect
scenarios / 124,416 attack-defense dice pairs and compares defense/dodge stats,
card gains, encounter KO bounds, pre-roll/revealed KO pairs and final damage.
Additional resolver tests cover live parameter changes, wrong schemas/associations,
stacks, duplicates, delays and explicit immunity. Run with:

```powershell
.dotnet/dotnet.exe run --project self-test/BetterAstralParty.SelfTest.csproj -c Release
```

These tests verify implementation of the documented model, **not independent
evidence of server semantics**. No in-game verification was performed.

- The existing additive-before-cap model remains provisional for interacting
  effects; immunity dominates in this model. Individual original descriptions
  do not prove every compound-effect priority or multiplier rounding rule.
- Only currently public stacks affect this hit. Stacks gained/consumed during
  the hit, lifesteal, revival, HP floors, damage links, extra hits, conditional
  distance/partner effects and special dice are not newly implemented.
- Encounter evaluation still models one exchange; it does not simulate a full
  stateful counterattack with consumed shields or newly acquired effects.
- Unmapped buffs retain previous behavior. With diagnostics ON,
  `combatEffects.unmodeled.<role>` records their IDs; this includes unrelated
  stat buffs and is not itself proof of a missing damage effect. A displayed
  condition is only guaranteed within the supported public model, not all
  possible game rules. The original all-effects request remains incomplete.
- No UI wording/layout, fonts, loader defaults or gameplay state are changed.

## 2026-09-26 — all-effects overhaul investigation (not a release)

The installed mod remains 0.9.0. No new combat rule or shipped DLL was changed
by this investigation. The requested all-effects implementation is **not complete**.

### Current installed-client evidence

The downloaded INT catalog differs from the old research catalog. Its
`GameData_INT` dependency is `617e5f04971725c241e3102a7bdda629.bundle`, and its
`AstralParty.Runtime.dll` dependency is `5574058c33a393c58a2d30108a7b0572.bundle`.
Both were extracted from the existing local Unity cache into
`.research/combat-current/`; no download, game invocation or network mutation was
used. The extraction does not prove what an unseen server deployment implements.

Despite the different bundle/catalog identities, the extracted runtime and these
17 tables are byte-identical to the previous research snapshot: Card, Skill,
Buff, Character, Monster, PVENurturance, Relic, Mutator, MapEvent, STRCard,
STRSkill, STRBuff, STRPVENurturance, STRRelic, STRMutator and STRMapEvent.

- Current downloaded catalog SHA-256:
  `D9F8E24DE90283C0E312EF3C1069C226C672126C8F5D162AEB93A1991205A7FD`.
- Extracted runtime SHA-256:
  `DDEFD944846C612CC8935BCEF33170BAA8F3EE3974CBF2EC0F8F001678CED341`.
- Config inventories: 69 cards, 273 skills, 465 buffs. These are parsed inventory
  counts, **not a claim of 807 individually reviewed or implemented effects**.
- Comparisons use original Chinese localization field 2, not the Korean-patched
  English slot. A scan of all current STR tables for halving/reduction wording
  did not identify an incoming-damage-halving character rule. The specific
  character/ability the user means still needs identification; do not infer it
  from a 50% HP threshold, lifesteal, healing-stack decay or bonus ATK.

Research tools now accept explicit catalog/table/assembly paths so that current
data can be checked without overwriting historical evidence:

```powershell
node .research/catalog-lookup.mjs '^(GameData_INT|AstralParty.Runtime.dll)$' <current-catalog.json>
python .research/combat_catalog.py Skill --root .research/combat-current/gamedata --id 102611 --id 105312 --id 30613
.research/Inspect-Runtime.ps1 -AssemblyPath .research/combat-current/runtime/AstralParty.Runtime.dll.dll -Type '^SinglePlayer.GamePlay.BuffSystem.(PropertyChange|StateChange)$' -Method Execute
```

### What the existing common formula actually covers

Defense/dodge advice, card advice, encounter advice, KO thresholds and the final
damage display already converge on `CombatAdvisor.HitDamage`. The deficiency is
the **effect inputs and outcome model**, not five unrelated damage formulas.
`PublicCombatEffects.IncomingBonus` silently maps unhandled ordinary buff IDs to
zero. It supports mark stacks, seven explicit ordinary-buff adjustment mappings
and the one-damage cap. This is not all-effects support.

Confirmed gaps include public reductions 10261101 and 10531201; damage immunity;
distance/partner/HP-dependent reductions; source-dependent effects beyond the
1291202 Sykes combat support added in 0.14.0 (field tentacles remain separate); relic and
mutator effects; attack/defense dice limited to 3 or changed to 0; fixed or raised
dodge faces; non-additive attack-card previews; damage links; HP floors; lifesteal;
extra hits and revival. Some alter the present hit, others the return attack or
later phase. They cannot all be represented by a single signed damage bonus.

`FightWindow.CalculateATK/CalculateDEF` were rechecked: they forward the supplied
public ranges to `UIFight_PlayerInfo.RefreshAttackAttr/RefreshDefendAttr`.
Reapplying native ATK/DEF buffs to those ranges would double-count them. The
encounter path also uses a one-exchange survival assumption; it needs post-hit
state handling before claiming complete counterattack outcomes.

### Evidence gate before changing result semantics

The current runtime still has empty `PropertyChange.Execute` and
`StateChange.Execute` bodies. `Card_10010.CardCallBack` returns CompletedTask;
it does not implement the original card's final-ATK +50% rule. The tutorial HP
updater implements a limited additive path, not the full normal-match resolver.
Thus a native universal damage resolver has not been found in the inspected
client paths. Do not infer a server rule from a presentation callback.

Before an all-effects release, resolve:

1. Source-specific active effects and public predicates on both actors, including
   the exact incoming-halving ability mentioned by the user.
2. Multiplier rounding and whether it affects base ATK, dice-inclusive ATK or
   post-defense damage; shield/increase/cap/fatal-shield priority.
3. Restricted-die support and weights. A stated allowed range does not establish
   an equiprobable distribution or the precedence of overlapping restrictions.
4. Per-hit state transitions: consumed shields, newly gained stacks, linked damage,
   healing, revival and counterattack. Zero HP this hit is not permanent removal.

Use original executable evidence where available, otherwise isolated **solo**
observations and explicitly bounded outcomes. Do not access unrevealed rolls,
future result fields or other players' hands to fill missing rules. Do not ship
an arbitrary multiplier/order or declare all effects implemented merely because
the pure-model tests pass. A completed substantial overhaul is a 0.10.0 minor
release; research-only changes do not warrant relabeling the installed 0.9.0.

## Historical audit — 2026-09-25

Status: staged implementation in 0.4.81, not completed all-effects coverage.
The first stage adds public Berserk incoming damage; stage 2 adds selected public
shield/reduction/increase effects and a per-hit cap.

## Evidence and limits

- Inspected the mod's `CombatAdvisor`, `VisibleCombatAdvisor`, `CardAdvisor`,
  `EncounterAdvisor`, `GameUi`, `CardUi`, and public buff rendering path.
- Inspected local extracted `AstralParty.Runtime.dll.dll` with Mono.Cecil.
  Reading IL did not invoke game methods or send requests.
- Scanned all extracted `STR*.bytes` localization tables with the read-only
  `.research/read_combat_tables.py` helper. Keyword results are candidates,
  **not proof of exhaustive rule coverage**. Names and descriptions are separate rows.
- The extracted tables were present from September 20. Their English slot
  contains Korean-patch text. They are a research snapshot, not verified fresh
  server rules or necessarily current official translations.
- Normal combat receives `BattleRole` results from the server. The directly
  inspectable damage calculation found here is in `TutorialLogic`, which is not
  sufficient to establish every normal/PvE rule or stacking priority.

Reproduce the local table scan (also runs parser smoke checks):

```powershell
python .research/read_combat_tables.py --summary
python .research/read_combat_tables.py --table STRBuff --all
python .research/read_combat_tables.py --table STRRelic --all
```

Do not distribute extracted game tables or assemblies with the mod.

## Confirmed weaknesses in the existing mod

1. Defense is always `max(1, attack - defense - die)`; dodge failure always
   loses `attack`. No incoming damage modifiers, immunity, HP floors or caps.
2. Every future die is assumed uniform over 1..6. Some descriptions explicitly
   restrict the available faces. A restricted support does not itself prove
   equal probabilities or how multiple restrictions interact.
3. Card previews and encounter previews share this model, so fixing only the
   final defense/dodge UI would leave sibling calculations wrong.
4. Calculation cache keys have no special-effect context. An effect-only change
   would reuse the previous result unless the cache key is extended.
5. Encounter preview assumes a knocked-out defender cannot return attack and
   models only one exchange. Revival, damage links, self-KO and on-hit triggers
   need a distinct outcome model before treating KO as final death.

## Do not double-count visible stats

Verified client call chain:

`FightWindow.RefreshCardWinData` → `BattleRole.MinAtk/MaxAtk/MinDef/MaxDef`
→ `CalculateATK/CalculateDEF` → `UIFight_PlayerInfo.RefreshAttackAttr/RefreshDefendAttr`.

The mod reads these UI ranges. Adding card/buff ATK and DEF bonuses again is not
the fix. Retain authoritative displayed stat ranges, and separately model rules
that are not represented by those ranges.

## Rule families requiring coverage

IDs below are localization entry IDs (often, but not always, the same as entity IDs).
They identify evidence; they are not an implementation-ready hardcoded registry.

| Family | Examples in the snapshot | Required treatment |
| --- | --- | --- |
| Stat changes | Buff 2000801, 3021101, 3041101; additive combat cards | Use displayed resolved ranges once |
| Incoming damage increases | Buff 10006, 1140101/2, 2000801, 4000401, 10671302 | Apply to damage on a successful hit, not ATK; no damage on a successful dodge |
| Stacking damage reductions | Buff 1261101, 10261101, 10531201, 10551101, 5006001 | Public current stacks; verify zero/minimum damage and stack ordering |
| Conditional reductions | Skill 306130; nurture 120010; relic 500750, 500820 | Public distance, partner, HP and stack predicates; do not assume unconditional protection |
| One-hit protection | Buff 1071101, 4000301; skill 107110 | Ren has distinct PvP/PvE amounts; see the original-config follow-up below. Verify consumption and stacking order |
| Immunity | Buff 10331101; nurture 103010 | Distinguish immunity, targeting restriction, counter prohibition |
| Per-hit damage cap | Buff 10591101; monster 10594 | Cap per hit, not per battle or after summing multiple hits |
| HP floor / nonlethal attack | Buff 10131103, 10141401; skill 1003120 | Surviving HP floor differs from a damage reduction |
| Restricted dice | Buff 1250101/1250201; skill 125120, 1040110, 1063110; relic 500570/500590 | Separate attack, defense and dodge face sets; verify distribution and precedence |
| Ignore defense | Nurture 105010 | Confirm whether DEF stat, defense die, or both are ignored on an attack six |
| Dodge prohibition and self-KO | Buff 2002001; card 200201 | Preserve native dodge availability; attacker outcome depends on defender result |
| Conditional fatal-hit shield | Relic 500860 | Verify strict `>` vs `>= HP`, stack threshold, timing and priority |
| Damage links/shared HP | Buff 2000701, 10131102, 10141201 | Account for affected units and ordering without reading hidden data |
| Lifesteal/post-hit healing | Buff 1060101; skill 1036110 | Define damage basis, rounding and whether healing happens before a subsequent exchange |
| Retaliation/post-hit stats | Nurture 110010; skill 1026110, 1027110; relic 500610 | Recompute counterattack state, not the pre-hit snapshot |
| Revival | Skill 1067110, 1068110; relic 500630/500740 | KO now, survival after revival and final removal are different outcomes |
| Extra/AOE hits | Nurture 113010; relic 500910 | Each hit may trigger shields, stacks and caps separately |
| Mode/map effects | STRMutator, STRMapEvent, STRPVENurturance | Mode-specific active conditions; not every catalog entry is active |

Turn-start/end poison, regeneration, movement-only dice, rewards and unrelated
card damage must not be folded into the immediate defense/dodge hit calculation.

## Berserk: a concrete missing condition

Buff 2000801 describes ATK +3 and incoming damage +1. Tutorial
`OnCreate_BuffData_2000801.Apply` takes the two values from card Params indices
2 and 3, updates ATK separately and changes incoming HP loss separately.
`TutorialLogic.RequestBattleChoiceC2S` applies the defense minimum before calling
`TutorialBoardCharacterManager.GetHpUpdate`; successful dodge skips that damage.
This corroborates the separation, but does not prove the ordering of every
normal-match interaction.

Example under that isolated rule: defender HP 2, DEF 4, final attack 6,
attacker die 4. Without the incoming modifier, all defense rolls lose 1 HP,
so defense has 0% KO risk. With incoming damage +1, every defense roll loses
2 HP, so defense has 100% KO risk; ordinary dodge has 4/6 KO risk. The recommended
choice changes. Do not add the defender's ATK +3 to incoming damage.

## Implementation boundary and next verification

The request was to analyze all variables before replacing formulas. That gate
has **not** been met. An all-effects release or a guaranteed-death animation
would currently overstate what has been verified. The user approved a staged
implementation and requires actual verification in solo play only.

For a staged implementation:

1. One shared pure hit resolver for defense/dodge, card preview, encounter preview
   and later remaining-HP UI. Preserve existing PvE/public-data boundaries.
2. Public, phase-specific effect context in cache keys. Read only effects that
   native public detail UI exposes; never consume future `IncHp`, concealed
   cards, RNG state or internal result fields before their visible phase.
3. Verify isolated Berserk, damage reduction at base damage 1, simultaneous
   shield/increased damage, capped damage, exact-lethal shield threshold,
   modified dice and revival against visible normal-PvE outcomes.
4. Use bounds for explicitly unresolved combinations. Do not infer weights from
   descriptions. Preserve the configured robust defense/dodge preference, but
   do not label an unverified special-effect outcome as certain death.
5. Record supported, bounded and unsupported cases separately. Unit tests verify
   the model, while observed PvE outcomes verify that it matches the game.

No public matchmaking, server mutation, synthetic RPC calls, input automation,
gameplay hooks or hidden-state reads are necessary for this work.

## Stage 1 — 0.4.77

- Added one shared hit-damage adjustment, propagated through visible defense/dodge,
  card and encounter calculations and their value-based cache keys.
- Reads active public buff 2000801 and validates the active card 20008 schema;
  incoming damage comes from Params[3], not translated text or a hardcoded +1.
  Native ATK values already include the separate ATK bonus. Successful dodge
  remains zero damage. Other special effects above remain unsupported.
- Release build and existing checks passed; 15 new assertions cover the isolated
  modifier, recommendation changes, cache invalidation and propagation.
- Previous installed DLL backed up under artifacts/combat-backups/20260925-024258.
- Actual solo path: Solo Mode > Basic Challenge > PVE Basic Challenge > Challenge 2.
  Entered successfully with 0.4.77. Public effect reading ran during bot combat:
  current.log at 02:52:08 records combatEffects.defender=1150101:0:0 and visible
  phase transitions through step 6. This is a runtime smoke check, not proof of
  the Berserk formula. Initial automated card drags did not use Berserk; the user
  subsequently applied it and played the solo battles manually.
- Observed Berserk case at 02:54:04–02:54:20: public defender buff
  `2000801:0:1`; runtime recommendation input HP 11, ATK 4, attack die 1,
  DEF 2, incoming bonus 1. Visible dice were 1/4, then final totals 5/6.
  Visible defender HP changed 11 -> 9, confirmed by the field HP afterward.
  This matches `max(1, 5 - 6) + 1 = 2`. The native minimum is applied before
  Berserk's incoming bonus. LogOutput.log also confirms the recommendation
  received IncomingDamageBonus=1 (defense mean 13/6, dodge mean 1).
  This validates this isolated minimum-damage case only, not shields, caps,
  revival, unusual dice, stacking priorities, or successful-dodge behavior.
- Practice Mode > cooperative library led to a Create Room form. Cancelled before
  creation; no public matchmaking or Steam lobby was created for this test.
- Do not ship the proposed guaranteed-death indicator until the relevant remaining
  interactions and actual outcomes are validated.

## Original-config follow-up: shield, cards, and remaining gaps

The goal is to reduce unresolved outcomes by resolving actual rules, not to replace
special effects with a neutral marker. This follow-up changes research tooling and
documentation only; it does not extend the installed 0.4.77 resolver.

### Evidence quality and reproducibility

`.research/combat_catalog.py` joins configuration IDs through NameID/DescID to
original Chinese text (field 2), translated text, signed Params and buff IDs.
The snapshot contains **69 cards, 273 skills and 465 buffs**. These are catalog
counts, not 807 implemented or individually runtime-verified effects. All 69 card
descriptions/parameters were reviewed; skill/buff rule discovery remains incomplete.
Localization IDs must not be mistaken for configuration IDs.

```powershell
python .research/combat_catalog.py Card
python .research/combat_catalog.py Skill --id 10701 --id 10702 --id 10703 --id 10711 --id 10712
python .research/combat_catalog.py Buff --id 1071101
```

### Ren's shield: the apparent contradiction has a mode explanation

Character 107 maps ActiveSkill=10701, PveActiveSkill=10702,
PassiveSkills=[10711], PvePassiveSkills=[10712]. Original skill Params specify
10711=[-3], 10712=[-99], 10702=[1071101,-99]. Both use buff 1071101.
Skill 10703 also contains the -99 shield and additional parameters.

`CharacterInfoConfigure.GetBattleActiveSkillId` branches on `RoomInfo.IsPVE()`
and applies the nurture replacement active skill when configured.
`GetBattlePassiveSkills` selects the corresponding mode list and applies nurture
additions/deletions. This is executable evidence for mode-aware skill selection,
not proof of shield/minimum/incoming-bonus ordering.

Thus buff text saying -99 and the PvP passive saying -3 are not grounds to apply
one constant everywhere. Resolve the active mode/source/upgrade first. PvE -99
is damage reduction, not proven unconditional immunity against every damage size.
`Core.LianSkillShieldBuffEffectHandler` plays visual effects; it is not the damage
resolver. The serialized Buff schema contains Params, Source, Priority, UseTime,
TargetIds and related fields, but their presence does not establish key meanings,
execution order, or permission to consume non-public state.

### Complete card-ID review ledger (snapshot only)

The groups below cover all 69 configured cards once. Effects outside combat can
consume a shield or change HP before combat; read the resulting public state, do
not reapply those earlier effects to the current hit.

| IDs | Consequence for calculation |
| --- | --- |
| 10001–10007 | Additive ATK/DEF ranges; use native resolved values once |
| 10008, 10009 | ATK addition; 10008 prohibits counter, 10009 generates 10010 |
| 10010 | ATK +6 and final ATK +50%; establish inclusion in visible values and rounding before multiplying |
| 10011–10013 | No-counter cards; remove scales / add awakening / change target DEF and movement respectively |
| 20001, 20012, 20013, 20025, 21001, 21007, 21012, 21013 | Direct or area damage outside the immediate combat hit |
| 20002, 20014, 20027, 21006, 21020 | Healing, with movement/card-use side effects on some cards |
| 20003, 20015, 20017 | Effect-card protection/reflection/retargeting; not automatically combat-hit immunity |
| 20004, 20005, 20010, 20011, 20022, 20023, 20024, 20026, 20032, 20033 | Coins, traps, movement or cooldown; do not treat movement dice as combat dice |
| 20006, 20019, 20021, 20028, 20029 | Immediate and delayed damage/healing; separate trigger timing and each hit |
| 20007 | Linked damage: account for other affected units and verify recursion/prevention order |
| 20008 | Berserk: resolved ATK plus separate incoming-damage increase; isolated minimum case tested |
| 20009 | Self-damage plus area damage; can alter HP/protection before combat |
| 20018 | Damage trap; not an immediate combat modifier |
| 20020 | ATK increase, opponent cannot dodge, attacker KO on failure to defeat; revival interaction unresolved |
| 20030, 20031 | Damage and marks, with card generation on 20031; character passive can increase damage |
| 21002, 21003, 21008 | Card generation, movement dice or teleport |
| 21004, 21005 | Self-damage/ATK or DEF change; avoid adding already-resolved stats twice |
| 21009 | Damage, effect removal and counter prohibition; recapture state after removal |
| 21010 | Ally damage and curse/held-card triggers; distinguish end-turn damage |
| 21014–21016 | Healing/stat/card changes and modification-stack consumption |
| 21017 | Choice of 21018 or 21019; resolve the publicly selected branch |
| 21018 | Forced monster attack followed by counter, monster ATK/DEF reduction; exchange direction matters |
| 21019 | Self-damage and target damage with additional separate hits from curse removal |
| 21021 | Vague original description; Params=[1,1050], buff=2102101 do not alone specify a damage rule |
| 21022 | Held warmth stacks and conversion to 21020, not an immediate combat hit |
| 21023 | Large event stat/heal/reward changes; use resulting current public state |
| 21024, 21025 | Mind/evidence and card-use/transfer effects; relevant to conditional monster rules |

### Additional phase-dependent conditions

- Skill 12611 / buff 1261101 and skill 105511 / buff 10551101 add reduction
  **after damage**. Newly earned reduction must not protect the hit that created it.
- Skill 30613 has a partner/distance condition, not a permanent unconditional -1.
- Skill 106111's zero-damage state is removed when challenged: evaluate the battle
  phase, not merely the monster's static passive description.
- Skill 106311 restricts attack dice to 3–6, or 6 against a target without Mind.
  Skill 104011 fixes the final dodge result to 4. Uniform ordinary dice are wrong.
- Skill 106711 immediately revives until the culprit is revealed; skill 106811
  revives later until the clue condition is met. A zero-HP hit is not necessarily
  permanent removal. Separate this-hit KO from post-trigger HP.
- Skill 106713 changes revival and stats using round/clue conditions; skill
  106812 changes attack based on evidence. Check active public map state.

### Why a complete formula is not established yet

The extracted client's Card_10010.CardCallBack returns CompletedTask, rather than
implementing the 50% damage/stat operation. Card_20020's callback presents and
refreshes results. The inspected SinglePlayer BuffSystem PropertyChange.Execute
and StateChange.Execute are empty. These paths cannot prove normal-match server
resolution order. Tutorial OnHitExtraDamage supports the isolated Berserk rule,
not the complete shield/link/cap/revival system.

Still required: current-table freshness, remaining skill/buff/nurture/relic/map
coverage, multiplier rounding, shield consumption at zero damage, incoming bonus
versus reduction/cap order, link propagation, and lethal/revival timing. Verify
the remaining combinations in **solo play only**, starting with Ren shield alone,
then shield+Berserk, exact lethal, repeated hits and revival. No future result
fields, concealed cards, RNG state or network modifications may fill these gaps.

## Stage 2 — 0.4.78 (provisional application approved)

- Applied public buff 1071101 using PvE skill 10712's catalog reduction. It is
  one-hit -99 in the inspected catalog, not universal immunity. This reader is
  invoked only after the existing PvE gate; it does not model the PvP -3 variant.
- Added 1140101/1140102 incoming increases, 1261101/10551101 per-public-stack
  reductions and 10141101 per-public-stack increases. Numbers come from their
  linked skill Params, checked for expected shape and associated buff ID.
- Public buff 10591101 caps a hit at 1, as described in the original table.
- Shared provisional order: native base hit (including defense minimum), signed
  additive modifiers, clamp at zero, per-hit cap. Successful dodge stays zero.
  This is a selected model for testing, NOT server-order verification.
- Propagated reduction/cap through defense/dodge, additive card preview and both
  sides of encounter preview, including value-based cache invalidation. Corrected
  defense NoDamageChance for hits fully absorbed by protection.
- Existing opt-in diagnostic records include public buff ID/count/rounds and the
  selected net adjustment/cap. Entries are state-deduplicated and remain off when
  diagnostics are disabled. No gameplay/network writes were introduced.
- Pure combat-effect checks increased from 15 to 29, covering shield thresholds,
  shield+increase, zero clamp, cap ordering, propagation and cache changes.
- Still NOT implemented: arbitrary effects, 10261101 threshold-trigger ordering,
  damage links, revival, multi-hit state changes, restricted dice, non-additive
  card previews and the proposed final-HP circle. Encounter counter state remains
  the earlier single-exchange approximation. Do not describe this build as full
  card/character coverage or as an exact permanent-death predictor.
- Release build/self-tests passed. No game launch or solo runtime verification
  was performed for this stage; mixed-effect ordering needs observed solo results.

## Final-number HP view — 0.4.79

- Reuses a private Fight_Com_Point between the native attacker/defender points,
  with an HP caption. Local/global coordinate conversion follows their positions.
  Surviving HP uses a desaturated background; projected zero uses red and the
  native MaxPoint transition once, without modifying either original indicator.
- Snapshots public HP and supported damage adjustments before result phase 6.
  Reads only visible numbers, showPoint/changeState controllers and UI transition
  state. The original Dice_Show sets showPoint=2 before summing stats, so the
  view waits for com_value's Cut_out (or observed disappearance) before freezing
  the result. Successful dodge skips the summation. No BattleRole outcome read.
- Once the public HP label changes, use that value instead of subtracting again.
  Logs before HP, final numbers, modifiers, projected and observed HP when local
  diagnostics are enabled. Reset on phase rollback, target/window changes and
  UI cleanup. A view error disables only this optional view for the session.
- The red animation means zero HP under the current single-hit model, NOT a
  verified permanent-death guarantee. Stage 2's unsupported effects still apply.
- Added 10 pure final-HP tests; Release build and existing automated checks pass.
  Native rendering, animation timing and actual solo combat remain unverified.

## Visibility correction — 0.4.80

The user's 0.4.79 diagnostic trace proves the result-display phase assumption was
wrong: final numbers remain in step 5; step 6 starts after the hit with hidden
points. At 03:23:12 the UI showed 8/5 and HP10 in step5; at 03:23:14 step6 had HP7
and both points hidden. Two further battles show 9/1 -> HP1 from HP9, and 3/8 ->
HP9 from HP10. There was no RemainingHpUi exception or shown record.

- Moved the display path to step5 and restricted raw attack-die capture to native
  showPoint state1. Totals <=6 no longer overwrite the original die.
- Capture HP/modifiers once on entering step5 (or during card preparation), rather
  than overwriting the snapshot on every final-number frame.
- Observe com_value before point-readiness early returns, so its exit is not
  missed simply because a point was animating or hidden. Added hide-reason logs.
- Added regression assertions for phase5 versus phase6 and all three logged HP
  outcomes. Build and automated checks pass; corrected native rendering has not
  yet been visually verified in a fresh solo run.

## World-space positioning correction — 0.4.81

The next trace reaches `remainingHP.visibility=shown` at 03:29:53, calculating
HP9 - (8-2) = 3, matching the later visible HP3. Thus the phase fix executes,
but `shown` only meant setting a visibility flag, not confirmed screen rendering.
The dice are world-space FairyGUI, while the original implementation used plain
LocalToGlobal/GlobalToLocal. Existing BattleStatusUi already documents that this
selects the wrong camera for battle-world UI.

Both views now share NativeUi.ProjectBattlePoint: display.LocalToWorld,
PKMainCamera.WorldToViewportPoint, BattleStatusLayout.Project, RootToLocal.
The HP circle's size also derives from projected geometry, and its sorting order
matches battle-status rows. Diagnostics now include screen-local position, size,
onStage and inherited visibility. Projection wiring regression and existing pure
projection tests pass; this is not a claim of native visual verification.

## Screen-fixed damage indicator — 0.4.82

The circle now shows resolved single-hit damage, including overkill, rather than
remaining HP. It uses the same public final totals, dodge result, incoming
modifier and damage cap. Existing model limitations above still apply; a red
highlight means modeled lethal damage, not a guarantee against revival.

The private native circle is attached to GRoot at the upper screen center, with
size relative to the viewport and the user's UI scale. It no longer follows
world-space dice. A native-style scale entrance and fade take 0.5 seconds,
followed by a 3-second hold and 0.5-second fade-out. The native MaxPoint effect
still highlights lethal hits. Capturing a new exchange is separate from the
display timer, so hidden dice or the result phase do not cancel the display.
Feature disable, home return and teardown clear the view. No native gameplay
values, controls, animation speeds or network calls are modified.

Release build, calculation checks (including overkill, shield, cap and dodge),
animation timing checks and screen-fixed wiring checks pass. Actual in-game
rendering has not yet been visually verified; any play test must be solo.

## Damage display latency — 0.4.83

Fight_Com_Point's packaged PointChange contains visibility, sound and scale
keys (ending at roughly 0.33s), not text changes. Damage no longer waits for
that cosmetic animation to finish. The arithmetic-exit guard remains because
showPoint=2 can precede the final arithmetic. While final numbers are pending,
cached combat UI is checked every frame; scene discovery and other advice
retain their existing polling cadence. Polling stops once this hit is shown.

Lethal damage starts the native MaxPoint transition immediately, at full alpha
and resting parent scale, so a parent fade/zoom cannot hide its initial flash.
Nonlethal entrance opacity reaches full strength in 0.12s. The four-second
display lifetime and exit fade remain unchanged. Release and regression tests
pass; animation appearance still requires solo in-game verification.

## Pre-hit damage preview — 0.4.84

Inspection of extracted runtime IL identifies the remaining delay:
BattleShowDirector.Dice_Show writes the final attacker/defender labels through
RefreshPoint_Attacker/Defender, waits 800ms, then calls HideFightValue. Each
RefreshPoint method writes text before playing PointChange. The previous
arithmetic-exit guard therefore withheld an already readable result.

After arithmetic has appeared, simultaneous PointChange playback now admits
the visible final totals immediately. Arithmetic exit remains a late-sample
fallback, not a mandatory delay. showPoint=2 alone still cannot admit raw dice.
Successful dodge keeps its no-arithmetic path. No future battle-result fields
are read. Diagnostics record arithmeticExited at display time; false on a hit
confirms use of the earlier path. Five readiness regression checks cover these
branches. Actual solo-play preview timing remains to be verified.

## Public mark coverage — 0.4.85

BattleProperty registers MarkCount as property buff 10006. GetShowBuffs returns
it in Item2; IncomingBonus previously inspected only Item1. The shared reader
now includes visible mark stacks as incoming damage (+1 per stack), excludes
unrelated properties, validates counts and deduplicates IDs. Existing ordinary
buff adjustments and hit caps still apply. Successful dodge remains zero damage.
This feeds final damage, defense/dodge, card and encounter advice through their
existing modifier inputs/cache keys. No gameplay or network state is changed.

Diagnostics now include property:10006:<count>. The previous trace at 04:02:12
showed damage4 for ATK10/DEF6, then HP10 to HP5, but did not record property
effects, so that event alone cannot prove its mark count. Production-reader tests
with a native tuple substitute cover absent/multiple/duplicate/invalid marks and
propagation to all advisor paths. Build and regression checks pass; a fresh solo
run is still needed to verify actual marked hits and mixed-effect ordering.

## 2026-10 table audit — 1.6.0-dev

The 2026-10-15 game-data bundle (current INT tables) adds shown buffs that the
2026-09-26 snapshot lacked. Original STRBuff text:

- 10711102: +2 ATK per stack, one stack lost after being challenged.
- 10711103: DEF -1, move -1.
- 10721101 and 10741101: +1 ATK per stack.
- 5008702: +2 ATK per stack. 8000101: +2 move on the next roll.
  3081104: cost limit +3, move +2, one extra card play.

All change only public ATK/DEF (already in the native totals), movement or
cards, so they join CombatBuffAudit.Neutral. `Developer.cmd combat-audit` over
the local runtime logs replayed 37 hits with 0 mismatches, including 8 hits
with 10711102, 7 with 10711103 and 4 with 10721101.

1071102 is new: skill 10711 (the PvP passive, Params [-3]) now grants its own
buff instead of sharing 1071101. It is modeled like 1071101 from its skill's
Params[0] with the buff association guard, but it is not the Shushu shield and
does not trigger the Dodge override. 10009 (modification: damage equal to
current HP when stacks reach 0) stays not counted; no logged hit shows it, and
its trigger timing relative to a hit is unverified.
