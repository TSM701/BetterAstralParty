<div align="center">

<img src=".github/assets/banner.jpg" alt="BetterAstralParty" width="100%">

# BetterAstralParty

Astral Party INT(Global)의 PvE 정보 표시와 판단을 돕는 편의성 모드<br>
A quality-of-life mod for Astral Party INT(Global) PvE

[![Release](https://img.shields.io/github/v/release/TSM701/BetterAstralParty?label=release&color=ff4fa3)](https://github.com/TSM701/BetterAstralParty/releases)
[![Downloads](https://img.shields.io/github/downloads/TSM701/BetterAstralParty/total?color=f7c948)](https://github.com/TSM701/BetterAstralParty/releases)
[![License](https://img.shields.io/github/license/TSM701/BetterAstralParty?color=8bd3a8)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-b9a7f5)

[한국어](#한국어) · [English](#english)

</div>

---

## 한국어

<div align="center">

[![다운로드](https://img.shields.io/badge/%EB%8B%A4%EC%9A%B4%EB%A1%9C%EB%93%9C-ff4fa3?style=for-the-badge&logo=github&logoColor=white)](https://github.com/TSM701/BetterAstralParty/releases)
[![설치 방법](https://img.shields.io/badge/%EC%84%A4%EC%B9%98_%EB%B0%A9%EB%B2%95-f7a440?style=for-the-badge)](#설치와-실행)
[![문제 제보 및 기능 요청](https://img.shields.io/badge/%EB%AC%B8%EC%A0%9C_%EC%A0%9C%EB%B3%B4_%EB%B0%8F_%EA%B8%B0%EB%8A%A5_%EC%9A%94%EC%B2%AD-6c8cff?style=for-the-badge)](https://github.com/TSM701/BetterAstralParty/issues/new/choose)

</div>

### 주요 기능

| 전투 | 필드 | 편의 |
| --- | --- | --- |
| 공개 정보 기반 전투 추천과 카드 평가 | 플레이어·몬스터 필드 인디케이터 | 인게임 모드 설정과 한국어·영어 UI |
| KO·생존 최소 조건 표시 | 공개 버프·반격 표시 | 창 비활성화 시 음소거, 입력 대기 알림 |
| 전투 확률과 최종 피해 표시 | 필드 휠 확대·축소와 자유 시점 | PvE 매칭 성사 시 창 포커스 |
| 슈슈 쉴드 표시와 방어 제한 | 손패 기능별 정렬 | 감사합니다 추천 자동 전송 |
| 카드·칩 핑 상세 팝업 | 캐릭터 역할·준비 설명 | 게임 안에서 적용되는 자동 업데이트 |

> [!NOTE]
> PvP 방·경기가 감지되면 모드 편의 기능이 일시 중지되고, PvP에서 나오면 저장된 설정대로 다시 작동합니다. 업데이트와 진단은 PvP에서도 계속됩니다.

### 지원 환경

- Windows x64, .NET Framework 4.8, Steam, 설치된 INT(Global) 클라이언트가 필요합니다. CN 클라이언트는 지원하지 않습니다.
- 한국어 패치는 별도로 설치하세요. 기존에 설치한 패치는 유지합니다.
- 다른 모드나 기존 공용 로더와 구성·버전이 달라도 설치·실행·업데이트·제거를 막지 않으며 기존 파일과 설정은 보존합니다.

### 설치와 실행

> [!IMPORTANT]
> 1.3.x 사용자는 게임을 종료하고 1.4.0 패키지의 `Install.cmd`를 실행하세요. 기존 설치를 이어받아 이전 실행기·업데이트 도우미 파일을 정리하고 Astral Party - Mod 항목을 새 실행 방식으로 바꾸며, 설정은 유지합니다. 1.3.x는 1.4.0으로 자동 업데이트되지 않고 게임 안에 `수동 다운로드 필요`로 표시됩니다.

> [!WARNING]
> 1.2.x 이하 사용자는 사용하던 패키지의 `Uninstall.cmd`로 먼저 제거한 뒤 새로 설치하세요. 제거에 실패하면 새 패키지의 `Force-Uninstall.cmd`를 시도하고, 그래도 해결되지 않으면 보관할 데이터를 백업한 뒤 게임을 클린 설치하고 다시 시도해주세요.

1. [다운로드](https://github.com/TSM701/BetterAstralParty/releases)에서 `BetterAstralParty-<버전>.zip`을 받으세요. 소스 ZIP은 설치용이 아닙니다.
2. 순정 Steam 클라이언트에서 Astral Party INT(Global)를 한 번 실행해 업데이트와 데이터 다운로드를 마친 뒤 정상 종료하세요.
3. 쓰기 가능한 별도 폴더에 ZIP 전체를 압축 해제하세요. 게임 폴더에 직접 덮어쓰거나 ZIP 안에서 실행하지 마세요.
4. Steam에 로그인한 상태에서 압축을 푼 폴더의 `Install.cmd`를 실행하세요. 경로를 묻는 경우 Steam의 설치된 파일 → 찾아보기로 확인한 `Astral Party` 폴더 또는 `8vJXnINT` 하위 폴더를 선택하세요.
5. Steam 라이브러리의 Astral Party - Mod로 실행하세요. 콘솔 창이 잠깐 보였다 사라질 수 있으며, 첫 실행은 로더 초기화와 파일 다운로드로 시간이 걸리고 인터넷 연결이 필요할 수 있습니다.

> [!TIP]
> 모드 없이 실행하려면 순정 Steam 클라이언트로 실행하거나 게임 폴더의 `AstralParty-Vanilla.cmd`를 실행하세요. 두 실행 방법은 BetterAstralParty 적용 여부만 구분하며 다른 BepInEx 모드는 그대로 유지합니다. 전환 전 게임을 종료하고 Steam의 실행 옵션은 바꾸지 마세요.

<details>
<summary>설치되는 파일과 추가 안내</summary>

- 설치 도구는 패키지의 `artifacts/BetterAstralParty-Setup.exe`이며 게임 폴더에 복사되지 않습니다. 게임 폴더에는 `BetterAstralParty-Mod.cmd`, `AstralParty-Vanilla.cmd`, `BetterAstralParty.compatibility.json`, `BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll`과 설치 기록만 남습니다.
- Steam 경로나 계정을 찾지 못하면 바로가기는 건드리지 않고 파일만 설치합니다. 순정 Steam 클라이언트에서 게임을 실행한 뒤 `Install.cmd`를 다시 실행하면 바로가기를 추가할 수 있습니다.
- 게임이나 Steam을 종료해야 하면 설치 도구가 안내합니다. 직접 종료한 뒤 재확인하거나 취소할 수 있으며, 설치 도구가 프로세스를 강제로 종료하지는 않습니다.
- 새 공용 로더는 ON으로 설치됩니다.

</details>

### 설정

로비의 모드 설정 버튼 또는 F8로 열고 모드 기능·일반 설정 탭에서 조절하세요. 변경 사항은 자동 저장되며, 업데이트해도 기존 저장값은 유지합니다. 아래는 처음 설치했을 때의 기본값입니다.

<details>
<summary>모드 기능 탭</summary>

전투 추천
- 전투 추천: 기본 ON. 공개 정보를 바탕으로 공격·놓아주기, 방어·회피, 카드 사용을 추천합니다. 선택을 자동 입력하지 않으며, 모드 전체를 끄는 스위치가 아닙니다.
- 호버 수치: 기본 ON. 전투 추천에 마우스를 올리면 피해와 전투불능 확률을 표시합니다. OFF는 수치만 숨기고 추천 아이콘은 유지합니다.
- KO 최소 조건 표시: 기본 ON. 공격·다른 사람 전투에서는 KO 조건, 내가 방어자이면 생존 조건을 가장 유리한 결과 기준으로 표시합니다. 카드 호버 조건도 포함합니다.
- 최종 피해 표시: 기본 ON. 공개된 최종 수치로 계산한 피해를 표시합니다.

전투 표시
- 전투 정보 표시: 기본 ON. 전투 HP 아래 공개 손패 장수·효과와 플레이어·몬스터의 반격 상태를 표시합니다.
- 슈슈 쉴드 표시: 기본 ON. 전투 캐릭터의 활성 쉴드 효과를 표시합니다.
- 슈슈 쉴드 방어 제한: 기본 ON. 쉴드가 있는 내가 방어자일 때 방어 버튼·방어 카드를 잠급니다. 회피 가능 여부는 바꾸지 않고 회피를 자동 입력하지 않습니다.
- 반격 표시 애니메이션·소리: 각각 기본 ON. 애니메이션 OFF는 정적인 반격 아이콘을 표시합니다.

필드·카메라
- 플레이어 필드 인디케이터: 기본 ON. 필드에 플레이어 이름·HP·공개 효과와 추가 반격 아이콘을 표시합니다. 효과 아이콘을 짧게 클릭하면 게임 기본 설명을 엽니다.
- 필드 휠 줌: 기본 ON. PvE 필드에서 휠 줌과 자유 시점을 사용하며 턴이 바뀌어도 유지합니다. 턴 바의 핀 오른쪽 자유 시점 카메라 종료 버튼으로 기본 시점에 복귀합니다.
- 줌 아웃 인디케이터 반투명·원거리 인디케이터 숨김: 각각 기본 ON. 필드 휠 줌을 사용할 때 인디케이터에 적용합니다.

카드
- 손패 기능별 정렬: 기본·클릭 펼치기·호버 펼치기 중 선택하며 처음에는 기본입니다. 내 PvE 필드 손패를 기능별로 묶고 클릭은 가로로, 호버는 위로 펼칩니다. 클릭은 카드 위쪽, 호버는 오른쪽의 강조된 제출 영역으로 드래그하세요.
- 카드·칩 핑 팝업: 기본 ON. 게임의 카드·칩 핑 말풍선을 클릭하면 원본 이미지와 설명을 추가 팝업으로 엽니다.
- 카드 대상 선택의 확인·취소 버튼은 항상 카드보다 앞에 표시됩니다.

캐릭터
- 캐릭터 이름: 본명·이명 중 선택하며 기본은 본명입니다.
- 캐릭터 역할 표시·캐릭터 준비 설명: 각각 기본 ON. 준비 초상화 호버에 공개 스킬·재사용 대기시간·잠재력을 표시합니다.

편의 기능
- 자동 감사합니다: 기본 ON. 게임 기본 감사합니다 추천이 전송 가능할 때 추천 한 건당 한 번 전송합니다. PvP·관전·재생에서는 전송하지 않습니다.
- 창 전환 후 핑 모드 해제: 기본 ON. Alt+Tab 후 남은 핑 선택 모드를 전송 없이 해제합니다.

</details>

<details>
<summary>일반 설정 탭</summary>

화면·언어
- 언어: 자동·한국어·English 중 선택하며 기본은 자동입니다. 자동은 게임 메뉴 언어를 따릅니다.
- 표시 크기: 기본 100%, 75~150%. 모드 추천·전투 상태 패널만 조절합니다.
- 배경 불투명도: 기본 100%, 0~100%. 모드 전투 패널의 바탕만 바꿉니다.

알림·소리
- 창 비활성화 시 음소거: 기본 ON. 다른 창으로 전환하면 게임 소리를 끄고 복귀하면 복원합니다.
- 입력 대기 알림: 작업표시줄·Windows 알림·OFF 중 선택하며 기본은 작업표시줄입니다.
- 매칭 성사 시 창 포커스: 기본 ON. PvE 매칭 성사나 방장의 게임 시작 때 창을 한 번 앞으로 올립니다.
- 시작 공지 자동 열기 방지: 기본 ON. 로그인 공지가 자동으로 열리는 것만 막습니다.

업데이트
- 업데이트 채널: 릴리즈·프리 릴리즈·비공개 베타 중 선택하며 기본은 릴리즈입니다. 비공개 베타만 토큰 인증이 필요합니다.
- 자동 업데이트 확인·업데이트 알림 팝업: 각각 기본 ON.
- 자동 다운로드·자동 적용: 각각 기본 ON. 자동 적용의 이전 이름은 종료 후 적용입니다.
- 업데이트 확인·업데이트 받기·취소·다운로드 페이지는 실행 버튼입니다.

진단
- 충돌 진단 로그: 기본 OFF. 오류·성능·공개 전투 계산의 상세 진단을 기록합니다.
- 진단 폴더 열기·진단 ZIP 만들기는 실행 버튼입니다.

</details>

### 업데이트

- 자동 업데이트는 1.4.0 설치부터 지원합니다. 자동 다운로드와 자동 적용을 켜 두면 검증된 업데이트를 게임 실행 중에 적용하고 다음 실행부터 새 버전을 사용합니다. 게임을 종료할 필요가 없고 별도 창도 열리지 않습니다.
- 수동 업데이트는 게임을 종료한 뒤 상위 버전 전체 패키지의 `Install.cmd`를 실행하세요. 설정은 유지합니다.
- 안전 모드: 두 번 연속 홈 화면까지 시작하지 못하면 다음 실행은 모드 기능을 끄고 업데이트만 동작합니다. 홈 화면에 도달하면 해제됩니다.

<details>
<summary>채널 전환과 세부 동작</summary>

- 릴리즈와 프리 릴리즈는 공개 저장소에서 로그인 없이 확인하며, 공개 채널에서는 1.4.0 이상만 자동 업데이트 대상으로 선택합니다. 지원 버전이 없으면 현재 설치를 유지합니다.
- 채널 전환은 선택한 채널의 지원되는 최신 릴리스를 확인합니다. 설치된 버전보다 낮아도 전환할 수 있으며, 업데이트 채널 아래의 `채널 전환` 버튼에서 대상 버전을 확인하고 직접 승인해야 합니다. 같은 버전만 있으면 `채널 전환 대기`로 표시합니다. 업데이트 채널을 다시 바꾸면 승인이 취소됩니다. 같은 채널의 일반 업데이트는 버전 역행을 허용하지 않습니다.
- 같은 공개 버전이 프리 릴리즈↔릴리즈로 바뀌어도 설치된 파일과 저장한 채널은 그대로 둡니다.
- 수동 업데이트는 버전 역행과 같은 버전의 다른 파일 덮어쓰기를 차단합니다.
- Mod 실행 시 공용 로더가 OFF라면 안내창 없이 ON으로 전환하며, 변경 전 원본은 `BepInEx/config/BetterAstralParty.loader-before-*.bak`에 보관합니다.
- 이전에 받은 패키지의 설치·제거 도구는 자동 업데이트로 바뀌지 않으므로 최신 전체 패키지를 사용하세요.
- 자동 업데이트 파일과 임시 작업 파일은 게임 폴더의 `BetterAstralParty.Update/`에 모입니다.
- 게임 핫픽스로 파일 해시나 카탈로그가 달라진 것만으로 설치·실행을 막지 않습니다. 실제로 맞지 않는 선택 기능만 중단합니다.
- 안전 모드로 세 번 실행하면 전체 모드를 한 번 다시 시도합니다. 새 BetterAstralParty 버전이나 바뀐 게임 빌드는 횟수를 초기화합니다.
- 모드를 켠 상태로 게임 자체가 시작되지 않으면 순정 Steam 클라이언트로 실행하거나 `Create-Diagnostics.cmd`로 진단 ZIP을 만들고 최신 전체 패키지를 설치하세요.

</details>

### 제거

최신 전체 패키지의 `Uninstall.cmd`를 실행하고 제거 목록을 확인하세요. 모드 파일과 설정은 `%LOCALAPPDATA%/BetterAstralParty/Backups/v5`에 백업한 뒤 제거합니다. 공용 BepInEx 파일과 로더 설정, 다른 모드, 한국어 패치는 유지합니다. 1.2.x 이하 버전은 사용하던 패키지의 제거기를 사용하세요.

> [!CAUTION]
> 정상 제거가 실패하면 `Force-Uninstall.cmd`를 실행하고 삭제 목록을 확인하세요. 동의하면 표시된 BetterAstralParty 파일·설정·해당 설치의 복구 백업을 되돌릴 수 없게 삭제합니다. 공용 로더와 다른 모드는 보존합니다.

<details>
<summary>제거 추가 안내</summary>

- Steam 경로나 계정을 찾지 못하면 바로가기 정리는 건너뜁니다. 남은 Astral Party - Mod 항목은 Steam에서 직접 제거할 수 있습니다.
- 완전 제거 후에는 전체 패키지의 `Install.cmd`로 재설치할 수 있습니다. 복구 안내가 나오면 로그와 백업을 보관하고 표시된 절차를 따르세요.

</details>

### 문제 해결과 제보

- Steam 항목이 안 보이면 Steam을 재시작하고 라이브러리 필터와 로그인 계정을 확인하세요. 항목 복구에는 `Install.cmd`를 사용하세요.
- 설치·실행 오류는 `Open-Logs.cmd`로 로그를 확인하세요. 계산 문제는 모드 설정에서 충돌 진단 로그를 켠 뒤 재현하세요.
- 진단 ZIP은 패키지의 `Create-Diagnostics.cmd` 또는 F8 → 일반 설정 → 진단 ZIP 만들기로 만들며, 바탕 화면의 `BetterAstralParty-Diagnostics` 폴더에 저장됩니다.

[문제 제보](https://github.com/TSM701/BetterAstralParty/issues) 때는 모드 버전, 재현 순서, 스크린샷과 로그 또는 진단 ZIP을 함께 전달하세요. 공유 전 개인정보와 사용자 지정 경로를 확인하세요.

<details>
<summary>진단 ZIP에 포함되는 내용</summary>

- 최근 설치·초기 실행·호환성 오류의 상세 본문
- BepInEx `LogOutput.log`와 Unity `Player.log`에서 오류 주변만 걸러 가린 줄
- 게임의 Windows 오류 보고·응용 프로그램 오류 기록, Unity 충돌 폴더의 `error.log`
- 시작 단계 기록과 안전 모드 상태, 설치된 다른 BepInEx 플러그인의 이름·버전 목록
- Steam 관련 설치·제거 오류 당시의 프로세스 확인 결과와 단계
- 게임·모드 파일이 일부 없을 때 고정 모드 파일과 복구·백업 표식의 존재·크기·해시를 담은 `recovery-inventory.json`

충돌 덤프는 크기만 기록합니다. 토큰·개인 경로를 가리고 아무것도 업로드하지 않으며, 용량 제한이나 누락 사유는 ZIP의 `summary.json`에 기록합니다.

</details>

### 소스와 라이선스

BetterAstralParty 자체 코드는 [MIT 라이선스](LICENSE)로 제공합니다. 외부 구성 요소의 고지와 대응 소스 안내는 `THIRD-PARTY-NOTICES/`, 소스 빌드 방법은 공개 소스의 `docs/BUILD.md`에 있습니다.

---

## English

<div align="center">

[![Download](https://img.shields.io/badge/Download-ff4fa3?style=for-the-badge&logo=github&logoColor=white)](https://github.com/TSM701/BetterAstralParty/releases)
[![Install guide](https://img.shields.io/badge/Install_guide-f7a440?style=for-the-badge)](#install-and-play)
[![Bug report & feature request](https://img.shields.io/badge/Bug_report_%26_feature_request-6c8cff?style=for-the-badge)](https://github.com/TSM701/BetterAstralParty/issues/new/choose)

</div>

### Features

| Combat | Field | Convenience |
| --- | --- | --- |
| Combat recommendations and card evaluation from public information | Player/monster field indicators | In-game settings with Korean/English UI |
| Minimum KO/survival conditions | Public buff and counter markers | Mute when unfocused, input alerts |
| Combat probabilities and final damage | Field wheel zoom and free view | Focus the game when a PvE match is found |
| Shushu shield display and defense restriction | Hand grouping by function | Automatic Thanks for native recommendations |
| Card/chip ping detail popups | Character roles and preparation details | Automatic updates applied in game |

> [!NOTE]
> When a PvP room or match is detected, mod conveniences pause and resume with your saved settings after leaving PvP. Updates and diagnostics continue during PvP.

### Requirements

- Windows x64, .NET Framework 4.8, Steam and an installed INT(Global) client. The CN client is not supported.
- Install a Korean patch separately if needed. An existing patch is preserved.
- Differences from other mods or an existing shared loader do not block installation, launch, updates or removal; existing files and settings are preserved.

### Install and play

> [!IMPORTANT]
> If you use 1.3.x, close the game and run `Install.cmd` from the 1.4.0 package. It takes over the existing installation, removes the old launcher and update helper files, converts the Astral Party - Mod entry to the new launch method and keeps your settings. 1.3.x does not update to 1.4.0 automatically; the game reports that a manual download is required.

> [!WARNING]
> If you use 1.2.x or earlier, uninstall with your previous package's `Uninstall.cmd` first, then install fresh. If removal fails, try `Force-Uninstall.cmd` from the new package. If that still does not help, back up any data you want to keep, perform a clean installation of the game and try again.

1. Download `BetterAstralParty-<version>.zip` from [Download](https://github.com/TSM701/BetterAstralParty/releases). Source ZIPs are not installation packages.
2. Launch Astral Party INT(Global) once through the Steam client, let game and data updates finish, then exit normally.
3. Extract the entire ZIP to a separate writable folder. Do not overwrite the game folder directly or run inside the ZIP.
4. While signed in to Steam, run `Install.cmd` from the extracted folder. If prompted, select the `Astral Party` folder or its `8vJXnINT` child, as shown by Steam's Installed Files → Browse.
5. Launch Astral Party - Mod from your Steam library. A console window may flash briefly. First-time loader initialization and file downloads may take time and require internet access.

> [!TIP]
> To play without the mod, launch through the Steam client or run `AstralParty-Vanilla.cmd` in the game folder. The two launch methods only select BetterAstralParty; other BepInEx mods stay available in both. Close the game before switching and leave Astral Party's Steam launch options unchanged.

<details>
<summary>Installed files and more</summary>

- The installer is `artifacts/BetterAstralParty-Setup.exe` inside the package and is not copied into the game folder. The game folder only receives `BetterAstralParty-Mod.cmd`, `AstralParty-Vanilla.cmd`, `BetterAstralParty.compatibility.json`, `BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll` and installation records.
- If Steam or its account cannot be discovered, files are installed without changing shortcuts. Launch the game through the Steam client, then rerun `Install.cmd` to add the entry.
- When the game or Steam must be closed, the installer tells you. Close it yourself, then recheck or cancel; the installer never force-closes processes.
- A newly installed shared loader defaults to ON.

</details>

### Settings

Open Mod Settings in the lobby using its button or F8, then select Mod Features or General. Changes are saved automatically, and updates keep your saved values. Defaults below apply to a first installation.

<details>
<summary>Mod Features tab</summary>

Battle Advice
- Battle Advice: ON by default. Recommends Attack/Let Go, Defend/Dodge and cards from public information without selecting actions. It is not a master switch for the whole mod.
- Hover Details: ON by default. Hover over combat advice to see damage and KO probabilities. OFF hides the numbers but keeps recommendation icons.
- KO Conditions: ON by default. Shows KO conditions when attacking or watching another player's combat and survival conditions when you defend, using best-case outcomes. Includes card-hover conditions.
- Final Damage: ON by default. Shows damage calculated from final public numbers.

Battle Display
- Battle Info: ON by default. Shows public hand counts/effects and player/monster counter status below combat HP.
- Shushu Shield: ON by default. Shows active shield effects on combat characters.
- Shushu Defense Restrictions: ON by default. Locks Defend and defense cards for the shielded local defender. Does not change Dodge availability or choose it automatically.
- Counter Indicator Animation / Sound: both ON by default. Animation OFF keeps a static counter icon.

Field / Camera
- Player Field Indicator: ON by default. Shows field player names, HP, public effects and added counter icons. A short click on an effect opens its native description.
- Field Wheel Zoom: ON by default. Enables PvE field wheel zoom and free view that persist across turns. Select Exit Free Camera to the right of the turn-bar pin to restore the default view.
- Zoomed-out Indicator Fading / Distant Indicator Hiding: both ON by default. Apply to field indicators while Field Wheel Zoom is in use.

Cards
- Grouped Hand: Native, Click or Hover; the default is Native. Groups your PvE field hand by function; Click expands horizontally and Hover upward. Drag cards to the submission area above the cards with Click, or to the highlighted area on the right with Hover.
- Card / Chip Pings: ON by default. Click a native card/chip ping bubble to open a popup with its original image and description.
- Target Confirm/Cancel buttons always appear above cards.

Characters
- Character Names: Name or Title; the default is Name.
- Character Roles / Character Preparation Details: both ON by default. Preparation portrait hover shows public skills, cooldowns and potentials.

Convenience
- Automatic Thanks: ON by default. Sends each available native Thanks recommendation once. Never sends in PvP, spectating or replays.
- Reset Ping Mode After Task Switching: ON by default. Cancels lingering ping selection after Alt+Tab without sending a ping.

</details>

<details>
<summary>General tab</summary>

Display / Language
- Language: Auto, 한국어 or English; the default is Auto, which follows the game menu language.
- UI Scale: 100% by default, 75–150%. Scales only mod advice and battle-status panels.
- Opacity: 100% by default, 0–100%. Changes only the background of mod battle panels.

Alerts / Audio
- Mute When Unfocused: ON by default. Mutes game audio on focus loss and restores it on return.
- Input Alert: Taskbar, Windows or OFF; the default is Taskbar.
- Focus on Match Found: ON by default. Brings the game forward once when a PvE match is found or the host starts the game.
- Suppress Startup Notices: ON by default. Only stops login notices from opening automatically.

Updates
- Update Channel: Release, Pre-release or Private Beta; the default is Release. Only Private Beta requires a token.
- Automatic Update Checks / Update Notification Popups: both ON by default.
- Auto Download / Apply Automatically: both ON by default. Apply Automatically was previously named Apply After Exit.
- Check Updates, Get Update, Cancel Update and Download Page are action buttons.

Diagnostics
- Diagnostic Log: OFF by default. Records detailed errors, performance and public combat calculations.
- Open Diagnostics Folder and Create Diagnostics ZIP are action buttons.

</details>

### Updates

- Automatic updates are supported from the 1.4.0 installation. With Auto Download and Apply Automatically ON, a verified update is applied while the game runs and used from the next launch. You do not need to close the game, and no separate window opens.
- For manual updates, close the game and run `Install.cmd` from a newer complete package. Settings are kept.
- Safe mode: if two starts in a row do not reach the home screen, the next start turns mod features off and keeps only updates working. Reaching the home screen clears it.

<details>
<summary>Channel switching and details</summary>

- Release and Pre-release are checked in the public repository without login, and public channels select only 1.4.0 or newer. If no supported release exists, the current installation stays unchanged.
- Channel switching checks the latest supported release in the selected channel. It can be lower than the installed version; review the target with the `Switch Channel` button below Update Channel and approve it. If only the same version exists, the status shows `Channel Switch Waiting`. Changing the update channel again cancels the approval. Ordinary updates within the same channel reject downgrades.
- Reclassifying the same public version between Pre-release and Release leaves installed files and your saved channel unchanged.
- Manual updates block downgrades and replacing a version with different files at the same version.
- A Mod launch turns an OFF shared loader ON without a prompt and keeps the original configuration at `BepInEx/config/BetterAstralParty.loader-before-*.bak`.
- Automatic updates do not replace the installation/removal tools in previously downloaded packages; use the latest complete package.
- Automatic-update downloads and temporary working files are kept in `BetterAstralParty.Update/` inside the game folder.
- Changed game hashes or catalogs after a hotfix alone do not block installation or launch. Only an incompatible optional feature stops.
- After three safe-mode runs, the full mod is retried once. A new BetterAstralParty version or a changed game build resets the count.
- If the game itself cannot start with the mod, launch through the Steam client or create a diagnostics ZIP with `Create-Diagnostics.cmd`, and install the latest complete package.

</details>

### Uninstall

Run `Uninstall.cmd` from the latest complete package and review the removal list. Mod files and settings are backed up under `%LOCALAPPDATA%/BetterAstralParty/Backups/v5` before removal. Shared BepInEx files and loader settings, other mods and Korean patches are preserved. Installations of 1.2.x or earlier must use their previous package's uninstaller.

> [!CAUTION]
> If normal removal fails, run `Force-Uninstall.cmd` and review the deletion list. After confirmation, it permanently deletes the listed BetterAstralParty files, settings and recovery backups for that installation. Shared loader files and other mods are preserved.

<details>
<summary>More about removal</summary>

- If Steam or its account cannot be discovered, shortcut cleanup is skipped. Remove any remaining Astral Party - Mod entry manually in Steam.
- After complete removal, run `Install.cmd` from a complete package to reinstall. If recovery is required, keep logs and backups and follow the displayed procedure.

</details>

### Troubleshooting and reports

- Missing Steam entry: restart Steam and check library filters and the signed-in account. Use `Install.cmd` to repair the entry.
- Installation or launch errors: open logs with `Open-Logs.cmd`. For calculation issues, turn on Diagnostic Log in mod settings and reproduce the issue.
- Create a diagnostics ZIP with the package's `Create-Diagnostics.cmd` or F8 → General → Create Diagnostics ZIP. It is saved to the `BetterAstralParty-Diagnostics` folder on the Desktop.

When you [report an issue](https://github.com/TSM701/BetterAstralParty/issues), include the mod version, reproduction steps, screenshots and logs or the diagnostics ZIP. Review personal information and custom paths before sharing.

<details>
<summary>What the diagnostics ZIP contains</summary>

- Detailed text of recent installation, bootstrap and compatibility errors
- Filtered and redacted BepInEx `LogOutput.log` and Unity `Player.log` lines around errors
- Windows Error Reporting and Application Error records for the game, and Unity crash-folder `error.log` text
- The startup stage record and safe-mode state, and the names and versions of other installed BepInEx plugins
- Process observations and stage for Steam-related installation and removal errors
- `recovery-inventory.json` with the presence, size and hashes of fixed mod files and recovery/backup markers when files are missing

Crash dumps are not included, only their size. Tokens and private paths are redacted and nothing is uploaded. Size limits and missing evidence are listed in the ZIP's `summary.json`.

</details>

### Source and licenses

BetterAstralParty-owned code is provided under the [MIT license](LICENSE). See `THIRD-PARTY-NOTICES/` for third-party notices and corresponding-source information, and `docs/BUILD.md` in the public source for build instructions.
