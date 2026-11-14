<div align="center">

<img src="https://raw.githubusercontent.com/TSM701/BetterAstralParty/main/.github/assets/banner.png" alt="BetterAstralParty" width="100%">

# BetterAstralParty

Astral Party INT(Global)의 PvE 정보 표시와 판단을 돕는 편의성 모드<br>
A quality-of-life mod for Astral Party INT(Global) PvE

[![License](https://img.shields.io/badge/license-MIT-8bd3a8)](LICENSE)
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
| KO·생존 주사위 조건 표시 | 공개 버프·반격 표시 | 창 비활성화 시 음소거, 입력 대기 알림 |
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
> 1.3.x 사용자는 게임을 종료하고 1.4.0 패키지의 `Install.cmd`를 실행하세요. 기존 설치를 이어받아 이전 실행기·업데이트 도우미 파일을 정리하고 Steam 외부 게임을 새 실행 방식으로 바꾸며, 설정은 유지합니다. 1.3.x는 1.4.0으로 자동 업데이트되지 않고 게임 안에 `수동 다운로드 필요`로 표시됩니다. 1.3.x 설치를 바로 이어받는 기능은 2026년 12월 15일 이후 첫 공개 버전부터 지원하지 않으며, 그 뒤에는 1.3.x 패키지의 `Uninstall.cmd`로 먼저 제거한 다음 설치해야 합니다.

> [!WARNING]
> 1.2.x 이하 사용자는 사용하던 패키지의 `Uninstall.cmd`로 먼저 제거한 뒤 새로 설치하세요. 제거에 실패하면 새 패키지의 `Force-Uninstall.cmd`를 시도하고, 그래도 해결되지 않으면 보관할 데이터를 백업한 뒤 게임을 클린 설치하고 다시 시도해주세요.

1. [다운로드](https://github.com/TSM701/BetterAstralParty/releases)에서 `BetterAstralParty-<버전>.zip`을 받으세요. 소스 ZIP은 설치용이 아닙니다.
2. Steam에서 Astral Party INT(Global)를 한 번 실행해 업데이트와 데이터 다운로드를 마친 뒤 정상 종료하세요.
3. 쓰기 가능한 별도 폴더에 ZIP 전체를 압축 해제하세요. 게임 폴더에 직접 덮어쓰거나 ZIP 안에서 실행하지 마세요.
4. 압축을 푼 폴더의 `Install.cmd`를 실행하면 설치 창이 열립니다. 게임 폴더를 확인한 뒤 설치를 누르세요. 게임 폴더를 찾지 못하면 Steam의 설치된 파일 → 찾아보기로 연 `Astral Party` 폴더를 고르세요.
5. Steam에서 Astral Party를 실행하세요. 첫 실행은 로더 초기화와 파일 다운로드로 시간이 걸리고 인터넷 연결이 필요할 수 있습니다.

> [!TIP]
> 모드 없이 실행하려면 게임 폴더의 `AstralParty-Vanilla.cmd`를 실행하세요. 그 실행에서만 BetterAstralParty가 꺼지고 다른 BepInEx 모드는 그대로 유지합니다. 전환 전 게임을 종료하고 Steam의 실행 옵션은 바꾸지 마세요.

<details>
<summary>설치되는 파일과 추가 안내</summary>

- 설치 도구는 패키지의 `artifacts/BetterAstralParty-Setup.exe`이며 게임 폴더에 복사되지 않습니다. 게임 폴더에는 `BetterAstralParty-Mod.cmd`, `AstralParty-Vanilla.cmd`, `BetterAstralParty.compatibility.json`, `BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll`과 설치 기록만 남습니다.
- 설치는 Steam 외부 게임을 추가하지 않으므로 Steam을 종료하지 않아도 됩니다.
- 이전 버전이 추가한 Steam 외부 게임 Astral Party - BetterAstralParty 또는 Astral Party - Mod는 계속 쓸 수 있습니다. 필요 없으면 Steam에서 직접 지워도 되고, `Uninstall.cmd`로 제거할 때도 함께 정리됩니다.
- 게임을 켤 때 뜨는 BepInEx 로그 창은 설치 창의 확인란으로 끌 수 있습니다. BepInEx를 새로 설치할 때는 기본으로 꺼 두고, 이미 있던 설정은 고를 때만 바꿉니다.
- 게임이나 Steam을 종료해야 하면 설치 창이 안내합니다. 직접 종료한 뒤 다시 확인을 누르거나 취소할 수 있으며, 설치 도구가 프로세스를 강제로 종료하지는 않습니다.
- `Install.cmd`나 `Uninstall.cmd`에 `-CheckOnly`, `-WhatIf` 같은 인자를 주면 창 대신 콘솔에서 진행합니다.
- 새 공용 로더는 ON으로 설치됩니다.
- 이미 있던 BepInEx가 일부 지워져 게임이 모드 없이 시작되는 상태면, 빠진 BepInEx 파일을 채우고 남은 설정은 유지합니다. 남은 파일이 다른 버전이면 BepInEx는 그대로 두고 BetterAstralParty만 설치한 뒤, 지우고 다시 설치할 파일을 안내합니다.

</details>

### 설정

로비의 모드 설정 버튼이나 F8, 또는 전투 중 ESC 메뉴의 모드 설정 버튼으로 열고 모드 기능·일반 설정 탭에서 조절하세요. 변경 사항은 자동 저장되며, 업데이트해도 기존 저장값은 유지합니다. 로비의 모드 설정 버튼은 게임의 배경 조정 모드(홈 화면 오른쪽 아래의 이동 버튼)에서 끌어 다른 자리로 옮길 수 있습니다. 확인을 누르면 그 자리가 저장되고, 모드 설정 위치 초기화를 누르면 처음 자리로 돌아갑니다. 목록 위 검색창에 단어를 입력하면 두 탭에서 이름·위치·설명에 그 단어가 들어간 설정만 모아 보여 줍니다. 목록 오른쪽 스크롤바에 마우스를 올리면 모든 카테고리 이름이 스크롤바 옆에 뜨고, 점이나 이름을 누르면 그 카테고리로 이동합니다. 각 줄 오른쪽 스위치로 켜고 끄며, 맨 위 줄은 그 기능 전체를 켜고 끄고, 아래 트레이의 줄로 세부 표시를 따로 켜고 끕니다. 아래는 처음 설치했을 때의 기본값입니다.

<details>
<summary>모드 기능 탭</summary>

전투 추천
- 행동 추천: 기본 ON. 공개 정보를 바탕으로 공격·놓아주기, 방어·회피, 카드 사용을 추천합니다. 방어·회피는 방어를 추천하고, 방어해도 반드시 쓰러질 때는 회피를 추천합니다. 선택을 자동 입력하지 않으며, 모드 전체를 끄는 스위치가 아닙니다. 슈팅 스타 칩이 있으면 놓아주기 버튼에 놓아줄 때 몬스터에게 주는 피해와 얻는 스타라이트를 표시합니다.
- 추천 상세 수치: 기본 ON. 행동 추천에 마우스를 올리면 피해와 전투불능 확률을 표시합니다. OFF는 수치만 숨기고 추천 아이콘은 유지합니다.
- KO 주사위 조건: 기본 ON. 공격하거나 다른 사람 전투에서 몬스터가 방어할 때는 KO 주사위 조건, 내가 방어하거나 다른 사람 전투에서 플레이어가 방어할 때는 생존 주사위 조건을 표시합니다. 조건은 카드 보너스가 가장 낮게 나와도 결과가 확정되는 주사위이고, 그런 주사위 조합을 가장 많이 담는 조건을 보여 줍니다. 관전 중이거나, 내가 공격하고 카드 선택이 끝나면 카드 보너스는 실제로 나온 값으로 계산합니다. 양쪽 선택이 끝나면 서버가 정한 주사위로 확정 KO 또는 KO 불가를 바로 표시합니다. 카드 호버 조건도 포함합니다. 계산에 반영되지 않은 효과가 전투에 있으면 아래에 그 효과 아이콘과 "미반영 효과"를 표시하고, 물음표에 마우스를 올리면 설명이 뜹니다.
- 최종 피해: 기본 ON. 양쪽 선택이 끝나 주사위 연출이 시작되면 서버가 정한 피해량을 표시합니다.

전투 표시
- 전투 상태: 기본 ON. 전투 HP 아래 공개 손패 장수·효과와 플레이어·몬스터의 반격 상태를 표시합니다. 계산에 반영되지 않은 효과 아이콘에는 물음표 표시가 붙고, 설명에 "계산 미반영"이 표시됩니다.
- 슈슈 쉴드 효과: 기본 ON. 전투 캐릭터의 활성 쉴드 효과를 표시합니다.
- 슈슈 쉴드 방어 잠금: 기본 ON. 쉴드가 있는 내가 방어자일 때 방어 버튼·방어 카드를 잠급니다. 회피 가능 여부는 바꾸지 않고 회피를 자동 입력하지 않습니다.
- 반격 아이콘 애니메이션·소리: 각각 기본 ON. 애니메이션 OFF는 정적인 반격 아이콘을 표시합니다.

필드·카메라
- 플레이어 인디케이터: 기본 ON. 필드에 플레이어 이름·HP·공개 효과와 추가 반격 아이콘을 표시합니다. 효과 아이콘을 짧게 클릭하면 게임 기본 설명을 엽니다. 휠 줌으로 확대·축소해도 캐릭터 옆 위치를 유지합니다.
- 인디케이터 높이: 기본 100%. 플레이어 인디케이터의 점을 캐릭터 발밑(0%)부터 원래 위치(100%) 사이 높이에 표시합니다. 슬라이더로 조절합니다.
- 이동 방향 강조: 기본 화살표만. 플레이어와 몬스터 발밑의 이동 방향 표시를 캐릭터·몬스터·나무에 가려지지 않게 위에 다시 그립니다. 화살표만은 각 원이 가리키는 자리에 화살표를 바닥에 눕혀 그립니다. 원과 화살표는 원까지 그리며 캐릭터 뒤쪽 절반은 점선으로 표시합니다. 끄기는 게임 기본 표시만 남깁니다.
- 휠 줌·자유 시점: 기본 ON. PvE 필드에서 휠 줌과 자유 시점을 사용하며 턴이 바뀌어도 유지합니다. 턴 바의 핀 오른쪽 자유 시점 카메라 종료 버튼으로 기본 시점에 복귀합니다. 플레이어 칸의 얼굴을 누르면 그 플레이어로 시점이 옮겨지고, 이름·칩 부분을 누르면 줌을 유지한 채 상세정보만 열립니다. 줌 중에도 몬스터 인디케이터는 몬스터 옆 같은 자리에 표시됩니다. 주사위 화면 고정(기본 ON)을 켜 두면 굴린 주사위가 줌과 자유 시점에서도 기본 시점과 같은 화면 위치·크기로 보이고, 끄면 게임 기본처럼 필드 위에 남아 줌에 따라 크기가 바뀝니다.
- 줌 아웃 시 불투명도·멀면 숨기기: 각각 기본 ON. 휠 줌을 사용할 때 인디케이터에 적용합니다.
- 줌 아웃 시 불투명도 슬라이더: 기본 30%, 0~100%. 가장 멀리 줌 아웃했을 때 인디케이터의 불투명도를 슬라이더로 조절합니다. 100%는 원래대로, 0%는 보이지 않습니다.
- 줌 감도: 기본 100%, 10~300%. 휠 한 칸에 확대·축소되는 양입니다.
- 최대 줌 아웃: 기본 100%, 0~100%. 휠로 멀어질 수 있는 최대 거리이며, 100%는 필드 전체가 보이는 거리이고 0%는 기본 시점보다 멀어지지 않습니다.
- WASD 속도: 기본 100%, 10~300%. WASD로 자유 시점을 움직이는 속도입니다.
- 숨김 거리: 기본 25%, 0~100%. 멀면 숨기기에서 인디케이터가 숨고 다시 나타나는 줌 아웃 거리입니다.

손패·카드
- 손패 묶기: 기본·클릭 펼치기·호버 펼치기 중 선택하며 처음에는 기본입니다. 내 PvE 필드 손패를 기능별로 묶고 클릭은 가로로, 호버는 위로 펼칩니다. 두 방식 모두 카드 위쪽의 제출 영역으로 드래그하며, 호버에서는 이 영역이 강조됩니다.
- 접힌 카드 불투명도: 기본 75%, 0~100%. 손패 묶기를 켰을 때 접혀 있는 카드의 불투명도입니다.
- 핑 상세 보기: 기본 ON. 게임의 카드·칩 핑 말풍선을 클릭하면 원본 이미지와 설명을 추가 팝업으로 엽니다.
- 카드 대상 선택의 확인·취소 버튼은 항상 카드보다 앞에 표시됩니다.

캐릭터
- 이름 표기: 본명·이명 중 선택하며 기본은 본명입니다.
- 역할 표시·준비 화면 스킬 설명: 각각 기본 ON. 준비 초상화 호버에 공개 스킬·재사용 대기시간·잠재력을 표시합니다.
- 플레이어 목록 탭: 기본 ON. 필드 왼쪽 플레이어 목록의 코인·별 바 아래 탭에 공개 손패 장수, 역할, 스킬 쿨타임을 표시합니다. 스킬 쿨타임은 게임 정보 창처럼 스톱워치와 남은 턴/전체 턴으로 보여주며, 0이면 액티브 스킬을 바로 쓸 수 있습니다. 다른 플레이어의 쿨타임은 정보 창처럼 게임 서버에서 받아 옵니다. 손패 장수, 역할, 스킬 쿨타임은 각각 끌 수 있습니다.

편의 기능
- 감사합니다 자동 전송: 기본 ON. 게임 기본 감사합니다 추천이 전송 가능할 때 추천 한 건당 한 번 전송합니다. PvP·관전·재생에서는 전송하지 않습니다.
- 좋아요 일괄 전송: 기본 ON. PvE 게임 결과 화면의 확인 버튼 위에 좋아요 버튼을 둡니다. 누르면 아직 좋아요를 보내지 않은 다른 플레이어 모두에게 게임의 좋아요 버튼을 차례로 눌러 보냅니다. 자동으로는 보내지 않고, 보낼 사람이 없으면 회색으로 바뀝니다. 버튼이 보이는 동안 채팅 말풍선은 버튼 왼쪽으로 비켜 납니다. PvP·관전·재생에서는 나오지 않습니다.
- 상점 일괄 구매: 기본 ON. 스타 코인 상점 카드 아래 오른쪽에 무료 일괄 수령과 일괄 구매 버튼을 둡니다. 무료 일괄 수령은 누르면 무료 상품을 바로 받고, 받을 것이 없으면 회색으로 바뀝니다. 일괄 구매는 살 수 있는 상품 목록을 열고, 무료·배지·50% 이상 할인·전부 버튼으로 해당 상품을 한꺼번에 넣고 빼거나 상품마다 체크한 뒤 합계와 남을 스타 코인을 보고 구매를 누르면 그 상품만 삽니다. 구매는 목록 창 안에서 하나씩 진행되며 중간에 중지할 수 있고, 끝나면 목록으로 돌아갑니다. 구매가 실패로 멈췄을 때만 그 결과를 잠깐 보여 줍니다. 여러 개를 사도 획득 창은 한 번에 모아서 뜹니다.
- Alt+Tab 핑 모드 해제: 기본 ON. Alt+Tab 후 남은 핑 선택 모드를 전송 없이 해제합니다.
- 홈 화면에서 Esc를 누르면 게임의 종료 확인 창이 열립니다.

실험 기능 (BETA)
- 구버전 리플레이 재생: 기본 OFF. 전적에서 게임 버전이 달라 재생할 수 없던 리플레이의 재생 버튼을 켭니다. 리플레이는 지금 게임으로 재생되므로 규칙이 바뀐 부분에서 도중에 멈추거나 화면이 틀릴 수 있습니다.

</details>

<details>
<summary>일반 설정 탭</summary>

화면·언어
- 언어: 자동·한국어·English 중 선택하며 기본은 자동입니다. 자동은 게임 메뉴 언어를 따릅니다.
- 표시 크기: 기본 100%, 75~150%, 슬라이더로 1% 단위 조절. 모드 추천·전투 상태 패널만 조절합니다.
- 배경 불투명도: 기본 100%, 0~100%, 슬라이더로 1% 단위 조절. 모드 전투 패널의 바탕만 바꿉니다.

알림·소리
- 백그라운드 음소거: 기본 ON. 다른 창으로 전환하면 게임 소리를 끄고 복귀하면 복원합니다.
- 입력 대기 알림: 작업표시줄·Windows 알림·OFF 중 선택하며 기본은 작업표시줄입니다.
- 매칭 시 게임 창 띄우기: 기본 ON. PvE 매칭 성사나 방장의 게임 시작 때 창을 한 번 앞으로 올립니다.
- 시작 공지 끄기: 기본 ON. 로그인 공지가 자동으로 열리는 것만 막습니다.

진단
- 진단 로그: 기본 OFF. 오류·성능·공개 전투 계산의 상세 진단을 기록하고, 최근 다섯 개의 로그를 보관합니다.

설정 보관
- 자동 보관: 기본 ON. 설정을 바꿀 때마다 `%LOCALAPPDATA%\BetterAstralParty\Settings`에 보관합니다. 제거·재설치나 게임 폴더를 새로 받아 설정 파일이 없을 때만 다음 실행에서 되살리고, 이미 있는 설정은 덮어쓰지 않습니다.
- 지금 보관·보관본 불러오기: 원하는 시점의 설정을 따로 남기고, 두 번 눌러 그 설정으로 되돌립니다. 자동 보관은 이 보관본을 덮어쓰지 않습니다.
- 보관 폴더 열기: 보관 파일을 다른 PC로 옮길 때 씁니다.
- 모든 설정 초기화: 두 번 누르면 모든 설정을 처음 설치한 상태로 되돌립니다. 되돌리기 전에 지금 보관으로 남겨 두면 보관본 불러오기로 복구할 수 있습니다.

업데이트
- 업데이트: 지금 할 수 있는 작업 하나를 보여주는 버튼입니다. 상황에 따라 업데이트 확인, 받기, 적용, 취소, 채널 전환 승인으로 바뀝니다. 업데이트 확인일 때는 이름 아래에 결과와 마지막 확인 시각이 보이고, 오른쪽 다시 확인 버튼으로 새 버전을 다시 확인합니다.
- 자동 업데이트: 끄기·확인만·자동 받기·자동 적용 중 선택하며 기본은 자동 적용입니다. 자동 받기는 받아 두기만 하고, 업데이트 버튼을 눌러 적용합니다. 이전 버전에서 따로 저장한 조합은 사용자 지정으로 보이고 그대로 유지됩니다.
- 업데이트 알림 팝업: 기본 ON. 새 버전이 나오면 홈 화면 위쪽에 잠깐 알림을 띄우고, 누르면 변경 내역이 열립니다. OFF는 알림 없이 모드 설정 버튼에만 표시합니다. 새 버전이 있으면 모드 설정 버튼에 빨간 점이 붙고, 그 뒤 처음 열면 업데이트 항목으로 이동합니다.
- 다운로드 페이지: 선택한 채널의 GitHub 릴리스 목록을 엽니다.
- 업데이트 채널: 릴리즈·프리 릴리즈·비공개 베타 중 선택하며 기본은 릴리즈입니다. 프리 릴리즈는 더 새로운 릴리즈 버전도 함께 받습니다. 비공개 베타를 고르면 그 아래에 토큰 인증·로그아웃이 나타납니다.
- 진단 폴더 열기·진단 ZIP 만들기는 실행 버튼입니다.

</details>

### 업데이트

- 자동 업데이트는 1.4.0 설치부터 지원합니다. 자동 업데이트를 자동 적용으로 두면 검증된 업데이트를 게임 실행 중에 적용하고 다음 실행부터 새 버전을 사용합니다. 게임을 종료할 필요가 없고 별도 창도 열리지 않습니다.
- 수동 업데이트는 게임을 종료한 뒤 상위 버전 전체 패키지의 `Install.cmd`를 실행하세요. 설정은 유지합니다.
- 안전 모드: 지난번 실행이 홈 화면까지 가지 못했으면 게임 화면이 나오기 전에 안전 모드로 실행할지 묻습니다. `예`를 누르면 모드 기능을 끄고 업데이트만 동작하고, `아니요`를 누르면 평소처럼 다시 시도합니다. 안전 모드에서 새 버전을 적용하면 확인 창으로 알려 주고, 다음 실행부터 원래대로 동작합니다.

<details>
<summary>채널 전환과 세부 동작</summary>

- 릴리즈와 프리 릴리즈는 공개 저장소에서 로그인 없이 확인하며, 공개 채널에서는 1.4.0 이상만 자동 업데이트 대상으로 선택합니다. 지원 버전이 없으면 현재 설치를 유지합니다.
- 채널 전환은 선택한 채널의 지원되는 최신 릴리스를 확인합니다. 설치된 버전보다 낮아도 전환할 수 있습니다. 업데이트 버튼을 누르면 대상 버전을 보여주고, `전환 시작`을 눌러야 받기 시작합니다. 받기와 적용은 게임 안에서 진행되니 끝날 때까지 게임을 끄지 마세요. 끝나면 `종료 및 전환` 버튼이 켜지고, 누르면 게임이 종료되며 다음 실행부터 새 버전을 사용합니다. 같은 버전만 있으면 `채널 전환 대기`로 표시합니다. 업데이트 채널을 다시 바꾸면 승인이 취소됩니다. 같은 채널의 일반 업데이트는 버전 역행을 허용하지 않습니다.
- 업데이트나 채널 전환을 받고 적용하는 동안 홈 화면의 모드 설정 버튼 아래에 진행 캡슐이 나타납니다(버튼을 화면 아래쪽으로 옮겼으면 버튼 위에 나타납니다). 받은 용량에 따라 채워지는 막대와 오른쪽 둥근 버튼의 상태 아이콘(받는 중, 완료, 오류)을 보여주고, 누르면 진행 창이 다시 열립니다. 진행 창에는 같은 막대와 퍼센트, 받은 용량이 표시됩니다.
- 업데이트 중 오류가 나면 캡슐에 "오류"와 함께 이유와 오류 코드(예: `BAP-UPD-DOWNLOAD-TIMEOUT`)가 표시되고, 진행 창에서 할 일 안내와 `다시 시도` 버튼을 볼 수 있습니다. 문의할 때는 오류 코드와 진단 ZIP을 함께 보내 주세요.
- 받는 도중 게임을 끄거나 인터넷 연결 문제로 받지 못해도 자동 업데이트는 꺼지지 않고 다음 실행 때 다시 받습니다. 파일을 바꾸는 적용 도중에 끊긴 경우에만 자동 업데이트가 꺼지고, 업데이트 설정에서 `다시 사용`을 눌러야 합니다.
- 같은 공개 버전이 프리 릴리즈↔릴리즈로 바뀌어도 설치된 파일과 저장한 채널은 그대로 둡니다.
- 수동 업데이트는 버전 역행과 같은 버전의 다른 파일 덮어쓰기를 차단합니다.
- 공용 로더가 OFF면 Steam에서 실행해도 모드가 켜지지 않습니다. 게임 폴더의 `BetterAstralParty-Mod.cmd`로 실행하면 그 실행에서만 로더를 켜고, Steam이 실행 옵션 확인 창을 띄웁니다. 로더 설정 파일은 바꾸지 않습니다.
- 이전에 받은 패키지의 설치·제거 도구는 자동 업데이트로 바뀌지 않으므로 최신 전체 패키지를 사용하세요.
- 자동 업데이트 파일과 임시 작업 파일은 게임 폴더의 `BetterAstralParty.Update/`에 모입니다. 제거 후 다시 설치할 때 남기는 이전 업데이트 기록은 그 안의 `history/`에 보관합니다.
- 게임 핫픽스로 파일 해시나 카탈로그가 달라진 것만으로 설치·실행을 막지 않습니다. 실제로 맞지 않는 선택 기능만 중단합니다.
- 안전 모드로 실행한 다음 실행에서도 다시 묻습니다. 새 BetterAstralParty 버전이나 바뀐 게임 빌드에서는 묻지 않고 평소처럼 시작합니다. 안전 모드에서 새 버전을 자동으로 적용할 수 없으면(자동 적용이 꺼져 있거나 자동 업데이트가 꺼진 상태) 확인 창이 설치 ZIP으로 설치하라고 안내합니다.
- 모드를 켠 상태로 게임 자체가 시작되지 않으면 `AstralParty-Vanilla.cmd`로 실행하거나 `Create-Diagnostics.cmd`로 진단 ZIP을 만들고 최신 전체 패키지를 설치하세요.

</details>

### 제거

최신 전체 패키지의 `Uninstall.cmd`를 실행하면 제거 창이 열립니다. 지울 파일 수를 확인한 뒤 제거를 누르세요. 모드 파일과 설정은 `%LOCALAPPDATA%/BetterAstralParty/Backups/v5`에 백업한 뒤 제거합니다. 공용 BepInEx 파일과 로더 설정, 다른 모드, 한국어 패치는 유지합니다. 1.2.x 이하 버전은 사용하던 패키지의 제거기를 사용하세요.

> [!CAUTION]
> 정상 제거가 실패하면 `Force-Uninstall.cmd`를 실행하세요. 강제 제거 창에서 지울 파일 수를 확인하고, 전체 목록은 자세히 보기에서 볼 수 있습니다. 되돌릴 수 없다는 확인란을 선택한 뒤 영구 삭제를 누르면 BetterAstralParty 파일·설정·해당 설치의 복구 백업을 백업 없이 삭제합니다. 공용 로더와 다른 모드는 보존합니다.

<details>
<summary>제거 추가 안내</summary>

- 이전 버전이 추가한 Steam 외부 게임이 있으면 지우기 위해 Steam을 종료해야 합니다. 제거가 끝나도 Steam은 다시 켜지지 않으니 필요하면 직접 실행하세요.
- Steam 경로나 계정을 찾지 못하면 Steam 외부 게임 정리는 건너뜁니다. 남은 Astral Party - BetterAstralParty 또는 Astral Party - Mod는 Steam에서 직접 제거할 수 있습니다.
- 완전 제거 후에는 전체 패키지의 `Install.cmd`로 재설치할 수 있습니다. 복구 안내가 나오면 로그와 백업을 보관하고 표시된 절차를 따르세요.

</details>

### 문제 해결과 제보

- `Install.cmd`가 버전 때문에 설치를 거부하면, 화면에 막은 버전과 할 일이 함께 나옵니다. 그 버전 이상을 받거나 `Uninstall.cmd`로 제거한 뒤 새로 설치하세요. 설정은 보관된 사본으로 다시 불러올 수 있습니다.
- 설치·실행 오류는 `Open-Logs.cmd`로 로그를 확인하세요. 계산 문제는 모드 설정에서 진단 로그를 켠 뒤 재현하세요.
- 진단 ZIP은 패키지의 `Create-Diagnostics.cmd` 또는 F8 → 일반 설정 → 진단 ZIP 만들기로 만들며, 바탕 화면의 `BetterAstralParty-Diagnostics` 폴더에 저장됩니다.

[문제 제보](https://github.com/TSM701/BetterAstralParty/issues) 때는 모드 버전, 재현 순서, 스크린샷과 로그 또는 진단 ZIP을 함께 전달하세요. 공유 전 개인정보와 사용자 지정 경로를 확인하세요.

<details>
<summary>진단 ZIP에 포함되는 내용</summary>

- 최근 설치·초기 실행·호환성 오류의 상세 본문
- BepInEx `LogOutput.log`와 Unity `Player.log`의 전체 본문(앞부분과 최근 부분, 개인정보와 카드·채팅 줄은 가림)
- 모드 실행 로그와 마지막 실행에서 모드가 켜지거나 꺼진 이유, 로더 켜짐 여부, 게임 빌드 정보
- 게임의 Windows 오류 보고·응용 프로그램 오류 기록, Unity 충돌 폴더의 `error.log`
- 시작 단계 기록과 안전 모드 상태, 설치된 다른 BepInEx 플러그인의 이름·버전 목록
- Steam 관련 설치·제거 오류 당시의 프로세스 확인 결과와 단계
- 게임·모드 파일이 일부 없을 때 고정 모드 파일과 복구·백업 표식의 존재·크기·해시를 담은 `recovery-inventory.json`

충돌 덤프는 크기만 기록합니다. 토큰·개인 경로·Steam ID·Windows 사용자 이름·Steam 계정 이름을 가리고 아무것도 업로드하지 않으며, 용량 제한이나 누락 사유는 ZIP의 `summary.json`에 기록합니다.

</details>

### 소스와 라이선스

BetterAstralParty 자체 코드는 [MIT 라이선스](LICENSE)로 제공합니다. 외부 구성 요소의 고지와 대응 소스 안내는 `THIRD-PARTY-NOTICES/`, 소스 빌드 방법은 공개 소스의 `docs/BUILD.md`에 있습니다.

포크와 수정 배포는 [MIT 라이선스](LICENSE)에 따라 자유롭게 할 수 있습니다. 공식 버전과 헷갈리지 않도록 비공식 버전임을 밝히고 다른 이름을 써 주세요. 공식 배포처는 이 저장소의 [다운로드](https://github.com/TSM701/BetterAstralParty/releases)뿐이고, 게임 안 자동 업데이트는 이 저장소에서 서명한 버전만 받습니다.

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
| KO/survival dice targets | Public buff and counter markers | Mute when unfocused, input alerts |
| Combat probabilities and final damage | Field wheel zoom and Free Camera | Focus the game when a PvE match is found |
| Shiyu-Shiyu Shield display and defense restriction | Hand grouping by function | Automatic Thanks for the game's suggestions |
| Card/chip ping detail popups | Character roles and preparation details | Automatic updates applied in game |

> [!NOTE]
> When a PvP room or match is detected, mod conveniences pause and resume with your saved settings after leaving PvP. Updates and diagnostics continue during PvP.

### Requirements

- Windows x64, .NET Framework 4.8, Steam and an installed INT(Global) client. The CN client is not supported.
- Install a Korean patch separately if needed. An existing patch is preserved.
- Differences from other mods or an existing shared loader do not block installation, launch, updates or removal; existing files and settings are preserved.

### Install and play

> [!IMPORTANT]
> If you use 1.3.x, close the game and run `Install.cmd` from the 1.4.0 package. It takes over the existing installation, removes the old launcher and update helper files, converts the Steam entry to the new launch method and keeps your settings. 1.3.x does not update to 1.4.0 automatically; the game reports that a manual download is required. Direct takeover of a 1.3.x installation ends with the first public version after December 15, 2026; after that, remove 1.3.x with its own `Uninstall.cmd` first, then install.

> [!WARNING]
> If you use 1.2.x or earlier, uninstall with your previous package's `Uninstall.cmd` first, then install fresh. If removal fails, try `Force-Uninstall.cmd` from the new package. If that still does not help, back up any data you want to keep, perform a clean installation of the game and try again.

1. Download `BetterAstralParty-<version>.zip` from [Download](https://github.com/TSM701/BetterAstralParty/releases). Source ZIPs are not installation packages.
2. Start Astral Party INT(Global) once from Steam, let game and data updates finish, then exit normally.
3. Extract the entire ZIP to a separate writable folder. Do not overwrite the game folder directly or run inside the ZIP.
4. Run `Install.cmd` from the extracted folder. An install window opens; check the game folder, then press Install. If it cannot find the game, choose the `Astral Party` folder Steam opens from Installed Files → Browse.
5. Start Astral Party from Steam. First-time loader initialization and file downloads may take time and require internet access.

> [!TIP]
> To play without the mod, run `AstralParty-Vanilla.cmd` in the game folder. Only that launch runs without BetterAstralParty; other BepInEx mods stay available. Close the game before switching and leave Astral Party's Steam launch options unchanged.

<details>
<summary>Installed files and more</summary>

- The installer is `artifacts/BetterAstralParty-Setup.exe` inside the package and is not copied into the game folder. The game folder only receives `BetterAstralParty-Mod.cmd`, `AstralParty-Vanilla.cmd`, `BetterAstralParty.compatibility.json`, `BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll` and installation records.
- Installation adds no Steam library entry, so Steam does not need to be closed.
- The Astral Party - BetterAstralParty or Astral Party - Mod entry added by earlier versions keeps working. Remove it in Steam if you no longer need it; `Uninstall.cmd` also removes it.
- The BepInEx log window shown at game start can be turned off with a check box in the install window. A newly installed BepInEx starts with it off; an existing setting changes only when you tick the box.
- When the game or Steam must be closed, the install window tells you. Close it yourself, then press Check again or cancel; the installer never force-closes processes.
- Running `Install.cmd` or `Uninstall.cmd` with an argument such as `-CheckOnly` or `-WhatIf` uses the console instead of the window.
- A newly installed shared loader defaults to ON.
- If an existing BepInEx is partly deleted, so the game starts without mods, installation adds the missing BepInEx files and keeps its remaining settings. If the remaining files are another version, it leaves BepInEx as it is, installs only BetterAstralParty and names the files to delete before installing again.

</details>

### Settings

Open Mod Settings in the lobby using its button or F8, or from the Mod Settings button in the in-battle Esc menu, then select Mod Features or General. Changes are saved automatically, and updates keep your saved values. To move the lobby's Mod Settings button, drag it in the game's background adjustment mode (the move button at the lower right of the home screen); Confirm keeps the new place, and Reset Mod Settings Position returns it to its first place. Type in the search box above the list to show only the settings from both tabs whose name, location or description contains your words. Hover the scroll bar to the right of the list to see every category name beside it; click a dot or name to jump there. Each row has its switch on the right; a top row turns its whole feature on or off, and the rows in its tray control the individual parts. Defaults below apply to a first installation.

<details>
<summary>Mod Features tab</summary>

Battle Advice
- Action Advice: ON by default. Recommends Attack/Let Go, Defend/Dodge and cards from public information without selecting actions. Defend/Dodge recommends Defend, or Dodge when defending is a sure KO. It is not a master switch for the whole mod. With a Shooting Star chip, the Let Go button shows the damage dealt to the monster and the Starlight gained when you let it go.
- Advice Details: ON by default. Hover over action advice to see damage and KO probabilities. OFF hides the numbers but keeps recommendation icons.
- KO Dice Target: ON by default. Shows the KO dice target when you attack or a monster defends in another player's combat, and the survival dice target when you defend or a player defends in another player's combat. A target is the roll that decides the outcome even at the lowest card bonus, chosen to cover the most such dice combinations. While spectating, or once your attack cards are locked, card bonuses count at the values actually rolled. Once both sides have chosen, it shows the certain or impossible KO from the dice the server decided. Includes card-hover conditions. When the combat has effects the calculation does not count, their icons appear below with "Effects not counted"; hover the question mark for an explanation.
- Final Damage: ON by default. Shows the damage the server decided as soon as both sides have chosen and the dice show starts.

Battle Display
- Combat Status: ON by default. Shows public hand counts and effects below combat HP, and whether each player or monster can counterattack. Effect icons the calculation does not count carry a question mark, and their description shows "Not in calculations".
- Shiyu-Shiyu Shield Effect: ON by default. Shows active Shiyu-Shiyu Shield effects on combat characters.
- Shiyu-Shiyu Shield Defend Lock: ON by default. Locks Defend and defense cards for the shielded local defender. Does not change Dodge availability or choose it automatically.
- Counter Icon Animation / Sound: both ON by default. Animation OFF keeps a static counter icon.

Field / Camera
- Player Indicators: ON by default. Shows field player names, HP, public effects and added counter icons. A short click on an effect opens its native description. It stays beside the character when you zoom with wheel zoom.
- Indicator Height: 100% by default. Places the player indicator marker between the character's base (0%) and its original height (100%), adjusted with a slider.
- Move Direction Highlight: Arrows Only by default. Redraws the walk-direction marks under players and monsters on top, so characters, monsters and trees no longer hide them. Arrows Only draws an arrow where each circle points, lying flat on the ground. Circles and Arrows also draws the circles, with the half behind the character dashed. Off leaves only the game's own marks.
- Wheel Zoom and Free Camera: ON by default. Enables PvE field wheel zoom and a Free Camera that persist across turns. Select Exit Free Camera to the right of the turn-bar pin to restore the default view. Clicking a player's portrait moves the view to that player; clicking the name and chip area opens the details and keeps the zoom. Monster indicators stay in the same place beside the monster while zoomed. With Pin Dice to Screen (ON by default), rolled dice keep the default view's screen position and size while zoomed or in Free Camera; turn it off to leave them on the field as in the base game, so zoom changes their size.
- Opacity When Zoomed Out / Hide When Far: both ON by default. Apply to field indicators while wheel zoom is in use.
- Opacity When Zoomed Out slider: 30% by default, 0–100%. A slider for indicator opacity at the farthest zoom-out; 100% is unchanged and 0% hides them.
- Zoom Sensitivity: 100% by default, 10–300%. How far one wheel tick zooms.
- Max Zoom-Out: 100% by default, 0–100%. How far the wheel can zoom out; 100% shows the whole field and 0% stays at the default view.
- WASD Speed: 100% by default, 10–300%. How fast WASD moves the Free Camera.
- Hide Distance: 25% by default, 0–100%. The zoom-out distance at which Hide When Far hides indicators and shows them again.

Hand and Cards
- Group Hand: Native, Click or Hover; the default is Native. Groups your PvE field hand by function; Click expands horizontally and Hover upward. In both modes, drag cards to the submission area above the cards; Hover highlights it.
- Folded Card Opacity: 75% by default, 0–100%. Opacity of folded cards while Group Hand is on.
- Ping Details: ON by default. Click a native card/chip ping bubble to open a popup with its original image and description.
- Target Confirm/Cancel buttons always appear above cards.

Characters
- Name Style: Name or Title; the default is Name.
- Role Labels / Skill Info in Preparation: both ON by default. Preparation portrait hover shows public skills, cooldowns and Potential.
- Player List Tab: ON by default. A tab under each coin/star bar of the field player list shows the public hand count, the role and the skill cooldown. The skill cooldown shows a stopwatch with turns left/total, as the game's info window does; 0 means the active skill is ready. Other players' cooldowns come from the game server, as in the info window. Hand Count, Role and Skill Cooldown can each be turned off.

Convenience
- Auto Thanks: ON by default. Sends the game's Thanks suggestion once each time it appears. Never sends in PvP, spectating or replays.
- Send All Likes: ON by default. Adds a like button above Confirm on the PvE results screen. Pressing it presses the game's own like button for every other player you have not liked yet. It never sends on its own and turns gray when no one is left. While it shows, chat bubbles move to its left. Hidden in PvP, spectating and replays.
- Shop Bulk Purchase: ON by default. Adds Claim All Free and Bulk Buy under the right of the Star Coin Shop cards. Claim All Free takes the free goods at once and turns gray when there is nothing to claim. Bulk Buy opens a list of the goods you can buy; tick them one by one or add and remove a group at once with the Free, Badges, 50%+ Off and All buttons, check the total and the Star Coins left, and press Buy to buy only those. The purchase runs one item at a time inside the list, Stop ends it before the next item, and the list then comes back. Only a purchase stopped by a failure shows its result for a moment. Several items bought at once share one reward window.
- Clear Ping Mode on Alt+Tab: ON by default. Cancels lingering ping selection after Alt+Tab without sending a ping.
- Press Esc on the home screen to open the game's exit confirmation.

Experimental (BETA)
- Old Version Replays: OFF by default. Enables the play button for match record replays from another game version, which the game otherwise refuses. The replay plays through the current game, so it may stop midway or show the wrong outcome where the rules have changed.

</details>

<details>
<summary>General tab</summary>

Display / Language
- Language: Auto, 한국어 or English; the default is Auto, which follows the game menu language.
- UI Scale: 100% by default, 75–150%, adjusted in 1% steps with a slider. Scales only mod advice and battle-status panels.
- Panel Opacity: 100% by default, 0–100%, adjusted in 1% steps with a slider. Changes only the background of mod battle panels.

Alerts / Audio
- Mute in Background: ON by default. Mutes game audio on focus loss and restores it on return.
- Input Alert: Taskbar, Windows or OFF; the default is Taskbar.
- Bring Game Forward on Match: ON by default. Brings the game forward once when a PvE match is found or the host starts the game.
- Skip Startup Notice: ON by default. Only stops login notices from opening automatically.

Diagnostics
- Diagnostic Log: OFF by default. Records detailed errors, performance and public combat calculations, keeping the five most recent logs.

Settings Backup
- Automatic Backup: ON by default. Backs up settings to `%LOCALAPPDATA%\BetterAstralParty\Settings` whenever settings change. It is restored at the next launch only when the settings file is missing, such as after removal, reinstallation or a fresh game folder; existing settings are never overwritten.
- Back Up Now / Load Backup: keep the settings of a chosen moment and return to them with two presses. Automatic backups never overwrite this copy.
- Open Backup Folder: use it to move the backup files to another PC.
- Reset All Settings: two presses return every setting to its first-install value. Use Back Up Now first to be able to restore them with Load Backup.

Updates
- Update: one button showing the action available now. Depending on the state it checks for, downloads, applies or cancels an update, or confirms a channel switch. While it checks, the result and the time of the last check show under its name, and Check Again on the right checks for a new version again.
- Automatic Updates: Off, Check Only, Download Automatically or Apply Automatically; the default is Apply Automatically. Download Automatically only downloads; press Update to apply. A combination saved separately by an earlier version shows as Custom and is kept.
- Update Notification Popups: ON by default. A new version shows a short notice at the top of the home screen; click it to see the release notes. OFF shows no notice, only a mark on the Mod Settings button. While a new version is available, the Mod Settings button shows a red dot, and the first open after it appears jumps to the update rows.
- Download Page: opens the GitHub release list for the selected channel.
- Update Channel: Release, Pre-release or Private Beta; the default is Release. Pre-release also receives newer Release versions. Choosing Private Beta shows Connect and Sign Out below it.
- Open Diagnostics Folder and Create Diagnostics ZIP are action buttons.

</details>

### Updates

- Automatic updates are supported from the 1.4.0 installation. With Automatic Updates set to Apply Automatically, a verified update is applied while the game runs and used from the next launch. You do not need to close the game, and no separate window opens.
- For manual updates, close the game and run `Install.cmd` from a newer complete package. Settings are kept.
- Safe mode: if the last start did not reach the home screen, a window asks before the game shows its first screen whether to start in safe mode. `Yes` turns mod features off and keeps only updates working; `No` tries again as usual. When safe mode applies a new version, a message box says so, and the next start runs normally.

<details>
<summary>Channel switching and details</summary>

- Release and Pre-release are checked in the public repository without login, and public channels select only 1.4.0 or newer. If no supported release exists, the current installation stays unchanged.
- Channel switching checks the latest supported release in the selected channel. It can be lower than the installed version. The Update button shows the target, and the download starts only after `Start Switch`. Downloading and applying happen inside the game, so keep it open until they finish. Then `Exit and Switch` turns on; it closes the game, and the new version is used from the next launch. If only the same version exists, the status shows `Channel Switch Waiting`. Changing the update channel again cancels the approval. Ordinary updates within the same channel reject downgrades.
- While an update or channel switch downloads and applies, a progress capsule appears under the Mod Settings button on the home screen (above it when the button was moved to the lower part of the screen). Its bar follows the downloaded size and the round button on its right shows the state (downloading, ready or error); clicking it reopens the progress window, which shows the same bar with the percentage and downloaded size.
- If an update hits an error, the capsule shows "Error" with the reason and an error code (for example `BAP-UPD-DOWNLOAD-TIMEOUT`); the progress window also explains what to do and offers `Try Again`. When asking for help, include the error code and a diagnostics ZIP.
- Closing the game during a download, or a download that fails on the network, does not turn automatic updates off; the next start downloads again. Only an apply cut off while it replaces files turns them off, and then `Re-enable Updates` in Update Settings is needed.
- Reclassifying the same public version between Pre-release and Release leaves installed files and your saved channel unchanged.
- Manual updates block downgrades and replacing a version with different files at the same version.
- With the shared loader OFF, starting from Steam runs no mods. `BetterAstralParty-Mod.cmd` in the game folder turns the loader on for that launch only, and Steam asks you to confirm its launch options. The loader configuration file is not changed.
- Automatic updates do not replace the installation/removal tools in previously downloaded packages; use the latest complete package.
- Automatic-update downloads and temporary working files are kept in `BetterAstralParty.Update/` inside the game folder. The earlier update record kept when you reinstall after a removal goes to its `history/` folder.
- Changed game hashes or catalogs after a hotfix alone do not block installation or launch. Only an incompatible optional feature stops.
- The start after a safe-mode run asks again. A new BetterAstralParty version or a changed game build starts normally without asking. If safe mode cannot apply a new version by itself (Apply Automatically is off, or automatic updates are turned off), a message box asks you to install the complete ZIP.
- If the game itself cannot start with the mod, start it with `AstralParty-Vanilla.cmd` or create a diagnostics ZIP with `Create-Diagnostics.cmd`, and install the latest complete package.

</details>

### Uninstall

Run `Uninstall.cmd` from the latest complete package; a removal window opens. Check how many files it removes, then press Uninstall. Mod files and settings are backed up under `%LOCALAPPDATA%/BetterAstralParty/Backups/v5` before removal. Shared BepInEx files and loader settings, other mods and Korean patches are preserved. Installations of 1.2.x or earlier must use their previous package's uninstaller.

> [!CAUTION]
> If normal removal fails, run `Force-Uninstall.cmd`. The force-uninstall window shows how many files it deletes, and Details shows the full list. Tick the box confirming that deleted files cannot be restored, then press Delete permanently; it deletes the BetterAstralParty files, settings and recovery backups for that installation without a backup. Shared loader files and other mods are preserved.

<details>
<summary>More about removal</summary>

- Removing a Steam library entry added by an earlier version needs Steam closed. Steam is not started again after removal; start it yourself when needed.
- If Steam or its account cannot be discovered, shortcut cleanup is skipped. Remove any remaining Astral Party - BetterAstralParty or Astral Party - Mod entry manually in Steam.
- After complete removal, run `Install.cmd` from a complete package to reinstall. If recovery is required, keep logs and backups and follow the displayed procedure.

</details>

### Troubleshooting and reports

- If `Install.cmd` refuses a version, it shows the version that blocks it and what to do: install that version or newer, or remove the mod with `Uninstall.cmd` and install again. Settings can be restored from the kept copy.
- Installation or launch errors: open logs with `Open-Logs.cmd`. For calculation issues, turn on Diagnostic Log in mod settings and reproduce the issue.
- Create a diagnostics ZIP with the package's `Create-Diagnostics.cmd` or F8 → General → Create Diagnostics ZIP. It is saved to the `BetterAstralParty-Diagnostics` folder on the Desktop.

When you [report an issue](https://github.com/TSM701/BetterAstralParty/issues), include the mod version, reproduction steps, screenshots and logs or the diagnostics ZIP. Review personal information and custom paths before sharing.

<details>
<summary>What the diagnostics ZIP contains</summary>

- Detailed text of recent installation, bootstrap and compatibility errors
- The full text of BepInEx `LogOutput.log` and Unity `Player.log` (beginning and newest part, with personal data and card/chat lines redacted)
- Mod runtime logs, why the mod was on or off for the last launch, the loader switch and the game build
- Windows Error Reporting and Application Error records for the game, and Unity crash-folder `error.log` text
- The startup stage record and safe-mode state, and the names and versions of other installed BepInEx plugins
- Process observations and stage for Steam-related installation and removal errors
- `recovery-inventory.json` with the presence, size and hashes of fixed mod files and recovery/backup markers when files are missing

Crash dumps are not included, only their size. Tokens, private paths, Steam IDs, the Windows user name and Steam account names are redacted and nothing is uploaded. Size limits and missing evidence are listed in the ZIP's `summary.json`.

</details>

### Source and licenses

BetterAstralParty-owned code is provided under the [MIT license](LICENSE). See `THIRD-PARTY-NOTICES/` for third-party notices and corresponding-source information, and `docs/BUILD.md` in the public source for build instructions.

Forks and modified builds are welcome under the [MIT license](LICENSE). To avoid confusion with official releases, please state that yours is unofficial and use a different name. Official builds come only from [Download](https://github.com/TSM701/BetterAstralParty/releases), and in-game automatic updates accept only versions signed for this repository.
