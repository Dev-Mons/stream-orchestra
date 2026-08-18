# StreamOrchestra SOOP 다중 방송 동기화 개선 조사

작성일: 2026-08-18
범위: 조사와 설계만 수행했으며 제품 코드는 변경하지 않았다. 영상 프레임의 픽셀, 화면 내용, 음성, 오디오 파형은 분석하거나 비교하지 않았다.

이 문서에서 **확인**은 저장소 코드·공식 명세·공식 공개 문서로 확인한 사실, **추론**은 확인된 신호로부터의 설계 판단, **미확인**은 실제 SOOP 응답이나 운영 데이터가 있어야 검증할 수 있는 항목을 뜻한다.

## 1. 결론 요약

**임의의 서로 다른 SOOP 방송을 실제 장면 발생 시각 기준으로 완전히 자동 동기화하는 것은 현재 제약 아래에서는 불가능하다.** 각 스트림의 캡처, 송출 프로그램, 인코더 큐, 업로드, SOOP 수신·트랜스코딩·패키징 지연이 외부에 공통 timecode로 노출되지 않기 때문이다. HLS `EXT-X-PROGRAM-DATE-TIME`(PDT)은 다음 세그먼트 첫 샘플과 벽시계를 연결하지만, 그 벽시계가 SOOP에서 카메라 캡처 시각이라는 공개 보장은 없다. RFC도 playlist 날짜가 콘텐츠 생산 시각일 수도 있고 다른 목적의 시각일 수도 있다고 명시한다. [RFC 8216 §4.3.2.6](https://www.rfc-editor.org/rfc/rfc8216.html#section-4.3.2.6), [§6.3.3](https://www.rfc-editor.org/rfc/rfc8216.html#section-6.3.3)

따라서 목표를 세 층으로 나눠야 한다.

| 목표 | 달성 가능성 | 올바른 표현 |
| --- | --- | --- |
| 실제 장면 발생 시각 정렬 | 송출측 공통 timecode 또는 내용 비교가 없으면 세션별 완전 자동화 불가 | 원리적으로 관측 불가능한 상류 지연이 남음 |
| 플랫폼 패키징/CDN 시간축 정렬 | PDT 의미와 playlist를 올바르게 검증하면 개선 가능 | 플랫폼 출구 시간축 정렬 |
| 브라우저에 실제 제시되는 미디어 시각 정렬 | player·compositor 계측과 보수적 제어로 상당히 안정화 가능 | 플레이어 표시 위치 정렬 |
| 반복되는 채널별 잔여 편향 보정 | 수동 보정을 noisy proxy label로 학습하면 점진적으로 개선 가능 | 상류 지연을 포함할 수 있는 과거 총잔차 기반 제안이며, 장면 시각의 직접 측정은 아님 |

현재 체감 오차가 큰 이유는 원리적 한계만이 아니다. 저장소를 직접 검토한 결과, CDN/플레이어 정렬 층에도 다음과 같은 수정 우선순위가 있다.

1. **playlist identity와 timeline epoch를 먼저 정확히 관리한다.** 현재 한 슬롯에서 모든 `.m3u8` 응답을 포착하지만 identity를 보존하지 않는다. 표준 master는 `EXTINF`가 없어 보통 parser가 버리지만, video/audio/다른 rendition 중 parser 조건을 만족한 응답은 하나의 `LatestTimeline`을 서로 덮어쓴다. media sequence, discontinuity, segment/part identity도 저장하지 않는다.
2. **HTTP `Date`를 HLS edge 시각으로 사용하지 말고 요청·응답·cache 계측을 분리한다.** `Date`는 HTTP 메시지 origination 시각이지 마지막 세그먼트 끝, 게시, 캡처 시각이 아니다. [RFC 9110 §6.6.1](https://www.rfc-editor.org/rfc/rfc9110.html#section-6.6.1)
3. **source/epoch별 robust estimator와 confidence-gated control을 도입한다.** rolling median/MAD gate 뒤 `[offset, drift]` Kalman 또는 짧은 Huber 회귀를 사용하고, 불확실성이 큰 동안에는 hard seek를 금지한다.
4. **player의 실제 제시 시각과 연속 playable range를 계측한다.** `requestVideoFrameCallback`의 `mediaTime`, `expectedDisplayTime`, `processingDuration`은 픽셀을 읽지 않고 합성 단계의 미디어 시각을 제공한다. 다만 이것도 캡처 시각은 알려주지 않는다. [requestVideoFrameCallback 명세](https://wicg.github.io/video-rvfc/)
5. **수동 보정을 채널·화질·CDN 맥락별 계층 prior로 학습한다.** 같은 세션의 최종 수동값을 학습과 평가에 동시에 쓰지 않고, 독립 세션 holdout에서 수동 작업량과 residual 조정량이 줄었는지 확인한다.

가장 유망한 세 개선은 (1) playlist/source epoch와 네트워크 계측 정상화, (2) robust timeline 추정과 저신뢰 seek 억제, (3) 수동 보정의 온디바이스 계층 prior 학습이다. 첫 두 항목은 **CDN/플레이어 정렬을 안정화**하고, 세 번째는 **반복되는 장면 편향을 간접적으로 줄여 수동 작업을 감소**시킨다. 어떤 항목도 임의 방송의 상류 지연을 직접 측정하지는 않는다.

## 2. 현재 구현과 오차 발생 구조

### 2.1 실제 장면부터 화면 표시까지

스트림 `i`의 한 장면에 대해 전체 지연은 다음처럼 분해할 수 있다.

$$
L_i=L_{capture,i}+L_{program,i}+L_{encode,i}+L_{upload,i}+L_{platform,i}+L_{CDN,i}+L_{buffer,i}+L_{decode/render,i}
$$

| 단계 | 예시 | 현재 직접 관측 여부 | 현재 신호가 닿는 경계 |
| --- | --- | --- | --- |
| 캡처 | 카메라 exposure, capture card | 관측 불가 | 없음 |
| 송출 프로그램 | OBS scene/mixer/filter/queue | 관측 불가 | 없음 |
| 인코딩 | frame reorder, encoder buffer, GOP | 현재는 실제 컨테이너 PTS를 파싱하지 않고 vendor tag를 첫 PTS로 가정 | 검증된 PTS를 읽어도 UTC나 실제 장면 시각은 아님 |
| 업로드 | 스트리머→플랫폼 one-way 전송 | 관측 불가 | 시청자 측 RTT로 분리 불가 |
| 플랫폼 수신·트랜스코딩·패키징 | ingest queue, ABR encoding, HLS packaging | PDT가 있다면 패키징된 media timeline과 벽시계의 매핑 가능 | PDT가 어느 내부 단계의 시계인지는 SOOP에서 미확인 |
| CDN 게시·전송 | cache, edge, download | `Date`, `Age`, cache header, request timing으로 일부 관측 | 장면 시각이 아니라 응답/cache 상태 |
| 플레이어 버퍼 | seekable, buffered, stall | 관측 가능 | 현재 media timeline 위치와 playable range |
| 디코딩·렌더링 | decode, compositor | 현재는 `currentTime`만 간접 관측 | `requestVideoFrameCallback`으로 제시 직전까지 개선 가능 |

원하는 장면 시각을 `E_i`, 플랫폼이 media timestamp에 붙인 벽시계를 `W_i`, 그 앞의 관측 불가능한 지연을 `U_i`라 두면 `W_i=E_i+U_i`이다. 현재 방식이 `W_i=W_j`를 완벽히 맞춰도 실제 장면 오차는 `E_i-E_j=-(U_i-U_j)`로 남는다. **현재 자동 방식은 주로 `W`, CDN edge, player position을 정렬하며 `U_i-U_j`는 직접 알 수 없다.**

### 2.2 네 시간값은 서로 다른 뜻을 가진다

| 값 | 명세상/구현상 의미 | 해서는 안 되는 해석 |
| --- | --- | --- |
| HLS PDT | 다음 media segment 첫 sample의 media timestamp와 ISO 8601 벽시계 매핑 | SOOP 카메라 캡처 시각이라고 단정 |
| PTS | encoder/program clock에서 presentation unit이 표시될 상대 시각 | 서로 다른 encoder의 같은 PTS가 같은 사건이라고 간주 |
| HTTP `Date` | HTTP 메시지 origination 시각 | segment 끝·게시·캡처 시각이라고 간주 |
| local observation time | 앱이 관측을 기록한 로컬 시각 | 서버 시계나 응답 도착시각과 동일하다고 간주 |

MPEG-TS의 PTS/DTS/PCR은 공통 encoder system clock 안에서 presentation, decode, clock recovery를 표현한다. UTC가 아니며 서로 다른 encoder 사이의 공통 원점을 제공하지 않는다. [ITU-T H.222.0](https://www.itu.int/dms_pubrec/itu-t/rec/h/T-REC-H.222.0-202504-I%21%21TOC-HTM-E.htm) MPEG-TS PTS는 rollover와 discontinuity 처리도 필요하다. [W3C MPEG-2 TS byte stream §7](https://www.w3.org/TR/mse-byte-stream-format-mp2t/#timestamp-rollover--discontinuities)

### 2.3 현재 시간축·제어 흐름

확인한 주요 구현은 [HlsTimelineParser.cs](../src/StreamOrchestra.App/Services/HlsTimelineParser.cs), [StreamSlotView.Sync.cs](../src/StreamOrchestra.App/Views/StreamSlotView.Sync.cs), [StreamSyncCoordinator.cs](../src/StreamOrchestra.App/Services/StreamSyncCoordinator.cs), [StreamSyncModels.cs](../src/StreamOrchestra.App/Models/StreamSyncModels.cs)이다.

1. 각 `StreamSlotView`는 `WebResourceResponseReceived`에서 URL path가 `.m3u8`로 끝나는 모든 2xx 응답을 잡는다.
2. 응답 `Date`를 읽고, `GetContentAsync`로 본문 전체를 읽은 뒤 `observedAt=UtcNow`를 기록한다.
3. parser는 모든 `EXTINF`를 모으고, 첫 `EXT-X-FIRST-SEGMENT-TIMESTAMP`, 첫 PDT만 읽는다.
4. PDT가 있으면 `PDT + 뒤쪽 EXTINF 합`을 tail `EdgeUtc`로, vendor timestamp와 앞쪽 `EXTINF` 합을 media→UTC offset으로 만든다. PDT가 없고 vendor timestamp와 `Date`가 있으면 **tail PTS를 `Date`에 직접 대응**시킨다.
5. 같은 source의 새 offset은 고정 `0.8 old + 0.2 new` EMA로 평활하며, edge가 후퇴하거나 예상 jump가 크면 버린다.
6. 페이지 bridge는 500ms마다 가장 큰 재생 video의 `currentTime`, `readyState`, `buffered`, `seekable`, `playbackRate` 등을 host로 보낸다.
7. coordinator는 500ms마다 fresh timeline의 projected edge 중 최솟값을 공통 edge로 고른다. timeline이 없는 member는 자기 `seekableEnd`에서 안전 딜레이를 뺀 추정 모드를 쓴다.
8. parser의 media→UTC offset이 없거나, 그 offset이 만든 implied media edge가 player `seekableEnd`와 `max(5초, 3×segment duration)`보다 크게 다르면, `ResolveMediaToUtcOffset`은 source/confidence를 바꾸지 않은 채 `projectedEdgeUtc-seekableEnd`로 offset을 다시 만든다. 따라서 vendor timestamp가 없는 PDT playlist도 표시상 `ProgramDateTime/Confidence=1`인 live-edge mapping이 될 수 있다.
9. 절대 timeline member의 target은 다음과 같다.

   $$
   target_i=\frac{commonEdge(now)-snapshotAge_i-safety-mediaToUtcOffset_i}{1000}-manualDelay_i
   $$

   여기서 `snapshotAge_i`는 공통 edge를 각 player snapshot 관측 시점으로 되감는 항이다. 양의 `ManualDelayMs`는 더 과거 media 위치를 재생해 해당 방송을 늦춘다.
10. `error=currentTime-target`의 절댓값이 350ms 이내면 1.0배, 그 밖에는 `1-0.02×error(seconds)`를 0.98~1.02로 제한한다. 1.5초 이상 오차가 같은 방향으로 3 tick 확인되고 10초 cooldown이 끝나면 hard seek한다. 최초 정렬은 350ms 밖이면 즉시 seek한다.
11. 안전 딜레이는 최소 설정값, timeline segment duration 중앙값의 두 배, 추정 member가 있을 때 5초 중 큰 값이다. 지속 buffering이면 모두 pause하고 안전 딜레이를 늘린 뒤 재정렬한다.

기존 parser/coordinator 테스트는 위 산술, deadband, recovery, 3-tick seek, preset reset을 검증한다. 그러나 실제 master/media/rendition association, discontinuity, cache, LL-HLS, body-read timing, player range gap, command 적용 확인은 fixture에 없다. 검토 시 기존 Release test binary의 관련 20개 테스트는 통과했지만, 이것은 위 미계측 영역의 정확성을 입증하지 않는다.

### 2.4 구현상 오차·잘못된 가정

| 우선도 | 문제 | 결과 |
| --- | --- | --- |
| 매우 높음 | **포착한 `.m3u8`의 identity 없이 슬롯당 단일 timeline 사용** | 표준 master는 보통 duration이 없어 무시되지만, video/audio rendition, quality switch, duration을 가진 광고/보조 playlist는 서로 덮어쓰거나 EMA로 섞일 수 있다. observation에 request URI·frame·rendition identity가 없다. |
| 중간 | URL path가 `.m3u8`인 응답만 포착 | HLS는 `.m3u` path 또는 HLS Content-Type으로도 식별할 수 있어 허용된 extensionless/다른 suffix media playlist를 놓칠 수 있다. 실제 SOOP 관련성은 미확인이다. [RFC 8216 §4](https://www.rfc-editor.org/rfc/rfc8216.html#section-4) |
| 매우 높음 | `EXT-X-FIRST-SEGMENT-TIMESTAMP`를 100ns tick의 첫 video PTS라고 가정 | 이 태그는 RFC 8216과 최신 HLS 2판 초안에 없는 vendor-private tag다. 단위, track, PTS/DTS, discontinuity 의미는 SOOP 실제 표본으로 미확인이다. |
| 매우 높음 | `Date == playlist tail edge` | HTTP 응답 생성/전달과 마지막 segment boundary 사이의 packaging/cache lag, 초 단위 양자화, 서버 clock bias가 모두 media mapping bias가 된다. `Age`도 무시한다. |
| 높음 | 첫 PDT만 사용하고 segment 객체를 만들지 않음 | 여러 PDT, tag 순서, gap, discontinuity를 올바르게 처리하지 못한다. `EXTINF` 누적을 actual PTS increment로 간주해 rounding·timestamp gap도 흡수한다. |
| 높음 | 고정 tail boundary를 `ProjectEdgeUtc`로 벽시계만큼 계속 전진 | 같은 playlist가 반복되면 실제 progress 없이 최대 15초 동안 edge가 움직이는 것처럼 보인다. 15초 이내 duplicate는 거부되어 freshness가 갱신되지 않지만, gap이 15초를 넘으면 같은 tail도 다시 수용되어 freshness와 projection 원점이 재설정될 수 있다. tail boundary와 예측 live clock을 분리해야 한다. |
| 높음 | source/quality/CDN/discontinuity 변경 시 epoch reset 없음 | 실제 step change를 outlier로 버리거나 과거 EMA와 섞는다. 8초/3 segment jump gate가 정당한 discontinuity도 버릴 수 있다. |
| 높음 | PDT/PTS mapping 부재·불일치 시 player `seekableEnd`로 조용히 재기준화 | 절대 mapping이 사실상 live-edge-distance 추정으로 바뀌어도 source와 confidence는 그대로다. vendor timestamp 부재/오류를 숨기고 서로 다른 의미의 관측을 같은 값처럼 제어에 넣는다. |
| 높음 | PDT, CDN Date, live-edge 추정을 동일 제어 입력으로 혼합 | `Confidence=1/0.55`는 교정된 확률이 아니고 coordinator가 사용하지 않는다. `CdnDate`도 UI 일부와 runtime에서 사실상 절대 source처럼 취급된다. |
| 높음 | timezone이 없는 PDT를 UTC로 가정 | RFC의 timezone 표시는 SHOULD라 생략 가능하지만 parser는 `AssumeUniversal`로 해석한다. 실제 producer timezone을 모르면 큰 고정 오차가 될 수 있으므로 reject 또는 미확인 source로 강등해야 한다. |
| 높음 | `buffered`의 마지막 range 끝과 `seekable`의 전체 첫/끝만 사용 | `currentTime`을 포함하지 않는 미래 range로 buffer를 과대평가하고, gap 안의 target을 유효하다고 판단할 수 있다. HTML `seekable`은 “seek 가능한 범위”이고 MSE에서는 application이 live seekable range를 설정할 수도 있으므로 CDN canonical edge가 아니다. [HTML Standard](https://html.spec.whatwg.org/multipage/media.html#dom-media-seekable-dev), [Media Source Extensions](https://www.w3.org/TR/media-source-2/#htmlmediaelement-extensions) |
| 중간~높음 | 응답 본문을 다 읽은 후 `observedAt` 기록 | body 크기, UI thread scheduling, PC 부하가 관측 시각에 섞인다. WebView2 `GetContentAsync` stream read는 데이터 대기를 block할 수 있으므로 background read가 권고된다. [WebView2 response view](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2webresourceresponseview) |
| 중간 | 500ms JS interval과 host 수신시각만 기록 | 슬롯별 scheduler/IPC 지연과 최대 한 polling interval 수준의 검출 지연이 생긴다. `currentTime`을 읽은 정확한 monotonic 시각이 없다. |
| 중간 | `waiting`, `stalled`, `error`를 같은 last buffer event로 합침 | `waiting`은 다음 frame 부재, `stalled`는 fetch data 미도착, `error`는 실패로 의미가 다르다. 현재 coordinator는 이 합쳐진 시각을 사용하지 않지만 진단 의미를 잃고, 향후 하나의 recovery trigger로 쓰면 오판할 수 있다. [HTML media events](https://html.spec.whatwg.org/multipage/media.html#mediaevents) |
| 중간 | command 성공 여부를 확인하지 않음 | frame navigation, gap target, player override로 rate/seek가 적용되지 않아도 coordinator는 성공으로 본다. `seeked`, 다음 rVFC/currentTime, 실제 playbackRate로 닫힌고리 확인이 필요하다. |
| 중간 | 공통 edge를 단순 최솟값으로 결정 | 하나의 stale/biased timeline이 전체를 늦춘다. 각 stream의 불확실성과 공통 playable interval을 보지 않는다. |
| 중간 | 간격 계산에 `DateTimeOffset.UtcNow` 사용 | OS clock step/NTP 보정이 freshness·projection에 영향을 줄 수 있다. interval과 drift는 monotonic clock으로 계산해야 한다. |
| 중간 | manual key가 normalized page URL 하나 | 안정적인 channel identity와 broadcast session identity가 분리되지 않아 과거 prior의 재사용·reset 단위를 정확히 표현하지 못한다. |

`reference/soop-multisync/background.js`는 원 응답의 `Date`를 저장한 뒤 같은 URL을 `cache:'no-store'`로 다시 fetch해 본문을 파싱한다. 두 reload의 `Date`와 playlist tail이 결합될 수 있고 요청도 중복된다. 현재 제품의 passive `GetContentAsync`가 이 점에서는 더 안전하지만, 제품도 response arrival와 body-read completion을 분리하지 않는다. reference 폴더에는 실제 SOOP manifest/header 표본이 없으므로 tag 존재율이나 의미를 입증하는 근거로 사용해서는 안 된다.

## 3. 자동으로 관측 가능한 정보와 관측 불가능한 정보

### 3.1 허용된 신호의 접근성·신뢰도

| 신호 | WebView2/공개 접근 | 신뢰할 수 있는 용도 | 실제 장면 시각 기여 | 상태 |
| --- | --- | --- | --- | --- |
| media sequence, segment URI/byte range | manifest body를 읽으면 가능 | 동일 playlist의 progress·duplicate·rollback 탐지 | 없음 | 표준, 현재 미사용 |
| discontinuity sequence/tag | manifest body에서 가능 | timestamp epoch reset, rendition 대응 | 없음 | 표준, 현재 미사용 |
| segment/part URI 생성 패턴 | URI에서 관측 가능 | 장기 검증 뒤 progress 보조 feature | 없음 | 비표준·SOOP별 미확인 |
| PDT | manifest에서 가능 | media sample↔wall-clock anchor | SOOP이 capture-clock 의미를 보장할 때만 | 표준, SOOP 의미 미확인 |
| `SERVER-CONTROL`, `PART`, `PRELOAD-HINT`, `RENDITION-REPORT` | LL-HLS manifest에 있을 때 가능 | part-level egress progress, hold-back, rendition alignment | 없음 | SOOP 제공 여부 미확인 |
| TS PTS/DTS/PCR, fMP4 `tfdt` | segment metadata를 읽을 수 있을 때 가능 | stream 내부 정밀 timeline, PDT anchor 보강 | 단독으로 없음 | 암호화·비용·format에 따라 제한 |
| fMP4 `prft`, timed metadata | 실제 box/tag가 있을 때 가능 | NTP↔media production mapping 후보 | 의미가 capture로 정의된 경우만 | SOOP 존재·의미 미확인 |
| HTTP `Date`, `Age`, cache header | response header에서 가능 | response/cache freshness와 endpoint bias | 없음 | `Age` 등 SOOP 실제 노출 미확인 |
| request start/header/body end, RTT | WebResourceRequested/CDP로 가능 | queueing jitter, body-read 편향, cache/endpoint 비교 | 없음 | 구현 가능 |
| Resource Timing | page script에서 가능 | same-origin 또는 TAO 허용 시 network timing | 없음 | CDN cross-origin 세부값은 `Timing-Allow-Origin` 없으면 제한 [Resource Timing](https://www.w3.org/TR/resource-timing/) |
| CDP `Network.*` | WebView2가 CDP method/event receiver 제공 | requestId, monotonic timestamp, frameId, initiator, timing, cache flags | 없음 | 가장 완전하지만 CDP schema/runtime 호환성 관리 필요 [WebView2 CDP event receiver](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.getdevtoolsprotocoleventreceiver), [CDP Network](https://chromedevtools.github.io/devtools-protocol/tot/Network/) |
| `currentTime`, full `buffered`/`seekable`, ready/network state, events | injected script에서 가능 | player state와 contiguous playable interval | 없음 | 현재 일부 사용 |
| `requestVideoFrameCallback` metadata | Chromium/WebView2 runtime 지원 시 가능 | 화면 합성에 제출된 frame의 media PTS와 예상 표시 시각 | 캡처 전 지연은 없음 | 픽셀 분석 없이 사용 가능 |
| playback quality dropped/total frames | `getVideoPlaybackQuality()` | PC 부하·decode/render 불안정 guardrail | 없음 | [Media Playback Quality](https://w3c.github.io/media-playback-quality/) |
| SOOP `broad_no`, `broad_start`, API response `time` | 공식 Open API에서 제공 | session identity와 목록 생성 시각 | 없음 | `client_id`와 제휴/API key 필요 [SOOP Open API](https://openapi.sooplive.co.kr/apidoc) |
| SOOP Extensions SDK `startTime` | 승인된 extension context에서 제공 | session start metadata | 없음 | 일반 WebView 앱에서 사용 가능하다고 가정 금지 [SOOP Extensions SDK](https://developers.sooplive.co.kr/?part=broadcast&sub=api&szWork=extension) |
| 채널·화질·CDN별 반복 bias | 여러 session log로 추정 | 고정/느린 편향 prior | 과거 경향으로 간접 개선 | 현재 데이터 없음 |
| 사용자 수동 보정 | 현재 UI에서 이미 발생 | accepted alignment의 noisy proxy label | 반복 편향을 간접 학습 | 가장 현실적인 총잔차 신호; 성분 분해 불가 |

`WebResourceResponseReceived`는 WebView가 응답을 받은 때 발생하지만 WebView의 response 처리와 host handler 실행 순서는 보장되지 않으며, event args는 request와 response view만 노출한다. 따라서 callback 시각을 HTTP header 도착의 정밀 timestamp로 간주할 수 없고, 본문 완료시각과도 분리해야 한다. CDP `Network.*`의 requestId·monotonic timing으로 lifecycle을 결합하되 runtime별 schema를 versioning한다. [Microsoft WebView2 response event](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.webresourceresponsereceived), [event args](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webresourceresponsereceivedeventargs), [CDP Network](https://chromedevtools.github.io/devtools-protocol/tot/Network/)

HLS media sequence는 동일 playlist 안의 순서값이다. 서로 다른 playlist에서 같은 sequence가 같은 콘텐츠라는 보장은 없고 URI에 sequence가 들어갈 필요도 없다. [RFC 8216 §4.3.3.2](https://www.rfc-editor.org/rfc/rfc8216.html#section-4.3.3.2) 그러므로 독립 방송 간 sequence·URI 번호를 직접 맞추는 접근은 금지해야 한다.

PDT 문자열은 timezone과 밀리초 단위의 fractional second를 표시하도록 권고되지만, 이것은 **표기 정밀도**이지 시계 정확도나 캡처 시각 보장이 아니다. 서버는 PDT mapping을 모호하게 만들어서는 안 되고, PDT를 제공한다면 discontinuity가 붙은 segment마다 적용하는 것이 권고되며, 같은 master의 media playlist들은 일관된 mapping을 가져야 한다. client는 첫 PDT 앞의 segment에는 뒤로, 이후 PDT가 없는 segment에는 마지막 anchor에서 앞으로 `EXTINF`와/or media timestamp로 외삽하도록 규정되어 있다. 따라서 parser는 모든 PDT의 `PDT(next)-[PDT(current)+실제 media timestamp delta]` residual을 검사하고 discontinuity마다 새 epoch를 만들며, 소수 자릿수·반복 양자화·clock step을 데이터로 측정해야 한다. timezone이 빠진 값은 명세상 local time으로 볼 수 있으므로 현재처럼 UTC라고 단정하지 않는다. [RFC 8216 §4.3.2.6](https://www.rfc-editor.org/rfc/rfc8216.html#section-4.3.2.6), [§6.2.1](https://www.rfc-editor.org/rfc/rfc8216.html#section-6.2.1), [§6.2.4](https://www.rfc-editor.org/rfc/rfc8216.html#section-6.2.4), [§6.3.3](https://www.rfc-editor.org/rfc/rfc8216.html#section-6.3.3)

LL-HLS의 `PART`와 blocking reload는 segment보다 촘촘하게 **플랫폼 egress 진행도**를 관측한다. `SERVER-CONTROL`의 `HOLD-BACK`/`PART-HOLD-BACK`은 서버 권장 재생 거리이지 실제 end-to-end latency 측정값이 아니다. `RENDITION-REPORT`도 같은 presentation의 rendition 전환을 돕지, 서로 다른 방송을 장면 기준으로 연결하지 않는다. [HLS 2nd Edition draft-22 §4.4.3.8](https://datatracker.ietf.org/doc/html/draft-pantos-hls-rfc8216bis-22#section-4.4.3.8), [Apple LL-HLS](https://developer.apple.com/documentation/http-live-streaming/enabling-low-latency-http-live-streaming-hls)

player 표본의 변화율도 신호로 쓸 수 있다. monotonic sample time으로 `ΔcurrentTime/Δt`를 계산하면 명령한 `playbackRate`가 실제 진행에 반영됐는지 확인할 수 있고, `seekableEnd`·tail range의 step과 `bufferAhead`의 증가/감소율은 playlist 진전, 다운로드 충전, 재생 소모를 구분하는 feature가 된다. 단, polling 지연과 stall/seek event를 함께 넣은 robust finite difference로만 사용하며 이 변화율을 장면 시계나 CDN canonical edge로 승격하지 않는다.

### 3.2 HTTP 시계와 네트워크 지연의 식별 한계

request 시작 `t0`, response header 수신 `t1`, HTTP `Date=D`를 얻으면 `D-(t0+t1)/2`를 server clock offset 후보로 만들 수 있다. 그러나 이는 대칭 경로, 짧은 server processing, 정확한 server clock을 가정한 추정일 뿐이다. NTP는 client/server 송수신 네 timestamp를 사용해 offset과 RTT를 계산한다. [RFC 5905 §8](https://www.rfc-editor.org/rfc/rfc5905.html#section-8) HTTP `Date` 하나로는 다음 성분을 분리할 수 없다.

- outbound와 inbound one-way delay의 비대칭
- CDN/origin processing time
- cache residence와 재검증
- server clock bias
- playlist tail이 만들어진 뒤 response가 생성되기까지의 packaging/publication lag

반복 관측에서 작은 RTT 표본을 선택하고 robust regression을 쓰면 일시적 queueing jitter는 줄일 수 있다. 고정 경로 비대칭과 server clock/packaging bias는 남는다. `Age`는 cache에서 origin 생성·재검증 이후의 추정 age를 제공하지만 정수 초이며, 부재가 origin contact를 보장하지 않는다. [RFC 9111 §5.1](https://www.rfc-editor.org/rfc/rfc9111.html#section-5.1)

### 3.3 원리적으로 관측 불가능하거나 미확인인 정보

- 임의 방송의 카메라 exposure/capture-card/OBS queue/encoder lookahead 지연
- 스트리머→SOOP one-way upload 지연과 SOOP 내부 ingest/transcode queue의 개별 성분
- SOOP PDT가 캡처, ingest, transcode output, packager 중 어느 clock을 뜻하는지
- 시청자 PC의 물리 모니터 scanout과 오디오 장치 출력 지연의 정확한 값
- HTTP `Date`만으로 server clock bias와 one-way network delay를 분리한 값
- 실제 SOOP이 LL-HLS, `Age`, `Cache-Status`, `prft`, timed metadata를 제공하는지

이번 조사에서는 실제 SOOP media playlist와 response header capture를 확보하지 못했다. 저장소와 `reference/soop-multisync/`에도 scrubbed 실응답 fixture가 없다. 따라서 PDT 존재율·소수 정밀도, vendor tag 단위, cache header, CDN endpoint별 동작, LL-HLS 제공 여부는 모두 **미확인**이다.

## 4. 후보 기법 비교표

효과 구분은 `P`=CDN/플레이어 정렬 안정화, `S`=실제 장면 편향에 간접 접근, `L`=장기 고정 편향 학습이다. 기대 효과는 현재 결함을 제거할 상대적 우선순위이며 정확도 수치가 아니다.

| 개선안 | 효과 | 기대 효과 | 실시간 변화 추적 | 고정 편향 학습 | 안정성 | 난이도 | CPU·네트워크 | SOOP 의존성 | WebView2 접근 | 개인정보·정책 위험 | 오판 UX 영향 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| playlist identity + media/discontinuity sequence + epoch | P | 매우 높음 | 높음 | 없음 | 높음 | 중간 | 낮음 | 낮음 | 높음 | 낮음 | 낮음; 주로 seek 억제 |
| response arrival/body end 분리, `Date`/`Age`/cache 계측 | P | 높음 | 높음 | endpoint bias 가능 | 높음 | 낮음~중간 | 낮음 | 중간 | 높음 | URL/header redaction 필요 | 낮음 |
| CDP `Network.*` request correlation | P | 높음 | 높음 | endpoint bias 가능 | 중간~높음 | 중간 | 낮음 | 낮음 | 높음 | initiator/URL 최소수집 필요 | 낮음 |
| PDT + container PTS anchor | P; 송출측 계약으로 anchor가 capture time임이 검증된 경우만 S | 높음 | 높음 | clock drift 가능 | 입력이 검증되면 높음 | 높음 | metadata parse CPU; 추가 fetch 금지 | 높음 | response body 접근 가능 | DRM/암호화는 즉시 fallback | 잘못된 epoch면 큼 |
| LL-HLS part/preload/rendition signals | P | 제공될 때 높음 | 매우 높음 | 없음 | profile 준수 시 높음 | 중간 | passive면 낮음 | 매우 높음 | 높음 | 낮음 | 중간 |
| rVFC + full ranges + playback quality | P | 높음 | 매우 높음 | PC별 render bias 진단 | 높음 | 낮음~중간 | 낮음 | 낮음 | 높음 | GPU fingerprinting성 telemetry 최소화 | 낮음 |
| median/MAD gate + `[offset, drift]` Kalman | P | 높음 | 높음 | paired/reference anchor가 있을 때만 measurement bias 분리 | tuning 후 높음 | 중간 | 매우 낮음 | 낮음 | 높음 | 낮음 | 저신뢰 gate 없으면 큼 |
| interval-based target + confidence-gated hybrid control | P | 매우 높음 | 높음 | 없음 | 높음 | 중간~높음 | 낮음 | 낮음 | 높음 | 낮음 | 오판 hard seek를 직접 줄임 |
| 수동 보정 계층 prior | S, L | 수동 작업 감소에 매우 유망 | 느린 변화만 | 매우 높음 | shrinkage·decay 시 중간~높음 | 중간~높음 | 낮음 | 중간 | 높음 | 시청 채널 이력; local-first 필요 | 잘못된 prior 자동 적용 시 큼 |
| pairwise channel graph | S, L | 데이터가 연결될 때 중간~높음 | 낮음 | 높음 | gauge·연결성 관리 필요 | 높음 | 낮음 | 중간 | 높음 | 중앙 집계 시 높음 | component 간 잘못된 일반화 위험 |
| SOOP Open API/Extensions session metadata | P 보조 | session identity에 중간 | session 단위 | 없음 | 공식 계약 안에서는 높음 | 중간 | 낮음 | 매우 높음 | 승인 방식에 따름 | API 약관·key 관리 | 낮음 |
| 여러 사용자 aggregate prior | S, L | 충분한 표본이면 잠재적 높음 | 낮음 | 높음 | 편향·희소 cohort 위험 | 매우 높음 | 업로드 비용 | 중간 | 가능 | 매우 높음; opt-in 필수 | 잘못된 전역 prior 영향 큼 |
| URI 숫자 패턴 추론 | P 보조 | 낮음 | 높음 | endpoint 패턴 가능 | 낮음 | 낮음 | 낮음 | 매우 높음 | 높음 | signed query 저장 금지 | 중간 |

### 4.1 추정 알고리즘 비교

| 기법 | 장점 | 핵심 약점 | 권장 역할 |
| --- | --- | --- | --- |
| 고정 EMA | 구현·비용이 작음 | 관측 간격, uncertainty, outlier, source step을 무시 | 기존 baseline으로만 유지 |
| rolling median + MAD | gross outlier에 강하고 설명 가능 | trend/drift를 직접 예측하지 못하며 window lag | 모든 estimator 앞 innovation gate |
| Huber affine regression | offset와 drift를 함께 추정하고 큰 residual 영향을 제한 | stale가 다수이면 잘못된 다수에 수렴 | 짧은 epoch window estimator 또는 Kalman 비교군 [Huber, 1964](https://doi.org/10.1214/aoms/1177703732) |
| RANSAC | gross outlier 비율이 클 때 model fitting 가능 | 작은 online window에서 불안정·비결정적, stale majority에 취약 | 오프라인 분석·초기화 비교만 [Fischler & Bolles, 1981](https://doi.org/10.1145/358669.358692) |
| Kalman `[offset, drift]` | 불규칙 `Δt`, 예측, covariance를 O(1)로 제공 | 선형/Gaussian 가정과 `Q/R` misspecification, outlier에 민감 | robust gate 뒤 online core [Kalman, 1960](https://doi.org/10.1115/1.3662552) |
| CUSUM | O(1), 평균 shift를 빠르게 감지 | false alarm과 detection delay threshold trade-off | 명시적 protocol event를 보완하는 online detector [Page, 1954](https://academic.oup.com/biomet/article-abstract/41/1-2/100/456627) |
| BOCPD | run length와 change probability를 제공 | hazard/model 설정과 계산량 | 로그가 쌓인 뒤 실험 후보 [Adams & MacKay](https://arxiv.org/abs/0710.3742) |
| PELT | 전체 session의 복수 change를 효율적으로 분할 | online controller가 아님 | 과거 log·prior epoch 재분석 [Killick et al.](https://arxiv.org/abs/1101.1438) |

어떤 robust estimator도 **동일한 stale playlist가 관측의 다수**가 된 상황을 통계만으로 해결할 수 없다. sequence, URI/byte range, discontinuity, part index로 progress와 epoch를 먼저 판정해야 한다.

## 5. 권장 아키텍처

```text
WebView2/CDP network ─┐
HLS structured parser ├─> Source/Epoch Normalizer ─> Robust Timeline Estimator ─┐
Player/rVFC samples ──┘                                                         │
                                                                                ├─> Confidence/Policy ─> Rate/Seek/Pause
Manual accepted labels ─> Local Hierarchical Bias Prior ────────────────────────┘                     │
                                                                                                      └─> 결과 확인·로그
```

### 5.1 clock domain을 먼저 분리한다

- **local monotonic time**: request interval, freshness, estimator `Δt`, convergence 측정
- **local UTC**: 외부 벽시계와의 mapping 및 사람이 읽는 로그
- **HTTP server/CDN clock**: `Date`, `Age`가 표현하는 response/cache domain
- **HLS program clock**: PDT가 mapping하는 벽시계
- **media clock**: TS PTS, fMP4 decode/presentation time, browser `currentTime`/rVFC `mediaTime`

서로 다른 domain의 값을 mapping과 uncertainty 없이 직접 빼지 않는다. OS UTC가 step해도 monotonic estimator는 유지하고 UTC mapping epoch만 reset한다.

### 5.2 playlist/source identity와 epoch

관측 단위를 `(slot, navigation generation, CDP session/frame, canonical playlist identity, rendition, source epoch)`로 둔다. 저장 시에는 signed query를 제거하거나 keyed hash로 바꾸고, 메모리에서만 full request identity로 correlation한다.

playlist를 line list가 아니라 segment/part 구조로 파싱한다.

- master/media 구분, variant·audio rendition 관계
- media sequence, discontinuity sequence, per-segment discontinuity number
- URI + byte range의 runtime identity와 redacted persistence hash
- per-segment `EXTINF`, PDT, gap, map, endlist
- target duration, part target, server control, skip, preload hint, rendition report
- vendor tag는 raw name/value와 검증 상태를 보존하되 표준 PTS로 승격하지 않음

progress key는 `(playlist identity, tail discontinuity number, last MSN, last part, tail URI+byte range, tail PDT)`로 둔다. 동일 key 재수신은 duplicate로 기록하고 estimator measurement update를 하지 않는다. HTTP cache freshness와 HLS progress 정지는 별도 상태다. live playlist update 기대시간은 고정 15초가 아니라 `TARGETDURATION`/`PART-TARGET`과 명세 규칙을 사용한다. RFC 8216은 live playlist에 새 segment를 이전 버전 이후 `1.5×TARGETDURATION` 이내 추가하도록 규정한다. [RFC 8216 §6.2.1](https://www.rfc-editor.org/rfc/rfc8216.html#section-6.2.1)

다음 사건은 즉시 새 epoch를 만든다.

- navigation 또는 broadcast session identity 변경
- discontinuity number 증가, rollover unwrapping으로 설명되지 않는 PTS reset/jump
- PDT↔CDN-Date↔live-edge source 변경
- quality/rendition/CDN endpoint 변경
- player media timeline reset 또는 `currentTime`/seekable domain 재기준화
- local UTC step

### 5.3 media↔wall mapping

같은 epoch에서 PDT와 실제 presentation timestamp anchor를 얻으면 다음 선형 mapping을 사용한다.

$$
wall(sample)=PDT_{anchor}+\frac{PTS_{sample}-PTS_{anchor}}{timescale}
$$

TS PTS는 rollover를 풀고, fMP4는 `tfdt`, timescale, composition offset/edit를 반영한다. DTS나 PCR을 화면 presentation 시각으로 직접 쓰지 않는다. PDT만 있고 container PTS를 읽지 못하면 `EXTINF` 누적은 근사치로 유지하되 precision·discontinuity·rounding에 따른 넓은 uncertainty를 부여한다.

HTTP `Date` fallback은 더 이상 `EdgeUtc=Date`로 만들지 않는다. PDT와 `Date`가 동시에 있는 정상 표본에서 context별 `responseDate - PDT_tail` bias 분포를 먼저 학습할 수 있다. PDT가 없는 session에서는 같은 endpoint/quality의 held-out 검증된 bias prior가 있을 때만 낮은 confidence의 edge 후보로 사용한다. prior가 없거나 cache가 의심되면 **절대시각 source가 아니라 live-edge-distance fallback**으로 남긴다.

`Age`, `Cache-Control`, `Expires`, `Last-Modified`, `ETag`, 가능하면 `Cache-Status`를 기록한다. 표준 `Cache-Status`는 hit/stale-forward 등의 cache 동작을 설명할 수 있지만 선택적 header다. [RFC 9211](https://www.rfc-editor.org/rfc/rfc9211.html)

### 5.4 robust estimator와 bias 분리

각 source epoch에서 raw media→platform offset 관측을 `z`라 하고 다음 상태를 둔다.

$$
x_k=\begin{bmatrix}o_k\\d_k\end{bmatrix},\quad
x_k=\begin{bmatrix}1&\Delta t\\0&1\end{bmatrix}x_{k-1}+w_k
$$

$$
z_{k,s}=\begin{bmatrix}1&0\end{bmatrix}x_k+\beta_{source,endpoint,quality}+v_k
$$

- `o`: media→platform wall-clock offset
- `d`: clock drift
- `β`: source/endpoint/quality에 반복되는 measurement bias
- `v`: 일시적 network·scheduler jitter

단일 context의 `z`만 보면 `o`와 `β`는 합으로만 나타나 공동 식별되지 않는다. 검증된 기준 source에 `β=0`을 둘 수 있거나, 같은 epoch의 PDT·Date paired observation 및 context overlap이 있을 때만 차이로 `β`를 추정한다. 그런 anchor가 없으면 `o+β`를 combined context offset으로 저장하고 source·endpoint 원인으로 분해했다고 주장하지 않는다.

처리 순서는 다음과 같다.

```text
if explicit epoch change:
    dynamic state/covariance/seek confirmation reset
    hard-seek inhibit
if progress key unchanged or HTTP/HLS stale:
    measurement update skip; uncertainty만 시간에 따라 증가

predict using monotonic Δt
residual = z - contextBias - predictedOffset
scale = rolling MAD of same-epoch innovations
if gross outlier:
    reject and log reason
else:
    Huber weight와 source-specific calibrated variance로 update

if unexplained persistent change:
    new epoch; coherent observations가 쌓일 때까지 seek 금지
```

고정 bias와 jitter는 반드시 두 층으로 나눈다.

1. **timeline measurement bias**: PDT/Date/endpoint/quality가 media mapping에 만드는 편향. 검증 anchor 또는 paired/overlap 기계 신호가 있을 때만 분리 가능하고, 그 외에는 combined mapping으로 남긴다.
2. **manual residual prior**: 캡처·송출·인코더 지연을 포함할 수 있는 반복 총잔차. 수동 보정으로만 간접 학습한다.

수동 label만으로는 이 총잔차를 상류 scene 지연, 남은 timeline/CDN bias, player/render bias, 사용자의 지각·선호로 분해할 수 없다. 따라서 이를 “스트리머의 실제 인코더 지연을 측정했다”고 해석하지 않는다. 수동 prior를 timeline filter에 넣어 PDT/PTS의 물리 mapping을 왜곡하지 않고, 최종 target 계산에서 별도의 additive residual 항으로만 적용한다.

### 5.5 세 confidence를 분리하고 실제 결과로 교정한다

현재 `Confidence=1`은 “PDT tag가 parse됨” 이상의 의미가 없다. 다음을 별도 보존한다.

- **timeline confidence**: platform mapping의 prediction interval과 epoch 일관성
- **bias confidence**: channel/context prior의 support, recency, prediction interval
- **controllability confidence**: target이 현재 contiguous buffered+seekable range 안에 있고 command가 적용될 가능성

세 점수는 서로 다른 outcome으로 교정한다.

- timeline confidence: synthetic known mapping, 통제된 source timecode, 검증된 same-epoch paired anchor에서 mapping interval coverage와 residual을 평가한다. 실제 SOOP에 기준 anchor가 없으면 “장면 정확도 확률”로 표시하지 않는다.
- bias confidence: 독립 session의 accepted manual proxy에 대한 interval coverage와 `qε=P(|manual proxy residual|≤ε | evidence)`를 평가한다.
- controllability confidence: command ack, target range 유효성, seek/rate 적용 성공, 후속 stall/error로 평가한다.
- 최종 사용자 정렬 확률이 필요할 때만 위 evidence를 결합해 held-out manual proxy 성공률로 별도 교정한다.

각 확률의 reliability diagram, Brier score, interval coverage를 target별로 보고한다. confidence가 실제 해당 outcome의 성공 빈도와 맞아야 한다는 원칙은 calibration 연구와 같다. [Guo et al., 2017](https://proceedings.mlr.press/v70/guo17a.html) `ε`와 action threshold는 파일럿의 사용자 허용 오차와 오판 비용으로 정하며 임의의 정확도 수치를 만들지 않는다.

### 5.6 point minimum 대신 공통 playable interval을 쓴다

각 stream의 현재 contiguous seekable/buffered range를 timeline mapping과 prediction interval로 UTC interval `I_i`에 투영한다.

$$
I_i=[1000s_i+\hat o_i+margin^-_i,\;1000e_i+\hat o_i-margin^+_i]
$$

모든 `I_i`의 교집합에서 가장 최신이면서 안전 딜레이를 만족하는 target을 고른다. 교집합이 없으면 한 outlier에 맞춰 전체를 seek하지 않고 degraded/suggestion 상태로 둔다. target은 실제 `TimeRanges` 중 `currentTime`을 포함하거나 검증된 contiguous range 안에 있어야 한다.

제어 정책은 다음처럼 보수적으로 만든다.

```text
if epoch unsettled, stale, target interval invalid, or confidence low:
    playbackRate=1; hard seek 금지; 수동 보정 제안만 표시
elif P(|error| > hardSeekThreshold)와 controllability가 충분히 높음:
    seek 후 seeked/rVFC/currentTime으로 적용 결과 확인
elif rate correction의 방향이 충분히 확실함:
    uncertainty에 따라 gain을 낮춘 bounded rate correction
else:
    playbackRate=1
```

## 6. 우선순위별 개선안

각 항목은 이번 보고서 이후 구현할 때의 적용 위치를 제시한다. 현재 작업에서는 코드를 변경하지 않았다.

### 6.1 빠른 개선

#### Q1. 구조화된 playlist identity·epoch·stale 판정

- **적용 파일**: `HlsTimelineParser.cs`, `StreamSlotView.Sync.cs`, `StreamSyncModels.cs`, `HlsTimelineParserTests.cs`
- **필요 데이터**: request identity, path suffix와 response Content-Type, master/media/rendition, MSN, discontinuity sequence, URI/byte-range hash, tail PDT, target duration, endlist
- **핵심 알고리즘**: `.m3u8`뿐 아니라 표준 `.m3u`/HLS MIME을 안전하게 식별하고, segment 객체 파싱, progress key 중복 제거, explicit epoch reset을 수행한다. vendor tag는 검증 전 raw extension으로만 보존한다.
- **기대 효과**: CDN/플레이어 정렬 안정화. 실제 장면 시각 개선은 아님.
- **fallback**: 구조가 모호하면 해당 observation을 사용하지 않고 player live-edge estimate로 강등한다.

#### Q2. response 시각과 HTTP cache 의미 정상화

- **적용 파일**: `StreamSlotView.Sync.cs`, `StreamSyncModels.cs`, 새 telemetry recorder, 관련 tests
- **필요 데이터**: monotonic request start, response event/header time, body end, local UTC, `Date`, `Age`, cache allowlist, status, endpoint bucket
- **핵심 알고리즘**: `observedAt`을 body-read completion 하나로 쓰지 않고 세 시점을 분리한다. `Date`를 tail edge로 직접 사용하지 않는다. 같은 request correlation이 확보되기 전에는 RTT midpoint도 confidence를 낮춘다.
- **기대 효과**: response 지연·PC 부하 편향 제거, cache/stale 분류.
- **fallback**: PDT가 없고 검증된 bias prior도 없으면 `LiveEdgeEstimate`로 전환한다.

#### Q3. player range·event·presentation 계측 수정

- **적용 파일**: `StreamSlotView.Sync.cs`, `StreamSyncModels.cs`, `StreamSyncCoordinator.cs`, coordinator tests
- **필요 데이터**: full `buffered`/`seekable` ranges, `seeking`, `networkState`, 개별 waiting/stalled/error, rVFC metadata, dropped/total frames, JS monotonic sample time
- **핵심 알고리즘**: `currentTime` 포함 range의 buffer를 계산하고 gap-aware target validation을 한다. rVFC `mediaTime`을 우선 presentation sample로 사용한다. event별 recovery policy를 분리한다.
- **기대 효과**: 실제 화면에 제시되는 media 위치의 관측·제어 개선.
- **fallback**: rVFC 미지원 시 timestamp가 포함된 `currentTime` snapshot을 쓰고 confidence를 낮춘다.

#### Q4. 저신뢰 hard seek 억제와 command 확인

- **적용 파일**: `StreamSyncCoordinator.cs`, `StreamSlotView.Sync.cs`, models/tests
- **필요 데이터**: source/epoch 안정 여부, target range, command id, issue/apply/verify 시각, 후속 media position
- **핵심 알고리즘**: `CdnDate`, source switch, discontinuity 직후에는 hard seek를 금지한다. 3개 tick이 동일 playlist observation에서 파생되면 독립 확인으로 세지 않는다. `seeked`와 후속 sample로 성공을 확인한다.
- **기대 효과**: 잘못된 큰 보정과 반복 seek 감소.
- **fallback**: rate 1.0과 수동 suggestion으로 유지한다.

#### Q5. privacy-safe shadow telemetry

- **적용 파일**: 신규 `SyncTelemetryRecorder`, `DiagnosticReportService` 연계 후보, models
- **필요 데이터**: 8절의 최소 schema
- **핵심 알고리즘**: 기존 제어는 그대로 두고 raw/filtered 후보와 action outcome을 shadow log한다. query/cookie/token/raw signed URI는 저장하지 않는다.
- **기대 효과**: 근거 없는 threshold·정확도 추정을 피하고 다음 단계의 비교 가능성 확보.
- **fallback**: 사용자가 diagnostics를 끄면 aggregate counter만 메모리에 유지한다.

### 6.2 중기 개선

#### M1. CDP request graph와 active rendition 식별

- **적용 파일**: `StreamSlotView.Sync.cs` 또는 신규 `WebViewNetworkTimingObserver`, models/tests
- **필요 데이터**: CDP requestId, frameId/sessionId, initiator, document URL, response timing, cache flags, mime, master→media→segment URI graph
- **핵심 알고리즘**: `Network.requestWillBeSent`, `responseReceived`, `loadingFinished`를 requestId로 결합하고 selected frame/rendition과 correlation한다. worker request는 initiator chain과 observed segment request로 보수적으로 연결한다.
- **기대 효과**: playlist 혼합 제거와 network jitter 추정.
- **fallback**: mapping이 ambiguous하면 candidate를 제어에서 제외하고 log만 한다.

#### M2. robust timeline estimator

- **적용 파일**: 신규 `SyncTimelineEstimator`, `StreamSyncModels.cs`, `StreamSlotView.Sync.cs`, estimator tests
- **필요 데이터**: source epoch, raw offset, monotonic `Δt`, precision, RTT/cache, progress, context
- **핵심 알고리즘**: median/MAD gate + `[offset, drift]` Kalman을 기본 후보로 하고 Huber affine regression을 shadow 비교한다. `Q/R`과 gate는 held-out log로 결정한다.
- **기대 효과**: 고정 EMA의 outlier·불규칙 interval·drift 문제 제거.
- **fallback**: estimator가 발산하거나 interval이 넓으면 마지막 stable state의 uncertainty를 키운 뒤 live-edge estimate로 강등한다.

#### M3. interval 기반 공통 target과 uncertainty-aware controller

- **적용 파일**: `StreamSyncCoordinator.cs`, models/tests
- **필요 데이터**: per-member mapped playable interval, prediction covariance, bias interval, command outcome
- **핵심 알고리즘**: interval intersection, 확률 기반 rate/seek gate, action cooldown을 실제 독립 evidence로 갱신한다.
- **기대 효과**: 한 outlier가 전체를 지배하는 문제와 invalid seek 감소.
- **fallback**: 공통 interval이 없으면 자동 seek를 하지 않고 가장 문제인 source를 표시한다.

#### M4. 수동 보정의 로컬 계층 prior와 suggestion UI

- **적용 파일**: `MainWindow.Sync.cs`, `StreamSyncCoordinator.cs`, `SyncPresetNormalizationService.cs`, `PresetStorageService.cs`, models, 신규 `SyncBiasPriorStore`
- **필요 데이터**: stable channel id, session id, quality, endpoint, source, prior/residual/final delay, accepted/rejected 상태
- **핵심 알고리즘**: channel→channel×quality→channel×quality×CDN partial pooling, robust likelihood, recency decay, change detection. 처음에는 suggestion-only다.
- **기대 효과**: 상류 지연을 포함할 수 있는 반복 총잔차에 대한 수동 작업 감소. 성분별 원인은 식별하지 않는다.
- **fallback**: support·recency·consistency가 부족하면 상위 prior 또는 0으로 backoff하고 자동 적용하지 않는다.

#### M5. deterministic replay와 fault-injection test harness

- **적용 파일**: tests 프로젝트의 신규 estimator/replay tests, `HlsTimelineParserTests.cs`, `StreamSyncCoordinatorTests.cs`
- **필요 데이터**: scrubbed manifest fixtures와 synthetic trace
- **핵심 알고리즘**: duplicate plateau, cache age, RTT spike, PTS wrap, discontinuity, source switch, gap ranges, command failure를 주입한다.
- **fallback**: live trial 전 모든 후보를 shadow-only로 제한한다.

### 6.3 실험 후 결정할 개선

| 후보 | 적용 파일 | 필요 데이터 | 핵심 알고리즘 | 기대 효과 | 먼저 확인·채택 조건 | 실패 시 fallback |
| --- | --- | --- | --- | --- | --- | --- |
| LL-HLS part/blocking signals | `HlsTimelineParser.cs`, `StreamSlotView.Sync.cs`, models/parser tests | `PART`, part MSN/index, `SERVER-CONTROL`, preload/rendition report, request/cache timing | part progress key와 hold-back을 별도 epoch/source로 파싱하고 segment 관측과 교차검증 | P: 플랫폼 egress 추적을 촘촘하게 함 | 실제 SOOP 제공률·profile·cache 일관성이 충분하고 regular HLS보다 held-out freshness가 개선 | segment-level parser |
| TS/fMP4 container metadata passive parse | `HlsTimelineParser.cs` 또는 신규 metadata parser, models/tests | 허용된 기존 response의 timing header/box, track·timescale·암호화 상태, CPU 비용 | PTS rollover/`tfdt`·composition time을 풀어 PDT anchor에 mapping; media payload는 해석하지 않음 | 기본은 P: stream 내부 mapping 정밀화. 별도 송출측 계약이 capture 의미를 보장할 때만 S | 추가 fetch 없이 안정적으로 읽히고 mapping residual을 줄이며 자원 guardrail 통과 | PDT+EXTINF 근사 |
| `prft`/timed metadata | 신규 metadata parser와 `SyncTimelineEstimator`, models/tests | box/tag 존재, NTP/media 값, producer의 semantic contract | 문서화된 clock domain일 때만 별도 source로 fusion하고 epoch별 residual 검증 | P, 의미가 capture로 보장될 때만 S | SOOP에서 존재하고 의미가 공식 문서 또는 통제 실험으로 일관됨 | source를 사용하지 않음 |
| SOOP Open API/Extensions | `MainWindow.Sync.cs`, models, 신규 공식 API adapter 후보 | 승인된 credential flow, `broad_no`/`broad_start`/`startTime`, 약관·rate limit | 공식 ID를 session identity/reset key로만 정규화하고 frame clock으로 승격하지 않음 | P 보조: session 오염 감소 | 일반 WebView 적용 권한과 정책 운영이 가능하고 session identity 품질이 개선 | URL/페이지에서 합법적으로 얻은 공개 identity |
| pairwise graph model | `MainWindow.Sync.cs`, coordinator/models, 신규 `SyncBiasEstimator`, prior store/tests | 독립 session의 accepted pairwise label, context, support·cycle residual | robust graph objective, 명시적 gauge, component 분리, 계층 prior regularization | S, L: 연결된 채널 사이 과거 상대 편향 학습 | 연결성·label volume이 충분하고 session holdout에서 단순 계층 prior보다 개선 | 계층 channel prior |
| 여러 사용자 aggregate prior | local prior/consent UI 및 향후 별도 집계 경계; 현재 repo 밖 서비스는 별도 설계 | 명시적 opt-in label, coarse context, 삭제·retention·최소 cohort 상태 | 개인 prior와 분리한 robust aggregation, cohort suppression, per-user/session weighting | S, L: 희소 채널 cold-start 보조 | privacy·서비스 정책 review를 통과하고 local-only baseline 대비 명확한 추가 가치 | 온디바이스 개인 prior |

### 6.4 권장하지 않는 접근

- 영상 픽셀, 화면 내용, 음성, 오디오 파형을 분석·비교하는 방법
- 로그인, DRM, 암호화 key, 접근 통제를 우회하는 방법
- 독립 방송의 media sequence, PTS, URI 숫자를 같은 사건으로 간주하는 방법
- `Date` 또는 PDT가 있다는 이유만으로 capture time과 1.0 confidence를 부여하는 방법
- discontinuity/source epoch 없이 EMA·Kalman·RANSAC만 붙이는 방법
- reference extension처럼 signed playlist를 별도 재요청해 다른 reload의 header/body를 결합하는 방법
- 몇 번의 수동값을 channel의 영구 고정 offset으로 평균 내고 자동 적용하는 방법
- 동의 없이 channel viewing history와 signed URL을 중앙 수집하는 방법

## 7. 수동 보정 학습 설계

### 7.1 label은 실제 truth가 아니라 accepted proxy다

현재 양의 `ManualDelayMs`는 해당 stream을 늦춘다. 학습 시 다음을 분리 기록해야 한다.

- 알고리즘이 제안/자동 적용한 prior
- 사용자가 추가한 residual adjustment
- 최종 effective delay
- suggestion 수락·거절·되돌림
- 마지막 조정 뒤 안정 상태와 명시적 “정렬됨” 확인 여부

중간의 반복 클릭을 각각 독립 label로 세지 않는다. buffering, source change, quality switch, seek 중인 조정은 품질이 낮은 label로 제외하거나 큰 noise를 부여한다. **조정이 없었다는 사실도 0-offset label이 아니다.** 사용자가 차이를 보지 못했거나, 신경 쓰지 않았거나, session을 떠났을 수 있으므로 명시적 “정렬됨” 확인이 없으면 unlabeled로 둔다. suggestion 노출·확인 여부와 label coverage를 기록하고, label을 남긴 session의 선택 편향을 별도 보고한다.

### 7.2 상대값과 gauge

group `g`의 stream `i`, `j`에 대해 최종 accepted delay를 `d*`라 하면 학습 가능한 값은 다음 상대차다.

$$
y_{g,ij}=d^*_{g,i}-d^*_{g,j}
$$

모든 stream에 같은 상수를 더해도 상대 정렬은 같다. 따라서 connected pairwise graph에서도 전역 상수는 식별되지 않는다. 각 group의 weighted median을 0으로 정규화하거나 `Σw_i b_i=0` gauge를 고정한다. disconnected component는 data로 비교할 수 없고 prior에만 의존한다.

pairwise graph objective의 한 예는 다음과 같다.

$$
\min_b \sum_e w_e\rho\left(\frac{y_e-(b_i-b_j)}{\sigma_e}\right)+(b-p)^T\Lambda(b-p)
$$

cycle sum residual이 큰 label은 오입력, session 변화, context 누락 후보다. 한 group에서 생성된 모든 pair는 서로 상관되므로 독립 표본 수로 세지 않는다.

### 7.3 계층 partial-pooling prior

$$
d^*_{g,i}=\kappa_g+a_{channel(i)}+h_{channel\times quality}+j_{channel\times quality\times endpoint}+u_{session,i}+\epsilon
$$

- `κ_g`: group 공통 상수; pairwise 차분에서 제거
- channel, channel×quality, channel×quality×endpoint 항: fallback 계층과 일치하는 반복 prior
- session 항: 해당 방송에서만 생기는 변화; 미래에 정확히 예측할 수 없음
- sparse context: 상위 channel/global 평균으로 shrink
- label noise: Student-t 또는 Huber loss로 큰 오입력 영향 제한
- 오래된 observation: recency decay와 change-point로 영향 감소

작은 group의 과적합을 줄이는 partial pooling의 일반 원리는 다층 모델의 전형적 장점이다. [Gelman, 2006](https://doi.org/10.1198/004017005000000661)

추천 fallback은 `channel×quality×CDN → channel×quality → channel → 0`이다. 서로 다른 context가 충분히 교차 관측되지 않으면 channel과 CDN bias를 분리했다고 주장하지 않고 결합 context bias로만 저장한다.

### 7.4 적용 정책

1. 독립 session support가 부족하면 저장만 하고 UI에 표시하지 않는다.
2. support는 있으나 calibration이 부족하면 “과거 보정 제안”만 표시한다.
3. suggestion-only holdout에서 residual adjustment와 반대 방향 되돌림이 개선된 context만 opt-in 자동 초기값 후보가 된다.
4. 새 session, quality/CDN/source change가 감지되면 context를 다시 선택하고 uncertainty를 확대한다.
5. 사용자가 수정하면 timeline estimator는 유지하고 bias prior의 residual event만 갱신한다.

개인 사용자의 수동값은 취향과 지각을 포함할 수 있다. 기본 저장은 온디바이스로 하고, export/delete/retention을 제공해야 한다. 다중 사용자 집계는 명시적 opt-in, 희소 cohort 억제, raw URL·query·cookie 비수집, 개인 prior와 aggregate prior의 분리를 전제로 한다.

## 8. 필요한 로그·데이터 모델

### 8.1 최소 log schema

| entity | 핵심 필드 |
| --- | --- |
| `SyncSession` | schema/app/WebView version, local session id, redacted channel id, broadcast session id와 출처, quality/codec, CDN hostname bucket, network/PC-load bucket, 시작·종료 monotonic/UTC |
| `NetworkObservation` | request id, frame/session/initiator, URL runtime identity와 persistence hash, request start, response headers, body end, local UTC, status, protocol, cache flags, `Date/Age/Cache-Control/Expires/ETag/Last-Modified/Cache-Status`, byte count |
| `PlaylistSnapshot` | playlist identity/type/rendition, target/part target, MSN, discontinuity sequence, tail progress key, segment/part structured metadata, PDT raw string·fraction digits·timezone, LL-HLS fields, endlist, parse warnings |
| `ContainerTimingSample` | format, track type, timescale, raw/unwrapped PTS/DTS/PCR 또는 tfdt/composition time, encryption/parse status, source epoch |
| `PlayerSample` | JS monotonic sample time와 host receive time, currentTime, rVFC mediaTime/presentation/expectedDisplay/processing duration/presentedFrames, full buffered/seekable ranges, ready/network/seeking/paused/rate, 개별 events, dropped/total frames |
| `TimelineEstimate` | source type, source epoch, raw mapping, filtered offset/drift, covariance/prediction interval, innovation, MAD/Huber weight, context bias, stale/outlier/change reason |
| `SyncDecision` | common playable interval, chosen target/safety, timeline/bias/controllability confidence, policy/model/calibration version, suppressed-action reason |
| `SyncAction` | command id/type/target/rate, issue/apply/verify 시각, pre/post error, seeked/rVFC 확인, failure/reversal/stall outcome |
| `ManualCalibrationEvent` | old/new prior, residual, effective delay, group/reference, suggestion shown/accepted/rejected, stable-final flag, source/quality/endpoint context |
| `BiasPrior` | hierarchy key, posterior mean/variance 또는 robust interval, independent session support, last update, decay/change state, model version |

interval은 local monotonic clock을 사용한다. UTC는 절대 mapping과 audit에만 사용한다. page의 `Date.now()`와 host UTC를 그대로 비교하지 말고 page `performance.timeOrigin + performance.now()`와 host receipt의 mapping을 기록한다.

### 8.2 최소수집·보안

- cookie, authorization header, password, access token, signed query는 log 금지
- full manifest/segment URL은 runtime correlation 후 폐기; hostname, path template 또는 keyed hash만 저장
- raw playlist 저장은 별도 diagnostics opt-in과 automatic scrub 검증이 있는 짧은 fixture capture에만 허용
- channel id와 수동 이력은 viewing history이므로 local encryption/OS user boundary, retention, export/delete 필요
- container는 timing box/header만 읽고 payload sample, decoded frame, audio sample을 보존하지 않음
- encrypted/DRM media는 decrypt하거나 key를 추가 요청하지 않고 `metadata unavailable`로 fallback

### 8.3 실제 SOOP 계측 선행 계획

1. 사용자가 정상적으로 볼 권한이 있는 공개/허용 방송에서 passive diagnostics만 실행한다.
2. 각 quality와 여러 channel에서 master/media/segment request graph를 수집하되 credential·query를 즉시 redact한다.
3. PDT, vendor tag, LL-HLS, `Age`, cache header, content type, extensionless manifest의 **존재율과 일관성**만 먼저 측정한다.
4. 같은 request의 request start/header arrival/body end를 correlation해 현재 body-read 편향과 RTT 분포를 측정한다.
5. quality/CDN switch, discontinuity, buffering session을 별도 표시한다.
6. scrubbed fixture를 수동 검토해 secret이 없음을 확인한 뒤 unit/replay test에 넣는다.

이 계측 전에는 다음을 사실로 간주하지 않는다: SOOP PDT가 항상 존재함, PDT가 capture clock임, vendor timestamp가 10,000,000 tick/s의 video PTS임, SOOP이 LL-HLS임, CDN `Date`가 edge clock과 동기화됨, `Age`가 항상 제공됨.

## 9. 검증 실험과 성공 기준

### 9.1 평가 label과 오차 정의

수동 최종값은 실제 장면 time ground truth가 아니라 **사용자가 받아들인 상대 정렬 proxy**다. 같은 session의 최종값을 model에 학습시킨 뒤 그 값으로 평가하면 leakage로 오차가 인위적으로 0이 된다. 평가 단위는 500ms tick이 아니라 독립 방송 session/group이다.

baseline/후보가 예측한 delay를 `d_hat`, 그 session의 안정된 최종 수동값을 `d*`라 하면 pairwise 절대 proxy error는 다음과 같다.

$$
AE_{g,ij}=|[(\hat d_i-\hat d_j)-(d^*_i-d^*_j)]|
$$

- **수동 보정 전 절대 오차**: 자동 initial prediction과 `d*`의 pairwise 차
- **보정 제안 후 절대 오차**: suggestion을 적용한 뒤 사용자가 남긴 residual adjustment의 pairwise 크기
- **수동 보정 후 오차**: 같은 final label과 비교해 0으로 정의하지 않는다. 다음 독립 session label, 명시적 재확인, 또는 suggestion 이후 residual로 측정한다.

seen-channel temporal holdout과 unseen-channel holdout을 따로 보고한다. 학습/test는 session과 시간으로 분리한다. labeled-only 성능과 함께 eligible session 중 명시적 label coverage, labeled/unlabeled context 차이를 보고해 selection bias를 숨기지 않으며, 무조정 session을 임의의 0 label로 대치하지 않는다.

### 9.2 필수 지표

| 지표 | 정의·분모 |
| --- | --- |
| 수동 전/제안 후 절대 오차 | 위 `AE`; group 내 pair를 요약한 뒤 session 단위 집계 |
| median, p90, p95 | 긴 session의 tick 수가 가중치가 되지 않도록 session-level distribution에서 계산 |
| 초기 수렴 시간 | 두 개 이상의 usable source가 생긴 시점부터 estimator interval과 controller state가 사전 정의 안정조건을 연속 만족할 때까지 |
| 시간당 hard seek | 실제 active playback hour당 verified seek 수; 실패 seek 별도 |
| 잘못된 보정 발생률 | label이 있는 action 중 proxy error를 키웠거나 사용자가 반대 방향으로 되돌린 action 비율; source reset/invalid target action 별도 |
| 수동 조정 횟수·조정량 | session당 event 수와 residual의 총 절댓값; 중간 key repeat와 final accepted label 분리 |
| source별 confidence calibration | timeline은 known/paired anchor mapping, bias는 held-out manual proxy, controllability는 command outcome을 target으로 각각 reliability/Brier/interval coverage와 width 계산; 결합 `qε`는 별도 |
| player guardrail | stall/buffering time, rate≠1 시간 비율, command failure, seek 뒤 error 악화 |
| 자원 비용 | sync off 대비 CPU/GPU/memory, log I/O, network byte/request 증분 |

`ε`, 안정조건, reversal window, non-inferiority margin은 파일럿 전 임의로 쓰지 않는다. 파일럿의 baseline 분산, 사용자 허용 오차, hard-seek 오판 비용으로 정한 뒤 본 실험 전에 고정한다.

### 9.3 단계별 실험

#### 실험 A: parser·clock synthetic replay

- fixture: multi-rendition, duplicate playlist, stale plateau, sequence rollback, discontinuity, PTS wrap, multiple PDT, timezone 없음, LL part, gap, Date/Age/cache 조합
- 비교: 현재 parser/EMA, structured parser+median/MAD, Huber, robust Kalman
- 판정: epoch 혼합·stale update·잘못된 source 승격이 없어야 하며 known synthetic mapping의 interval coverage를 확인한다.

#### 실험 B: 실제 SOOP passive shadow

- 제품 제어는 변경하지 않고 현행 decision과 후보 decision을 함께 기록한다.
- 측정: manifest/source ambiguity, tag/header 제공률, response/body timing bias, estimator innovation, source switch 빈도
- 한계: offline replay는 seek/rate가 이후 player 상태를 바꾸는 폐루프 counterfactual을 재현하지 못한다.

#### 실험 C: suggestion-only 수동 prior

- 후보 prior를 자동 적용하지 않고 예상 보정으로 표시한다.
- 측정: suggestion 수락률, residual 조정량, 반대 방향 되돌림, 수동 event 수, seen/unseen channel holdout error
- channel별 최근 session과 과거 session을 시간순 분리한다.

#### 실험 D: 고신뢰 제어 A/B

- session/channel을 cluster로 하고 source·quality·endpoint 안에서 block randomization한다.
- 현행 controller와 confidence-gated controller를 비교한다.
- hard seek는 가장 신뢰도가 높은 tier부터 제한적으로 시작하고 guardrail 악화 시 즉시 suggestion-only로 fallback한다.

#### 실험 E: 선택적 source-side 기준 실험

협력 가능한 송출자가 있다면 NTP 동기화된 capture/encoder timestamp를 **별도 허용 telemetry나 표준 metadata**로 제공하는 lab stream을 사용한다. 픽셀·오디오를 분석하지 않고 상류 timecode가 있는 경우의 상한을 측정한다. 이 결과를 일반 SOOP 방송에 그대로 일반화하지 않는다.

### 9.4 층화 계획

모든 결과는 최소 다음 strata로 분리한다.

- network: RTT, throughput, response delay, loss/retry, cache hit 추정
- PC: CPU/GPU/memory pressure, dropped-frame rate, WebView runtime
- channel: cold-start/learned, seen/unseen, session 수
- quality/rendition/codec
- CDN endpoint/pathway
- 정상 재생 대 buffering/stall/seek 직후
- PDT, CDN-Date, live-edge, LL-HLS source
- source/discontinuity 전후

lab에서는 bandwidth/RTT/loss와 PC 부하를 통제해 반복하고, 실제 운영에서는 관측 bucket으로 층화한다. channel이 항상 같은 CDN/quality만 쓰면 효과가 collinear하므로 개별 bias로 해석하지 않는다.

### 9.5 성공 기준

고정 정확도 수치를 근거 없이 제시하지 않는다. 파일럿 후 다음 go/no-go를 사전등록한다.

1. held-out session에서 현행 대비 median·p90·p95 proxy error와 residual 수동 조정량이 개선된다.
2. 수동 조정 횟수와 총 조정량이 줄고, “수동 조정 없는 session” 비율이 증가한다.
3. hard seek, wrong correction, stall, resource overhead가 사전 정의 non-inferiority guardrail을 넘지 않는다.
4. confidence가 높을수록 실제 성공률이 단조롭게 높고 prediction interval coverage가 명목 수준과 맞는다.
5. aggregate 개선이 주요 network/PC/channel/quality/CDN strata의 악화를 숨기지 않는다.
6. session/channel cluster bootstrap 또는 계층 분석의 불확실성을 함께 보고한다.

## 10. 구현 단계 및 관련 파일

| 단계 | 산출물 | 기존 파일 | 신규 파일 후보 | 완료 gate |
| --- | --- | --- | --- | --- |
| 0. 계측 schema | privacy-safe shadow log, scrubber, versioning | `StreamSyncModels.cs`, `DiagnosticReportService.cs` | `SyncTelemetryRecorder.cs` | secret 누출 test, sync-off overhead baseline |
| 1. parser/source 정합성 | structured manifest, playlist graph, progress/epoch | `HlsTimelineParser.cs`, `StreamSlotView.Sync.cs` | `HlsPlaylistModels.cs`, `WebViewNetworkTimingObserver.cs` | captured/synthetic fixtures에서 master/rendition/discontinuity 정확 |
| 2. player/제어 안전성 | rVFC sample, full ranges, event 분리, command ack | `StreamSlotView.Sync.cs`, `StreamSyncCoordinator.cs`, `StreamSyncModels.cs` | 선택적으로 `SyncPlayerObserver` | invalid seek와 저신뢰 hard seek 0, fallback 검증 |
| 3. robust estimator | median/MAD, offset+drift, interval, reset | `StreamSlotView.Sync.cs`, models | `SyncTimelineEstimator.cs` | shadow holdout에서 calibration/오판 guardrail 통과 |
| 4. interval controller | common interval, uncertainty-aware rate/seek | `StreamSyncCoordinator.cs` | 없음 또는 `SyncControlPolicy.cs` | live A/B에서 proxy error 개선·guardrail 비열화 |
| 5. manual prior | channel/session identity, local hierarchy, suggestion UI | `MainWindow.Sync.cs`, `SyncPresetNormalizationService.cs`, `PresetStorageService.cs`, coordinator/models | `SyncBiasPriorStore.cs`, `SyncBiasEstimator.cs` | temporal/unseen holdout와 suggestion-only gate 통과 |
| 6. 조건부 기능 | LL-HLS, container metadata, official API, aggregate prior | 위 파일 | feature별 adapter | 실제 SOOP 계측과 정책 review 후 별도 결정 |

테스트 보강 지점은 다음과 같다.

- `HlsTimelineParserTests.cs`: tag 순서, 여러 PDT, discontinuity, gap, sequence, LL part, invalid vendor tag, timezone/precision
- `StreamSyncCoordinatorTests.cs`: low-confidence seek suppression, interval intersection 없음, source reset, duplicate evidence, gap range, command failure/verification
- estimator 신규 tests: outlier, stale majority, drift, change point, UTC step, covariance growth
- bias 신규 tests: gauge invariance, disconnected graph, partial-pooling backoff, recency/change, train/test leakage
- end-to-end replay: navigation/quality/CDN switch와 buffering fault sequence

## 11. 기술적 한계와 사용자에게 알려야 할 내용

### 11.1 보장할 수 없는 것

- 영상·음성 내용 비교나 송출측 공통 timecode 없이 임의 방송의 동일 장면을 자동 식별할 수 없다.
- PDT·PTS·HTTP timing을 정밀하게 처리해도 SOOP anchor 앞의 capture/OBS/encoder/upload/platform 차이는 남는다.
- 과거 channel prior는 다음 session의 장비·설정 변경을 알 수 없으며 정확한 truth가 아니다.
- `requestVideoFrameCallback`은 compositor 제출/예상 표시 시각까지의 신호이지 물리 display scanout이나 capture timestamp가 아니다.
- HTTP midpoint 반복 측정은 jitter를 줄일 수 있지만 server clock bias와 path asymmetry를 분리하지 못한다.

관측 불가능한 upstream delay 차이를 `U_i-U_j`라 하면 자동 장면 오차의 하한에는 그 불확실성이 그대로 포함된다. 실제 분포를 측정하지 않은 상태에서 “±N ms” 같은 수치를 제시하면 안 된다. HLS segment duration이나 500ms polling도 단독으로 정확도의 엄밀한 하한은 아니다. PTS와 rVFC가 있으면 segment보다 세밀한 mapping이 가능하지만, source semantics와 upstream uncertainty가 별도로 남는다.

### 11.2 UI 문구와 혼합 자동화

사용자에게는 최소 다음을 구분해 보여야 한다.

- `플랫폼 절대시각`: 검증된 PDT/PTS epoch
- `CDN 응답 기반 추정`: Date/cache/bias prior를 쓴 낮은 등급 source
- `라이브 엣지 거리 추정`: 서로의 실제 장면 시각을 모르는 fallback
- `과거 수동 보정 제안`: channel/context history 기반이며 support·마지막 관측 시각 표시

“절대”라는 표현은 PDT가 있다는 이유만으로 장면 절대시각을 뜻하지 않게 해야 한다. 저신뢰·source 변경·discontinuity·공통 playable interval 없음 상태에서는 자동 seek를 억제하고 다음을 제공한다.

- 왜 자동 보정을 보류했는지
- 제안 offset과 uncertainty/support
- 수동 적용/거절/초기화
- 새 session 또는 quality/CDN 변경으로 prior가 강등되었다는 알림

제품 설명에는 다음 취지를 명시해야 한다.

> 자동 동기화는 HLS/CDN과 플레이어 위치를 정렬하고, 과거 수동 보정 경향을 제안할 수 있습니다. 방송별 캡처·송출·인코더 지연은 외부 timecode가 없으면 직접 측정할 수 없으므로 동일 장면의 완전한 동기화를 보장하지 않습니다.

## 12. 출처

### 저장소 근거

- [README.md](../README.md)
- [HlsTimelineParser.cs](../src/StreamOrchestra.App/Services/HlsTimelineParser.cs)
- [StreamSyncCoordinator.cs](../src/StreamOrchestra.App/Services/StreamSyncCoordinator.cs)
- [StreamSlotView.Sync.cs](../src/StreamOrchestra.App/Views/StreamSlotView.Sync.cs)
- [StreamSyncModels.cs](../src/StreamOrchestra.App/Models/StreamSyncModels.cs)
- [HlsTimelineParserTests.cs](../src/StreamOrchestra.Tests/HlsTimelineParserTests.cs)
- [StreamSyncCoordinatorTests.cs](../src/StreamOrchestra.Tests/StreamSyncCoordinatorTests.cs)
- [reference/soop-multisync](../reference/soop-multisync/README.md)

### HLS·컨테이너·HTTP

- [RFC 8216: HTTP Live Streaming](https://www.rfc-editor.org/rfc/rfc8216.html)
- [HTTP Live Streaming 2nd Edition draft-22, 2026-05-01](https://datatracker.ietf.org/doc/html/draft-pantos-hls-rfc8216bis-22) — 아직 Internet-Draft이며 확정 RFC가 아님
- [Apple: Enabling Low-Latency HLS](https://developer.apple.com/documentation/http-live-streaming/enabling-low-latency-http-live-streaming-hls)
- [Apple HLS Authoring Specification](https://developer.apple.com/documentation/http-live-streaming/hls-authoring-specification-for-apple-devices/)
- [ITU-T H.222.0: MPEG systems timing](https://www.itu.int/dms_pubrec/itu-t/rec/h/T-REC-H.222.0-202504-I%21%21TOC-HTM-E.htm)
- [W3C MPEG-2 TS Byte Stream Format](https://www.w3.org/TR/mse-byte-stream-format-mp2t/)
- [W3C ISO BMFF Byte Stream Format](https://www.w3.org/TR/mse-byte-stream-format-isobmff/)
- [RFC 9110: HTTP Semantics](https://www.rfc-editor.org/rfc/rfc9110.html)
- [RFC 9111: HTTP Caching](https://www.rfc-editor.org/rfc/rfc9111.html)
- [RFC 9211: Cache-Status](https://www.rfc-editor.org/rfc/rfc9211.html)
- [RFC 5905: NTPv4](https://www.rfc-editor.org/rfc/rfc5905.html)

### WebView2·브라우저 media

- [Microsoft: WebView2 network request/response management](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/webresourcerequested)
- [Microsoft: WebView2 `WebResourceResponseReceived`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.webresourceresponsereceived)
- [Microsoft: WebView2 response event args](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webresourceresponsereceivedeventargs)
- [Microsoft: CoreWebView2WebResourceResponseView](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2webresourceresponseview)
- [Microsoft: WebView2 CDP method](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.calldevtoolsprotocolmethodasync)
- [Microsoft: WebView2 CDP event receiver](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.getdevtoolsprotocoleventreceiver)
- [Chrome DevTools Protocol Network domain](https://chromedevtools.github.io/devtools-protocol/tot/Network/)
- [WHATWG HTML media elements](https://html.spec.whatwg.org/multipage/media.html)
- [W3C Media Source Extensions](https://www.w3.org/TR/media-source-2/)
- [W3C Resource Timing](https://www.w3.org/TR/resource-timing/)
- [HTMLVideoElement.requestVideoFrameCallback](https://wicg.github.io/video-rvfc/)
- [W3C Media Playback Quality](https://w3c.github.io/media-playback-quality/)

### SOOP 공개 자료

- [SOOP Developers/Open API 안내](https://developers.sooplive.co.kr/?szWork=openapi)
- [SOOP Open API 상세: broad/list의 `broad_no`, `broad_start`, `time`](https://openapi.sooplive.co.kr/apidoc)
- [SOOP Extensions SDK](https://developers.sooplive.co.kr/?part=broadcast&sub=api&szWork=extension)

공식 공개 문서에서 현재 frame의 capture timestamp나 end-to-end latency를 반환하는 SOOP field는 확인하지 못했다. Open API의 방송 시작 시각은 session metadata이며 현재 장면 시각이 아니다.

### 추정·제어·학습

- [Kalman, 1960, A New Approach to Linear Filtering and Prediction Problems](https://doi.org/10.1115/1.3662552)
- [Huber, 1964, Robust Estimation of a Location Parameter](https://doi.org/10.1214/aoms/1177703732)
- [Fischler & Bolles, 1981, RANSAC](https://doi.org/10.1145/358669.358692)
- [Page, 1954, Continuous Inspection Schemes](https://academic.oup.com/biomet/article-abstract/41/1-2/100/456627)
- [Adams & MacKay, Bayesian Online Changepoint Detection](https://arxiv.org/abs/0710.3742)
- [Killick, Fearnhead & Eckley, PELT](https://arxiv.org/abs/1101.1438)
- [Gelman, 2006, Prior distributions for variance parameters in hierarchical models](https://doi.org/10.1198/004017005000000661)
- [Guo et al., 2017, On Calibration of Modern Neural Networks](https://proceedings.mlr.press/v70/guo17a.html)
