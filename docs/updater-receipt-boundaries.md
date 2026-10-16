# receipt 경계와 실패 분류

2026-10-02 TP-02 후속 수정. 이전 delivery-1307 후보의 receipt 검사는 Prepare 시작 전만 검증했고, Prepare 뒤 helper.ready 전에 receipt가 없어지면 helper exit 4가 Queue에서 Failed로 바뀌었다. 이전 기록의 TP-02 해결 판정은 이 경계에 대해 불완전했다. 이전 후보·기록은 보존한다.

UpdateHandoff의 준비 전 종료 관찰과 Job.Poll은 같은 종료 코드 분류를 사용한다. 준비 전 ManualUpgradeRequired·FilesBusy·AlreadyClaimed·Cancelled는 실패 IPC를 설정하지 않는다. 성공 종료는 인증된 ready 없이 허용하지 않으며, 시간 초과에는 실행 중인 프로세스의 ExitCode를 읽지 않는다. 실제 준비·무결성 실패는 기존 failure 신호와 두 옵션 OFF 정책을 유지한다.

helper는 receipt가 없거나 지원하지 않는 형식일 때 정확한 ticket hash에 묶인 ManualUpgradeRequired 결과를 저장하고 자기 operation guard를 완료한다. 같은 완료 ticket 재실행은 exit 4로 끝나며 새 작업을 시작하지 않는다. 준비 완료 후 게임 종료 대기 중에 receipt가 바뀌는 경우도 transaction의 첫 교체 전에 검사한다. 이때도 부재·지원하지 않는 receipt는 InstallationRequired로 분류한다. 유효한 receipt와 소유 파일 해시 불일치, 링크·경로·파일 정체성 오류, signed package 오류는 이 분류로 완화하지 않는다. 실제 실패 latch는 명시적인 재검사·재활성화까지 유지한다.

receipt 형식 자체가 지원되지 않는 경우는 기존 Prepare 정책과 같은 수동 업그레이드 전제 조건이다. 이 분류는 receipt 변경이 악의적인지 증명하지 않으며, 자동 적용을 허용하지 않는다. 모든 조건 검사는 현재 사용자 권한과 고정된 Windows 파일 fence로 수행한다.

ReceiptBoundaryFixture는 실제 UpdateHandoff.Prepare 후, Ready에서 적용 옵션을 켜기 전, helper.ready 이후 자연 종료 전의 세 경계를 검사한다. 실제 coordinator·BepInEx ConfigEntry/bridge·Framework helper를 사용하며, 원래 ON과 저장된 다운로드 OFF/적용 ON 조합, 설정 파일 재로딩, 실패 IPC, guard, ticket 결과, 8개 소유 대상과 무관한 파일 보존을 확인한다. receipt hardlink·package 손상·유효 receipt hash 불일치는 실제 실패 대조군이다. fixture만 임시 RSA 키와 가짜 설치·자연 종료 프로세스를 사용한다.

운영 인증 provider·HTTP/session 연결, 운영 공개 trust/fingerprint 선택과 전달, private 저장소 접근·외부 서명·배포는 별도 미완료 항목이다. 장기 토큰·운영 개인 키를 제품에 넣지 않는다. 이 fixture 결과는 native 게임/UI·실제 설치·실제 private 릴리스 검증을 대체하지 않는다.
