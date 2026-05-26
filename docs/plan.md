Stream Orchestra 개발 계획서 v0.1
1. 프로젝트 목표

Windows 전용 멀티 스트리밍 시청 앱을 개발한다.

주 사용 플랫폼은 SOOP이며, 사용자는 같은 SOOP 계정으로 로그인한 상태에서 여러 방송을 동시에 시청한다. 현재는 크롬, 웨일, 엣지 등 여러 브라우저를 사용해 브라우저 단위 동시 재생 제한을 피하고 있다.

이 앱의 목표는 기존의 수동 브라우저 배치 작업을 대체하는 것이다.

앱 실행
→ 마지막 상태 자동 복원
→ SOOP 방송 9~16개 자동 배치
→ 방송 화면만 최대한 크게 표시
→ 특정 슬롯만 쉽게 교체
→ 프리셋으로 자주 쓰는 구성을 저장/복원
2. 중요한 전제

아직 검증되지 않은 핵심 리스크가 있다.

하나의 Windows 앱 안에서 WebView2 프로필을 여러 개 분리했을 때,
SOOP이 이를 서로 다른 브라우저처럼 인식해서 동시 재생 제한이 완화되는가?

따라서 본 개발 전에 반드시 Feasibility Spike를 먼저 만든다.

이 프로젝트는 SOOP의 DRM, 인증, 보안 제한을 해킹하거나 우회하지 않는다.
사용자가 현재 여러 일반 브라우저로 수행 중인 시청 방식을 앱 내부 WebView2 프로필 또는 외부 브라우저 제어 방식으로 재현할 수 있는지 검증한다.

3. 기술 스택 제안
기본 스택
- Windows Desktop App
- C#
- WPF
- WebView2
- JSON 기반 설정 저장
선택 이유
- WPF는 Windows 창/레이아웃 구현이 빠름
- WebView2는 주소창 없는 브라우저 뷰를 앱 내부에 넣기 적합
- JSON 설정은 프리셋/레이아웃/슬롯 상태 저장에 단순하고 충분함
4. 개발 단계
Phase 0. SOOP 동시 재생 검증 스파이크

가장 먼저 만든다.

목표

WebView2 기반 앱에서 여러 프로필 그룹을 만들고, SOOP 방송을 여러 개 동시에 재생할 수 있는지 검증한다.

구현 범위
- WPF 앱 생성
- WebView2 16개까지 동적 생성 가능
- Profile Group A/B/C/D 개념 도입
- 각 그룹당 최대 4개 WebView 배치
- SOOP 로그인 테스트 가능
- URL 수동 입력 가능
- 4개, 8개, 12개, 16개 동시 재생 테스트 가능
테스트 레이아웃
4x4 Grid

1   2   3   4     → Group A
5   6   7   8     → Group B
9   10  11  12    → Group C
13  14  15  16    → Group D
검증 항목
1. Group A 하나만 사용했을 때 몇 개까지 재생 가능한가?
2. Group A/B/C/D로 나누었을 때 8개 이상 재생 가능한가?
3. 12개 재생 가능한가?
4. 16개 재생 가능한가?
5. 각 그룹에서 같은 SOOP 계정 로그인이 유지되는가?
6. 앱 재실행 후 로그인 세션이 유지되는가?
7. CPU/GPU/메모리 사용량이 감당 가능한가?
성공 기준
- WebView2 프로필 그룹을 나누었을 때 SOOP 방송 9개 이상 재생 가능
- 같은 SOOP 계정 로그인 세션 유지 가능
- 앱 재실행 후 세션 유지 가능
실패 시 대안
1. WebView2 다중 User Data Folder 방식 테스트
2. 그래도 실패하면 외부 브라우저 제어 모드로 전환
   - Chrome
   - Edge
   - Whale
   - 기타 브라우저
5. MVP 목표

Phase 0 검증이 성공했을 때 본격 MVP를 만든다.

MVP 핵심 기능
1. SOOP 중심 멀티뷰
2. 8개 소형 + 1개 메인 레이아웃
3. 4x4 대회용 16분할 레이아웃
4. 슬롯별 방송 URL 저장
5. 앱 실행 시 마지막 상태 자동 복원
6. 프리셋 저장/불러오기
7. 슬롯 상단 컨트롤 바
8. 상단 드래그 핸들로 슬롯 교체
9. SOOP 탐색 패널
10. 앱 자체 즐겨찾기
11. 슬롯 선택 후 “선택 슬롯에 넣기”
12. 슬롯별 음소거 상태 유지
13. 슬롯별 프로필 그룹 유지
6. 확정된 UX 정책
6.1 앱 시작 방식
앱 실행 시:
- 마지막으로 보던 상태 자동 복원
- 이후 사용자가 다른 프리셋으로 전환 가능

즉, 기본 정책은 다음과 같다.

마지막 상태 복원 + 프리셋 전환 가능
6.2 프리셋 정책

프리셋은 슬롯 위치까지 고정한다.

예:

프리셋: 평일 기본

1번 슬롯 = 스트리머 A
2번 슬롯 = 스트리머 B
3번 슬롯 = 스트리머 C
...
9번 슬롯 = 스트리머 I

프리셋은 사용자가 명시적으로 저장할 때만 변경한다.

드래그로 슬롯 교체
방송 임시 변경
음소거 변경

→ 자동으로 프리셋을 덮어쓰지 않음

필요 버튼:

- 현재 상태 저장
- 다른 이름으로 저장
- 프리셋 불러오기
- 원래 프리셋으로 되돌리기
6.3 기본 레이아웃

MVP 기본 레이아웃은 다음이다.

1  2  3  4
5  6  9  9
7  8  9  9

9번 슬롯은 메인 슬롯이다.

6.4 대회용 레이아웃
1   2   3   4
5   6   7   8
9   10  11  12
13  14  15  16
6.5 슬롯 조작 방식

방송 화면 클릭은 WebView/SOOP 플레이어에 그대로 전달한다.

앱 조작은 슬롯 상단 컨트롤 바에서만 수행한다.

┌────────────────────────────┐
│ ⋮⋮  Slot 2 / 스트리머명 🔇 ↻ ⋯ │
├────────────────────────────┤
│                            │
│          방송 화면          │
│                            │
└────────────────────────────┘
컨트롤 바 기능
⋮⋮ : 드래그 핸들
Slot 번호 : 슬롯 선택
스트리머명 : 현재 방송 표시
🔇 : 음소거 토글
↻ : 새로고침
⋯ : 추가 메뉴
6.6 슬롯 교체 정책

상단 드래그 핸들을 잡고 다른 슬롯에 드롭하면 두 슬롯의 방송을 교체한다.

2번 슬롯 방송 ↔ 9번 슬롯 방송

단, 교체되는 것은 방송 URL/방송 정보다.

아래 속성은 슬롯에 남는다.

- 음소거 상태
- 프로필 그룹
- 슬롯 위치
- 슬롯 크기
6.7 음소거 정책

방송을 새로 넣을 때는 기존 슬롯의 음소거 상태를 유지한다.

예:

2번 슬롯이 음소거 상태
→ 새 방송을 2번에 넣음
→ 새 방송도 음소거 상태

드래그로 슬롯을 교체해도 음소거 상태는 슬롯에 남는다.

예:

교체 전:
2번 슬롯: 방송 B, 음소거
9번 슬롯: 방송 I, 소리 켜짐

교체 후:
2번 슬롯: 방송 I, 음소거
9번 슬롯: 방송 B, 소리 켜짐
6.8 프로필 그룹 정책

프로필 그룹도 슬롯에 귀속된다.

예:

1~4번 슬롯   → Group A
5~8번 슬롯   → Group B
9~12번 슬롯  → Group C
13~16번 슬롯 → Group D

드래그로 방송을 교체해도 프로필 그룹은 슬롯에 남는다.

즉, 방송이 다른 슬롯으로 이동하면 해당 슬롯의 프로필 그룹에서 다시 열린다.

7. 방송 선택 방식
MVP 방식
1. 슬롯 상단 바를 클릭해서 슬롯 선택
2. SOOP 탐색 패널에서 방송 찾기
3. 방송 페이지 열기
4. [선택 슬롯에 넣기] 버튼 클릭
5. 선택한 슬롯만 교체
MVP에서는 드래그 등록 제외

SOOP 탐색 패널이나 즐겨찾기에서 방송을 슬롯으로 드래그하는 기능은 MVP 이후로 미룬다.

8. SOOP 탐색 패널

앱 왼쪽에 접을 수 있는 탐색 패널을 둔다.

[SOOP 탐색 패널] | [멀티뷰 화면]

탐색 패널 기능:

- SOOP 웹페이지 열기
- 로그인 계정의 팔로우/즐겨찾기 목록 사용
- 현재 탐색 패널에서 열린 방송을 선택 슬롯에 넣기
- 현재 방송을 앱 자체 즐겨찾기에 추가
9. 앱 자체 즐겨찾기

SOOP 팔로우 목록과 별개로 앱 내부 즐겨찾기를 둔다.

저장 정보 예시:

{
  "id": "favorite_001",
  "name": "스트리머명",
  "platform": "SOOP",
  "url": "https://...",
  "memo": "",
  "lastUsedAt": "2026-05-26T00:00:00"
}

MVP에서는 최소한 다음만 있으면 된다.

- 이름
- 플랫폼
- URL
- 최근 사용 시간
10. 데이터 모델 초안
AppState
{
  "lastWorkspaceId": "workspace_weekday",
  "window": {
    "x": 100,
    "y": 100,
    "width": 1920,
    "height": 1080,
    "isMaximized": true
  },
  "selectedSlotId": 2
}
WorkspacePreset
{
  "id": "workspace_weekday",
  "name": "평일 기본",
  "layoutId": "layout_8_small_1_main",
  "slots": [
    {
      "slotId": 1,
      "streamId": "stream_a",
      "muted": true,
      "profileGroupId": "A"
    },
    {
      "slotId": 9,
      "streamId": "stream_i",
      "muted": false,
      "profileGroupId": "C"
    }
  ]
}
LayoutPreset
{
  "id": "layout_8_small_1_main",
  "name": "8 Small + 1 Main",
  "gridColumns": 4,
  "gridRows": 3,
  "slots": [
    { "slotId": 1, "x": 0, "y": 0, "w": 1, "h": 1 },
    { "slotId": 2, "x": 1, "y": 0, "w": 1, "h": 1 },
    { "slotId": 3, "x": 2, "y": 0, "w": 1, "h": 1 },
    { "slotId": 4, "x": 3, "y": 0, "w": 1, "h": 1 },
    { "slotId": 5, "x": 0, "y": 1, "w": 1, "h": 1 },
    { "slotId": 6, "x": 1, "y": 1, "w": 1, "h": 1 },
    { "slotId": 7, "x": 0, "y": 2, "w": 1, "h": 1 },
    { "slotId": 8, "x": 1, "y": 2, "w": 1, "h": 1 },
    { "slotId": 9, "x": 2, "y": 1, "w": 2, "h": 2 }
  ]
}
StreamEntry
{
  "id": "stream_a",
  "platform": "SOOP",
  "name": "스트리머명",
  "url": "https://...",
  "lastUsedAt": "2026-05-26T00:00:00"
}
ProfileGroup
{
  "id": "A",
  "name": "SOOP Group A",
  "userDataFolder": "Profiles/GroupA"
}
11. 추천 프로젝트 구조
StreamOrchestra/
 ├─ src/
 │   ├─ StreamOrchestra.App/
 │   │   ├─ MainWindow.xaml
 │   │   ├─ MainWindow.xaml.cs
 │   │   ├─ Views/
 │   │   │   ├─ MultiViewGrid.xaml
 │   │   │   ├─ StreamSlotView.xaml
 │   │   │   ├─ ExplorerPanel.xaml
 │   │   │   └─ PresetBar.xaml
 │   │   ├─ ViewModels/
 │   │   │   ├─ MainViewModel.cs
 │   │   │   ├─ SlotViewModel.cs
 │   │   │   ├─ WorkspaceViewModel.cs
 │   │   │   └─ ExplorerViewModel.cs
 │   │   ├─ Models/
 │   │   │   ├─ AppState.cs
 │   │   │   ├─ LayoutPreset.cs
 │   │   │   ├─ WorkspacePreset.cs
 │   │   │   ├─ StreamEntry.cs
 │   │   │   └─ ProfileGroup.cs
 │   │   ├─ Services/
 │   │   │   ├─ WebViewProfileService.cs
 │   │   │   ├─ PresetStorageService.cs
 │   │   │   ├─ AppStateService.cs
 │   │   │   ├─ SlotSwapService.cs
 │   │   │   └─ StreamNavigationService.cs
 │   │   └─ Resources/
 │   └─ StreamOrchestra.Tests/
 ├─ data/
 │   ├─ layouts.json
 │   ├─ workspaces.json
 │   ├─ favorites.json
 │   └─ appstate.json
 └─ README.md
12. Codex 작업 순서
Step 1. 스파이크 앱 생성
목표:
WPF + WebView2로 4x4 WebView Grid를 만든다.

기능:
- URL 입력
- Load 버튼
- Group A/B/C/D 선택
- 16개 WebView 생성
- 각 슬롯마다 Profile Group 지정
- SOOP 로그인 테스트 가능

완료 조건:

- 16개 슬롯이 화면에 보임
- 각 슬롯에 URL 로드 가능
- Profile Group별 WebView 초기화 가능
- 앱 재실행 후 로그인 유지 여부 확인 가능
Step 2. Profile Group 구현
WebViewProfileService 구현

역할:
- Group A/B/C/D 생성
- 각 그룹별 UserDataFolder 또는 ProfileName 관리
- WebView2 Environment 생성
- 슬롯에 맞는 WebView2 초기화 제공

완료 조건:

- 슬롯 1~4는 Group A
- 슬롯 5~8은 Group B
- 슬롯 9~12는 Group C
- 슬롯 13~16은 Group D
- 그룹별 세션 분리 확인 가능
Step 3. 기본 레이아웃 엔진 구현
LayoutPreset 기반으로 슬롯 위치 계산

지원 레이아웃:
- 8 Small + 1 Main
- 4x4

완료 조건:

- JSON 레이아웃을 읽어서 화면에 슬롯 배치
- 하드코딩 좌표 대신 LayoutPreset 사용
- 추후 커스텀 레이아웃 에디터를 붙일 수 있는 구조
Step 4. 슬롯 컨트롤 바 구현
StreamSlotView 구현

기능:
- 마우스 오버 시 상단 컨트롤 바 표시
- 슬롯 번호 표시
- 선택 상태 표시
- 음소거 버튼
- 새로고침 버튼
- 메뉴 버튼
- 드래그 핸들

완료 조건:

- 방송 화면 클릭은 WebView로 전달
- 상단 바 클릭 시 슬롯 선택
- 드래그 핸들만 드래그 시작점으로 사용
Step 5. 슬롯 교체 구현
SlotSwapService 구현

정책:
- 드래그한 슬롯과 드롭한 슬롯의 StreamEntry만 교체
- muted는 슬롯에 남김
- profileGroupId는 슬롯에 남김
- 교체 후 각 슬롯의 프로필 그룹에서 방송 URL 다시 로드

완료 조건:

2번 ↔ 9번 교체 시:
- 2번의 음소거 상태 유지
- 9번의 음소거 상태 유지
- 2번의 Profile Group 유지
- 9번의 Profile Group 유지
- 방송만 교체
Step 6. SOOP 탐색 패널 구현
ExplorerPanel 구현

기능:
- 별도 WebView2로 SOOP 탐색 페이지 표시
- 사용자가 SOOP 팔로우/즐겨찾기 페이지에서 방송 선택 가능
- 현재 탐색 패널 URL을 선택 슬롯에 넣기

완료 조건:

- 슬롯 선택 가능
- 탐색 패널에서 방송 페이지 열기 가능
- [선택 슬롯에 넣기] 클릭 시 선택 슬롯 URL 교체
Step 7. 프리셋 저장/복원 구현
PresetStorageService 구현

기능:
- 현재 상태 저장
- 다른 이름으로 저장
- 프리셋 불러오기
- 마지막 상태 자동 저장
- 앱 실행 시 마지막 상태 복원

완료 조건:

- 앱 종료 후 재실행 시 마지막 레이아웃/방송/음소거 상태 복원
- 프리셋을 불러오면 슬롯 위치와 방송이 고정 복원
- 임시 변경은 명시적으로 저장하기 전까지 프리셋을 덮어쓰지 않음
13. MVP 완료 기준

MVP는 다음 조건을 만족하면 완료로 본다.

1. SOOP 방송 9개 레이아웃 사용 가능
2. 16분할 레이아웃 사용 가능
3. 같은 SOOP 계정으로 로그인 유지 가능
4. 앱 재실행 시 마지막 상태 복원 가능
5. 슬롯 선택 후 SOOP 탐색 패널에서 방송 교체 가능
6. 슬롯 상단 드래그 핸들로 슬롯끼리 방송 교체 가능
7. 방송 화면 클릭은 SOOP 플레이어에 정상 전달
8. 음소거 상태는 슬롯에 유지
9. 프로필 그룹은 슬롯에 유지
10. 프리셋 저장/불러오기 가능
14. Codex에게 줄 첫 작업 지시문

아래 문장을 그대로 Codex 첫 요청으로 사용하면 됩니다.

Windows 전용 WPF + WebView2 기반 Stream Orchestra 앱의 feasibility spike를 만들어줘.

목표는 SOOP 방송을 여러 WebView2 프로필 그룹으로 나누어 동시에 재생할 수 있는지 검증하는 것이다.

우선 본 앱 전체를 만들지 말고, 4x4 그리드에 WebView2 16개를 배치하는 테스트 앱을 만들어라.

요구사항:
1. C# WPF 프로젝트를 생성한다.
2. WebView2를 사용한다.
3. 16개 슬롯을 4x4 그리드로 보여준다.
4. 슬롯 1~4는 Profile Group A, 5~8은 Group B, 9~12는 Group C, 13~16은 Group D로 분리한다.
5. 각 그룹은 독립된 WebView2 profile 또는 user data folder를 사용하도록 설계한다.
6. 상단에 URL 입력창과 “전체 로드” 버튼을 둔다.
7. 각 슬롯에도 개별 URL을 넣을 수 있게 한다.
8. 각 슬롯에 새로고침, 음소거 토글 버튼을 둔다.
9. 앱을 재실행했을 때 로그인 세션이 유지되는지 테스트할 수 있도록 프로필 데이터를 유지한다.
10. 코드는 이후 MVP 앱으로 확장할 수 있게 Models, Services, Views 구조로 분리한다.

주의:
- SOOP의 DRM, 인증, 보안 제한을 해킹하거나 우회하는 코드는 작성하지 않는다.
- 일반 브라우저 세션을 여러 개 쓰는 것과 유사하게 WebView2 프로필 분리가 가능한지만 검증한다.
- 첫 결과물은 동시 재생 가능성 검증용 spike 앱이다.
15. 개발 판단 포인트

스파이크 결과에 따라 다음처럼 결정합니다.

성공:
→ WebView2 내장형 MVP 계속 개발

부분 성공:
→ 프로필 그룹 수, UserDataFolder 방식, 슬롯 배치 최적화 실험

실패:
→ 외부 브라우저 제어 방식으로 전환
   앱은 레이아웃/프리셋/복원을 담당하고,
   실제 재생은 Chrome/Edge/Whale 창을 제어하는 구조로 변경