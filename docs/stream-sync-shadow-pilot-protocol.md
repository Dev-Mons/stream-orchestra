# Passive SOOP shadow pilot 사전등록 프로토콜

문서 버전은 `1.0`, 동결일은 2026-08-18이다. 이 문서는 첫 실제 SOOP 진단 export를 열어 보기 전에 표본 단위, 수집 순서, 분석식과 중단 기준을 고정한다. 실제 파일럿은 아직 수행하지 않았으며, 이 문서 자체는 성능이나 정확도 증거가 아니다. 변경이 필요하면 기존 내용을 덮어쓰지 않고 문서 끝의 deviation log에 시각·이유·데이터 열람 여부를 남긴다.

## 목적과 범위

1차 목적은 허용된 공개 SOOP 방송에서 privacy-safe passive telemetry가 안정적으로 만들어지는지, 그리고 PDT·playlist·response timing·player/rVFC 같은 후보 source가 얼마나 제공되는지 측정하는 것이다. 기존 legacy 동기화 제어는 그대로 유지하며 shadow estimator/controller의 명령을 dispatch하지 않는다.

다음은 이 파일럿의 목적이 아니다.

- 동일 장면의 ground truth, 촬영·캡처·인코더 지연 또는 `±N ms` 정확도 입증
- 로그인·성인 인증·DRM·접근 통제 우회
- 영상 픽셀, 화면 내용, 음성 또는 오디오 파형 분석
- candidate controller의 production 활성화 결정

## 표본과 독립 단위

용어를 다음처럼 고정한다.

- **진단 run**: 사용자가 싱크 팝업에서 수집 시작을 동의한 시점부터 수집 종료까지다.
- **session/group unit**: 한 run 안에서 동시에 관찰한 2개 이상 스트림의 고정된 group membership이다. primary 분석의 독립 표본은 tick, request, playlist, stream member 또는 source epoch가 아니라 이 unit이다.
- **source epoch**: playlist identity, discontinuity 또는 player source가 바뀐 구간이다. 한 unit 안의 반복측정이며 독립 표본 수를 늘리지 않는다.
- **broadcast-day cluster**: 목적별 HMAC으로 연계한 broadcast session과 UTC 날짜의 조합이다. 같은 cluster의 여러 unit은 서로 독립이라고 간주하지 않는다.

포함 기준은 정상 UI로 접근 가능한 공개 방송, 최소 2개 스트림, 2분 warm-up 뒤 최소 15분의 연속 관찰, 종료 가능한 진단 export다. 제외 기준은 접근 통제가 필요한 방송, 사용 권한이 불명확한 방송, 앱 crash로 final export가 없는 run, schema validation 실패, opt-in 이전 데이터다. 네트워크 장애·buffering·source switch가 발생한 run은 제거하지 않고 해당 stratum으로 남긴다.

목표 표본은 유효 session/group unit 60개다. 최소 20개 broadcast-day cluster와 12개 channel HMAC을 포함하고, 동일 channel set과 UTC 날짜 조합은 primary 분석에 최대 3개 unit만 사용한다. 초과분은 sensitivity 분석에만 쓴다. 특정 quality, CDN, runtime 또는 buffering stratum을 정상 UI에서 확보할 수 없다면 임의로 만들지 않고 `not observed`로 보고한다. 이 목표는 효과 검정력을 주장하는 수치가 아니라 capability/운영 결함을 찾기 위한 최소 pilot coverage다.

## 수집 순서와 데이터 분할

유효 unit을 export 생성 시각과 session 시작 시각 순으로 정렬한다. 첫 42개(70%)는 schema·source coverage 확인과 이후 suggestion-only calibration 후보인 development 구간, 마지막 18개(30%)는 잠긴 temporal holdout이다. 동률은 purpose-specific session HMAC의 사전식 순서로 결정한다.

holdout을 열기 전에 다음을 고정한다.

- evaluator 버전과 Git commit
- schema/model version, 필수 필드와 허용 enum
- 안정조건, epsilon, reversal window, non-inferiority margin
- strata 매핑과 missing 값 처리
- development에서 생성한 channel prior 및 estimator 파라미터

development에서 한 번이라도 나온 channel HMAC은 `seen`, 그렇지 않은 holdout channel은 `unseen`이다. 같은 session/group의 final manual delay를 그 unit의 입력으로 학습하거나 그 unit을 평가하는 leakage는 금지한다.

## 수집 필드와 개인정보 경계

허용 필드는 schema/model/app/runtime bucket, UTC·host monotonic·page monotonic 시각, 목적별 HMAC session/channel/broadcast/playlist/progress/request/observation/decision/command ID, 구조화 HLS/player/source/quality/CDN bucket, estimator·decision·command 결과와 명시적 수동 event다.

cookie, Authorization/Proxy-Authorization, password, token, signed query, full/raw URL, raw playlist/manifest/header/body, DRM key 또는 decrypted payload, 영상 픽셀, 화면 내용, 음성과 오디오 파형은 금지한다. URL은 scheme/host/path shape 같은 허용된 파생 분류와 `url-identity` 목적 HMAC만 남긴다. final JSON 직렬화 뒤 secret scanner와 sentinel regression을 다시 통과해야 한다.

event 데이터는 앱 메모리에만 bounded 보관하고 사용자가 종료 후 명시적으로 export한다. 목적별 연계 HMAC key는 opt-in 시에만 생성되어 현재 Windows 사용자 DPAPI로 보호된다. **수집본 삭제**는 메모리 수집본과 키를 삭제해 이후 연계를 회전한다. 이미 export한 파일은 앱이 추적하지 않으므로 pilot 보관 폴더에서 직접 폐기한다. 원본 export는 aggregate 검증 완료 후 30일 이내 삭제하고, 비식별 aggregate와 deviation log만 남긴다.

## 사전등록 분석

모든 비율은 먼저 unit별 비율로 요약하고, unit을 같은 가중치로 집계한다. event 수를 표본 수로 사용하지 않는다. primary point estimate와 broadcast-day cluster bootstrap 95% interval(10,000회, seed `120826`)을 함께 보고한다. 표본이 5개 미만인 stratum은 수치 비교 없이 `insufficient`로 표시한다.

Primary 결과는 다음과 같다.

1. export/schema validation 성공 unit 비율과 필수 field missing 비율
2. PDT, structured playlist identity, response timing, rVFC, quality, CDN/source bucket의 unit별 availability
3. duplicate/stale/rollback/discontinuity/source-switch와 dropped event의 unit별 비율 및 playback-hour rate
4. telemetry off/on 상태 전이 위반, unexpected shadow command dispatch, secret scanner 위반 건수

CDP hard-seek coverage gate는 runtime bucket별 correlation attempt 100개 이상, exact correlation coverage 95% 이상, ambiguous association 1% 이하, invalid association 0%, runtime schema mismatch 0건으로 고정한다. 이 기준은 request timing을 안전하게 사용할 최소 계측 gate일 뿐 실제 장면 정확도 기준이 아니다. 한 runtime이라도 실패하면 전체 hard-seek gate는 닫힌다.

Secondary 결과는 다음과 같다.

- network, PC load, seen/unseen channel, quality, CDN, normal/buffering, source, runtime별 source availability
- source epoch 수, stable observation까지의 시간, confidence abstention 사유
- 명시적으로 정렬 확인된 unit에 한해 baseline pairwise proxy error의 session-level median/p90/p95
- suggestion-only 단계가 별도로 승인된 뒤에만 suggestion 이후 residual, 수락·거절·되돌림과 manual event 수

결측은 0으로 대치하지 않는다. `not-applicable`, `not-observed`, `instrumentation-missing`, `dropped-cap`을 분리한다. warm-up 2분은 convergence 시간 외 primary source availability에서 제외한다. unit 전체를 제외하는 것은 사전 정의한 포함/제외 기준뿐이며, outlier를 제거한 결과는 sensitivity로만 병기한다.

## 진행·중단·후속 게이트

다음 중 하나라도 발생하면 즉시 수집을 중단하고 해당 export를 격리한다.

- 금지된 secret/raw content 1건 이상
- 동의 전 event 또는 자동 upload/file persistence 1건 이상
- shadow candidate가 실제 seek/rate 명령을 dispatch한 사례 1건 이상
- schema가 해석되지 않거나 session/group linkage가 불가능한 export

Passive pilot 완료 조건은 60개 유효 unit, privacy 위반 0건, opt-in/state 위반 0건, schema validation 성공 100%, primary 분석에 사용된 unit 중 dropped-cap 발생 unit 5% 이하이다. source availability에는 임의 합격선을 두지 않고 관측값과 interval을 보고한다. 실제 값이 후속 estimator/controller에 필요한 입력을 지지하지 못하면 해당 source 의존 기능은 evidence-gated 상태로 남긴다.

이 pilot만으로 active controller를 열지 않는다. 다음 단계는 별도 승인된 suggestion-only temporal holdout이며, 이후에도 proxy error와 stall/wrong-correction/resource non-inferiority margin을 holdout 열람 전에 고정해야 한다.

## 실행 체크리스트

1. Release 빌드와 전체 테스트, telemetry-off overhead baseline을 기록한다.
2. 진단 동의 화면에서 수집/비수집, 메모리 보관, key retention, export/delete를 확인한다.
3. 허용된 공개 방송만 정상 UI로 열고 2분 warm-up 후 15분 이상 passive 관찰한다.
4. 종료·export 후 schema validator와 final secret scan을 실행하고 금지 필드가 있으면 즉시 중단한다.
5. unit/cluster/strata ledger에는 raw URL이나 방송명을 쓰지 않고 HMAC/bucket만 기록한다.
6. 42번째 유효 unit 뒤 evaluator·threshold·commit을 동결한다. 60번째까지 holdout 결과를 열람하지 않는다.
7. aggregate report에 포함/제외 flow, missingness, drop, deviations와 모든 preregistered 결과를 함께 공개한다.

분석 명령은 다음과 같다. 첫 명령은 2분 warm-up과 총 17분 최소 길이, 같은 channel-set/UTC 날짜 최대 3개 primary unit, 60 unit·20 cluster·12 channel 목표를 코드로 검증한다.

```powershell
dotnet run --project src\StreamOrchestra.Tools -- sync-pilot analyze `
  --input .\pilot --output .\pilot\capability-report.json
dotnet run --project src\StreamOrchestra.Tools -- sync-pilot calibrate `
  --input .\pilot\calibration-sessions.json --output .\pilot\calibration-v1.json
dotnet run --project src\StreamOrchestra.Tools -- sync-pilot suggestion-evaluate `
  --input .\pilot\sync-bias-priors.json --output .\pilot\suggestion-holdout-v1.json
```

## Deviation log

현재 deviation 없음. 각 변경은 `UTC 시각 | 변경 전 | 변경 후 | 이유 | outcome 열람 여부 | 승인자` 형식으로 이 아래에 추가한다.
