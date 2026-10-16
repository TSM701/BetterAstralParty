# Input attention (0.19.0)

## Behavior

- General settings: Input Alert / 입력 대기 알림 cycles Taskbar → Windows → Off, default Taskbar, independent of battle advice and focus mute. The same config key is migrated from true to Taskbar and false to Off.
- Poll public local input readiness at 5 Hz while unfocused. No game/network mutations or identity/content logging.
- Only running PvE rooms containing the local player; exclude watchers and replay sessions.
- Taskbar mode flashes only this process's Unity window, never its console, another process or the foreground window.
- Windows mode uses the native notification-area balloon API with a temporary application icon and generic localized input-wait message; no identities or game content. It does not register shortcuts, change the registry, steal focus or play sound. OS notification settings can suppress the banner. This is a Shell notification, not a separately registered WinRT toast app; clicking it does not activate the game.
- Remove the temporary notification on focus return, wait end, OFF, mode change or normal process exit. A Shell failure is diagnosed and disables the feature rather than silently switching modes. Notification history/appearance is controlled by Windows, not by the mod.
- Start once per continuous wait, not on every frame. Stop on focus return, wait completion/cancellation/expiry, room exit, setting OFF or compatibility failure.
- Windows controls the appearance of flashing; OS taskbar settings may affect visibility.

## Native source audit

`GameLogic.OperationTimer.ActionDownTime` registers local input waits in `timerDict` and dispatches `BattleSignal.curPlayerOperate`. Other-player display countdowns use `ShowPlayerTimer` instead and are not included. Completion/cancellation callbacks remove the action; `Dispose` clears the registry.

29 native call sites were found: field move/card/quick-card prompts, movement arrows, fight challenge/card/defense choices, relic selection, discard, round-card selection, both assist-vote windows, monster pursuit, controlled movement, battery, divination, event skill, filling station/revive/mechanism, gambling and gambling dice, hospital, lottery, pursuit, relic purchase, vendor and PvE/PvP shops. PvP remains excluded by the feature's room gate. Attack dice that the native game rolls automatically are not a user-input wait.

The timer deliberately does not start for campaign/map 10. For eligible untimed PvE rooms, the fallback reads local `playerAction` or visible, touchable, non-grayed controls in the known action dialogs. Fight fallback is restricted to challenge/card/defense stages and excludes the native audience role. This is a verified-dialog list, not a claim that any future game dialog will automatically be recognized. Lobby selection, menus, chat and informational windows are outside the running-PvE scope.

## Verification

- `tools/test-input-attention.ps1`: native API/schema checks, current shared timer routes, cleanup wiring, no focus-stealing/network calls or identity logging.
- Self-test: all 192 combinations of focus, enable, running state, participation, replay, PvE and pending counts; untimed dialog filter; KO/EN text; settings layout; three-mode cycle, legacy preference migration and NOTIFYICONDATAW x64 marshaling offsets/size.
- Build tests do not prove actual Windows taskbar appearance or in-game UI readiness. QA should check Alt-Tab/minimize before each prompt, Alt-Tab during an existing prompt, returning to the game, timeout, OFF, room exit, watcher/replay suppression, practice mode and both UI languages.
- Win32 contracts: [FLASHWINFO](https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-flashwinfo), `FLASHW_TRAY | FLASHW_TIMERNOFG`, then `FLASHW_STOP`; [NOTIFYICONDATAW](https://learn.microsoft.com/windows/win32/api/shellapi/ns-shellapi-notifyicondataw), add/modify/delete with realtime (no stale queue), no sound and quiet-time respect. No additional dependencies. Actual banner appearance and in-game mode cycling still require QA; automated checks do not display desktop notifications.
