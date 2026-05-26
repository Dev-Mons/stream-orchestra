# Stream Orchestra Implementation Status

This document maps `docs/plan.md` to the current implementation. It separates implemented software behavior from evidence that still requires manual SOOP account/player verification.

## Current Build Evidence

Last verified commands:

```powershell
dotnet build StreamOrchestra.slnx
dotnet test StreamOrchestra.slnx --no-build
```

Current automated test coverage: 450 passing tests.

## Phase 0 Feasibility Spike

| Requirement | Status | Evidence |
| --- | --- | --- |
| WPF app | Implemented | `src/StreamOrchestra.App` |
| WebView2 usage | Implemented | `StreamSlotView`, `ExplorerPanel`, `Microsoft.Web.WebView2` package |
| 16 WebView2 slots | Implemented | `MainWindow.CreateSlots()` |
| 4x4 grid | Implemented | `data/layouts.json` / `layout_4x4` |
| Profile Group A/B/C/D | Implemented | `WebViewProfileService.GetGroupForSlot()` maps slots 1-4, 5-8, 9-12, and 13-16 to A-D, and tests verify the public slot profile groups expose exactly A-D |
| Separate persistent user data folders | Implemented | `%LOCALAPPDATA%\StreamOrchestra\Profiles\GroupA` through `GroupD`; tests verify A-D use distinct persistent folders under the profile root |
| URL input and load | Implemented | Global URL bar, group load, whole-app load, blank-all action, and per-slot URL editor; XAML tests verify the global URL input, scope selector, and load buttons are wired |
| Isolated profile group playback test | Implemented | `그룹 단독` blanks non-selected groups before loading the selected group, and XAML tests verify the A-D scope selector plus isolated-load action |
| 4/8/9/12/16 playback test buttons | Implemented | `PlaybackTestPlanService` |
| Playback test layout visibility | Implemented | 12/16 tests auto-select a layout that contains the actual playback-plan slot IDs, preflight checks the same 4/8/9/12/16 slot coverage, and the app fails explicitly if no such layout exists |
| Scope load layout visibility | Implemented | Whole-app and group loads switch to a layout containing the target slots before loading and fail explicitly if no such layout exists |
| Per-slot refresh and mute | Implemented | `StreamSlotView`; XAML tests verify the mute and refresh controls are wired to their handlers |
| App restart session test support | Implemented as tooling | Persistent WebView2 folders plus `docs/feasibility-test.md` |
| Feasibility scenario evidence | Implemented | `FeasibilityScenarioService` records whether the result came from Group A, 8-slot, 9-slot threshold, 12-slot, 16-slot, or manual group load; CLI `record` derives the same named scenario from `--count` when no custom scenario is supplied, `--group A-D` records isolated profile-group evidence without manually typing `isolated_group_*` scenario IDs, known scenario IDs validate their expected playback count, and `StreamOrchestra.Tools scenarios` lists the WPF/CLI scenario IDs plus valid partial/failure shapes, the partial/failure decision rule, and separate 9+ success shapes with restart/resource/CPU/GPU/memory evidence before manual recording |
| Feasibility result recording guard | Implemented | WPF result buttons require a playback/group load scenario before saving a result, show the current scenario/count before recording, CLI `record --dry-run` validates and previews the decision/audit without appending to `feasibility-results.json`, validation returns clear errors for missing or invalid outcomes, rejects `failure` records that include restart or resource OK evidence, CLI commands report filesystem/path failures as command failures instead of unhandled exceptions, audit/verify tolerate null outcomes or scenario IDs in hand-edited result files, malformed outcome evidence keeps the decision pending instead of forcing the external-browser failure path and does not prove account/restart/resource gates, and result storage normalizes hand-edited IDs/scenarios/known outcome casing/groups/decision text while dropping out-of-range playback counts, invalid or scenario-mismatched profile groups, invalid resource observations, incomplete account/restart/resource flags, hand-edited restart/resource flags on failure records, incomplete success outcomes, stale decision snapshots when decision-driving evidence is normalized, and restoring missing diagnostics |
| Group-level account evidence | Implemented | Feasibility results store verified profile groups A-D plus an account label; WPF exposes an account-label field and A-D checkboxes beside `계정 유지` and enables them only while `계정 유지` is checked, CLI `record` accepts `--account-label` and `--profile-groups`, account labels are accepted and retained only with same-account evidence, same-account evidence requires at least one scenario-consistent verified profile group plus a non-empty label, normalized result loading/saving clears hand-edited account flags that lack a label or scenario-consistent verified group, success records require the groups implied by the playback count, final MVP recommendation requires A-D same-account coverage using the latest scenario-consistent evidence per profile group with one shared non-empty account label, conflicting labels fail the same-account gate, scenario-specific failure records invalidate older same-account coverage for that group, partial playback evidence without same-account evidence does not invalidate older coverage, custom/unknown failure records without checked groups do not invalidate older coverage, malformed-outcome records cannot prove or invalidate group coverage, and scenario/profile-group consistency checks reject or ignore mismatched, blank, or null evidence |
| Structured resource observations | Implemented | Feasibility results store manual CPU %, GPU %, and memory MB alongside automatic WebView2 diagnostics; shared validation rejects non-finite values and invalid ranges, any non-failure `리소스 OK`/`--resources` record requires all three manual values, failure records cannot claim resource OK evidence, and normalized result loading/saving drops invalid hand-edited observation values and clears resource flags when the result is a failure or unless all three observations are complete and valid |
| Feasibility decision snapshot | Implemented | WPF and CLI result recording store the decision code/title/detail/next action that applied when the result was recorded |
| Feasibility preflight check | Implemented | `StreamOrchestra.Tools preflight` reports data/result paths, a non-destructive data-folder write check, A-D profile folders, WebView2 Runtime availability, playback-test layout coverage for 4/8/9/12/16, current decision, audit summary, verification status, success gate, and suggested record shapes before live SOOP testing, including `[blocked]` data storage diagnostics when the data folder cannot be created or written; `--output` saves the same setup artifact, `StreamOrchestra.Tools handoff` saves preflight/checklist/audit/verification/history text artifacts plus diagnostic report JSON, a normalized feasibility-results JSON snapshot, and a manifest with data storage status, preflight profile root, WebView2 runtime status, playback layout status, A-D profile group status, preflight readiness, verification status, current decision, gate counts, outstanding gate count, artifact file list, file sizes, and SHA-256 hashes into one folder, can still write a blocked-storage bundle when `--data-folder` cannot be opened but `--output-folder` is usable, and `validate-handoff` verifies the handoff folder has no extra files/directories, the canonical manifest JSON, generated-at/path consistency, the required standard artifact list and order, rejects unexpected or duplicate manifest entries, requires exact A-D manifest profile groups under the profile root, and checks saved artifacts, normalized results snapshot content, preflight/checklist/audit/history/verification content consistency, diagnostic report generated-at proximity plus data/results/profile context including standard appstate/workspaces/favorites/feasibility/external-browser data-file entries, A-D profile groups, workspace diagnostics, external-browser/fallback snapshot consistency including slot browser metadata, profile folders, and launch arguments, full decision snapshot, audit items, result count, decision, and recomputed gate summary against that manifest with optional `--output` text saving |
| Feasibility manual checklist | Implemented | `StreamOrchestra.Tools checklist` prints the current evidence status, outstanding gates, suggested `record` shapes, and the ordered Phase 0 manual SOOP verification flow from `docs/plan.md`: preflight, A-D same-account login, restart persistence, isolated Group A, 8/9/12/16 playback, CPU/GPU/memory observations, shared account label use, `record --dry-run` preview, final 9+ success evidence, `verify`, and final `handoff`/`validate-handoff` bundle review; `--data-folder` inspects alternate result files and `--output` saves a handoff copy |
| Plan gate audit | Implemented | WPF feasibility summary/copy button, `StreamOrchestra.Tools status`/`record`/`history`/`audit`/`report`/`verify`, and diagnostic reports include pass/pending/fail for the remaining `docs/plan.md` manual gates plus the next action for the current decision and suggested `record` shapes; WPF/CLI output shows overall plan-verification status plus exact 4-slot Group A, distinct 8-slot split-profile, 9-slot threshold, 12-slot, 16-slot, session, resource, structured observation, and Phase 0 success-gate states directly, `history` lists recorded decision snapshots, `audit --output <path>` saves a text artifact with compact pass/pending/fail summary, overall verification status, success gate, and suggested `record` shapes, WPF `감사 복사`, CLI `status`, CLI `record`, CLI `audit`, CLI `report`, report JSON, and CLI `verify` include suggested `record` shapes for missing evidence with `--account-label <label>` on same-account examples, 9-slot success suggestions are ordered after higher-slot playback/account evidence so the latest 9+ result can remain the final success record, `verify --output <path>` saves the exact verification artifact, `verify` exits `0` only when every Phase 0 plan gate passes and lists outstanding pending/fail gates plus suggested `record` shapes otherwise, its required-evidence summary explicitly calls out A-D account-label evidence, playback-count gates use their own latest slot-count evidence, the Group A gate requires exact 4-slot single-profile evidence, exact 8/9/12/16 playback gates require the matching plan scenario IDs, the 9-slot threshold gate requires an exact 9-slot result, the Phase 0 success/failure gate also requires a matching 9/12/16 plan scenario, mismatched or ambiguous scenario/playback-count records cannot satisfy gates, the same-account gate requires A-D profile-group evidence with one shared non-empty account label, restart evidence requires same-account evidence with an account label and the profile groups required for that 9+/12/16 playback count, resource acceptability evidence requires complete structured CPU/GPU/memory observations, and playback-only partial records do not invalidate older same-account, restart, resource, or structured-observation evidence while matching failure records fail those gates even if hand-edited criteria flags are true |
| SOOP 9-slot threshold playback | Manual verification pending | Requires live SOOP account/player test |
| Same-account session persistence | Manual verification pending | Record with `계정 유지`, the same account label, and verified profile groups A-D |
| Restart session persistence | Manual verification pending | Record with `재실행 유지` checkbox |
| Resource acceptability | Manual verification pending | Fill CPU/GPU/memory and record with the `리소스 OK` checkbox |
| Phase 0 decision recommendation | Implemented | `FeasibilityDecisionService` bases the current recommendation on the latest 9+ slot result, only recommends embedded WebView2 MVP when the result is a matching 9/12/16 plan playback scenario with A-D same-account evidence, restart persistence, resource acceptability, and valid structured resource observations, and only recommends external-browser transition from a matching 9/12/16 plan-scenario failure rather than an ambiguous custom failure record |
| External browser fallback discovery | Implemented | `ExternalBrowserDiscoveryService` reports Chrome/Edge/Whale/Brave/Vivaldi availability, merges custom candidates from `external-browsers.json`, ignores malformed/null custom candidate entries, and `StreamOrchestra.Tools browsers` prints installed/missing candidates |
| External browser fallback launch planning | Implemented as non-executing plan | `ExternalBrowserLaunchPlanService` maps active visible current-session HTTP/HTTPS stream URLs to installed browsers, per-slot profile folders, mute flags, display names, and layout window coordinates when a matching layout is available, falls back to the default layout when a saved session has no layout id, and ignores incomplete browser entries plus hidden, out-of-range, blank, malformed, null, or non-web saved slot URLs; fallback layout mapping also tolerates null layout entries, null/invalid layout slots, invalid grid sizes, and duplicate slot entries |
| External browser fallback script export | Implemented | `ExternalBrowserFallbackExportService` writes a reviewable PowerShell script with plan, browser, executable, profile, URL, Chromium mute/window-position/window-size arguments, and repeatable Windows placement fallback commands when a launchable plan with layout data exists; script generation skips incomplete slot plans, rebuilds missing browser arguments from the slot URL/profile/mute state, ignores invalid window layouts, and reports a clear error if no slot can be scripted; `StreamOrchestra.Tools fallback` exports the script directly from the last saved session, the WPF `브라우저 스크립트` button exports it from the current session, and WPF/CLI status includes the reason when no script can be created |

## MVP Feature Coverage

| MVP item from plan | Status | Evidence |
| --- | --- | --- |
| SOOP-centered multiview | Implemented | 16 WebView2 slots and SOOP default URL |
| 8 Small + 1 Main layout | Implemented as default | `data/layouts.json` / `layout_8_small_1_main`, `LayoutPresetService.SelectDefaultLayout()`, and tests lock the plan's 1-8 small-slot plus 9 main-slot geometry |
| 4x4 tournament layout | Implemented | `data/layouts.json` / `layout_4x4`, and tests lock the plan's 1-16 row-major 4x4 geometry |
| Layout validation | Implemented | `LayoutPresetService` rejects null layout/slot entries, missing/duplicate IDs, invalid grid sizes, duplicate/out-of-range slots, missing slot collections, out-of-bounds coordinates, and overlapping slot cells |
| Slot URL storage | Implemented | `WorkspacePreset`, `AppState.LastSession` |
| Slot URL sync after WebView navigation | Implemented | `StreamSlotView` listens to WebView2 source changes before saving presets/session state |
| Slot display names from page title | Implemented | `StreamSlotView` uses WebView2 document titles when no explicit stream name was supplied |
| Slot stream name display/storage | Implemented | `WorkspaceSlot.StreamName`, `StreamSlotView` control bar |
| Last state restore on launch | Implemented | `PresetStorageService`, `MainWindow_Loaded`; saved window placement is rejected for non-finite or invalid sizes and clamped to the current virtual screen before restore |
| Preset save/load/revert | Implemented | Preset toolbar includes load, current-state save, save-as, and revert-to-active-preset actions; the save-as dialog requires a preset name and supports Enter-to-save plus cancel; `workspaces.json` and `appstate.json` keep saved presets separate from transient last sessions; JSON list loads ignore null entries and saves write a same-folder temporary file before replacing the target, and diagnostic reports include saved workspace count, favorite count, last-session presence, selected slot, layout, slot count, and active stream URL count |
| Slot top control bar | Implemented | `StreamSlotView` uses compact drag, mute, refresh, and menu controls with title trimming; XAML tests verify the individual URL load controls, drag handle, drop target, mute, refresh, and slot menu actions |
| Slot control bar auto-hide | Implemented | `ToggleSlotControlBarsButton`, `AppState.AreSlotControlBarsAlwaysVisible`; XAML tests also verify explorer, slot URL editor, and control-bar visibility toggles |
| Drag handle slot swap | Implemented | `SlotSwapService`, `⋮⋮` drag handle, and XAML tests verify swap dragging starts from the handle while the slot border is the drop target; stream URL/name identity is normalized at the swap boundary while mute state and profile group remain attached to the slot |
| SOOP explorer panel | Implemented | `ExplorerPanel` provides SOOP navigation, back, refresh, current URL insertion into the selected slot, current URL favorite creation, and favorite insertion into the selected slot |
| Explorer current URL/title sync | Implemented | `ExplorerPanel` listens to WebView2 source and title changes before inserting current URL or creating a favorite |
| App-local favorites | Implemented | `FavoriteStorageService`, `favorites.json`; favorites are shown by most recent use, then by name; blank favorite names fall back to URL-derived names and stable `favorite` IDs with collision suffixes; malformed hand-edited favorite lists ignore blank URLs, trim text fields, de-duplicate IDs, and saves go through the shared temporary-file JSON writer |
| Selected slot insertion | Implemented | Explorer and favorite insertion into selected slot |
| MVP drag insertion deferral | Implemented | Explorer/favorite insertion remains button-based; `ExplorerPanelLayoutTests` verifies drag/drop registration is not exposed for MVP favorite insertion |
| Visible selected slot guard | Implemented | `SlotSelectionService` keeps restored/layout-changed selections on visible slots before explorer or favorite insertion and tolerates null saved layout slot collections/entries plus out-of-range layout slot IDs |
| Slot mute state persists | Implemented | `WorkspaceSlot.Muted`, `AppState.LastSession` |
| Slot profile group persists | Implemented | Profile group is slot-derived and stays with slot |
| Malformed workspace resilience | Implemented | `WorkspacePresetNormalizationService` normalizes slot range, URLs, duplicate slots, missing/null slots, blank-slot names, and profile groups; diagnostic reports and external-browser fallback planning also tolerate null or out-of-range last-session slot lists and entries |
| Workspace restore safety | Implemented | `WorkspaceRestoreService` prepares presets and last sessions by normalizing data, resolving the layout, and blanking slots outside that layout before WebViews are loaded |
| Hidden slot background playback prevention | Implemented | `WorkspaceSlotVisibilityService` blanks slots outside the active layout, tolerates null saved slot collections/entries plus malformed layout slot entries, and hidden slots are cleared without initializing WebViews when possible |
| Broadcast click passes to WebView/player | Implemented | `StreamSlotView.xaml` keeps `ControlBar` and `SlotUrlEditor` outside the WebView content row, and `StreamSlotViewLayoutTests` locks that structure |
| Named preset not auto-overwritten by temporary edits | Implemented | Last session is stored separately from `workspaces.json`, and tests verify saving `appstate.json` does not modify saved workspace presets |

## Data Files

Runtime data is stored under `%LOCALAPPDATA%\StreamOrchestra\Data`.

| File | Purpose |
| --- | --- |
| `appstate.json` | Last window, selected slot, view options, transient session |
| `workspaces.json` | Named presets saved explicitly by the user |
| `favorites.json` | App-local stream favorites |
| `feasibility-results.json` | Phase 0 test outcomes, scenario, account label, verified profile groups, resource observations, criteria, and decision snapshots |
| `external-browsers.json` | Optional custom Chromium-compatible fallback browser candidates |
| `diagnostic-report-*.json` | Exported report with profile folders, data file status, workspace/favorite/last-session diagnostics, external browser discovery, latest decision, account-label summary/conflict status, plan-gate audit, and suggested record shapes |
| `external-browser-fallback-*.ps1` | Reviewable fallback script generated only when active stream URLs and installed external browsers are available |

JSON saves write a same-folder `*.tmp.*` file first and then replace the target file. If a JSON file is corrupt, the app quarantines it as `*.corrupt.*` and starts with an empty/default state.

## Remaining Gate

The project cannot be considered fully complete until Phase 0 is manually verified with SOOP:

1. Run the app.
2. Sign into SOOP in the required profile groups.
3. Test 4, 8, 9, 12, and 16 playback.
4. Record whether at least 9 streams play.
5. Record same-account session persistence, account label, and verified profile groups A-D.
6. Restart the app and record session persistence.
7. Record CPU/GPU/memory and resource acceptability.
8. Use the app's `성공`, `부분`, or `실패` buttons to save the result.

The app then shows one of the plan's decision paths:

| Decision code | Meaning |
| --- | --- |
| `continue_webview2_mvp` | Continue embedded WebView2 MVP |
| `continue_webview2_experiments` | Run more WebView2 profile/layout experiments |
| `switch_external_browser` | Move toward external browser control mode |
