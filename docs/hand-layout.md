# Stacked local field hand — 0.28.2

## 2026-10-01 — 0.39.0 allocation reduction

Reuse frame-local card/group/pile/removal lists and tween-list storage instead
of rebuilding LINQ arrays and dictionaries every frame. Clear memberships and
native tween references after each tick, including failures and cleanup. Keep
sorting keys and their original enumeration tie-breaker; do not cache live
card lifecycle or native animation state. Native writes are skipped only when
the currently read native value already matches the target.

The regression extracts the production grouping block, compares old/new order
through additions/removals, and measures zero managed bytes in that block after
warm-up. HD through 4K geometry, native tween retargeting, world-click behavior
and card/outline ownership checks pass. This is not a zero-allocation claim for
the whole UI tick or proof that runtime frame-time spikes have disappeared.

한국어: 프레임 저장소만 재사용하고 카드 상태·애니메이션은 계속 확인합니다.
묶음 계산 구간의 워밍업 후 할당은 0바이트이며, 전체 UI·네이티브 호출의
무할당이나 잔렉 해소를 의미하지 않습니다. 인게임 재측정은 별도로 필요합니다.

Scope: own PvE field hand only. Spectator/PvP/combat hands keep native layout.
The discarded 0.23.0 theme snapshot is not reused. No release archive is created.

## Classification sources

Audited the original Chinese Card/STRCard tables in `.research/combat-current/gamedata`
and `CardInfoConfigure`, `CardLogic.InitCardMethod` in the extracted native assembly.
`InitCardMethod` registers individual `GameLogic.Card_<id>` handlers: there is no
generic functional taxonomy for these effects. In particular, Reply includes damage
(20019) and Direct includes healing (21014). Never infer function from EffectType alone.
`HandLayout.Classify` is the narrowly scoped, original-effect mapping, not a calculation.
Future Attack/Defend card types work automatically; unknown other IDs go to Other.

| Group | Original effect basis |
|---|---|
| Attack / Defense | CardType 1 / 2 first; three verified effect-counter cards also Defense |
| Disruption | Direct/area damage, harmful links, delayed damage, bombs and traps |
| Support | Healing, ally/self buffs, including attack/defense buffs played on the field |
| Movement | Dice/direction control, speed, teleport; 21020 prioritizes teleport over its heal |
| Resources | Theft/pickup of currency and card draw; 21016 prioritizes drawing three cards |
| Other | 21017 choice, 21022 passive transformation, 21023 broad mixed reward, 21025 evidence transfer; future/unverified effects |

Every card appears exactly once. Existing IDs are not inferred from translated names,
and localization is only used to display the original card name. Same-ID cards remain
adjacent, with original presentation order as tie-breaker; game hand data is untouched.

## Native interaction path

`HandCardPanel.UpdateCardList` binds original drag events to each
`UIHandCard_Button_Card`, with identity stored in `CardData.Guid`. This feature does
not reorder or write CardContainer/HandCards, invoke card-use methods, add hooks,
or synthesize mouse/network events. Original card artwork overlaps vertically in
each pile; no replacement name strip or separate hover artwork exists. Original
hover handlers remain installed, but owned cards temporarily use `zoomOutScale`
for `zoomInScale` too, preventing unbounded native enlargement. Instead, enlarge
the focused original card's outer transform by 12% within its reserved cell.
ZoomOutCard/ZoomInCard use display-effect scales 43/72: keep the three native
outline/temp effects at 43 while outer scaling owns the card, and restore native
hover/resting scales on cleanup, drag or pending use. Availability remains native.

`HandCardPanel.<DragEndEvent>d__37.MoveNext` compares the original dragged card's
root position to `UIHandCardPanel.hotZone` bounds. Move/resize that same zone rather
than adding a decorative rectangle with a different drop target. The native zone
still appears only during native dragging. Target selection and card restrictions
remain native. Face/effect offsets, alpha, visibility and input events are not modified.

`PlayTransition` retains hover events until `TranslateCard` finishes its tween callback.
Do not cancel these tweens or assume completion after 0.35 seconds. `IsDistribute`
detects pooled cards waiting offscreen. For XY tweens whose destination matches
`CustomPosition` or `_ShowPosition`, update the existing end value and resting
geometry to the stack. Preserve duration, easing and completion callbacks; rebase
the start value at the current eased progress when the endpoint changes so the
current rendered position does not jump. Enumerate all active card tweens once
per layout update: native GetTween returns only the first of potentially overlapping
RendererCard/ZoomOutCard/TranslateCard and hover movements.
Leave the initial deal centre waypoint and unrecognized use/removal movements alone.
Other native animations, visibility and ancestor alpha/scale remain authoritative.
`ReadyFight` hides the hand by scaling its panel to zero;
the layout must not cancel that transform. Native full-hand zoom signals no longer
suspend grouped field geometry; the global flag is read only for diagnosis, never
written. Native handlers/availability remain intact. Idle, visible, non-distributed
cards can recover from off-target coordinates; pending use, drag, hidden ancestors,
unknown movements and non-layout animations still block takeover.
Group captions live under the same hand container and hide while it is unavailable.
`ForbidOperate` sets `container_Card.touchable=false` during ordinary actions and
timeouts without hiding the hand. This input lock must not stop passive layout;
fold the expanded column but leave native input restrictions untouched.
Only a use-pending card is excluded: while input is locked, `DisplayCard` leaves
its `onRollOut._bridge._isLocking` set until native release/rebind. Deal/reflow
retains only roll-over, so it remains eligible. This is a read-only native-state
query; do not retain, release, replace or invoke the handlers from the mod.
Hovering a card opens its entire column upward without overlapping cards, capped
below the top UI. All base cards in that column have the same size; uniformly reduce
them to reserve a 12% zoom footprint per cell. Focus never changes neighbouring
slots or crosses column/UI boundaries. Other columns remain stacked.
Keep the maximum zoom footprint's bottom fixed and retain the open state through inter-card gaps and the
original column width, including when unfolding shrinks the cards. Native drag
freezes geometry until release; do not create replacement click/drag handlers.
Restore owned resting geometry and native zone bounds on OFF, replacement and exit;
do not pull animated/released/recycled cards back onto the screen during cleanup.
No private hand contents are added to diagnostic logs.

0.28.0: column geometry takes precedence over transient native pointer paths.
Retain the focused card through gaps until another base card is entered or the
column is left. The 22:57–22:58 log kept `expanded=Attack` while two native motions
repeatedly started, consistent with the inspected per-card hover enter/exit path.
A native hover tween starting at an unchanged owned slot is held at that slot
without killing it or changing callbacks; do not rebase its unwanted excursion
as a new animation back to the slot. Genuine layout/focus changes still animate.

0.28.1 root cause: EventSystem.Update precedes StageEngine.LateUpdate, where
Stage.InternalUpdate calls HandleEvents before its beforeUpdate event and the
DisplayObject.Update rendering pass. The former 60 Hz correction ran too early:
native hover could write effect scale 72 after correction and render it for a frame.
0.28.1 attempted to subscribe a retained System.Action to Stage.beforeUpdate.
That approach has been removed in 0.28.2 following the native startup crash below.
The current implementation uses the existing managed UI driver, with no injected
delegates or new subscriptions. Pointer bounds and effect correction run each UI tick.
Focus tests use the reserved enlarged rectangle plus its 3-reference-pixel outline,
and native border entry seeds focus immediately. Once settled in a cell, pin the
artwork/effect centre using the current outer scale; independent native XY/scale
tween timings must not move the zoom centre. Genuine column movement remains native.
Hand cost is again included in ModUi.Tick's existing performance measurements.

0.28.2 startup evidence: at 2026-09-27 23:23:00 local time, Windows Application
events 1000/1026 recorded GameAssembly.dll access violation c0000005, offset
003e2cd0; the launcher recorded exit -1073741819. 0.28.1 loaded successfully, but
the last BepInEx messages were DelegateSupport registration and exhausted Class::Init
signatures. This strongly implicates the newly introduced delegate registration;
no native stack identifies the exact failing instruction's caller. Remove that
entire path rather than relying on managed catch to contain access violations.
Metadata/substitute tests did not prove native delegate injection safety.
Border/centre geometry fixes remain, but same-frame outline flicker is no longer
claimed resolved: the original event still runs after the managed UI driver.

0.27.1 investigation: the local 22:31–22:33 session loaded successfully without a
HandLayout compatibility block. It did not record hand states, so the exact trigger
is not proven by that log. Code inspection found the persistent global-zoom gate,
off-target idle deadlock, first-tween-only redirection and instantaneous scale writes.
0.27.2 records sampled/deduplicated `hand.layout` gate/count states using the existing
opt-in rotating log, without card IDs, names or player information.

0.27.3 investigation: the local 22:48 session recorded normal grouped layout until
22:48:41, then `input-or-use-busy`; after combat it returned to that same blocked
state at 22:48:52 and 22:49:23. No HandLayout compatibility exception was logged.
The whole-container touchable guard caused this persistent suspension. It is now
replaced by the per-card pending-use check above. Layout diagnostics also record
the input-lock flag and pending count, never card contents.

## 0.28.3 same-event outline correction

The native ZoomInCard writes all three effect display scales to 72 after the
managed layout tick, while ZoomOutCard writes 43. Polling cannot prevent the first
render of 72. HandOutlineBinding appends native EventCallback0 delegates targeting
each existing effect graph's GObject.HandleScaleChanged. The callback type, target
and method all belong to the game: System.Delegate.CreateDelegate binds them
inside IL2CPP; no managed callback thunk, DelegateSupport, injected class or Stage
subscription is used. The existing managed layout driver remains unchanged.

While layout owns zoom, the graph's scale fields hold 43. Native hover handlers
still run first (including sounds, guides, card scopes and event locks), followed
by the native effect refresh before EventBridge.CallInternal returns. Outer card
zoom therefore scales artwork and effects together. Reattach only when native
RendererCard.Set replaces the callback chain. Match EventBridge.Add's remove/combine
semantics; cleanup removes only this binding and restores its saved graph scales.
Drag, pending use, hidden/distribution/foreign motion, pooled identity changes and
layout cleanup release the binding. This is native presentation event composition,
not replacement or unlocking of game input handlers.

Native reflection/delegate execution still needs an interactive runtime check;
the substitute tests below do not establish HybridCLR runtime compatibility.

## 0.28.8 render-order cache correction

0.28.7 restored `GObject.sortingOrder`, but that is not the final draw order.
The installed runtime's `UI.BasePanel<T>.Show` enables `fairyBatching`.
`Container.CollectChildren` flattens descendants until a batching boundary;
`DoFairyBatching` reorders non-overlapping graphics by material and caches them.
`DisplayObject.SetPosition`/`SetScale` mark bounds changed, but do not invalidate
that material-order cache. The expanded, non-overlapping arrangement can therefore
leave rear-card text above a front-card background after folding without a new
sorting-order change. This matches the reported mixed artwork/text, unlike a
whole-card ordering error; logs confirm 0.28.7 loaded and layout remained active,
but do not contain a captured native render list.

Grouped cards now enable their own native `fairyBatching` boundary, so the parent
collects each card as a unit and `SetRenderingOrder` recursively assigns its
descendants contiguous draw orders. This avoids global batching disablement or
per-frame invalidation. Save the original property once and restore it through
the existing zoom cleanup for hidden/foreign/use/drag/recycled/disposed/disabled
paths. Tests check native call contracts, execute the production ownership helper,
and model the cross-card cache failure/boundary behavior. They do not replace an
interactive in-game hover/collapse test or measure draw-call cost.

## 0.28.6 per-frame cost

Keep per-frame lifecycle/hover/tween handling, but check shared container ancestry
once and each card/face branch only once. The visibility-helper substitute benchmark
with 12 cards reduces native-call equivalents from 827 to 212; it is not an FPS
measurement. Reuse live/ready sets. Do not calculate the native hover destination
when no card tween needs classification. Captions remain visible instead of toggling
off/on every frame; update their layout/text only when geometry/language changes.
Native outline graph initialization and display correction run on binding changes,
not idle ticks; the existing native callbacks still handle each hover event.

Opt-in diagnostics aggregate `performance_hand` every ten seconds (tick average,
maximum and managed bytes/frame). This excludes native callback/render work; compare
like-for-like sessions before claiming runtime improvement. Earlier `popup_max_ms`
combined popup and hand ticks, so it cannot identify hand cost by itself.

## Verification

Automated: category and bilingual labels, five screen sizes, 1–7 groups, 1–11 rows,
centre/portrait/action-control separation, unfolded non-overlap/equal sizes/fixed
bottom/gap hover, reserved 12% focus bounds; native metadata checks and source guards.
Production motion-helper tests cover concurrent old/new reflow and hover endpoints,
retarget continuity, unchanged callbacks/durations, foreign movement/fade isolation,
pooled identity changes and off-target idle recovery (native substitutes).
Repeated native hover enter/exit at a settled slot must leave both tween endpoints
fixed while retaining callbacks; changing focus/column targets must still animate.
Tests forbid the removed delegate-injection path and require the existing managed
driver/circuit breaker. Effect correction is exercised over 90 alternating
hover states, including restoration; this does not simulate native render timing. Geometry checks cover
all enlarged edges/corners, outline margins and unchanged focus centres.
Input-lock regression checks execute the production pending-use helper: passive
locked cards recover, pending-use cards remain excluded, and native cancel/unlock
resumes layout without bypassing hidden/distribution/foreign-animation gates.
The production outline-binding class is tested with native API substitutes over
100 event transitions AFTER the layout tick and BEFORE simulated rendering,
including callback-chain replacement, duplicate prevention, preserving unrelated
callbacks, original scale restoration and leaving event locks untouched. Metadata
checks verify the real native method signatures, scale fields and callback order.
Actual in-game hover, drag, animated deal/convert, ping, target-selection/cancel,
reconnect and visual alignment still require an interactive runtime pass.
