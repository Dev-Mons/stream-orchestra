# Stream sync 보정·temporal holdout 절차

문서 버전은 `1.0`, 동결일은 2026-08-18이다. 이 절차는 actual SOOP 결과를 뜻하지 않는다. 현재 실제 독립 표본은 아직 수집되지 않았으므로 모든 production gate는 닫혀 있다.

## Estimator dataset 계약

`sync-pilot calibrate` 입력은 `SyncEstimatorCalibrationSession[]` JSON이다. session은 purpose-specific HMAC ID, 시작 UTC, 독립 session 여부와 observation 목록을 가진다. observation은 같은 HMAC observation ID에 raw·legacy offset, 외부에서 명시적으로 확정한 independent reference, source/epoch, host monotonic 시각, 세 confidence domain, strata와 fault kind를 함께 둔다. raw URL, 방송명, header/body, token은 허용하지 않으며 CLI가 분석 전에 privacy scan을 수행한다.

동일 independent session ID가 둘 이상이거나 observation ID가 중복된 session, 명시적 independent reference가 아닌 label, 비유한 값은 통째로 제외한다. 무조정 session이나 같은 session의 최종 수동값을 0 또는 학습 label로 대치하지 않는다.

```powershell
dotnet run --project src\StreamOrchestra.Tools -- sync-pilot calibrate `
  --input .\pilot\calibration-sessions.json `
  --output .\pilot\stream-sync-calibration-v1.json
```

## 잠긴 분할과 조정

유효 session을 시작 UTC와 session HMAC 순으로 정렬한다. 첫 42개만 development, 다음 18개만 temporal holdout으로 쓴다. holdout은 development-only tuning이 반환된 뒤 처음 평가되며 두 ID 집합의 교집합은 허용하지 않는다.

development에서 순차 좌표 탐색으로 다음 후보를 고른다.

- MAD gate multiplier: 4, 6, 8
- Huber delta: 100, 150, 250 ms
- Kalman measurement R scale: 80, 120, 200 ms
- offset Q: 1, 4, 16
- drift Q: 0.01, 0.04, 0.16
- absolute drift bound: 25, 50, 100 ms/s
- CUSUM allowance: 10, 25, 50 ms
- CUSUM threshold: 400, 600, 900 ms

선택 목적함수는 Kalman/Huber mean absolute error와 95% interval miscoverage penalty다. artifact는 선택값뿐 아니라 원래 `SyncTimelineEstimatorOptions` 전체를 rollback 값, evidence SHA-256, tuning trace와 함께 저장한다.

## Holdout 산출물

- 같은 observation ID에서 legacy, Kalman, Huber의 matched count와 MAE/median/p90/p95
- timeline, manual-bias, controllability confidence calibration bin
- network/PC/channel/quality/CDN/buffering/source 결합 strata별 Kalman/Huber interval coverage와 mean width
- UTC step, source/epoch switch, stale majority, outlier burst 등 fault kind의 development/holdout session coverage

42/18이 모두 채워져야 artifact status가 `ready-for-review`가 된다. 이 상태도 자동 합격이 아니며 actual guardrail 검토가 필요하다. fault replay 빈도는 capability report의 actual 분포가 생긴 뒤에만 갱신하고, synthetic 빈도를 actual인 것처럼 기록하지 않는다.

## Suggestion-only 평가

`sync-pilot suggestion-evaluate`는 privacy-safe 로컬 prior export만 읽는다. `AlignmentConfirmed`, stable final, independent session 조건을 모두 만족하는 pair label만 시간순 42/18로 분리한다. development prior를 holdout 학습에 갱신하지 않는다.

```powershell
dotnet run --project src\StreamOrchestra.Tools -- sync-pilot suggestion-evaluate `
  --input .\pilot\sync-bias-priors.json `
  --output .\pilot\sync-suggestion-holdout-v1.json
```

보고서는 `channel×quality×CDN → channel×quality → channel → 0` 중 실제 제안이 나온 hierarchy와 independent-session support/학습 age, seen/mixed/unseen channel, connected/disconnected graph component, shown/accepted/rejected/reverted 수, 수락 뒤 첫 residual 재조정량을 분리한다. 중앙 업로드와 다중 사용자 prior는 범위 밖이며 구현되어 있지 않다.
