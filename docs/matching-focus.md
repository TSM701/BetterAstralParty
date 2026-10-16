# 매칭 성사 시 창 활성화 — 분리 개발 기록

2026-10-02. 자동 업데이트 checkpoint를 먼저 확정한 뒤 별도 기준선으로 시작했다. 이 기능은 **미완료·비활성**이며 실게임에 설치하지 않았다. 일반 설정의 기본 OFF 토글과 매칭 이벤트 연결은 아직 적용하지 않았다. 현재 DLL에 독립 유틸리티만 포함되며 외부 호출자가 0개다. 자동 업데이트 후보 ZIP은 이 유틸리티를 포함하지 않는 이전 checkpoint로 보존한다.

사용자 요구는 매칭이 성사되고 다른 프로그램이 활성화된 경우 게임 창을 앞으로 가져오는 기능과 별도 설정 토글이다. 계획한 설정 이름은 `매칭 성사 시 창 활성화` / `Focus When Match Found`, 초기값 OFF이며 저장된 선택을 유지한다. 실제 기능이 연결되기 전에 동작하는 것처럼 토글을 노출하지 않았다.

## 구현·검증된 독립 부분

`MatchFocusState`는 전달된 성공 이벤트에서만 동작하며 룸 상태를 폴링하지 않는다. OFF, 잘못된 룸, 매칭 룸 아님, 캐릭터 선택 단계 아님, 재접속을 거른다. 활성화 또는 실패 결과와 관계없이 동일 룸을 다시 처리하지 않는다. OFF/ON 및 재접속 시 기존 룸을 기록해 재실행을 막는다. 4,096개의 세션 식별자를 보존한 뒤 새 처리를 중단하므로 오래된 식별자를 지워 반복 포커스 이동을 허용하지 않는다. 식별자는 로그에 쓰지 않는다.

`WindowsMatchFocus`는 현재 프로세스의 최상위·보이는 `UnityWndClass` 창이 하나일 때만 대상으로 삼는다. 각 동작 전에 소유자·클래스·핸들을 다시 확인한다. 이미 같은 게임 프로세스가 활성화돼 있으면 동작하지 않는다. 최소화 시 `ShowWindowAsync(SW_RESTORE)`를 한 번 요청하고, `SetForegroundWindow`를 한 번 시도한다. 거부 시 작업표시줄을 3회 깜박이는 요청 한 번만 보낸다. 기존 입력 대기 알림의 중지 요청을 보내지 않는다.

Windows는 포커스 전환을 거부할 수 있다. 이 구현은 허용된 API의 결과를 따르며 키 입력 합성, 입력 스레드 연결, 포커스 권한/시스템 설정 변경, 강제 topmost를 사용하지 않는다. [SetForegroundWindow 제한](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow), [비동기 복원](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindowasync), [작업표시줄 알림](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-flashwindowex).

| 검사 | 결과와 한계 |
| --- | --- |
| NET6 모의·API 메타데이터 | 4,627 assertion PASS; Win32 함수는 호출하지 않음 |
| native 이벤트·비활성 production 호출자 감사 | 16 PASS; Cecil 읽기만 수행 |
| NET6 플러그인 빌드 | 경고 0 / 오류 0; 설치하지 않음 |
| 실제 서버 매칭·취소·재접속 이벤트 연결 | 미실행·미구현 |
| 기본 OFF 저장 설정·KR/EN 토글 및 렌더링 | 미구현·미실행 |
| 실제 다른 창에서 복원·포커스·taskbar | 미실행 |

## 실제 이벤트 근거와 차단된 연결안

읽은 파일은 `.research/extracted/AstralParty.Runtime.dll.dll`이다. `GameLogic.MatchLogic.OnMatchSuccessS2CServerCallBack(party.protocol.MatchSuccessS2C, int, bool) -> UniTask`의 상태 머신은 `errid != 0`에서 종료한다. 성공 때 `RoomController.CreateRoom`, `SwitchRoomState(..., CHOICE=2)`, `CancelMatch.Dispatch(Waiting=1)`를 수행하고 `UI.MatchSuccessWindow.ShowMatchResult()`를 호출한다. 이 마지막 메서드의 전체 IL 호출자는 성공 콜백 한 곳뿐이다. `ShowMatchResult -> TryShow -> FairyGUI.Window.Show`가 실제 성공 창의 생명주기 이벤트를 만든다.

`CancelMatch` signal만 성공 이벤트로 간주할 수 없다. `RefreshMatchTeamInfoS2C`와 `RefreshMatchTeamStateNotify`도 기존 상태를 전달해 취소 signal을 보낸다. 따라서 취소 signal/룸 상태를 단순 감시해 성공을 추측하는 구현을 추가하지 않았다.

검토에 제출했던 연결안은 성공 창의 `onAddedToStage`에 **기존 게임 네이티브 메서드**를 호출하는 delegate를 추가하고, 별도 mod 소유 `MatchData`의 `_teamId`가 초기값 1에서 0으로 바뀐 것을 기존 UI 프레임이 소비하는 구조였다. 네이티브 `MatchData.Dispose()`는 자신의 `_teamId`와 로컬 목록만 초기화한다. `DelegateSupport`, 새 관리형 게임 콜백, IL2CPP 타입 주입, detour, 네트워크 변경은 제안하지 않았다. 창의 기존 표시 상태를 성공 신호로 폴링하는 방식도 아니다.

자동 승인 검토는 이 패치를 **거부**했다. 원문 사유: “이 패치는 과거 네이티브 크래시가 확인되어 재사용하지 않기로 한 managed callback/EventBridge 주입 경로를 다시 사용하며, 게임 크래시 위험이 있고 해당 구현 방식은 승인되지 않았습니다.” 거부된 이벤트 연결 코드는 적용하지 않았으며 간접 실행으로 우회하지 않았다.

관련 [AGENTS.md](../AGENTS.md)는 “Do not reintroduce the 0.28.1 managed-delegate registration into hot-loaded FairyGUI Stage events.”라고 명시한다. 이 규칙은 관리형 Stage delegate 등록을 구체적으로 금지한다. 제출안은 기존 네이티브 메서드와 성공 창 객체의 이벤트를 사용하는 방식이지만, 검토기는 EventBridge 체인 변경도 재사용 위험으로 판단했다. 관리형 catch나 메타데이터 검사로 실제 네이티브 실행 안전성을 증명할 수 없다는 문서의 제한을 유지한다.

잔여 작업은 검토·승인된 안전한 이벤트 전달 경로 확정, 실제 연결, 별도 기본 OFF 저장 토글/KR·EN 문구, OFF/ON·재바인딩·중복·취소·재접속·창 종료 통합 검사, 명시적으로 허용된 실게임 검증이다. 승인만으로 실게임·설치·포커스 테스트의 범위를 넓히지 않는다.

증거와 별도 빌드: `artifacts/matching-focus-development/matching-focus-20261002`. 이전 자동 업데이트 후보·GA·설치 beta·vanilla loader OFF 보호 해시는 별도 보존 검사에 기록한다.

## English

This feature remains incomplete and inactive. Independent event-consumer/window utilities compile and pass 4,627 mock/metadata assertions and 16 read-only game/production metadata checks. They have zero production callers. No setting toggle, game event transport, installation, native foreground call or runtime rendering test was executed.

The actual successful server callback is the sole caller of the match-success window's `ShowMatchResult`. Cancellation signals are also dispatched during team refresh, so they cannot alone establish a match-found event. The proposed native-only event mailbox was rejected by automatic approval review as a crash-risk EventBridge path. It was not applied or bypassed. Approved event transport, a persisted default-OFF KR/EN toggle, integration verification and separately authorized runtime checks remain necessary.
