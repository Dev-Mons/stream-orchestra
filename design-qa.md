# Recording Workspace Refinement QA

- Selected visual direction: `D:\MyProject\stream-orchestra\output\ideation\selected-recording-workspace.png`
- Current Release workspace: `D:\MyProject\stream-orchestra\output\qa\recording-workspace-empty-final.png`
- Current Release add flow: `D:\MyProject\stream-orchestra\output\qa\recording-add-dialog-final.png`
- Combined visual evidence: `D:\MyProject\stream-orchestra\output\qa\recording-workspace-refinement-comparison.png`
- Viewport: 1220 x 868 for the WPF recording workspace.
- State note: the selected concept contains illustrative queue data, while the Release screenshot intentionally shows the empty persisted-catalog state. The state difference is required by the request to remove sample broadcasts and sample thumbnails; shell, proportions, actions, typography, and visual tokens are compared at the same viewport.

## Findings

- No actionable P0, P1, or P2 differences remain for the requested refinement.
- [P3] The official white SOOP symbol is visually brighter than the dark concept icon. This is intentional: it uses the official CI asset and is materially cleaner at 20–48 px.
- [P3] A populated-state screenshot was not manufactured because doing so would reintroduce sample content or overwrite the user's recording catalog. The live thumbnail pipeline was instead verified read-only against an active public SOOP page and its `og:image` response.

## Required Fidelity Surfaces

- Typography and spacing: Korean Segoe UI/Malgun Gothic hierarchy, the 35/65 queue-detail split, 64 px title region, persistent storage summary, and footer status remain aligned with the selected direction.
- Colors: deep navy surfaces, blue primary actions, low-contrast dividers, muted secondary copy, and green ready status are consistent across the workspace and add dialog.
- Brand assets: the previous noisy approximation was replaced by SOOP's official white symbol for title/fallback surfaces and by a rebuilt multi-resolution application icon. No handcrafted SVG, emoji, or text-symbol logo is used.
- Empty state: no sample cards or sample thumbnails are packaged or injected at runtime. The empty state clearly explains the next action and exposes both `방송 추가` entry points.
- Save destination: `저장 위치 변경` is persistent and global in the left storage summary. The add dialog contains no per-broadcast folder field and explicitly explains that every broadcast uses the common destination.

## Interaction Verification

- Add flow: the Release `방송 추가` button opened the modal, and `취소` closed it successfully through Windows UI Automation.
- Add dialog: URL, quality, and optional subscriber credentials remain; the per-broadcast output-folder control is absent.
- Recording action: active entries expose `녹화 중지`; stopping temporarily shows disabled `정리 중`; stopped, completed, failed, and restored entries expose `녹화 시작` again.
- Removal: inactive entries expose an explicit `방송 제거` action with confirmation. Removing an entry never deletes an already-recorded video file.
- Persistence: broadcast URL, quality, username requirement, metadata, thumbnail cache path, added time, and global output folder round-trip through `%LOCALAPPDATA%\StreamOrchestra\Data\recordings.json`. Passwords are not stored and restored entries remain idle until the user starts them.
- Metadata: yt-dlp is invoked in read-only metadata mode for title/channel. Because SOOP currently returns `thumbnail: null` through yt-dlp, the app also reads the broadcast page's `og:image`, downloads that actual live thumbnail, and caches it locally. The official SOOP symbol is used only when both paths fail.
- Concurrency: each active item owns an independent recording service/cancellation source and the existing maximum of five simultaneous recordings remains enforced.

## Verification Results

- `dotnet test StreamOrchestra.slnx -nologo`: 738 passed, 0 failed, 0 skipped.
- `dotnet build src/StreamOrchestra.App/StreamOrchestra.App.csproj -c Release -nologo`: 0 warnings, 0 errors.
- `git diff --check`: passed; only Git's existing LF-to-CRLF notices were emitted.
- Runtime Release QA: workspace, global storage action, official symbol, add modal, and modal close behavior verified in the actual WPF application.
- Browser-console checks are not applicable to this native WPF surface.

## Implementation Checklist

- [x] Real SOOP metadata and thumbnail pipeline
- [x] All packaged sample thumbnails removed
- [x] Stop-to-record button lifecycle
- [x] Explicit broadcast removal
- [x] Persisted broadcast catalog and safe idle restore
- [x] Official SOOP symbol and rebuilt application icon
- [x] One global recording destination
- [x] Release build, automated tests, and native visual QA

final result: passed
