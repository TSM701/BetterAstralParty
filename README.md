# BetterAstralParty

[한국어](#한국어) · [English](#english)

## 한국어

Astral Party 국제판 INT(Global)의 PvE 정보 표시와 판단을 돕는 한국어·영어 지원 편의성 모드입니다.

[1.3.0 프리 릴리즈 다운로드](https://github.com/TSM701/BetterAstralParty/releases/tag/1.3.0)

### 주요 기능

- 공개 정보를 사용하는 PvE 전투 추천과 카드 평가
- KO·생존 최소 조건, 전투 확률과 최종 피해 표시
- 플레이어·몬스터 필드 인디케이터와 공개 버프·반격 표시
- 카드·칩 핑 상세 팝업과 손패 정렬 옵션
- 게임 기본 감사합니다 추천을 추천마다 한 번 자동 전송
- 인게임 모드 설정, 캐릭터 이름 옵션, 창 비활성화 시 음소거
- PvE 매칭 성사 또는 직접 만든 PvE 방의 게임 시작 시 창 포커스
- PvE 필드 휠 확대·축소와 턴 전환에도 유지되는 자유 시점, 자유 시점 카메라 종료 버튼
- 별도의 Astral Party - Mod Steam 항목
- 모드 실행 문제 발생 시 런처에서 업데이트 확인·다운로드와 진단 ZIP 생성

PvP 방·경기가 감지되면 전투 추천·카드 정렬·추가 정보 표시·비활성 창 음소거 등 모드 편의 기능이 일시 중지됩니다. PvP에서 나오면 저장된 설정대로 다시 작동하며, 업데이트와 진단 처리는 PvP에서도 계속됩니다.

### 지원 환경

- Windows x64, .NET Framework 4.8, Steam, 설치된 INT(Global) 클라이언트가 필요합니다. CN 클라이언트는 지원하지 않습니다.
- 한국어 패치는 별도로 설치하세요. 기존에 설치한 패치는 유지합니다.

다른 모드나 기존 공용 로더의 구성·버전이 다르다는 이유만으로 설치·게임 실행·업데이트·제거를 차단하지 않습니다. 기존 파일과 설정은 보존하며, 확인할 수 없는 호환성 정보는 진단에 남깁니다. 실제로 동작하지 않는 선택 기능은 해당 기능만 일시 중지합니다.

### 설치와 실행

BetterAstralParty 1.2.x 이하 버전을 사용 중이라면, 게임과 모드 실행기를 종료하고 사용하던 패키지의 `Uninstall.cmd`로 먼저 제거하세요. 제거가 정상 완료되면 아래 순서로 1.3.0 이상을 새로 설치하세요. 이전 설치 체계에서 자동 업데이트하거나 새 패키지로 덮어쓰는 설치는 지원하지 않습니다. 제거 오류가 나면 임의로 파일을 삭제하지 말고 오류 로그와 함께 제보하세요. 처음 설치하는 사용자는 바로 아래 순서대로 진행하면 됩니다.

1. GitHub Releases에서 설치할 버전의 `BetterAstralParty-<버전>.zip` 전체 설치 ZIP을 받으세요. 소스 ZIP은 설치용이 아닙니다.
2. 순정 Steam 클라이언트의 라이브러리에서 Astral Party를 열고 INT(Global) 게임을 먼저 한 번 실행하세요. Astral Party - Mod가 아닌 게임 자체를 실행해 업데이트와 데이터 다운로드를 마친 뒤 정상 종료하세요.
3. 쓰기 가능한 별도 폴더에 ZIP 전체를 압축 해제하고, 동봉된 파일과 폴더를 함께 유지하세요. 게임 폴더 위에 직접 덮어쓰거나 ZIP 내부에서 실행하지 마세요.
4. Steam에 로그인한 상태에서 압축을 푼 폴더의 `Install.cmd`를 실행하세요. PowerShell에서는 `.\Install.cmd`를 실행합니다. 경로를 묻는 경우 Steam의 설치된 파일 → 찾아보기로 확인한 `Astral Party` 폴더 또는 `8vJXnINT` 하위 폴더를 선택하세요.
5. Steam 라이브러리의 Astral Party - Mod로 실행하세요. 첫 실행은 로더 초기화와 파일 다운로드로 시간이 걸리며 인터넷 연결이 필요할 수 있습니다.

Steam 경로나 계정을 자동으로 찾지 못하면 바로가기는 건드리지 않고 파일만 설치합니다. 순정 Steam 클라이언트에서 게임을 실행한 뒤 `Install.cmd`를 다시 실행하면 바로가기를 추가할 수 있습니다.

Steam 항목 등록·수정 때문에 종료 안내가 나오면 Steam을 정상 종료한 뒤 `r`로 재확인할 수 있습니다. `y`를 입력하면 표시된 Steam 게임·앱과 Steam이 강제 종료될 수 있으므로 진행 상황을 저장하고 동기화를 마치세요. 빈 입력이나 거절은 취소합니다. 강제로 종료한 경우 작업 후 Steam만 다시 실행합니다. 실행 경로를 확인할 수 없는 프로세스는 강제 종료하지 않습니다.

BetterAstralParty 없이 실행하려면 순정 Steam 클라이언트에서 Astral Party를 실행해 INT(Global)를 선택하거나 게임 폴더의 `AstralParty-Vanilla.cmd`를 실행하세요. Astral Party - Mod에서는 BetterAstralParty를 함께 불러옵니다. 두 실행 방법은 BetterAstralParty 적용 여부만 구분하며 다른 BepInEx 모드는 그대로 유지합니다. 새 공용 로더는 ON으로 설치됩니다. 전환 전 게임을 종료하고, Steam의 Astral Party 실행 옵션은 변경하지 마세요.

### 설정

로비의 모드 설정 버튼 또는 F8로 열고 모드 기능·일반 설정 탭에서 조절하세요. 변경 사항은 자동 저장됩니다. 아래는 처음 설치했을 때의 기본값이며, 이후 업데이트 시 기존 저장값은 유지합니다. 전투 추천은 모드 전체를 끄는 스위치가 아닙니다.

#### 모드 기능 탭

- 전투 추천: 기본 ON이며 개별 OFF가 가능합니다. 공개 정보를 바탕으로 공격·놓아주기, 방어·회피, 카드 사용을 추천합니다. 선택을 자동 입력하지 않으며 최종 피해·KO 조건·전투 정보·슈슈 쉴드는 별도 설정입니다.

- 최종 피해 표시: 공개된 최종 수치로 계산한 피해를 표시하며 전투 추천·KO 조건과 별도로 켜고 끕니다. 기존 설정에 항목이 없으면 이전 전투 추천의 ON/OFF 상태를 최초값으로 이어받습니다.

- 호버 수치: 기본 ON입니다. 전투 추천에 마우스를 올리면 피해와 전투불능 확률을 표시합니다. OFF는 수치만 숨기고 추천 아이콘은 유지합니다. 전투 추천이 OFF이면 이 옵션만 켜도 추천 수치는 나오지 않습니다.

- KO 최소 조건 표시: 기본 ON이며 전투 추천·호버 수치와 독립적으로 끌 수 있습니다. 공격·다른 사람 전투에서는 KO 조건, 내가 방어자이면 생존 조건을 표시합니다. 카드 호버 조건도 포함하며 가장 유리한 결과를 기준으로 합니다. 최종 피해 표시를 켜는 옵션은 아닙니다.

- 카드·칩 핑 팝업: 기본 ON입니다. 게임의 카드·칩 핑 말풍선을 클릭하면 원본 이미지와 설명을 추가 팝업으로 엽니다. OFF는 추가 팝업만 끄며, 게임 기본 핑 말풍선과 일반 카드 호버는 유지합니다.

- 손패 기능별 정렬: 기본·클릭 펼치기·호버 펼치기 중 선택하며, 처음에는 게임 원래 배치인 기본을 사용합니다. 내가 참가한 PvE 필드의 손패를 기능별로 묶고, 클릭은 가로로, 호버는 세로로 펼칩니다. 전투 화면과 관전자 손패에는 적용하지 않습니다. 펼치는 방식과 제출 영역이 함께 바뀌며, 접힌 카드의 반투명도는 별도 조절할 수 없습니다.

- 전투 정보 표시: 기본 ON입니다. 전투 HP 아래 공개 손패 장수·효과와 플레이어·몬스터의 반격 상태를 표시합니다. 효과 아이콘 호버로 설명을 봅니다. 반격 애니메이션·소리는 각각 별도 옵션으로 끌 수 있습니다. 전투 정보를 OFF로 하면 여기에 속한 추가 표시도 사라집니다.

- 슈슈 쉴드 표시: 전투 캐릭터의 활성 쉴드 효과만 켜고 끕니다. 전투 정보·방어 제한과 독립적입니다.

- 슈슈 쉴드 방어 제한: 쉴드가 있는 내가 방어자일 때 방어 버튼·방어 카드의 모드 추가 잠금을 켜고 끕니다. 쉴드 표시와 별도로 선택하며 OFF는 모드가 추가한 잠금만 복원합니다. 게임 자체 제한이나 회피 가능 여부는 바꾸지 않고 회피를 자동 입력하지 않습니다. 처음 추가될 때는 기존 쉴드 표시의 ON/OFF를 이어받습니다. 쉴드를 반영한 계산·추천은 이 옵션들과 별개입니다.

- 플레이어 필드 인디케이터: 기본 ON입니다. 필드에 플레이어 이름·HP·공개 효과와 플레이어·몬스터의 추가 반격 아이콘을 표시합니다. 효과 아이콘을 짧게 클릭하면 게임 기본 설명을 엽니다. OFF는 추가 플레이어 인디케이터와 추가 필드 반격 아이콘을 함께 끄며, 게임 원래 몬스터 인디케이터·효과 설명은 유지합니다.

- 필드 휠 줌: 기본 ON입니다. PvE 필드에서 휠 줌과 자유 시점 이동을 사용하며, 배율과 자유 시점은 턴이 바뀌어도 유지합니다. OFF는 휠 줌·추가 자유 시점·종료 버튼·줌에 따른 인디케이터 흐림과 숨김을 함께 해제하고 원래 시점을 복원합니다.

- 캐릭터 이름: 본명·이명 중 선택하며 기본은 본명입니다. 캐릭터 선택·목록에서 게임에 등록된 이름을 사용합니다. 이명은 게임 원래 표기이며, 계정 닉네임·캐릭터 소개·역할 표시는 바꾸지 않습니다.

기존 설정에 새 항목이 없을 때 KO 최소 조건 표시는 이전 전투 추천과 호버 수치가 모두 켜져 있었는지, 슈슈 쉴드 표시는 이전 전투 정보 표시 상태를 최초값으로 이어받습니다.

#### 일반 설정 탭

- 언어: 자동·한국어·English 중 선택하며 기본은 자동입니다. 모드의 메뉴·설명을 번역하고, 자동은 게임 메뉴 언어를 따릅니다. 게임 자체 언어·원본 설명·플레이어 이름은 바꾸지 않습니다.

- 창 비활성화 시 음소거: 기본 ON이며 개별 OFF가 가능합니다. 다른 창으로 전환하면 게임 소리를 끄고 복귀하면 복원합니다. 저장된 볼륨은 바꾸지 않으며 PvP에서는 적용하지 않습니다.

- 입력 대기 알림: 작업표시줄·Windows 알림·OFF 중 선택하며 기본은 작업표시줄입니다. 비활성 창에서 내 PvE 입력을 기다릴 때 알립니다. 두 방식 모두 모드가 소리를 재생하지 않으며, Windows 알림은 OS 알림 설정을 따릅니다.

- 매칭 성사 시 창 포커스: 기본 ON이며 개별 OFF가 가능합니다. PvE 매칭 성사 또는 공개·비밀번호 로비 방장이 시작해 캐릭터 선택에 진입할 때 한 번 창을 앞으로 올립니다. 잠깐 최상위로 올린 뒤 원상복원하며, Windows가 전환을 거절하면 작업표시줄을 깜빡입니다. 영구 항상 위는 아닙니다.

- 표시 크기: 기본 100%이며 75~150% 사이에서 5%씩 조절합니다. 모드 추천·전투 상태 패널만 조절하며, 게임 전체 UI나 다른 기능의 ON·OFF에는 영향을 주지 않습니다.

- 배경 불투명도: 기본 100%이며 0~100% 사이에서 5%씩 조절합니다. 모드 전투 패널의 바탕만 바꾸고, 0%에서도 글자·아이콘·테두리는 유지합니다. 필드 인디케이터의 줌 반투명도와는 별개입니다.

- 충돌 진단 로그: 기본 OFF이며 개별 ON·OFF가 가능합니다. 오류·성능·공개 전투 계산의 상세 진단을 기록합니다. OFF여도 기본 오류 로그와 진단 ZIP 생성은 유지합니다.

- 업데이트 채널: 릴리즈·프리 릴리즈·비공개 베타 중 선택합니다. 릴리즈와 프리 릴리즈는 공개 저장소에서 토큰 없이 자동 업데이트하며 비공개 베타만 토큰 인증이 필요합니다. 채널을 바꾸면 버전 숫자의 대소와 관계없이 선택한 채널의 최신판을 확인하고, 대상을 승인한 뒤 전환합니다. 채널 선택만으로 파일을 교체하지는 않습니다.

- 자동 업데이트 확인·업데이트 알림 팝업: 각각 기본 ON이며 따로 끌 수 있습니다. 자동 확인 OFF는 주기적인 조회만 끄며 수동 확인과 이미 진행 중인 작업은 유지합니다. 알림 OFF는 일반 새 버전 팝업만 숨기며 수동 채널 전환 확인은 유지합니다.

- 시작 공지 자동 열기 방지: 기본 ON입니다. 로그인 공지가 자동으로 열리는 것만 막으며 수동 공지 버튼은 유지합니다. OFF는 게임 원래의 자동 열기를 따릅니다.

- 자동 다운로드·종료 후 적용: 기본은 각각 ON이며 두 옵션을 따로 끌 수 있습니다. 새 버전을 내려받고 검증한 뒤, 적용 대기 상태에서 게임·모드 실행기가 종료되면 적용합니다. 저장된 OFF와 업데이트 실패 후 OFF로 저장된 값은 유지합니다. 자동 다운로드 OFF는 업데이트 확인까지 끄는 옵션이 아닙니다.

진단 폴더 열기·진단 ZIP 만들기, 업데이트 확인·다운로드 페이지·업데이트 받기·취소는 실행 버튼이며 ON·OFF 설정이 아닙니다. 새 버전의 변경 사항은 업데이트 알림 팝업에 표시합니다.

#### 모드 기능 탭의 추가 독립 설정

- 자동 감사합니다: 기본 ON이며 개별 OFF가 가능합니다. 게임 기본 감사합니다 추천이 표시되고 전송 가능할 때 추천 한 건당 한 번 전송합니다. 창 비활성 상태와 일시적 입력 잠금 대기를 지원하며 PvP·관전·재생에서는 전송하지 않습니다. 추천을 새로 만들거나 다른 핑을 자동 전송하지는 않습니다.

- 반격 표시 소리·반격 표시 애니메이션: 각각 기본 ON이며 인카운트·전투에 함께 적용됩니다. 소리만 끄거나 정적인 반격 아이콘만 표시할 수 있습니다. 게임 원래의 다른 효과음·애니메이션은 바꾸지 않습니다.

- 카드 대상 선택의 확인·취소 버튼은 항상 카드보다 앞에 표시됩니다. 원래 위치·크기·동작은 유지합니다.

- 캐릭터 역할 표시·캐릭터 준비 설명: 각각 기본 ON이며 따로 끌 수 있습니다. 역할은 게임 내부 분류를 사용하고 설명은 준비 초상화 호버에 공개 스킬·재사용 대기시간·잠재력을 표시합니다. 캐릭터 이름 설정과도 독립적입니다.

- 줌 아웃 인디케이터 반투명·원거리 인디케이터 숨김: 각각 기본 ON이며 따로 끌 수 있습니다. 필드 휠 줌을 사용할 때 플레이어·몬스터 인디케이터에 적용합니다. OFF는 해당 모드 효과만 복원하며 거리 기준을 직접 입력하는 설정은 아닙니다.

- 창 전환 후 핑 모드 해제: 기본 ON입니다. Alt+Tab 후 남은 핑 선택 모드를 전송 없이 해제합니다. OFF는 이 자동 정리만 끕니다.

필드 휠 줌은 위로 확대·아래로 축소합니다. 첫 유효 휠 입력부터 자유 시점이 되며, WASD·왼쪽 마우스 드래그는 게임 원래 카메라 조작 설정과 감도를 따릅니다. 턴 바의 핀 오른쪽 자유 시점 카메라 종료 버튼으로 기본 배율·캐릭터 추적으로 복귀하세요. 버튼은 상점·대상 선택 등 상위 창 뒤에 배치됩니다.

상점·카드·대상 선택 창 자체는 필드 줌을 막지 않습니다. 다만 전투 화면·설정창·실제 목록 스크롤·텍스트 입력·카드 드래그 중·터치 입력·포커스 상실 상태는 제외합니다. 확대·축소 한계는 지도 범위와 카메라의 안전 범위로 계산하며 고정 배율을 선택하는 설정은 없습니다.

손패 정렬의 클릭 펼치기는 카드 위쪽, 호버 펼치기는 오른쪽의 강조된 제출 영역으로 드래그하세요. 호버 방식의 안내 화살표도 오른쪽을 가리킵니다.

스크린샷과 오버레이는 본인의 Steam 단축키를 사용하세요.

### 업데이트

- 자동·수동 업데이트는 새 1.3.0 설치 체계부터 지원합니다. 1.2.x 이하 버전은 사용하던 패키지로 먼저 제거한 뒤 새 전체 패키지로 설치하세요. 새 패키지의 설치·제거 도구는 이전 설치 체계의 전환을 지원하지 않습니다.
- 릴리즈와 프리 릴리즈는 공개 저장소에서 로그인 없이 확인합니다. 비공개 베타만 토큰 인증이 필요합니다. 업데이트 채널을 바꾸면 해당 채널의 최신 결과를 갱신하며, 다운로드 페이지는 선택한 채널의 GitHub 릴리스 목록을 엽니다.
- 자동 업데이트: 새 1.3.0 이상 설치 후 모드 설정에서 자동 다운로드와 종료 후 적용을 켜세요. 적용 대기가 완료되면 게임과 모드 실행기를 정상 종료하고, 적용 후 직접 다시 실행하세요. 선택한 채널의 저장소만 확인합니다.
- 공개 채널에서는 1.3.0 이상만 자동 업데이트 대상으로 선택합니다. 선택한 채널에 지원 버전이 없으면 현재 설치를 유지하며, 1.2.x로 내려가지 않습니다.
- 채널 전환: 선택한 저장소의 지원되는 최신 릴리스를 확인합니다. 설치된 버전보다 낮거나 같은 버전이어도 다른 채널로 전환할 수 있으며, 전환 안내에서 직접 승인해야 적용을 준비합니다. 같은 채널의 일반 업데이트는 버전 역행을 허용하지 않습니다.
- 같은 공개 버전이 GitHub에서 프리 릴리즈↔릴리즈로 바뀌어도 설치된 파일과 저장한 채널은 그대로 둡니다. 새 업데이트 확인에는 변경된 분류를 사용하며, 분류 변경만으로 자동 재설치하거나 다운그레이드하지 않습니다.
- 수동 업데이트: 새 1.3.0 이상 설치 이후에는 게임과 모드 실행기를 종료한 뒤 상위 버전 전체 패키지의 `Install.cmd`를 실행하세요. 기존 설정은 유지하며, 버전 역행과 같은 버전의 다른 파일 덮어쓰기는 차단합니다.
- Mod 실행 시 공용 로더가 OFF라면 안내창 없이 ON으로 전환하며, 다른 모드 파일과 나머지 로더 설정은 보존합니다. 변경 전 원본은 `BepInEx/config/BetterAstralParty.loader-before-*.bak`에 보관됩니다.
- 이전에 받은 패키지의 설치·제거 도구는 자동 업데이트로 바뀌지 않으므로 최신 전체 패키지를 사용하세요.
- 새 자동 업데이트 파일과 임시 작업 파일은 게임 폴더의 `BetterAstralParty.Update/`에 모읍니다. 실행·제거에 필요한 일부 기록은 게임 폴더에 남으며, 기존 기록·백업·로그의 위치는 유지합니다.
- 게임 핫픽스로 파일 해시나 카탈로그가 달라진 것만으로 설치·실행을 막지 않습니다. 실제로 맞지 않는 선택 기능은 해당 기능만 중단하며, 필수 실행 파일 누락이나 미완료 복구 등으로 중단된 경우에는 런처의 복구 업데이트 화면에서 재확인하거나 업데이트를 내려받을 수 있습니다. 검증을 통과한 후보는 적용 버튼을 누르면 런처 종료 후 반영됩니다. 호환 패치가 없거나 확인에 실패한 경우에는 설치 파일을 변경하지 않으며, 순정 Steam 클라이언트로 실행하거나 진단 ZIP을 만들 수 있습니다.
- 비상 업데이트 화면에서는 이번 업데이트의 릴리즈·프리 릴리즈 채널만 선택합니다. 게임 안에 저장된 채널 설정은 바꾸지 않으며, 비공개 베타 채널은 인게임 업데이트 설정에서 선택하세요.
- 게임 UI 호환성 오류가 나도 업데이트 드라이버가 실행되는 동안에는 기존 설정에 따라 업데이트 확인·다운로드를 계속합니다. 로더나 드라이버 자체가 시작되지 않는 경우에는 런처의 복구 업데이트 또는 최신 전체 패키지를 사용하세요.

### 제거

새 1.3.0 이상 설치의 제거에는 최신 전체 패키지의 `Uninstall.cmd`를 사용하세요. 게임과 모드 실행기를 종료한 뒤 제거 목록을 확인하세요. 모드 파일과 설정은 `%LOCALAPPDATA%/BetterAstralParty/Backups/v4`에 백업한 뒤 제거합니다. 이전 설치 체계의 버전은 사용하던 패키지의 제거기를 사용해야 합니다.

Steam 경로나 로그인 계정을 자동으로 찾지 못하면 바로가기 정리는 건너뛰고, 소유가 검증된 모드 파일은 정상 제거 절차로 처리합니다. 남은 Astral Party - Mod 항목은 Steam에서 직접 제거할 수 있습니다.

공용 BepInEx 파일과 현재 로더 설정, 다른 모드, 한국어 패치와 순정 Steam 클라이언트의 Astral Party 실행 설정은 유지합니다. 다른 모드를 BetterAstralParty 설치 이후에 추가한 경우에도 해당 모드와 로더 설정을 그대로 둡니다.

완전 제거 후에는 전체 패키지의 `Install.cmd`로 재설치할 수 있습니다. 복구 안내가 나오면 로그와 백업을 보관하고 표시된 절차를 따르세요.

정상 제거가 실패해 강제 제거가 필요하면 `Force-Uninstall.cmd`를 실행하고 삭제 목록을 확인하세요. 동의하면 표시된 BetterAstralParty 파일·설정·해당 설치의 복구 백업을 되돌릴 수 없게 삭제합니다. 공용 로더와 다른 모드는 보존하며, 남은 Astral Party - Mod 항목은 Steam에서 직접 제거할 수 있습니다.

### 문제 해결과 제보

- Steam 항목이 안 보이면 Steam을 재시작하고 라이브러리 필터와 로그인 계정을 확인하세요. 항목 복구에는 `Install.cmd`를 사용하세요.
- 설치·실행 오류는 `Open-Logs.cmd`로 로그를 확인하세요. 호환성 검사 오류가 나면 오류 문구를 함께 제보하세요.
- 계산 문제는 모드 설정에서 진단을 켠 뒤 재현하세요.
- 진단 ZIP은 패키지의 `Create-Diagnostics.cmd` 또는 게임의 F8 → 일반 설정 → 진단 ZIP 만들기로 생성하세요. 기본 저장 위치는 `%LOCALAPPDATA%/BetterAstralParty/Diagnostics`입니다.
- 최근 설치·초기 실행·런처·호환성 오류의 상세 본문도 포함합니다. 토큰·개인 경로를 가리며, 용량 제한이나 누락 사유는 ZIP의 `summary.json`에 기록합니다.
- Steam 관련 설치·제거 오류는 실패 당시의 프로세스 확인 결과와 단계도 수집합니다. 확인하지 못한 정보는 미확인으로 남깁니다.
- 게임·모드 파일이 일부 없는 경우에도 고정 모드 파일·복구 및 백업 표식의 존재·크기·해시를 `recovery-inventory.json`에 기록합니다. 복구 기록 원문과 인증 저장소는 포함하지 않습니다.

모드 버전, 재현 순서, 스크린샷과 관련 로그 또는 진단 ZIP을 함께 전달하세요. 공유 전 개인정보와 사용자 지정 경로를 확인하세요.

### 소스와 라이선스

BetterAstralParty 자체 코드는 MIT 라이선스로 제공합니다. 외부 구성 요소의 고지와 대응 소스 안내는 `THIRD-PARTY-NOTICES/`에서 확인하세요. 소스 빌드 방법은 공개 소스 묶음의 `docs/BUILD.md`에 있습니다.

## English

A Korean/English quality-of-life mod for Astral Party INT(Global), focused on PvE information and decision support.

[Download the 1.3.0 pre-release](https://github.com/TSM701/BetterAstralParty/releases/tag/1.3.0)

### Features

- PvE combat recommendations and card evaluation using public information
- Minimum KO/survival conditions, combat probabilities and observed final damage
- Player/monster field indicators with public buffs and counterattack markers
- Detail popups for card and chip pings, and hand-layout options
- Automatically send each native Thanks quick-chat recommendation once
- In-game settings, character-name options and mute on focus loss
- Focus the game when a PvE match is found or a manually created PvE room starts
- PvE field wheel zoom and free view across turns, with an Exit Free Camera button
- A separate Astral Party - Mod Steam entry
- Launcher update checks, downloads and diagnostics ZIP creation when mod launch encounters a problem

When a PvP room or match is detected, mod conveniences such as combat recommendations, hand layouts, additional information displays and focus muting pause. They resume with your saved settings after leaving PvP; updates and diagnostics continue even during PvP.

### Requirements

- Windows x64, .NET Framework 4.8, Steam and an installed INT(Global) client. The CN client is not supported.
- Install a Korean patch separately if needed. An existing patch is preserved.

Differences in another mod or an existing shared loader's configuration or version do not block installation, game entry, updates or removal. Existing files and settings are preserved, and unavailable compatibility observations are recorded in diagnostics. An optional feature that actually fails is paused individually.

### Install and play

If you use BetterAstralParty 1.2.x or earlier, close the game and mod launcher and uninstall using your previous package's `Uninstall.cmd` first. After removal completes successfully, install 1.3.0 or newer fresh using the steps below. Automatic updates and in-place installation from the previous installation generation are not supported. If removal fails, report the error log instead of deleting files manually. First-time users can follow the steps below directly.

1. Download the complete `BetterAstralParty-<version>.zip` installation package for your chosen version from GitHub Releases. Source ZIPs are not installation packages.
2. In the Steam client, first launch Astral Party INT(Global) itself from your library, not Astral Party - Mod. Wait for game/data updates to finish, then exit normally.
3. Extract the entire ZIP to a separate writable folder and keep all bundled files and folders together. Do not overwrite the game folder directly or run inside the ZIP.
4. While signed in to Steam, run `Install.cmd` from the extracted folder. In PowerShell, run `.\Install.cmd`. If prompted, select the `Astral Party` folder or its `8vJXnINT` child, as shown by Steam's Installed Files → Browse.
5. Launch Astral Party - Mod from your Steam library. First-time loader initialization and file downloads may take time and require internet access.

If Steam or its account cannot be discovered, installation continues without changing shortcuts. Launch the original game through Steam, then rerun `Install.cmd` to add the entry.

If Steam entry registration or repair prompts you to close processes, you can close Steam normally and enter `r` to recheck. Entering `y` may force-close the listed Steam games/apps and Steam, so save progress and finish syncing first. A blank response or refusal cancels. After a forced shutdown, only Steam restarts. Processes with unverified executable paths are never force-closed.

To play without BetterAstralParty, launch Astral Party through the Steam client and select INT(Global), or run `AstralParty-Vanilla.cmd` in the game folder. Astral Party - Mod also loads BetterAstralParty. These launch methods select BetterAstralParty only; other BepInEx mods remain available in both. A newly installed shared loader defaults to ON. Close the game before switching and leave Astral Party's Steam launch options unchanged.

### Settings

Open Mod Settings in the lobby using its button or F8, then select Mod Features or General. Changes are saved automatically. Defaults below apply to a first installation; later updates preserve saved values. Battle Advice is not a master switch for the whole mod.

#### Mod Features tab

- Battle Advice: ON by default and independently switchable. Uses public information to recommend Attack/Let Go, Defend/Dodge and cards without selecting actions automatically. Final Damage, KO Conditions, Battle Info and Shushu Shield are separate settings.

- Final Damage: independently displays damage calculated from final public numbers. When first added to an existing configuration, inherits the previous Battle Advice ON/OFF value.

- Hover Details: ON by default. Hover over combat advice to see damage and KO probabilities. OFF hides numeric details, not recommendation icons. Enabling this alone does not show advice values when Battle Advice is OFF.

- KO Conditions: ON by default and can be disabled independently of Battle Advice and Hover Details. Shows KO conditions when attacking or viewing another player's combat, survival conditions when you defend, and card-hover conditions. Uses best-case outcomes; it does not enable final damage display.

- Card / Chip Pings: ON by default. Click a native card/chip ping bubble to open an added popup with its original image and description. OFF disables only the added popup; native ping bubbles and normal card hover remain.

- Grouped Hand: choose Native, Click or Hover; the default is the game's Native layout. Groups your hand by function in the PvE field; Click expands horizontally and Hover vertically. Excludes combat screens and spectators' hands. Expansion and submission areas change together; folded-card translucency has no separate setting.

- Battle Info: ON by default. Shows public hand counts/effects and player/monster counter status below combat HP. Hover effect icons for details. Counter sound and animation have separate toggles; disabling Battle Info also removes its added indicators.

- Shushu Shield: controls combat shield visuals only, independently of Battle Info and defense restrictions.

- Shushu Defense Restrictions: independently controls mod-added Defend/defense-card locks for the shielded local defender. OFF restores only mod-owned restrictions, not native game restrictions. Does not change Dodge availability or automatically choose it. Initially inherits the existing shield-display preference. Shield-aware calculations and recommendations remain independent.

- Player Field Indicator: ON by default. Shows field player names, HP, public effects and added player/monster counter icons. A short click on an effect opens its native description. OFF removes added player indicators and added field counter icons together; native monster indicators and effect descriptions remain.

- Field Wheel Zoom: ON by default. Enables PvE field wheel zoom and free-view movement; zoom and free view persist across turns. OFF disables wheel zoom, added free view, its exit button and zoom-dependent indicator fading/hiding together, restoring the original view.

- Character Names: choose Name or Title; the default is Name. Uses names registered by the game in selection screens and lists. Title restores native labels; player nicknames, character descriptions and role labels are unchanged.

When an existing configuration lacks the new entries, KO Conditions initially inherits whether both Battle Advice and Hover Details were enabled; Shushu Shield inherits the previous Battle Info state.

#### General tab

- Language: choose Auto, 한국어 or English; the default is Auto. Translates mod menus/help, with Auto following the game menu language. Leaves the game's language, original descriptions and player names unchanged.

- Mute When Unfocused: ON by default and can be disabled independently. Mutes game audio on focus loss and restores it on return. Saved volume is unchanged; not applied in PvP.

- Input Alert: choose Taskbar, Windows or OFF; the default is Taskbar. Alerts while the unfocused game awaits your PvE input. Neither mode plays a mod notification sound; Windows alerts follow OS notification settings.

- Focus on Match Found: ON by default and can be disabled independently. Brings the game forward once on a PvE match, or when a public/password lobby host starts character selection. Briefly raises the window to topmost, then restores its state; flashes the taskbar if Windows refuses. Not permanently always-on-top.

- UI Scale: defaults to 100%, adjustable from 75–150% in 5% steps. Scales only mod advice and battle-status panels; does not scale the entire game UI or change feature toggles.

- Opacity: defaults to 100%, adjustable from 0–100% in 5% steps. Changes only the background fill of mod battle panels; text, icons and borders remain visible even at 0%. Separate from zoom-dependent field-indicator translucency.

- Diagnostic Log: OFF by default and can be enabled or disabled independently. Records detailed errors, performance and public combat calculations. Basic error logs and diagnostics ZIP creation remain available when OFF.

- Update Channel: Release, Pre-release or Private Beta. Both public channels update without a token; only Private Beta requires token authentication. Switching checks the destination channel's latest version regardless of version-number order, then requires target confirmation. Selecting a channel alone does not replace files.

- Automatic Update Checks / Update Notification Popups: both default ON and can be disabled separately. Disabling periodic discovery preserves manual checks and active operations. Disabling ordinary popups preserves explicit channel-switch confirmation.

- Suppress Startup Notices: ON by default; suppresses automatic login notices only. Manual NOTICE buttons remain. OFF follows native automatic notice behavior.

- Auto Download / Apply After Exit: both default to ON and can be disabled separately. Downloads and verifies a new version, then applies a queued update after the game and mod launcher close. Saved OFF values and OFF values persisted after update failure remain. Auto Download OFF does not disable update checks.

Open Diagnostics Folder, Create Diagnostics ZIP, Check Updates, Download Page, Get Update and Cancel Update are action buttons, not ON/OFF settings. Release notes appear in the update notification popup.

#### Additional settings in Mod Features

- Automatic Thanks: ON by default and independently switchable. Confirms each available native Thanks recommendation at most once, including while unfocused and after temporary input locks. Excludes PvP, spectators and replays; never creates recommendations or sends other pings automatically.

- Counter Indicator Sound / Animation: both default ON and apply to added encounter and combat counters. Sound can be muted independently; animation OFF keeps a static counter icon. Other native effects and sounds remain unchanged.

- Target Confirm/Cancel buttons always appear above cards. Position, size and callbacks remain native.

- Character Roles / Preparation Details: both default ON and can be disabled separately. Roles use native classifications; preparation portrait hover shows public skills, cooldowns and potentials. Independent of Character Names.

- Zoomed-out Indicator Fading / Distant Indicator Hiding: both default ON and can be disabled separately. Apply to player/monster field indicators while Field Wheel Zoom is active; OFF restores only the selected mod effect. Distance thresholds are not manually configurable.

- Reset Ping Mode After Task Switching: ON by default. Cancels lingering ping selection after Alt+Tab without sending a ping; OFF disables this automatic cleanup.

Scroll up to zoom in and down to zoom out. The first accepted wheel input starts free view; WASD and left-mouse drag follow native camera-control settings and sensitivity. Select Exit Free Camera to the right of the turn-bar pin to restore default zoom and character tracking. The button stays behind higher-level windows such as shops and target selection.

Shop, card and target-selection windows do not themselves block field zoom. Combat screens, settings, actual list scrolling, text entry, card dragging, touch input and focus loss are excluded. Zoom limits are calculated from map bounds and the camera's safe range; there is no fixed zoom-percentage selector.

With Click hand layout, drag cards into the submission area above the cards. With Hover, use the highlighted area on the right; its guide arrow also points right.

Use your own Steam screenshot and overlay hotkeys.

### Updates

- Automatic and manual updates are supported from the new 1.3.0 installation generation. Uninstall 1.2.x or earlier with your previous package before installing the new complete package fresh. The new installation/removal tools do not migrate the previous installation generation.
- Release and Pre-release check the public repository without login. Only Private Beta requires token authentication. Switching channels refreshes that channel's latest result; Download Page opens its GitHub releases listing.
- Automatic updates: after installing the new 1.3.0 generation or newer, enable Auto Download and Apply After Exit in mod settings. Once the update is queued, close the game and mod launcher normally, then restart the game yourself after it finishes. Only the selected channel's repository is checked.
- Public channels select only version 1.3.0 or newer for automatic updates. If the selected channel has no supported release, the current installation stays unchanged; it does not downgrade to 1.2.x.
- Channel switching: checks the latest supported release in the selected repository. A different channel can have a lower or equal version; approve the transition notice to prepare it. Ordinary updates within the same channel still reject downgrades.
- Reclassifying the same public version between GitHub Pre-release and Release leaves installed files and your saved channel unchanged. Subsequent checks use the current classification; reclassification alone never reinstalls or downgrades the mod.
- Manual updates: after installing the new 1.3.0 generation or newer, close the game and mod launcher, then run `Install.cmd` from a newer complete package. Saved settings are preserved; downgrades and replacing a version with different files at the same version remain blocked.
- A Mod launch enables the shared loader without a prompt if it is OFF, preserving other mods' files and all remaining loader settings. The original configuration is retained at `BepInEx/config/BetterAstralParty.loader-before-*.bak`.
- Automatic updates do not replace installation/removal tools in previously downloaded packages; use the latest complete package for maintenance.
- New automatic-update downloads and temporary working files are kept in `BetterAstralParty.Update/` inside the game folder. Some records needed for launch and removal stay at the top level; existing records, backups and logs keep their locations.
- Changed game hashes or catalogs after a hotfix alone do not block installation or launch. An incompatible optional feature stops independently. If essential executables are missing or unfinished recovery prevents launch, use the launcher's recovery update window to retry or download an update. After a candidate passes verification, choose Apply to install it after the launcher exits. No compatible patch or a failed lookup leaves installation files unchanged; launching through the Steam client and creating a diagnostics ZIP remain available.
- The emergency update screen offers Release and Pre-release for this update only, without changing the saved in-game channel setting. Select Private Beta in the in-game Update Settings.
- Game UI compatibility errors do not stop update checks and downloads while the update driver is running; saved preferences still apply. If the loader or driver cannot start, use launcher recovery or the latest complete package.

### Uninstall

Use `Uninstall.cmd` from the latest complete package to remove an installation from the new 1.3.0 generation or newer. Close the game and mod launcher and review the removal list. Mod files and settings are backed up under `%LOCALAPPDATA%/BetterAstralParty/Backups/v4` before removal. Installations from the previous generation must use their previous package's uninstaller.

If the Steam directory or signed-in account cannot be discovered, shortcut cleanup is skipped while verified mod files still follow normal backed-up removal. Remove any remaining Astral Party - Mod entry manually in Steam.

Shared BepInEx files and current loader settings, other mods, Korean patches and Astral Party launch settings in the Steam client are preserved. Mods added after BetterAstralParty installation and their loader settings are also left unchanged.

After complete removal, run `Install.cmd` from a complete package to reinstall. If recovery is required, keep logs and backups and follow the displayed procedure.

If normal removal fails and forced removal is needed, run `Force-Uninstall.cmd` and review the deletion list. After confirmation, it permanently deletes the listed BetterAstralParty files, settings and recovery backups for that installation. Shared loader files and other mods are preserved; remove any remaining Astral Party - Mod entry manually in Steam.

### Troubleshooting and reports

- Missing Steam entry: restart Steam and check library filters and the signed-in account. Use `Install.cmd` to repair the entry.
- Installation or launch errors: open logs with `Open-Logs.cmd`. Include the error message when reporting a compatibility-check failure.
- Calculation issues: enable diagnostics in mod settings, then reproduce the issue.
- Create a diagnostics ZIP using the package's `Create-Diagnostics.cmd` or F8 → General → Create Diagnostics ZIP in game. The default output folder is `%LOCALAPPDATA%/BetterAstralParty/Diagnostics`.
- Recent installation, bootstrap, launcher and compatibility errors include detailed text with tokens and private paths redacted. Size limits and missing evidence are listed in the ZIP's `summary.json`.
- Steam-related installation and removal errors include the process observations and stage recorded at failure time. Unavailable information remains unknown.
- Even when game or mod files are missing, `recovery-inventory.json` lists the presence, size and hashes of fixed mod files and recovery/backup markers. Raw recovery records and credential stores are excluded.

Include the mod version, reproduction steps, screenshots and relevant logs or diagnostics ZIP. Review personal information and custom paths before sharing.

### Source and licenses

BetterAstralParty-owned code is provided under the MIT license. See `THIRD-PARTY-NOTICES/` for third-party notices and corresponding-source information. Build instructions are in `docs/BUILD.md` in the public source archive.
