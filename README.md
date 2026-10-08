# BetterAstralParty

[한국어](#한국어) · [English](#english)

## 한국어

Astral Party 국제판 INT(Global)의 PvE 정보 표시와 판단을 돕는 한국어·영어 지원 편의성 모드입니다.

[최신 안정판 다운로드](https://github.com/TSM701/BetterAstralParty/releases/latest)

### 주요 기능

- 공개 정보를 사용하는 PvE 전투 추천과 카드 평가
- KO·생존 최소 조건과 최종 피해 표시
- 필드 플레이어 이름·HP 및 필드·전투의 공개 버프 표시
- 카드·칩 핑 상세 팝업과 손패 정렬 옵션
- 인게임 모드 설정, 캐릭터 이름 옵션, 창 비활성화 시 음소거
- 별도의 Astral Party - Mod Steam 항목

PvP 방·경기가 감지되면 전투 추천·카드 정렬·추가 정보 표시·비활성 창 음소거 등 모드 편의 기능이 일시 중지됩니다. PvP에서 나오면 저장된 설정대로 다시 작동하며, 업데이트와 진단 처리는 PvP에서도 계속됩니다.

### 지원 환경

- Windows x64, Steam, 설치된 INT(Global) 클라이언트가 필요합니다. CN 클라이언트는 지원하지 않습니다.
- 한국어 패치는 별도로 설치하세요. 기존에 설치한 패치는 유지합니다.

### 설치와 실행

1. GitHub Releases에서 `BetterAstralParty-1.0.0.zip`처럼 버전명이 붙은 전체 설치 ZIP을 받으세요. `-update.zip`과 Source code ZIP은 최초 설치용이 아닙니다.
2. 원래 Steam 항목으로 INT(Global)를 한 번 실행해 게임 업데이트와 데이터 다운로드를 마친 뒤 종료하세요.
3. 쓰기 가능한 별도 폴더에 ZIP 전체를 압축 해제하고, 동봉된 파일과 폴더를 함께 유지하세요. 게임 폴더 위에 직접 덮어쓰거나 ZIP 내부에서 실행하지 마세요.
4. Steam에 로그인한 상태에서 압축을 푼 폴더의 `Install.cmd`를 실행하세요. PowerShell에서는 `.\Install.cmd`를 실행합니다. 경로를 묻는 경우 Steam의 설치된 파일 → 찾아보기로 확인한 `Astral Party` 폴더 또는 `8vJXnINT` 하위 폴더를 선택하세요.
5. Steam 라이브러리의 Astral Party - Mod로 실행하세요. 첫 실행은 로더 초기화와 파일 다운로드로 시간이 걸리며 인터넷 연결이 필요할 수 있습니다.

Steam 항목 등록·수정 때문에 종료 안내가 나오면 대상 목록을 확인하세요. `y`를 입력하면 표시된 Steam 게임·앱과 Steam이 강제 종료될 수 있으므로 진행 상황을 저장하고 동기화를 마치세요. 빈 입력이나 거절은 취소하며, 작업 후 Steam만 다시 실행합니다.

바닐라는 원래 Steam 항목에서 INT(Global)를 선택하거나 게임 폴더의 `AstralParty-Vanilla.cmd`로 실행하세요. 전환 전 게임을 종료하고, 원래 Steam 항목의 실행 옵션은 변경하지 마세요.

### 설정

메인메뉴의 모드 설정에서 기능과 표시 옵션을 조절합니다. 일반 설정 → 언어에서 자동, 한국어, English를 선택하세요. 자동은 게임 메뉴 언어를 따르며 게임 원본 설명과 플레이어 이름은 그대로 표시합니다.

스크린샷과 오버레이는 본인의 Steam 단축키를 사용하세요.

### 업데이트

- 자동 업데이트: 모드 설정에서 자동 다운로드와 종료 후 적용을 켜세요. 적용 대기가 완료되면 게임과 모드 실행기를 정상 종료하고, 적용 후 직접 다시 실행하세요.
- 수동 업데이트: 게임과 모드 실행기를 종료한 뒤 새 전체 패키지의 `Install.cmd`를 실행하세요. 기존 설정은 유지합니다. 설치된 버전보다 낮은 버전으로 덮어쓰는 것은 차단합니다.
- 비공개 Beta 인증: 모드 설정의 업데이트 인증을 선택하고, Windows 보안 입력 창에 저장소 읽기 권한이 있는 PAT을 입력하세요. 인증은 현재 Windows 사용자의 자격 증명 관리자에 저장돼 재실행 후에도 유지됩니다. 업데이트 로그아웃을 선택하면 삭제하며, 만료·철회된 토큰은 재인증하세요. 브라우저 로그인과 모드의 업데이트 인증은 별개입니다.

### 제거

게임과 모드 실행기를 종료한 뒤 전체 패키지의 `Uninstall.cmd`를 실행하고 제거 목록을 확인하세요. 모드 파일과 설정은 `%LOCALAPPDATA%/BetterAstralParty/Backups`에 백업한 뒤 제거합니다. 최신 전체 패키지 사용을 권장하며, 지원되는 이전 패키지의 제거 도구도 사용할 수 있습니다.

공용 BepInEx 파일, 다른 모드, 한국어 패치와 원래 Steam 항목은 유지합니다. 로더 설정은 설치 전 상태로 복원합니다. 구버전 기록이 없어 선택을 묻는 경우 `1`은 다른 BepInEx 모드 실행을 유지하고, `2`는 모든 BepInEx 모드를 끕니다. 빈 입력은 취소합니다.

완전 제거 후에는 전체 패키지의 `Install.cmd`로 재설치할 수 있습니다. 복구 안내가 나오면 로그와 백업을 보관하고 표시된 절차를 따르세요.

### 문제 해결과 제보

- Steam 항목이 안 보이면 Steam을 재시작하고 라이브러리 필터와 로그인 계정을 확인하세요. 항목 복구에는 `Install.cmd`를 사용하세요.
- 설치·실행 오류는 `Open-Logs.cmd`로 로그를 확인하세요. 호환성 검사 오류가 나면 오류 문구를 함께 제보하세요.
- 계산 문제는 모드 설정에서 진단을 켠 뒤 재현하세요.
- 진단 ZIP은 패키지의 `Create-Diagnostics.cmd` 또는 게임의 F8 → 일반 설정 → 진단 ZIP 만들기로 생성하세요. 기본 저장 위치는 `%LOCALAPPDATA%/BetterAstralParty/Diagnostics`입니다.

모드 버전, 재현 순서, 스크린샷과 관련 로그 또는 진단 ZIP을 함께 전달하세요. 공유 전 개인정보와 사용자 지정 경로를 확인하세요.

### 소스와 라이선스

BetterAstralParty 자체 코드는 MIT 라이선스로 제공합니다. 외부 구성 요소의 고지와 대응 소스 안내는 `THIRD-PARTY-NOTICES/`에서 확인하세요. 소스 빌드 방법은 공개 소스 묶음의 `docs/BUILD.md`에 있습니다.

## English

A Korean/English quality-of-life mod for Astral Party INT(Global), focused on PvE information and decision support.

[Download the latest stable release](https://github.com/TSM701/BetterAstralParty/releases/latest)

### Features

- PvE combat recommendations and card evaluation using public information
- Minimum KO/survival conditions and observed final damage
- Field player names/HP and public buff displays in the field and combat
- Detail popups for card and chip pings, and hand-layout options
- In-game settings, character-name options and mute on focus loss
- A separate Astral Party - Mod Steam entry

When a PvP room or match is detected, mod conveniences such as combat recommendations, hand layouts, additional information displays and focus muting pause. They resume with your saved settings after leaving PvP; updates and diagnostics continue even during PvP.

### Requirements

- Windows x64, Steam and an installed INT(Global) client. The CN client is not supported.
- Install a Korean patch separately if needed. An existing patch is preserved.

### Install and play

1. Download the complete installation ZIP from GitHub Releases, named by version, such as `BetterAstralParty-1.0.0.zip`. Neither `-update.zip` nor Source code ZIPs are first-install packages.
2. Launch INT(Global) through the original Steam entry, finish game/data updates, then close it.
3. Extract the entire ZIP to a separate writable folder and keep all bundled files and folders together. Do not overwrite the game folder directly or run inside the ZIP.
4. While signed in to Steam, run `Install.cmd` from the extracted folder. In PowerShell, run `.\Install.cmd`. If prompted, select the `Astral Party` folder or its `8vJXnINT` child, as shown by Steam's Installed Files → Browse.
5. Launch Astral Party - Mod from your Steam library. First-time loader initialization and file downloads may take time and require internet access.

If Steam entry registration or repair prompts you to close processes, review the target list. Entering `y` may force-close the listed Steam games/apps and Steam, so save progress and finish syncing first. A blank response or refusal cancels; only Steam restarts afterward.

For vanilla, select INT(Global) from the original Steam entry or run `AstralParty-Vanilla.cmd` in the game folder. Close the game before switching and leave the original Steam entry's launch options unchanged.

### Settings

Open Mod Settings from the main menu to configure features and display options. Select Auto, 한국어 or English under General → Language. Auto follows the game menu language; native game descriptions and player names remain unchanged.

Use your own Steam screenshot and overlay hotkeys.

### Updates

- Automatic updates: enable Auto Download and Apply After Exit in mod settings. Once the update is queued, close the game and mod launcher normally, then restart the game yourself after it finishes.
- Manual updates: close the game and mod launcher, then run `Install.cmd` from the new complete package. Existing settings are preserved. In-place downgrades are blocked.
- Private Beta authentication: select Connect Updates in mod settings and enter a PAT with repository read access in the secure Windows prompt. Windows Credential Manager saves it for the current Windows user across restarts. Sign Out of Updates deletes it; sign in again if the token expires or is revoked. Browser login and the mod's update authentication are separate.

### Uninstall

Close the game and mod launcher, run `Uninstall.cmd` from a complete package and review the removal list. Mod files and settings are backed up under `%LOCALAPPDATA%/BetterAstralParty/Backups` before removal. Use the latest complete package when possible; supported older uninstallers can also be used.

Shared BepInEx files, other mods, Korean patches and the original Steam entry are preserved. The loader setting is restored to its pre-installation state. If a legacy installation has no record and prompts for a choice, `1` keeps other BepInEx mods running and `2` disables all BepInEx mods. A blank response cancels.

After complete removal, run `Install.cmd` from a complete package to reinstall. If recovery is required, keep logs and backups and follow the displayed procedure.

### Troubleshooting and reports

- Missing Steam entry: restart Steam and check library filters and the signed-in account. Use `Install.cmd` to repair the entry.
- Installation or launch errors: open logs with `Open-Logs.cmd`. Include the error message when reporting a compatibility-check failure.
- Calculation issues: enable diagnostics in mod settings, then reproduce the issue.
- Create a diagnostics ZIP using the package's `Create-Diagnostics.cmd` or F8 → General → Create Diagnostics ZIP in game. The default output folder is `%LOCALAPPDATA%/BetterAstralParty/Diagnostics`.

Include the mod version, reproduction steps, screenshots and relevant logs or diagnostics ZIP. Review personal information and custom paths before sharing.

### Source and licenses

BetterAstralParty-owned code is provided under the MIT license. See `THIRD-PARTY-NOTICES/` for third-party notices and corresponding-source information. Build instructions are in `docs/BUILD.md` in the public source archive.
