# Stream Orchestra

여러 SOOP 방송을 한 화면에서 동시에 시청하기 위한 Windows 전용 WPF + WebView2 멀티뷰 앱입니다. 각 화면(슬롯)은 독립된 WebView2 프로필 그룹에서 동작하므로, 서로 다른 계정·세션을 분리한 채 최대 16개 방송을 격자 레이아웃으로 배치하고 제어할 수 있습니다.

> 본 프로젝트는 "SOOP 방송을 분리된 WebView2 프로필 그룹에서 동시에 재생할 수 있는가"를 검증하기 위한 타당성 스파이크(feasibility spike)에서 출발했습니다. 검증용 CLI 도구(`StreamOrchestra.Tools`)는 [타당성 검증 CLI](#타당성-검증-cli-streamorchestratools) 항목으로 남아 있으며, 본 문서는 현재 데스크톱 앱의 실제 기능을 기준으로 작성되었습니다.

## 설치 (포터블)

1. 최신 [GitHub Release](https://github.com/Dev-Mons/stream-orchestra/releases)에서 `StreamOrchestra-win-Portable.zip`을 내려받습니다.
2. **쓰기 가능한** 폴더(예: `D:\Apps\StreamOrchestra`, `%LocalAppData%\StreamOrchestra`)에 압축을 풉니다. 자동 업데이트가 설치 폴더에 쓰기 권한을 필요로 하므로 `C:\Program Files\` 아래에는 풀지 마세요.
3. `StreamOrchestra.App.exe`를 실행합니다. 바이너리에 코드 서명이 없어 첫 실행 시 Windows SmartScreen 경고가 뜰 수 있습니다. **추가 정보 → 실행**을 선택하세요.

### 요구 사항

- [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) — 없으면 앱이 실행되지 않고 Windows가 프레임워크 누락 안내창을 표시합니다.
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — 최신 Windows 11에는 기본 설치되어 있습니다. 구형 시스템에서는 Evergreen Runtime을 설치하세요.

### 자동 업데이트

앱은 시작 몇 초 후 GitHub Releases에서 새 버전을 한 번 확인합니다. 새 버전이 있으면 지금 설치 / 나중에 / 이 버전 건너뛰기를 묻는 창이 뜹니다. 메뉴 **설정 → 업데이트 확인**으로 언제든 직접 검사할 수도 있습니다. 업데이트에는 설치 폴더 쓰기 권한이 필요합니다.

## 주요 기능

### 멀티뷰 격자와 레이아웃

- 최대 16개 슬롯에 각각 별도의 WebView2 인스턴스를 띄워 SOOP 방송을 동시에 재생합니다.
- 레이아웃은 `data/layouts.json`에서 불러오며, 1·2·3 화면, 2x2, 5분할(2x2 + 하단 와이드) 등 다양한 슬롯 수 구성을 제공합니다.
- 메뉴 **설정 → 레이아웃**의 레이아웃 편집기에서 격자 구성을 직접 추가·수정할 수 있습니다.
- 현재 보이는 슬롯만 재생되며, 레이아웃에서 빠진 슬롯은 즉시 `about:blank`로 중지되어 숨은 화면이 의도치 않게 재생되지 않습니다.

### 프로필 그룹 분리

- 슬롯 1–3은 그룹 **A**, 4–6은 **B**, 7–9는 **C**, 10–12는 **D**, 13–16은 **E**를 사용합니다. 탐색 패널은 별도의 Explorer 그룹을 씁니다.
- 각 그룹은 `%LOCALAPPDATA%\StreamOrchestra\Profiles` 아래의 독립된 영속 WebView2 사용자 데이터 폴더를 사용하므로, 그룹별 로그인 세션이 분리·유지됩니다. 소셜 로그인 팝업이 완료되면 SOOP 공용 도메인의 세션 쿠키만 30일 만료 쿠키로 전환하며, 네이버 쿠키는 변경하지 않습니다. SOOP에서 로그아웃하면 해당 로그인 쿠키도 함께 제거됩니다.

### 마우스·키보드 조작

- **마우스 휠** — 슬롯 위에서 휠을 굴리면 해당 화면 볼륨이 10%씩 오르내립니다. **Ctrl+휠**은 5%씩 미세 조절합니다. 가운데에 `볼륨 N%` 오버레이가 잠시 표시되며, 볼륨 값은 저장되어 다음 실행에 복원됩니다.
- **Ctrl(누르고 있는 동안)** — 슬롯 위에 🗑 제거 버튼이 나타나며, 클릭하면 화면을 빼고 슬롯이 하나 적은 레이아웃으로 전환합니다.
- **Shift(누르고 있는 동안)** — 슬롯에 ⇄ 교체 오버레이가 떠서, 다른 슬롯으로 드래그해 위치를 맞바꿀 수 있습니다. 교체 시 음소거 상태와 프로필 그룹은 물리적 슬롯에 그대로 남습니다.
- **Alt(누르고 있는 동안)** — 현재 화면 수와 같은 슬롯 수의 레이아웃 카드가 표시되며, 카드를 고르면 채널을 순서대로 유지한 채 다른 레이아웃으로 전환합니다.
- **Tab** — 왼쪽 탐색(사이드바) 패널을 열고 닫습니다(누를 때마다 토글).
- **M** — 모든 화면의 볼륨을 한 번에 0%로 낮춥니다(최상단 우측 **볼륨** 버튼과 동일한 동작).
- 위 다섯 동작(제거·교체·전환·사이드바·전체 볼륨 0%)의 키 매핑은 **설정 → 단축키**에서 바꿀 수 있습니다. 각 동작의 키 버튼을 누르면 입력 대기 상태가 되고, 그 자리에서 **원하는 키(ESC 제외)를 누르면** 즉시 변경·저장됩니다(ESC로 취소). 기본값은 Ctrl·Shift·Alt·Tab·M이지만 문자·기능 키 등 임의 키를 쓸 수 있습니다. 다섯 동작은 서로 다른 키를 쓰며, 이미 쓰이는 키를 누르면 두 동작의 키가 자동으로 맞바뀝니다. 변경은 슬롯 안내 문구에도 반영됩니다.
- 슬롯 재생 영역을 클릭하면 해당 슬롯이 선택됩니다.

### 탐색 패널 (왼쪽)

- 상단 URL 입력창과 Go / Back / Refresh 버튼으로 SOOP을 탐색합니다.
- 현재 탐색 중인 URL 또는 SOOP 링크/카드를 보이는 재생 영역으로 **드래그**하면 해당 슬롯에 그 URL이 로드됩니다.
- 최상단 툴바의 토글 버튼이나 단축키(기본 **Tab**)로 패널을 열고 닫아 영상 영역을 넓힐 수 있으며, 이 설정은 다음 실행에 복원됩니다. 토글 단축키는 **설정 → 단축키**에서 바꿀 수 있습니다.

### 화질·볼륨 제어

- 메뉴 **설정 → 화질**에서 1440p / 1080p / 720p / 540p / 360p를 선택해 재생 중인 슬롯에 적용합니다.
- 최상단 우측 **볼륨** 버튼(또는 단축키 **M**)은 모든 화면의 볼륨을 한 번에 0%로 낮춥니다.

### SOOP 방송 주소 동시 녹화

- 최상단 **● 녹화** 버튼에서 최대 5개 SOOP 방송을 동시에 녹화할 수 있습니다. 각 방송은 독립된 녹화 작업으로 실행되므로 하나를 중지하거나 방송 하나가 종료되어도 나머지 녹화는 계속됩니다.
- 녹화 관리 화면의 왼쪽 목록에서 진행·연결·완료 상태와 경과 시간을 한눈에 확인하고, 선택한 방송의 화질·파일 크기·저장 위치·최근 활동을 오른쪽에서 확인할 수 있습니다. 방송별 썸네일과 제목은 SOOP의 실제 방송 메타데이터를 사용하며, 불러오지 못한 경우에만 공식 SOOP 심볼이 표시됩니다.
- 저장 위치는 녹화 관리 화면의 **저장 위치 변경**에서 한 번만 정합니다. 이후 추가하는 모든 방송과 기존 목록의 재녹화 파일은 이 공통 폴더에 저장됩니다.
- **방송 추가** 흐름에서는 SOOP 방송 주소와 화질만 지정해 현재 시점부터 원본 스트림을 파일로 저장합니다. 화면 캡처 방식이 아니므로 해당 방송을 슬롯에 띄워 둘 필요가 없습니다. 구독자 전용 방송의 계정 입력은 필요한 경우에만 펼쳐지며 비밀번호는 저장되지 않습니다.
- 현재 선택한 슬롯이 SOOP 방송이면 그 주소가 방송 추가 창에 자동으로 입력됩니다. 녹화 관리 창은 별도 창으로 열려 멀티뷰를 계속 조작할 수 있습니다.
- 최초 사용 시 앱이 [yt-dlp 공식 릴리스](https://github.com/yt-dlp/yt-dlp/releases)와 [yt-dlp용 FFmpeg 공식 빌드](https://github.com/yt-dlp/FFmpeg-Builds/releases)에서 Windows 실행 파일을 내려받아 각각의 SHA-256 체크섬을 확인한 뒤 `%LOCALAPPDATA%\StreamOrchestra\Tools`에 설치합니다.
- 녹화 중에는 경과 시간, 저장 중인 파일 크기와 사람이 읽기 쉬운 활동 내역이 표시됩니다. **녹화 중지**를 누르거나 방송이 끝나면 같은 버튼이 **녹화 시작**으로 돌아와 다시 녹화할 수 있습니다. **방송 제거**는 녹화가 멈춘 항목을 목록에서만 지우며 이미 저장된 동영상 파일은 삭제하지 않습니다.
- 추가한 방송 목록과 공통 저장 위치는 `%LOCALAPPDATA%\StreamOrchestra\Data\recordings.json`에 자동 저장되어 앱을 다시 열어도 유지됩니다. 안전을 위해 재실행 시 녹화가 자동 시작되지는 않으며 사용자가 **녹화 시작**을 눌러 재개합니다. 비밀번호는 저장되지 않습니다.
- 공개적으로 접근 가능한 SOOP/기존 AfreecaTV 주소를 지원합니다. 로그인 전용·성인 인증·DRM 등 플랫폼의 접근 통제를 우회하지 않으며, 녹화 권한이 있는 방송에만 사용해야 합니다.

### SOOP 방송 재생 동기화

- 최상단 **싱크** 버튼을 누르면 현재 로드된 SOOP 방송을 단일 동기화 그룹에 추가하거나 제거할 수 있습니다. 그룹은 2개부터 최대 16개 슬롯까지 지원합니다.
- 동기화는 구조화한 HLS playlist identity·sequence·discontinuity와 timezone이 명시된 `PROGRAM-DATE-TIME`을 **플랫폼 HLS 시각**으로 사용합니다. HTTP `Date`와 검증되지 않은 vendor timestamp를 playlist tail 또는 캡처 시각으로 승격하지 않으며, 안전한 시간축이 없으면 라이브 엣지 거리 추정으로 강등합니다.
- 플레이어의 전체 buffered/seekable 연속 구간, `requestVideoFrameCallback` 표본, 진행 상태와 명령 결과를 확인합니다. 진단 opt-in 동안에만 CDP `Network.*` lifecycle을 passive하게 연결하고, 불명확·스키마 불일치 시 WebView response-event reduced-confidence 경로로 즉시 내립니다. runtime별 100개 이상, correlation coverage 95% 이상, ambiguous 1% 이하, invalid 0%의 사전등록 gate가 실제 표본에서 통과하기 전에는 CDP 결과가 hard seek를 열지 않습니다.
- 방송별로 0.1초씩 앞당기거나 늦춰 송출 장비·인코더 차이를 수동 보정할 수 있습니다. 수동 보정 범위는 ±60초이며, 기존 프리셋의 부호와 단위는 유지됩니다.
- 안전 딜레이는 버퍼 상태에 따라 자동 조절됩니다. 사용자는 팝업에서 자동 조절이 내려가지 않을 최솟값을 1.5~15초로 지정할 수 있습니다.
- 최초 정렬 뒤에는 정상 재생을 계속 모니터링하되 작은 오차에는 재탐색하지 않습니다. source/epoch/stale/range/command 검증을 통과하지 못하면 seek하지 않고 정상 속도 또는 bounded rate로 안전하게 fallback합니다.
- 그룹 구성, 최소 안전 딜레이와 수동 보정값은 프리셋에 저장되지만, 앱이나 프리셋을 다시 불러온 뒤에는 사용자가 **시작**을 눌러야 합니다.
- 한 방송이 지속적으로 버퍼링되면 그룹을 잠시 멈추고 안전 딜레이를 늘려 재정렬합니다. 제한 시간 안에 회복하지 못한 방송은 그룹 구성에 남겨 둔 채 일시적으로 제외됩니다.
- robust offset/drift estimator와 공통 playable-interval controller는 기존 제어와 나란히 계산되는 **shadow 후보**이며 자동 제어에 사용되지 않습니다. 실제 SOOP passive 표본의 calibration·guardrail 검증을 통과하기 전에는 활성화하지 않습니다.
- **현재 수동 정렬 확인**을 누른 독립 방송 세션만 pairwise 로컬 prior 학습에 사용합니다. 과거 경향은 support와 함께 제안만 표시되며 자동 적용되지 않습니다. 수락·거절·되돌리기가 분리되고, 기록은 Windows 사용자 DPAPI로 암호화되어 `%LOCALAPPDATA%\StreamOrchestra\Data`에 저장됩니다. 팝업에서 해시 처리된 기록을 내보내거나 전부 삭제할 수 있습니다.
- 이 기능은 HLS/CDN 패키징 시간축과 브라우저 플레이어 위치를 정렬하고 과거 수동 경향을 제안합니다. 외부 공통 timecode나 영상·음성 내용 비교 없이 방송별 촬영·캡처·인코더 지연을 직접 측정할 수 없으므로 동일 장면의 완전한 동기화나 `±N ms` 정확도를 보장하지 않습니다.
- telemetry는 schema/model version을 가진 privacy-safe DTO와 bounded recorder를 제공하며 기본은 꺼져 있습니다. 싱크 팝업의 **진단 수집**에서 수집 항목과 보관 정책을 확인하고 매 세션마다 명시적으로 동의해야 시작됩니다. 동기화 제어 방식은 바뀌지 않습니다.
- 수집본은 파일럿 세션에서 카테고리별 최대 8,192개 이벤트를 앱 메모리에만 보관합니다. player 정기 표본은 5초 간격으로 제한하되 waiting/stalled/error/seeking/seeked/ratechange는 즉시 기록합니다. 세션을 종료한 뒤 사용자가 고른 위치로 privacy-safe JSON을 명시적으로 내보낼 수 있고, 메모리 수집본은 즉시 삭제하거나 앱 종료 시 폐기할 수 있습니다. 같은 목적의 식별자를 세션 간 연계하는 HMAC key만 Windows 사용자 DPAPI로 보호되며 **수집본 삭제**로 함께 회전할 수 있습니다. 자동 업로드와 자동 파일 저장은 없습니다.
- cookie·authorization·token·signed query·원본 manifest/URL/header/body·영상 픽셀·음성/오디오 파형은 수집하거나 저장하지 않습니다. 이미 내보낸 파일은 앱이 추적하지 않으므로 사용자가 선택한 위치에서 직접 관리해야 합니다. 개발·운영 검증 절차는 [동기화 검증 가이드](docs/stream-sync-validation.md), [Passive shadow pilot 사전등록 프로토콜](docs/stream-sync-shadow-pilot-protocol.md), [보정·holdout 절차](docs/stream-sync-calibration.md), [closed-loop 운영 절차](docs/stream-sync-closed-loop-operations.md), [현재 go/no-go 기록](docs/stream-sync-pilot-go-no-go.md), [telemetry-off overhead baseline](docs/stream-sync-telemetry-overhead-baseline.md)을 참고하세요.
- 설치된 WebView2의 CDP schema만 검증할 때는 실제 방송이나 사용자 프로필 대신 임시 프로필과 loopback 응답을 쓰는 `sync-pilot runtime-probe --output <evidence.json>`을 실행합니다. probe는 원본 요청 URL을 산출물에 저장하지 않고 종료 전에 `Network.disable`을 검증합니다.

### 프리셋과 세션 저장

- 메뉴 **설정 → 프리셋**에서 프리셋 불러오기 / 현재 상태 저장 / 다른 이름으로 저장이 가능합니다.
- 슬롯 채널 이름과 URL은 프리셋에 저장되며, 슬롯 교체·복원 시에도 따라갑니다. 저장된 URL은 WebView의 이동·리다이렉트·소스 변경을 따라 최신 값으로 갱신됩니다.
- 채널·레이아웃·볼륨 등 현재 세션 상태는 마지막 변경 후 3초가 지나면 한 번 자동 저장되어, 반복 조작 중 불필요한 디스크 쓰기를 줄입니다.
- 프리셋·마지막 세션은 `%LOCALAPPDATA%\StreamOrchestra\Data` 아래에 JSON으로 저장되며, 같은 폴더의 임시 파일에 먼저 쓴 뒤 원본을 교체하는 방식으로 안전하게 기록합니다.
- 저장된 JSON이 손상된 경우 `*.corrupt.*`로 격리한 뒤 비어 있는 기본 상태로 계속 실행되어, 시작 단계에서 실패하지 않습니다.
- 불러온 워크스페이스와 복원된 마지막 세션은 사용 전에 정리됩니다. 잘못된 슬롯은 무시하고, 누락 슬롯은 `about:blank`, 중복 슬롯은 마지막 값을 쓰며, URL을 정규화하고, 프로필 그룹은 슬롯 번호에 고정되며, 해석된 레이아웃 밖 슬롯은 WebView 로드 전에 비웁니다.

## 빌드와 실행

```powershell
dotnet run --project src\StreamOrchestra.App
```

솔루션은 다음 세 프로젝트로 구성됩니다.

- `src/StreamOrchestra.App` — WPF + WebView2 데스크톱 앱
- `src/StreamOrchestra.Tools` — 타당성 검증용 CLI 도구
- `src/StreamOrchestra.Tests` — 단위 테스트

## 릴리스

태그 기반 릴리스 파이프라인입니다. 기본 브랜치에 `vX.Y.Z` 태그를 푸시하면 `.github/workflows/release.yml` 워크플로가 실행되어 테스트를 돌리고, 프레임워크 종속 빌드를 게시한 뒤 [Velopack](https://github.com/velopack/velopack)으로 패키징해, 포터블 zip과 델타 패키지를 해당 GitHub Release에 업로드합니다.

동일한 단계를 로컬에서 실행하려면(`GITHUB_TOKEN`이 없으면 업로드 생략):

```powershell
pwsh scripts/release.ps1 -Version 0.1.0 -SkipUpload
```

## 타당성 검증 CLI (`StreamOrchestra.Tools`)

분리된 WebView2 프로필 그룹에서 동시 재생이 가능한지 수동 검증(Phase 0)하기 위한 CLI입니다. 검증 증거는 `%LOCALAPPDATA%\StreamOrchestra\Data\feasibility-results.json`에 기록되며, 검증 절차·게이트 상태·다음 권장 작업을 출력합니다.

```powershell
dotnet run --project src\StreamOrchestra.Tools -- <command>
```

주요 명령:

| 명령 | 설명 |
| --- | --- |
| `status` | 현재 권장 사항, 다음 작업, 계획 게이트 감사 요약, Phase 0 성공 게이트 상태 출력 |
| `preflight` | 데이터/결과 경로, A–E 프로필 폴더, WebView2 런타임 가용성, 3/8/9/12/16 재생 레이아웃 점검 |
| `checklist` | 순서가 정해진 수동 SOOP 검증 흐름과 현재 증거 상태, 남은 게이트 출력 |
| `audit` | 남은 Phase 0 게이트와 누락 증거에 대한 `record` 예시 출력 (`--output`으로 저장) |
| `verify` | 모든 Phase 0 게이트 통과 시에만 종료 코드 `0`, 아니면 미완 게이트 상세 출력 |
| `record` | WPF 앱 없이 수동 검증 결과 기록 (`--dry-run`으로 저장 없이 미리보기) |
| `history` | 저장된 검증 결과와 결정 스냅샷 목록 |
| `scenarios` | 명명된 재생/단독 그룹 시나리오 목록 |
| `report` | 진단 JSON 리포트 출력 |
| `handoff` | preflight·checklist·audit·verification·history·진단 리포트·정규화 결과·매니페스트를 한 폴더에 묶어 핸드오프 번들 생성 |
| `validate-handoff` | 핸드오프 번들의 무결성(매니페스트·해시·경로·아티팩트 일관성) 검증 |
| `browsers` | 설치/미설치된 외부 브라우저 폴백 후보 출력 |
| `fallback` | 마지막 세션 슬롯에서 검토 가능한 외부 브라우저 폴백 PowerShell 스크립트 생성 |
| `sync-telemetry-overhead` | 기본 비활성 telemetry guard의 wall/CPU/할당/메모리 overhead를 반복 측정하고 선택적으로 JSON 저장 |
| `sync-pilot analyze` | 하나 이상의 privacy-safe telemetry export를 독립 session/group 단위로 검증·집계해 source/runtime capability JSON 생성 |
| `sync-pilot calibrate` | 명시적 독립 reference가 있는 시간순 42/18 session dataset에서 estimator를 development-only 조정하고 versioned holdout artifact 생성 |
| `sync-pilot suggestion-evaluate` | privacy-safe 로컬 prior export를 시간순으로 분리해 hierarchy support, seen/unseen, graph component, suggestion lifecycle과 residual을 평가 |

대부분의 명령은 `--data-folder <path>`로 기본이 아닌 데이터 폴더를 검사할 수 있고, `--output <path>`로 결과 텍스트를 핸드오프 아티팩트로 저장할 수 있습니다.

`record` 예시:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- record `
  --count 9 --outcome success --account --account-label main_soop `
  --profile-groups A,B,C --restart --resources `
  --cpu-percent 45 --gpu-percent 60 --memory-mb 12000 `
  --scenario groups_a_b_c_9_slot_threshold `
  --scenario-name "Groups A/B/C, 9-slot success threshold" --notes "manual SOOP test"
```

- `success`는 9/12/16 재생 시나리오와 `--account`, `--profile-groups`, `--restart`, `--resources`, `--cpu-percent`, `--gpu-percent`, `--memory-mb`를 모두 요구합니다. 그 외에는 `partial` 또는 `failure`로 기록하세요.
- `--account` 증거(계정 라벨·재시작)는 검증된 프로필 그룹과 라벨이 함께 있을 때만 저장됩니다. A–E 전체 검증에는 동일한 비민감 계정 라벨 하나가 공유되어야 합니다.
- `failure` 기록에는 재시작·리소스 OK 증거를 포함할 수 없으며, 정규화 시 손으로 편집된 모순 플래그는 정리됩니다.
- `--scenario`를 생략하면 `--count`(3/8/9/12/16)로 WPF 앱과 동일한 재생 시나리오 이름을 유도합니다. 단독 프로필 그룹 증거는 `--group A`–`E`로 기록합니다.

> 이 검증은 DRM·인증·플랫폼 보안 통제를 우회하지 않습니다. 일반적인 다중 브라우저 방식 시청을 분리된 WebView2 사용자 데이터 폴더 안에서 재현할 뿐입니다.

### 외부 브라우저 폴백 (선택)

Chromium 호환 외부 브라우저를 `%LOCALAPPDATA%\StreamOrchestra\Data\external-browsers.json`에 추가할 수 있습니다.

```json
[
  {
    "id": "portable_chrome",
    "name": "Portable Chrome",
    "candidatePaths": ["D:\\Browsers\\PortableChrome\\chrome.exe"]
  }
]
```

## 수동 검증 체크리스트

1. 앱을 실행합니다.
2. 그룹 A에서 SOOP에 로그인합니다.
3. 앱을 닫았다 다시 열어 그룹 A 세션이 유지되는지 확인합니다.
4. 그룹 B·C·D에 대해 반복합니다.
5. 4·8·9·12·16개 동시 재생 수를 테스트합니다.
6. 계정 라벨, CPU·GPU·메모리 사용량, SOOP이 9개 이상 동시 재생을 허용하는지 기록합니다.

별도의 수기 문서를 유지하는 대신 `preflight`·`checklist`·`audit`·`verify`·`handoff` 명령으로 저장된 증거에서 현재 검증 자료를 생성하세요.
