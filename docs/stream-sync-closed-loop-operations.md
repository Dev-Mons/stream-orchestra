# Stream sync closed-loop 운영·rollback runbook

현재 상태는 `disabled`다. `SyncClosedLoopExperimentGate`는 준비되어 있지만 앱 runtime feature flag에 연결하지 않았고 actual passive/calibration/suggestion gate도 통과하지 않았다. 따라서 현재 production 동작은 legacy 제어, estimator/interval shadow, 로컬 suggestion-only이며 candidate closed loop는 명령을 dispatch하지 않는다.

## 동결 프로토콜

`sync-closed-loop-v1`은 2026-08-18 UTC에 다음 기준으로 동결했다.

| 항목 | 값 |
| --- | ---: |
| epsilon | 250 ms |
| 안정 독립 progress | 3개 |
| proxy error non-inferiority | +50 ms |
| wrong correction 중단 | 2% 초과 |
| stall 중단 | baseline 대비 +0.5%p 초과 |
| CPU 중단 | +3%p 초과 |
| memory 중단 | +100 MB 초과 |
| valid coverage | 95% 미만이면 중단 |
| candidate exposure | cluster/block의 50% |

1–5절 gate, 명시적 opt-in과 feature flag가 모두 있어야 HMAC cluster ID와 runtime/quality block을 이용한 결정적 무작위 배정이 수행된다. control은 suggestion-only, candidate는 bounded rate correction만 허용한다. raw channel이나 방송명은 배정키에 쓰지 않는다.

## Hard seek 별도 gate

첫 실험에서는 hard seek flag가 꺼져 있다. 이후 별도 승인에서도 stable epoch, 독립 progress 3개 이상, calibrated PDT mapping, 해당 request의 CDP correlation, runtime별 CDP coverage gate, fresh rVFC, buffered∩seekable target, apply 확인, seeked 확인, follow-up position 확인을 모두 요구한다. 하나라도 없으면 hard seek는 false다.

CDP coverage 기준은 runtime별 attempt 100개 이상, exact correlation 95% 이상, ambiguous 1% 이하, invalid 0%, runtime mismatch 0건이다. 한 응답의 correlation 성공만으로 hard seek를 열지 않는다.

## 즉시 rollback

privacy violation, invalid seek, coverage 부족 또는 동결한 proxy/wrong-correction/stall/CPU/memory 기준 악화가 한 번이라도 확인되면 process 내 rollback latch가 켜지고 suggestion-only로 복귀한다. 운영자는 외부 feature flag도 즉시 끄고 새 build나 명시적 재승인 전에는 latch를 우회하지 않는다.

명령 결과는 expected command ID가 일치하고 `Verified`, `WasApplied`, `WasVerified`가 모두 true일 때만 성공이다. failed, timeout, applied-only, wrong ID는 성공 수에 넣지 않고 playback rate 1 복구와 degraded 전환 대상으로 기록한다.

## 관측과 사용자 데이터 삭제

- dashboard 입력: coverage, proxy error, wrong correction/reversal, stall/rebuffer, CPU/GPU/memory, verified/failed seek, privacy scan
- 진단 수집 종료: CDP receiver 해제, `Network.disable`, raw in-memory correlation map 삭제
- **수집본 삭제**: 완료 snapshot과 DPAPI telemetry HMAC key 삭제·회전
- **prior 삭제**: 로컬 encrypted bias document 별도 삭제
- 이미 export한 JSON: 앱이 추적하지 않으므로 사용자가 선택한 pilot 폴더에서 직접 삭제
- 중앙 업로드/다중 사용자 집계: 구현하지 않음

rollback 후에는 privacy-safe export와 failure reason을 보존하되 raw URL/header/body를 incident 기록에 복사하지 않는다.
