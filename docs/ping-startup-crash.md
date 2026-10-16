# Ping focus startup crash (0.22.5–0.22.7)

## Evidence

- 0.22.5 registered `PingFocusGuard` as an injected MonoBehaviour, then terminated with an access violation in `MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook`. Type injection is the suspected trigger, not a proven general loader defect.
- 0.22.6 removed injection but added a Harmony patch to `EventSystem.OnApplicationFocus(bool)`. Its fatal stack explicitly names the generated OnApplicationFocus wrapper and `il2cpp_runtime_invoke`. The native method has a short body and a tail jump to `TickModules`; a metadata/signature existence test cannot validate its detour/trampoline execution. The exact interop defect is not established.
- Both sessions loaded the intended plugin before termination. The launcher correctly reported game exit `-1073741819` (0xC0000005); installation detection and Steam authentication were not the observed failure points.

## 0.22.7 correction

Remove both additional native integration paths. Keep only the three pre-existing Harmony patches (UI update and two notice gates). Poll focus and Alt/Tab from the existing UI update; cancel only an active native mark mode through its existing local ExitMark path. Do not send messages, alter gameplay, install global input hooks, or disable compatibility checks.

Regression checks exercise cancellation, held modifiers, focus regain, ordinary ping retention and missing scene state, and reject reintroduced injection/focus hooks. They do not reproduce the real IL2CPP runtime. Binary inspection and a real launch are required in addition to simulated tests.

Polling cannot detect a focus round-trip that occurs entirely between updates. The immediate native Alt-down cue may still appear before Tab is pressed. These limitations are preferable to an unsafe lifecycle hook and must not be represented as complete Alt-chord suppression.

## 0.22.8: cancellation versus confirmation

The 0.22.7 ExitMark call was not a true cancellation: ExitMarkMode dispatches MarkExit, whose MarkDetectionSystem listener calls SelectedCurrentTarget and then IMarkTarget.OnMarkSelected. Card targets send a BattleCardMessage; relic targets invoke ChatRelic. Checking only ExitMarkMode for direct network calls missed this event-dispatch path.

Before exiting, 0.22.8 calls ExitCurrentHover to clean native highlighting and hover timers, then clears only the detector's pending `_currentTarget` reference. ExitCurrentHover alone does not clear that field. The exit event still restores cursor/camera/UI state, but SelectedCurrentTarget now sees null and cannot confirm the pending target. Normal manual confirmations remain native. No extra hook or type injection is introduced, and no already-sent message is recalled.

The regression harness now models the exit-to-confirmation event: the old close-only path demonstrably sends a message, while cancellation with a pending target sends none. It covers shared card/relic/skill/task/player/land selection state and checks the installed native bindings. This is simulated testing, not multiplayer verification.

## Local verification

On 2026-09-27, the 0.22.7 build and automated checks passed; installed metadata and all eight deployed hashes matched. A normal Steam mod-shortcut launch at 17:56 loaded 0.22.7 and reached the UI update without either prior fatal signature. The user confirmed that they closed the game window manually. No new launcher failure log was produced; the new ErrorLog contained Steam initialization output, not the prior access violation. This confirms that this startup attempt survived the earlier crash points, not battle-time Alt+Tab behavior or all runtime paths.
