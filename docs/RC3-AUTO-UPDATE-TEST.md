# rc.2 to rc.3 automatic update test

## 한국어

현재 rc.3은 서명 전 검토 후보입니다. 비공개 릴리스 게시, 7개 자산 검증과 서명 승인이 끝나기 전에는 아래 실제 업데이트 시험을 시작하지 마세요. artifacts의 unsigned ZIP은 설치나 적용용이 아닙니다. 실제 게임 설치는 rc.2로 유지했습니다.

게시 완료 뒤 사용자 시험:

1. Steam의 Astral Party - Mod로 기존 rc.2를 실행하고 메인메뉴에서 모드 설정을 엽니다. 버전이 1.0.0-rc.2인지 확인합니다.
2. 일반 설정의 업데이트 채널에서 베타 포함을 선택합니다. 업데이트 연결에서 해당 비공개 저장소를 읽을 수 있는 기존 지원 PAT을 Windows 보안 입력 창에 입력합니다. 토큰을 로그·채팅·스크린샷에 넣지 마세요. 브라우저 로그인은 모드 연결과 별개입니다.
3. 자동 다운로드와 종료 후 적용을 켜고 업데이트 확인을 선택합니다. 새 버전 1.0.0-rc.3이 표시되는지 확인합니다.
4. 다운로드와 서명·호환성 검증이 끝나고 종료 후 적용 대기 상태가 표시될 때까지 기다립니다. 확인에 실패하거나 복구 안내가 나오면 시험을 중단하고 로그와 백업을 보존합니다.
5. 정상 메뉴로 게임과 모드 실행기를 종료합니다. 강제 종료하거나 ZIP을 게임 폴더에 직접 복사하지 않습니다. helper의 적용이 완료될 때까지 기다립니다.
6. Steam의 Astral Party - Mod를 직접 다시 실행합니다. 표시 버전 1.0.0-rc.3, 기존 설정 유지, 일반 설정·업데이트 설정 열림을 확인합니다. 한국어와 English로 각각 전환해 표시와 글리프를 확인합니다.
7. BetterAstralParty 미적용을 확인하려면 모드를 정상 종료한 뒤 순정 Steam 클라이언트에서 Astral Party의 INT를 실행합니다. loader 설정을 직접 바꾸지 않습니다.

구 rc.2 ZIP의 Install.cmd는 rc.3보다 오래된 묶음이라 차단되어야 합니다. 같은 버전의 다른 빌드도 검토된 복구 계획 없이 교체할 수 없습니다. 재설치·제거에는 현재 검증된 whole 패키지를 사용하세요. 기존 rc.2 제거 도구의 선택 정책은 격리 검사만 했으며 실제 백업·삭제 성공을 증명하지 않습니다.

사용자에게 확인받을 결과는 업데이트 알림, 다운로드 완료, 적용 대기, 정상 종료 후 적용, 재시작 버전, 설정 유지와 한국어·영어 UI입니다. 이번 작업은 이를 실제 게임에서 실행하지 않았습니다.

## English

RC.3 is an unsigned review candidate. Start the live test only after the private release is published and all seven assets and signatures are verified. The unsigned ZIP under artifacts is not for installation or application. The real installation remains rc.2.

1. Launch the existing rc.2 through Astral Party - Mod in Steam. Open Mod Settings at the home menu and confirm 1.0.0-rc.2.
2. Select the Beta-inclusive update channel. Connect Updates with an existing supported PAT that can read the private repository, using the secure Windows prompt. Keep the token out of logs, chat and screenshots; browser login is separate.
3. Enable Auto Download and Apply After Exit, then Check Updates. Confirm that 1.0.0-rc.3 is offered.
4. Wait for downloading, signature and compatibility verification, and the queued-after-exit status. Stop if verification fails or recovery is required; retain logs and backups.
5. Exit the game and mod launcher normally. Wait for the helper to finish. Do not force-close the game or copy ZIP contents into the game folder.
6. Restart Astral Party - Mod yourself. Confirm 1.0.0-rc.3, preserved settings, and working General and Update settings. Switch between Korean and English and inspect wording, clipping and glyphs.
7. For the vanilla check, close the mod normally and launch INT from the original Astral Party Steam entry. Do not edit loader settings.

An old rc.2 whole installer must reject a rc.3 installation. A different build of the same version also requires a reviewed recovery plan. Use the current verified whole package for reinstallation or removal. The older uninstaller selection policy has only been tested with isolated substitutes; real backup and removal remain unexecuted.

Record the offered version, download completion, queued status, application after normal exit, restarted version, settings preservation and Korean/English UI. No live game test was performed by this worker.
