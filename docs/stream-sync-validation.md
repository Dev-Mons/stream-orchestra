# 스트림 동기화 검증·활성화 가이드

이 문서는 `stream-sync-improvement-research.md`의 권고를 구현한 현재 코드가 무엇을 관측하고, 무엇을 제어하며, 실제 SOOP 표본 전에는 무엇을 주장하지 않는지 고정한다. 목표는 “완벽한 장면 동기화”가 아니라 안전한 계측과 재현 가능한 단계적 검증이다.

## 현재 기본 상태

| 기능 | 기본 상태 | 제어 영향 |
| --- | --- | --- |
| 구조화 HLS parser, playlist/rendition identity, progress/epoch | 활성 | timezone 명시 PDT만 플랫폼 mapping 후보로 제공 |
| full media ranges, rVFC/fallback, player event와 command verification | 활성 | invalid range와 확인 실패 명령 차단 |
| WebResourceResponse timing | reduced-confidence | request 시작/CDP correlation을 꾸며내지 않음 |
| CDP `Network.*` correlation | 진단 opt-in 전용/evidence-gated | 수집 중에만 passive lifecycle을 연결하며 mismatch·ambiguous는 reduced-confidence로 복귀 |
| hard seek | confidence-gated/현재 잠김 | CDP runtime별 coverage gate와 calibration/holdout 전에는 실행되지 않음 |
| MAD + Kalman/Huber estimator | shadow | 기존 controller 입력을 바꾸지 않음 |
| mapped playable-interval controller | shadow-only | candidate decision만 기록하고 명령을 dispatch하지 않음 |
| 로컬 channel prior | suggestion-only | 명시적 수락 전 delay를 변경하지 않음 |
| sync telemetry recorder | 기본 비활성 | 싱크 팝업에서 매 세션 명시적 동의 후 시작하며 기존 제어에는 영향 없음 |
| closed-loop candidate | disabled | 별도 feature flag가 앱 runtime에 연결되지 않았고 suggestion-only rollback이 기본 |

`OptInPreview`도 후보를 보여 주는 모드일 뿐 production 명령을 실행하는 플래그가 아니다. estimator Q/R, MAD/Huber gate, confidence 및 hard-seek threshold는 synthetic test용 시작값이며 실제 데이터로 calibration되기 전 production 값이 아니다.

## 진단 세션 opt-in과 보관

- 앱 시작과 동기화 시작만으로 telemetry가 켜지지 않는다. 사용자가 싱크 팝업의 **수집 시작**을 누르고 수집/비수집 항목, 메모리 보관, 내보내기와 삭제 정책을 확인한 뒤 동의해야 이번 세션만 활성화된다.
- 파일럿 세션 recorder는 카테고리마다 최대 8,192개 이벤트를 보관하며 한도를 넘으면 오래된 이벤트부터 제거하고 dropped count를 남긴다. player 정기 표본은 5초 간격이며 상태 event는 즉시 기록한다. 종료한 세션 하나만 앱 메모리에 유지한다.
- 자동 업로드와 자동 디스크 저장은 없다. 종료한 세션은 사용자가 **수집본 내보내기**로 선택한 경로에 privacy-safe JSON을 만들 때만 파일이 된다.
- **수집본 삭제**, 다음 진단 세션 시작 또는 앱 종료는 메모리 수집본을 폐기한다. 이미 내보낸 파일은 앱이 추적하거나 원격 삭제하지 않으며 사용자가 선택한 위치에서 직접 관리한다.
- 동의 창은 해시 처리된 session/channel/playlist/request/frame 식별자, passive CDP request lifecycle bucket, HLS/player 상태·시각, 추정/결정/명령 결과와 수동 이벤트를 수집 가능 항목으로 알린다. cookie, Authorization, token, signed query, 원본 URL/manifest/header/body, 영상 픽셀, 음성·오디오 파형은 비수집 항목으로 고정한다. CDP raw URL은 bounded correlation 메모리에서만 잠시 쓰고 opt-in 종료·navigation reset에 지운다.
- 같은 목적의 식별자를 세션 간 연계하는 256-bit HMAC key는 사용자가 처음 opt-in할 때만 생성되어 Windows 현재 사용자 DPAPI로 보호된다. **수집본 삭제**는 종료 수집본이 없어도 이 key를 삭제하며 다음 opt-in에서 새 key를 만든다.

## 시간축과 안전 경계

- UTC, host monotonic, page `performance.now()`는 서로 다른 clock domain으로 유지한다. freshness와 drift는 host monotonic으로 계산하며 UTC 역행은 estimator epoch reset으로 간주하지 않는다.
- PDT는 플랫폼 packaging clock anchor다. 캡처·촬영 시각이나 동일 장면 정답이 아니다.
- HTTP `Date`는 request/headers/body timing 및 cache metadata와 분리되며 playlist tail로 승격되지 않는다.
- 검증되지 않은 private vendor tag는 단위가 확인될 때까지 PTS가 아니다. 33-bit rollover helper는 provenance와 timescale이 검증된 container timestamp에만 사용한다.
- seek는 explicit buffered∩seekable contiguous range, stable epoch, 독립 progress evidence, fresh rVFC 표본, command apply/seeked/follow-up position 검증이 모두 있어야 한다.
- 영상 픽셀, 화면 내용, 음성, 파형을 분석하지 않으며 로그인·DRM·접근 통제를 우회하지 않는다.

## Shadow 비교와 평가 단위

`SyncTelemetryRecorder`는 같은 observation/tick에 active baseline과 shadow candidate를 join할 수 있도록 observation/decision ID를 HMAC 처리해 기록한다. raw offset, filtered offset/drift/covariance interval, legacy/candidate decision, issued/applied/verified action을 분리한다. 동일 playlist에서 500ms마다 계산된 projection은 독립 evidence가 아니다.

평가는 tick 수가 아니라 독립 방송 session/group 단위다. `SyncShadowEvaluator`는 각 session에서 pairwise proxy error를 먼저 요약한 후 session distribution의 median/p90/p95를 계산한다.

```text
AE(g,i,j) = |[(predictedDelay_i - predictedDelay_j)
             - (acceptedDelay_i - acceptedDelay_j)]|
```

같은 session의 final delay를 학습하고 그 session을 평가하는 leakage는 금지한다. 무조정 session을 0 label로 대치하지 않는다. “수동 보정 후 오차=0”도 지표로 쓰지 않고, 다음 독립 session 또는 suggestion 이후 residual로 평가한다.

현재 schema와 evaluator로 산출 가능한 항목은 다음과 같다.

- 수동 전 baseline 및 제안 후 pairwise proxy error의 session-level median/p90/p95
- 사전 정의 안정조건을 입력으로 한 초기 수렴 시간
- verified hard seek/active playback hour와 failed seek 수
- 평가 가능한 correction 중 proxy error 악화·반대 방향 되돌림 비율
- session당 수동 event 수와 residual 총 절댓값
- timeline, manual-bias, controllability를 분리한 confidence reliability/interval coverage/width
- network, PC load, seen/unseen channel, quality, CDN, normal/buffering, source별 strata

estimator tuning은 `sync-pilot calibrate`가 시간순 첫 42개 development session만 사용해 MAD/Huber, Kalman Q/R, drift bound와 CUSUM을 조정한 뒤 마지막 18개 holdout을 한 번 평가한다. 같은 observation ID에서 legacy/Kalman/Huber를 join하며 timeline/manual-bias/controllability confidence와 interval coverage/width를 strata별로 남긴다. 원본 시작값은 artifact의 rollback 값으로 함께 보존한다. 상세 입력 계약은 [보정·holdout 절차](stream-sync-calibration.md)를 따른다.

closed-loop 사전등록 값은 파일럿 전에 `sync-closed-loop-v1`으로 동결했다. epsilon 250 ms, 안정 독립 progress 3개, proxy error non-inferiority +50 ms, wrong correction 2%, stall 증가 0.5%p, CPU +3%p, memory +100 MB, coverage 95%가 경계다. 이는 합격 결과가 아니라 중단 기준이며 현재 actual pilot 미완으로 실험은 disabled다.

## 재생·fault 검증

개발용 deterministic suite는 다음을 포함한다.

- master/video/audio, duplicate/stale/rollback/discontinuity, PDT timezone/precision, GAP/MAP/BYTERANGE, LL-HLS syntax fixture
- outlier, drift, stale-majority, UTC step, explicit source/epoch reset, CUSUM diagnostic replay
- contiguous interval gap/no-intersection/source switch/buffering recovery sequence
- repeated progress key, wrong command ID, applied-only, timeout, failed seek/resume fault
- pairwise gauge, disconnected component, hierarchy backoff, independent-session support, suggestion accept/reject/revert
- secret-bearing URL/header/body 및 nested diagnostic serialization redaction

로컬 검증 명령은 저장소의 SDK/restore 환경이 준비된 경우 다음과 같다.

```powershell
dotnet build StreamOrchestra.slnx -c Release --nologo
dotnet test StreamOrchestra.slnx -c Release --nologo
```

기본 비활성 guard의 CPU/메모리/할당 측정 명령과 2026-08-18 Release 기준값은 [telemetry-off overhead baseline](stream-sync-telemetry-overhead-baseline.md)에 기록한다. 실제 SOOP 수집 전에 고정한 표본 단위·포함/제외 기준·시간순 holdout·중단 기준은 [Passive shadow pilot 사전등록 프로토콜](stream-sync-shadow-pilot-protocol.md)을 따른다. feature flag, rollback과 데이터 삭제 runbook은 [closed-loop 운영 절차](stream-sync-closed-loop-operations.md)에 있다.

synthetic interval coverage는 수학·상태 구현만 검증하며 실제 SOOP 장면 정확도를 입증하지 않는다. offline replay는 seek/rate가 이후 player 상태를 바꾸는 폐루프 counterfactual도 완전히 재현하지 못한다.

## 개인정보와 로컬 prior

- diagnostic persistence에는 cookie, Authorization/Proxy-Authorization, token, password, signed query, full URL, raw playlist/header/body가 들어갈 수 없다. final serialization boundary도 재검사한다.
- channel, broadcast session, playlist, progress, request, observation, decision, command와 suggestion ID는 purpose string을 domain separator로 둔 HMAC/opaque ID로 저장한다. 같은 원문이라도 목적이 다르면 다른 ID가 된다.
- 원본 manifest, video frame, audio sample, DRM key/decrypted payload를 보존하지 않는다.
- 로컬 bias document와 telemetry identity key는 Windows 현재 사용자 DPAPI로 각각 암호화한다. event는 bounded memory cap을 적용하고 identity key는 사용자가 삭제할 때까지 세션 간 같은 목적 연계에만 유지한다.
- export는 사용자의 명시적 동작으로만 생성되며 해시 context와 수치만 포함한다. telemetry 삭제는 메모리 snapshot과 identity key를 제거해 이후 ID를 회전하고, prior 삭제는 별도 bias document를 제거한다.
- 중앙 업로드, 다중 사용자 집계, 자동 prior 적용은 구현하거나 활성화하지 않았다.

## 아직 열리지 않은 evidence gate

실제 scrubbed SOOP 표본이 없으므로 다음은 미확인이다.

- PDT/LL-HLS/vendor tag/cache header 제공률과 단위·semantic contract
- audio/video/rendition 및 CDP request/frame association의 runtime 호환성
- CDN/path bias, server clock bias, one-way latency와 packaging lag의 분리 가능성
- rVFC 지원률, estimator Q/R·gate·confidence calibration
- channel×quality×CDN label support, seen/unseen temporal holdout 성능
- candidate controller의 proxy error 개선과 stall/wrong-correction/resource non-inferiority

따라서 정확도 향상, 동일 장면 자동 식별, `±N ms`, LL-HLS 지원, CDN clock 동기화 같은 표현을 제품 주장으로 사용하지 않는다.

## 다음 운영 실험 3개

1. **Passive SOOP shadow pilot**: 허용된 공개 방송에서 원본 URL/query/body를 저장하지 않고 source 제공률, request/headers/body timing, duplicate/stale/source-switch, rVFC/CDP capability를 session 단위로 측정한다. 제품 제어는 legacy 그대로 둔다.
2. **Suggestion-only temporal holdout**: 명시적으로 정렬 확인된 독립 session만 학습하고 이후 session을 시간순 holdout한다. seen/unseen channel을 분리해 pairwise error, residual 조정량, 수락·거절·되돌림을 측정한다.
3. **고신뢰 closed-loop 제한 실험**: 1·2의 calibration과 사전등록 guardrail을 통과한 뒤에만 cluster/block randomized opt-in으로 시작한다. verified seek/hour, wrong correction, stall, CPU/GPU/memory를 baseline과 비교하고 악화 시 즉시 suggestion-only로 되돌린다.
### 배포 WebView2 CDP runtime schema probe

실제 SOOP 진단 수집을 켜지 않고 설치된 runtime의 `Browser.getVersion`과 `Network.requestWillBeSent`, `responseReceived`, `loadingFinished`, `loadingFailed` schema를 검증할 수 있다. 명령은 임시 WebView2 profile과 loopback HTTP 응답만 사용하고 raw URL을 결과에 저장하지 않는다.

```powershell
dotnet run --project src\StreamOrchestra.Tools -- sync-pilot runtime-probe `
  --output .\docs\evidence\stream-sync-webview2-runtime.json
```

현재 배포 runtime `151.0.4129.86`/CDP `1.3` probe는 네 lifecycle event, frame/navigation association, `Network.disable`을 모두 통과했다. 이 runtime은 loopback 응답에서 `loadingFinished` monotonic timestamp가 `responseReceived`보다 약 0.35 ms 앞서는 현상을 보였으므로, tracker는 5 ms 이하의 미세한 cross-process clock skew만 앞선 lifecycle 시각으로 clamp하고 evidence에 조정 여부를 남긴다. 5 ms를 넘는 역전은 계속 `invalid`다. 원본 probe 결과는 [`docs/evidence/stream-sync-webview2-runtime.json`](evidence/stream-sync-webview2-runtime.json)에 저장한다. 이는 schema 호환성 증거일 뿐 actual SOOP correlation coverage를 대체하지 않는다.
