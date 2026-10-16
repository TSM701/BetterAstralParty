# Field indicator performance review — 0.4.63

Reviewed retained `BepInEx/BetterAstralParty-Diagnostics/current.log` (2026-09-22)
and `previous.log` (2026-09-21), both from 0.4.60. Earlier sessions are not retained
by the two-file rotation. No live gameplay or same-scene ON/OFF benchmark was run.

## Findings

- Current/previous logs recorded 160/180 plate creations and 39/44 clear events.
  The largest recorded clear removed four mod plates. This does not establish a
  growing live-plate leak; repeated creation accompanies battle transitions.
- Ten-second samples whose last field state was `scan`: mean of reported UI averages
  was approximately 0.17 ms in both files. The same metric in `fight-active` was
  approximately 0.20 ms. These intervals can cross state changes: not an ON/OFF test.
- Largest `gameui_max_ms` was 104.43 ms in current field-state samples and
  158.36 ms in a previous `parent-hidden` sample. Spikes are not exclusive to field display.
- Existing `status_max_ms` combines BattleStatus, EncounterCounter and FieldBuff.
  It does not measure native FairyGUI layout, rendering, native allocations or GPU work.
  Therefore these logs cannot attribute the user's stutter specifically to field plates.
- Native `UIBattleInfoPanel.UpdateAttrInfoPosition` checks visible nameplate pairs
  and movement-arrow overlap every frame. Adding four plates adds 4N+6 pair checks
  when N native plates are visible. This is real extra work, but its duration is not measured.
- Window lookup and real-name conversion also visited field labels and buff children,
  even though they cannot contain target windows or real-name slots.

## Changes / verification

- Keep native positioning, death/hidden rules, effect update frequency and lifecycle.
- Prune field-plate/buff subtrees from window lookup and real-name conversion.
- Reduce diagnostic owner/visibility inspection from 5 Hz to 1 Hz; ordinary data
  refresh remains 5 Hz. Avoid diagnostic slot-string construction while recording is OFF.
- Add separate 10-second field Tick average/max and plate/effect maxima. Explicitly
  mark native layout as excluded; retain original combined metrics for comparison.
- `tools/test-field-search.ps1` executes the production lookup against a UI substitute.
  With 24 plates × 12 buffs, native-call substitutes drop from 5366 to 134, with
  identical lookup results. At 0/4/12/24 buffs per plate the optimized count stays 134.
  This is a call-count regression check, not measured FPS or native execution time.

## Remaining validation

Use the same field/camera state, diagnostics ON, and alternate field display ON/OFF
for at least 20 seconds each. Compare game frame spikes alongside `performance_field`.
If Tick stays low while ON-only spikes persist, profile native placement/rendering
before changing the stable native placement implementation. Do not enter public matchmaking.
