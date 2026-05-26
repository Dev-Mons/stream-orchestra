# Stream Orchestra

Windows-only WPF + WebView2 feasibility spike for testing whether SOOP streams can run concurrently across separate WebView2 profile groups.

## Run

```powershell
dotnet run --project src\StreamOrchestra.App
```

## Feasibility Status CLI

```powershell
dotnet run --project src\StreamOrchestra.Tools -- status
```

`status`, `record`, and `report` print the current recommendation, next action, compact plan-gate audit summary, overall plan-verification status, Phase 0 success-gate status, and suggested `record` shapes for missing evidence. When multiple 9+ results exist, the current recommendation follows the latest 9+ result.

Run a local preflight check before live SOOP testing:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- preflight
```

`preflight` reports the data/result paths, A-D profile folders, WebView2 Runtime availability, playback-test layout coverage for 4/8/9/12/16, current decision, gate summary, and suggested evidence records. Add `--output .\phase0-preflight.txt` to save the exact preflight text as the first manual-verification artifact.

Create a complete handoff bundle for a manual SOOP verification run:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- handoff --output-folder .\phase0-handoff
```

`handoff` saves `phase0-preflight.txt`, `phase0-checklist.txt`, `phase0-audit.txt`, `phase0-verification.txt`, `phase0-history.txt`, `phase0-diagnostic-report.json`, a normalized `phase0-results.json` evidence snapshot, and `phase0-handoff-manifest.json` together. Use `--data-folder <path>` and `--profile-folder <path>` when inspecting non-default runtime data.

Print the Phase 0 manual test order from `docs/plan.md` before recording live SOOP evidence:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- checklist
```

`checklist` prints the current evidence status, outstanding plan gates, suggested records, and the safe SOOP test flow: preflight, A-D same-account login, restart persistence, Group A isolation, 8/9/12/16 playback, CPU/GPU/memory observations, evidence labels, `record --dry-run`, final success recording, and `verify`. Use `--data-folder <path>` to inspect a non-default result file, and `--output .\phase0-checklist.txt` to save a handoff copy.

Audit the remaining Phase 0 gates from `docs/plan.md`, including suggested `record` shapes for missing evidence:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- audit
```

Save the same audit text as a handoff artifact:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- audit --output .\plan-audit.txt
```

Verify whether the Phase 0 plan verification is complete. This exits `0` only when the recorded evidence covers distinct 4-slot Group A single-profile, 8-slot, 9-slot threshold, 12-slot, and 16-slot playback checks, plus A-D account-label, restart, resource, CPU, GPU, and memory evidence; when it fails, it prints each outstanding pending/fail gate and suggested `record` shapes for the missing evidence:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- verify
```

Add `--output .\phase0-verification.txt` to save the exact verification result as a handoff artifact.

Check the external-browser fallback candidates:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- browsers
```

Export a reviewable external-browser fallback script from the last saved session:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- fallback
```

Inspect the saved feasibility result history with recorded decision snapshots:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- history
```

List the named playback and isolated-group scenarios before recording manual evidence:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- scenarios
```

Record a manual result without opening the WPF app:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- record --count 9 --outcome success --account --account-label main_soop --profile-groups A,B,C --restart --resources --cpu-percent 45 --gpu-percent 60 --memory-mb 12000 --scenario groups_a_b_c_9_slot_threshold --scenario-name "Groups A/B/C, 9-slot success threshold" --notes "manual SOOP test"
```

Add `--dry-run` first to validate and preview the decision/audit output without saving to `feasibility-results.json`.

Record an isolated profile-group check without manually typing the scenario ID:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- record --group D --outcome partial --account --profile-groups D --account-label main_soop --notes "Group D session check"
```

`success` requires 9 or more streams plus `--account`, required `--profile-groups`, `--restart`, `--resources`, `--cpu-percent`, `--gpu-percent`, and `--memory-mb`; otherwise record `partial` or `failure`. Use the same non-sensitive `--account-label <text>` for every SOOP account evidence record across A-D; records with `--account` and `--profile-groups` require a label, final same-account verification requires one shared non-empty label, and conflicting labels fail the same-account gate. A 9-slot success proves the threshold scenario for A/B/C, but final verification still requires A-D same-account evidence. `--resources` always requires the CPU, GPU, and memory values so resource acceptability has structured evidence. If `--scenario` is omitted, the CLI derives the scenario from `--count` using the same 4/8/9/12/16 playback-test names as the WPF app. Use `--group A`, `--group B`, `--group C`, or `--group D` for isolated profile-group evidence; without `--count`, it records the group's 4-slot test. Profile-group evidence and playback count must match the selected scenario, so Group A evidence cannot be recorded against a Group D scenario and a single-group scenario cannot be recorded as a 16-slot result.

Use `partial` when the requested playback count or isolated group visibly plays but success-only evidence is incomplete; use `failure` when the requested playback count or group does not work. `success` is reserved for the 9+ Phase 0 success path.

Export a diagnostic report from the CLI:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- report
```

Use `--data-folder <path>` to inspect a non-default data folder.

## Spike Scope

- 16 WebView2 slots in a 4x4 grid.
- Layouts are loaded from `data/layouts.json`.
- The app includes `8 Small + 1 Main` and `4x4 Tournament` layouts; fresh sessions default to `8 Small + 1 Main`.
- Slots 1-4 use Profile Group A.
- Slots 5-8 use Profile Group B.
- Slots 9-12 use Profile Group C.
- Slots 13-16 use Profile Group D.
- Each group uses a separate persistent WebView2 user data folder under `%LOCALAPPDATA%\StreamOrchestra\Profiles`.
- The top URL bar can load all slots or a selected profile group.
- The selected profile group can also be tested in isolation, blanking all other groups before loading.
- Whole-app and group loads switch to a layout that contains the target slots before loading, so hidden slots are not started by accident.
- 4, 8, 9, 12, and 16 slot playback test buttons load the first N slots and blank the rest; 12/16-slot tests switch to a layout that shows enough slots.
- Each slot has an individual URL box, load action, compact refresh/mute/menu controls, and pinned-or-hover control bar mode; the slot menu can copy the current URL, clear the slot, or load SOOP home.
- Click a slot control bar to select it.
- Slot control bars show `Slot N / stream name`; names are saved in presets and move with the stream during slot swaps.
- Slot saved URLs follow WebView navigation, redirects, and source changes, so restored sessions use the latest observed slot URL.
- Slot display names use the loaded page title when no explicit favorite or preset name was supplied.
- The left SOOP explorer panel can load a page and send the current URL into the selected slot.
- The explorer panel can save app-local favorites and load them into the selected slot.
- Explorer and favorite insertion are button-based for the MVP; drag insertion from the explorer/favorites is intentionally not exposed.
- App-local favorites are shown by most recent use, then by name.
- Restored or layout-changed selections are kept on visible slots, so explorer and favorite insertion cannot accidentally start a hidden slot.
- Explorer current URL and default favorite names follow WebView source/title changes.
- The explorer panel, per-slot URL editors, and slot control bars can be hidden to maximize video area; those view options are restored on next launch.
- Drag the `⋮⋮` handle from one slot to another to swap only the stream URLs. Mute state and profile group stay attached to the slot.
- Presets, favorites, and the last session are saved as JSON under `%LOCALAPPDATA%\StreamOrchestra\Data`; saves are written through a same-folder temporary file before replacing the target JSON.
- The preset toolbar supports loading, saving the current preset, saving as a new preset, and reverting transient edits back to the active saved preset.
- Closing the app saves the current transient session separately from named presets.
- If a saved JSON file is corrupt, the app quarantines it as `*.corrupt.*` and continues with an empty/default state instead of failing at startup.
- Loaded workspaces and restored last sessions are prepared before use: invalid slots are ignored, missing slots become `about:blank`, duplicate slots use the last value, URLs are normalized, profile groups stay tied to slot number, and slots outside the resolved layout are blanked before WebViews are loaded.
- The toolbar shows WebView2 process count, CPU sample, working set memory, and private memory to support the manual feasibility check.
- Feasibility test outcomes can be recorded to `%LOCALAPPDATA%\StreamOrchestra\Data\feasibility-results.json` after running a playback test or group load.
- Recorded feasibility results include playback count, test scenario, same-account session persistence, account label, verified profile groups, restart session persistence, structured CPU/GPU/memory observations, resource acceptability, and a decision snapshot matching the Phase 0 decision path at record time. Full plan verification requires A-D same-account profile-group evidence with one shared non-empty account label, scenario/profile-group and scenario/playback-count consistency checks prevent mismatched evidence from being saved or counted, and `리소스 OK` cannot be recorded without all three manual resource values.
- CLI records default to the same named scenarios as WPF playback tests when `--scenario` is omitted, so 9/12/16-slot evidence remains traceable; `--group A-D` records isolated profile-group evidence without manually typing `isolated_group_*` scenario IDs.
- The WPF feasibility row shows the current playback scenario and slot count before a result is recorded.
- The WPF feasibility summary shows overall plan-verification status plus plan-gate audit counts, and `감사 복사` copies the detailed audit text plus suggested `record` shapes for sharing test evidence.
- Feasibility decisions include a concrete next action, such as continuing the WebView2 MVP, repeating targeted experiments, or exporting an external-browser fallback script.
- The CLI `status` and `record` commands print suggested `record` shapes for missing evidence after the current recommendation and plan-gate status, including `--account-label <label>` where same-account evidence is expected; `record --dry-run` validates and previews the same decision/audit without saving, and 9-slot success suggestions are listed after higher-slot playback/account evidence so the latest 9+ result can remain the final success record.
- The CLI `audit` command maps recorded results to the remaining plan gates: 4-slot Group A single-profile playback, distinct 8-slot split-profile, 9-slot threshold, 12-slot, and 16-slot playback evidence, A-D account persistence, restart persistence, resource acceptability, structured observations, and the Phase 0 success gate. Add `--output <path>` to save the audit text.
- The CLI `history` command lists saved feasibility results with their recorded decision snapshots for manual-test audit trails.
- The CLI `checklist` command prints the ordered manual SOOP verification flow from `docs/plan.md` with the current evidence status, outstanding gates, a `record --dry-run` preview step, and suggested `record` shapes before evidence is recorded. Add `--output <path>` to save the checklist text.
- The CLI `preflight` command can save the runtime/profile/layout readiness text with `--output <path>` so the manual verification run has a setup artifact before live SOOP playback evidence is recorded.
- The CLI `handoff` command saves the preflight, checklist, audit, verification, history, diagnostic report JSON, normalized feasibility-results JSON, and manifest artifacts into one folder for manual SOOP test handoff or post-run review.
- The CLI `scenarios` command lists the named playback and isolated-group scenarios with copyable partial/failure shapes, explains when partial evidence counts as visible playback evidence, and prints separate 9+ success shapes that include restart, resource, CPU, GPU, and memory evidence.
- The CLI `verify` command exits `0` only when every Phase 0 plan gate passes, and exits non-zero with outstanding pending/fail gate details plus suggested `record` shapes while manual SOOP evidence is still pending or failed. Add `--output <path>` to save the verification text.
- The CLI `browsers` command prints installed/missing external-browser fallback candidates, including custom candidates.
- The CLI `fallback` command exports a reviewable external-browser PowerShell script from visible last-session slots when launchable HTTP/HTTPS stream URLs and installed fallback browsers are available; muted slots include Chromium `--mute-audio`, and when layout data is available, the script passes Chromium window-position/window-size arguments and then applies a Windows window placement fallback from the saved layout.
- The `브라우저 스크립트` button exports the same reviewable external-browser script directly from the current session without writing a diagnostic report, including current layout placement.
- The `리포트 저장` button exports a diagnostic JSON report with profile folders, data files, saved workspace/favorite/last-session counts, Chrome/Edge/Whale/Brave/Vivaldi discovery, custom browser candidates, latest feasibility result, account-label summary/conflict status, current recommendation, plan-gate audit, suggested `record` shapes, and an external-browser fallback launch plan for active HTTP/HTTPS stream URLs in the current session. Blank, malformed, or non-web saved slot URLs are ignored. When a launchable fallback plan exists, it also writes a reviewable PowerShell script with per-slot browser/profile/URL comments instead of starting browsers automatically; otherwise the UI/CLI prints why no script was created.
- Optional extra Chromium-compatible fallback browsers can be added in `%LOCALAPPDATA%\StreamOrchestra\Data\external-browsers.json`:

```json
[
  {
    "id": "portable_chrome",
    "name": "Portable Chrome",
    "candidatePaths": ["D:\\Browsers\\PortableChrome\\chrome.exe"]
  }
]
```

This spike does not bypass DRM, authentication, or platform security controls. It only recreates the normal multi-browser style test inside separate WebView2 user data folders.

## Manual Verification Checklist

1. Run the app.
2. Load SOOP in Group A and sign in.
3. Close and reopen the app, then confirm the Group A session is still present.
4. Repeat for Groups B, C, and D.
5. Use `그룹 단독` with Group A to verify one-group playback behavior without stale streams in other groups.
6. Test 4, 8, 9, 12, and 16 simultaneous playback counts.
7. Record the account label, CPU, GPU, memory, and whether SOOP allows at least 9 active streams.

Use `docs/feasibility-test.md` as the detailed result sheet for the Phase 0 decision.
Use `docs/implementation-status.md` to see how the current code maps to `docs/plan.md` and what evidence is still pending.
