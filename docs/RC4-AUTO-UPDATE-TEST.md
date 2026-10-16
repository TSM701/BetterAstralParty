# rc.3 to rc.4 automatic update test

## 한국어

rc.4는 GitHub JSON 요청을 수정한 rc.3에서 버전 정보만 갱신한 자동 업데이트 검증 후보입니다. 소스 변경·빌드·격리 검사는 실제 설치나 인게임 성공을 증명하지 않습니다. 서명·독립 검토·비공개 게시와 자산 검증이 완료되기 전에는 실제 업데이트 시험을 시작하지 마세요. unsigned ZIP은 설치용이 아닙니다.

1. 검증된 수정 rc.3 whole ZIP을 기존 설치에 적용하고 설치된 DLL·런처·지원 파일의 해시와 버전을 확인합니다. 설치 오류나 미완료 복구 상태가 있으면 중단하고 로그·백업을 보존합니다.
2. 정상 실행된 rc.3 메인메뉴에서 모드 설정을 열고 베타 포함 채널, 자동 다운로드, 종료 후 적용을 확인합니다. 비공개 저장소 연결이 필요하면 지원되는 보안 입력 창에 기존 PAT을 사용자가 직접 입력합니다. 토큰을 채팅·로그·스크린샷에 넣지 않습니다.
3. 업데이트 확인 후 1.0.0-rc.4 제공, 다운로드 완료, 서명·호환성 검증, 종료 후 적용 대기를 확인합니다. 실패 시 로그와 백업을 보존하고 중단합니다.
4. 게임과 모드 실행기를 정상 종료하고 helper 적용 완료를 기다립니다. 게임을 강제 종료하거나 ZIP 내용을 게임 폴더에 직접 덮어쓰지 않습니다.
5. 설치된 파일의 rc.4 버전·해시·receipt를 확인한 뒤 모드를 다시 실행합니다. 설정 유지, 한국어·English UI, 일반·업데이트 설정 열림을 확인합니다. 설치 성공과 런타임 확인은 별도로 기록합니다.

이 문서 작성 시점에는 실제 rc.3→rc.4 자동 업데이트 시험을 실행하지 않았습니다. 기존 RC3-AUTO-UPDATE-TEST.md와 rc.3 배포 증거는 역사 기록으로 보존합니다.

## English

RC.4 is a version-only automatic-update test candidate based on the rc.3 GitHub JSON request fix. Source, build and isolated checks do not establish installation or in-game success. Start the live test only after signing, independent review, private publication and asset verification. Unsigned ZIPs are not installable releases.

1. Apply the verified corrected rc.3 whole ZIP through the supported installer. Verify installed DLL, launcher and supporting-file hashes and versions. Stop on an installation error or pending recovery; preserve logs and backups.
2. At the working rc.3 home menu, confirm the Beta-inclusive channel, Auto Download and Apply After Exit. If private repository authentication is required, the user enters an existing supported PAT in the secure prompt. Keep tokens out of chat, logs and screenshots.
3. Check Updates and confirm the rc.4 offer, download completion, signature and compatibility checks, and queued-after-exit status. Stop on failure and preserve logs and backups.
4. Exit the game and launcher normally, then wait for helper completion. Do not force-close the game or overwrite game files with extracted ZIP contents.
5. Verify installed rc.4 versions, hashes and receipt before restarting the mod. Check preserved settings, Korean and English UI, and General and Update settings. Record installation and runtime verification separately.

The live rc.3→rc.4 automatic-update test was unexecuted when this document was prepared. Preserve RC3-AUTO-UPDATE-TEST.md and rc.3 distribution evidence as historical records.
