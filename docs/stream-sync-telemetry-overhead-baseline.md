# Sync telemetry 기본 비활성 overhead baseline

2026-08-18 Release 빌드에서 `SyncTelemetrySessionController.IsEnabled`의 기본 비활성 guard를 실측했다. 실제 hot path는 이 guard가 `false`이면 telemetry DTO를 만들거나 recorder를 호출하지 않는다.

## 재현 명령

```powershell
dotnet build StreamOrchestra.slnx -c Release --no-restore --nologo
dotnet run --project src/StreamOrchestra.Tools/StreamOrchestra.Tools.csproj -c Release --no-build -- `
  sync-telemetry-overhead --iterations 20000000 --trials 9 `
  --output artifacts/sync-telemetry-off-overhead-20260818.json
```

측정 도구는 각 trial 전에 full GC를 수행하고 wall clock, process CPU, 현재 thread managed allocation, managed heap과 working-set 전후 차이를 기록한다. warm-up은 250,000회이며 모든 trial에서 controller가 계속 비활성인지 함께 검증한다.

## 환경과 결과

| 항목 | 값 |
| --- | --- |
| 기반 commit | `a9a44b48b9ede67cb8f72af8ec646a06f92b8d8e` + 이슈 #12 작업 트리 |
| 런타임 | .NET 8.0.27, x64, Release |
| OS | Windows build 26200 |
| CPU | AMD Ryzen 9 9950X3D, logical processor 32개 |
| RAM | 66,156,453,888 bytes |
| 반복 | 20,000,000 checks × 9 trials |
| wall time/check | p50 **1.082315 ns**, p95 **1.094595 ns** |
| process CPU/check | p50 **0.78125 ns**, p95 **1.5625 ns** |
| managed allocation | 모든 trial **0 bytes**, 0 bytes/check |
| managed heap delta | 최대 절댓값 **0 bytes** |
| working-set delta | 최대 절댓값 **8,192 bytes** |
| unexpected enabled observation | **0** |

원시 JSON은 위 명령의 `artifacts/sync-telemetry-off-overhead-20260818.json`에 생성된다. `artifacts/`는 저장소 ignore 대상이므로 동일 환경에서 명령으로 다시 생성하는 것을 기준으로 한다.

## 해석 한계

이 값은 비활성 guard 하나의 microbenchmark이며 전체 앱 CPU/GPU·WebView2 부하 비교나 telemetry 활성 상태 비용이 아니다. Windows process CPU timer의 해상도 때문에 trial별 CPU 값은 15.625ms 단위로 양자화됐고 working set은 OS 잡음의 영향을 받는다. 따라서 절대 ns 값을 다른 장비의 성능 보장으로 사용하지 않는다. 이 측정이 입증하는 범위는 해당 환경에서 기본 비활성 확인 경로가 managed allocation을 만들지 않았고, 수집을 우발적으로 활성화하지 않았다는 것이다. guard 또는 호출 위치가 바뀌면 같은 Release 명령을 다시 실행한다.
