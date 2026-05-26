# Stream Orchestra Feasibility Test

This checklist verifies the Phase 0 question from `docs/plan.md`: whether separate WebView2 user data folders can behave like separate browser profiles for simultaneous SOOP playback.

## Run

```powershell
dotnet run --project src\StreamOrchestra.App
```

## Profile Groups

| Slots | Profile group | User data folder |
| --- | --- | --- |
| 1-4 | A | `%LOCALAPPDATA%\StreamOrchestra\Profiles\GroupA` |
| 5-8 | B | `%LOCALAPPDATA%\StreamOrchestra\Profiles\GroupB` |
| 9-12 | C | `%LOCALAPPDATA%\StreamOrchestra\Profiles\GroupC` |
| 13-16 | D | `%LOCALAPPDATA%\StreamOrchestra\Profiles\GroupD` |

## Test Matrix

Use the same SOOP account in each profile group. Do not bypass SOOP DRM, authentication, player, or security behavior.

| Step | Action | Expected evidence | Result |
| --- | --- | --- | --- |
| 1 | Load SOOP in Group A and sign in. | Slots 1-4 share Group A login state. |  |
| 2 | Close and restart the app. | Group A login session remains. |  |
| 3 | Repeat login/session test for Groups B, C, and D. | Each group keeps its own persistent session. |  |
| 4 | Select Group A and click `그룹 단독`. | Slots 1-4 are loaded and all other groups are blanked, so Group A alone can be judged. |  |
| 5 | Click `4개`. | Slots 1-4 can play simultaneously. |  |
| 6 | Click `8개`. | Slots 1-8 can play simultaneously. |  |
| 7 | Click `9개`. | The minimum success threshold from `docs/plan.md` can be tested directly. |  |
| 8 | Click `12개`. | Slots 1-12 can play simultaneously. |  |
| 9 | Click `16개`. | Slots 1-16 can play simultaneously. |  |
| 10 | Observe diagnostics and Task Manager after playback stabilizes. | CPU, GPU, and memory are acceptable for the machine. |  |
| 11 | Fill the account label and CPU/GPU/memory fields, then check `계정 유지`, the verified profile group boxes A-D, `재실행 유지`, and `리소스 OK` when each criterion is true. | The recorded result includes the success criteria, account label, group-level account evidence, and structured resource evidence from `docs/plan.md`. |  |
| 12 | Click `성공`, `부분`, or `실패` in the app toolbar after running one of the playback/group load tests. | The result is saved to `%LOCALAPPDATA%\StreamOrchestra\Data\feasibility-results.json` with playback count, scenario, criteria, diagnostics, and a decision snapshot. |  |

## Decision

| Outcome | Decision |
| --- | --- |
| 9 or more streams play, login sessions persist, and resource usage is acceptable. | Continue WebView2 embedded MVP. |
| Some groups work but 9 or more streams are unreliable. | Experiment with profile count, user data folders, and layout density. |
| Profile separation does not help playback limits. | Switch to external browser control mode for Chrome, Edge, Whale, Brave, Vivaldi, and other compatible browsers. |

You can also inspect the saved result outside the WPF app:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- status
```

Before live SOOP testing, run a local preflight check. It verifies that the CLI can see the data folder, A-D profile folders, WebView2 Runtime, playback-test layouts for 4/8/9/12/16, and the current evidence gate status:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- preflight
```

Add `--output .\phase0-preflight.txt` to save the exact preflight text as the setup artifact for the manual verification run.

Create a full handoff bundle when you want the setup, checklist, audit, verification, and diagnostic snapshots in one folder:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- handoff --output-folder .\phase0-handoff
```

The bundle contains `phase0-preflight.txt`, `phase0-checklist.txt`, `phase0-audit.txt`, `phase0-verification.txt`, `phase0-history.txt`, `phase0-diagnostic-report.json`, a normalized `phase0-results.json` snapshot of the current feasibility evidence, and `phase0-handoff-manifest.json`. The manifest includes the preflight profile root, WebView2 runtime status, playback layout status, A-D profile group status, current decision, plan-verification status, pass/pending/fail gate counts, outstanding gate count, and artifact file sizes plus SHA-256 hashes. Use `--data-folder <path>` and `--profile-folder <path>` to point at non-default runtime data.

Validate the saved bundle before sharing or reviewing it:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- validate-handoff --input-folder .\phase0-handoff
```

Add `--output .\phase0-handoff-validation.txt` to save the validation result. `validate-handoff` exits `0` only when the folder contains only the standard handoff artifacts plus the manifest, the manifest itself is canonical JSON with valid generated-at and fully qualified data/results/profile paths, the results file belongs to the manifest data folder, the manifest lists only those standard artifacts exactly once in standard order, contains exactly the required A-D profile groups under the manifest profile root, every artifact detail is unique and in the same standard order, each listed artifact exists and still matches its recorded size and SHA-256 hash, `phase0-results.json` is the canonical normalized snapshot, the preflight/checklist/audit/history/verification artifacts agree with the results snapshot and manifest, and the diagnostic report agrees with the manifest's data folder, results file, profile root, A-D profile groups, result count, latest result, account-label summary, suggested records, full decision snapshot, audit items, and recomputed plan-gate summary.

Print the ordered Phase 0 manual test flow from `docs/plan.md` before recording live SOOP evidence:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- checklist
```

`checklist` keeps the manual run aligned with the plan and prints the current evidence status, outstanding gates, and suggested records before the steps: preflight, A-D same-account login, restart persistence, Group A isolation, 8/9/12/16 playback, Task Manager CPU/GPU/memory observations, shared account label use, `record --dry-run` preview, final 9+ success evidence, and `verify`. Use `--data-folder <path>` to inspect a non-default result file, and `--output .\phase0-checklist.txt` to save a handoff copy.

`status`, `record`, and `report` print the current recommendation, next action, compact plan-gate audit summary, overall plan-verification status, Phase 0 success-gate status, and suggested `record` shapes for missing evidence. When multiple 9+ results exist, the current recommendation follows the latest 9+ result.

Audit the remaining plan gates, including suggested `record` shapes for missing evidence:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- audit
```

Save the same audit text when you need a file artifact:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- audit --output .\plan-audit.txt
```

Verify whether the Phase 0 plan verification is complete. This exits `0` only after the recorded evidence covers distinct 4-slot Group A single-profile, 8-slot, 9-slot threshold, 12-slot, and 16-slot playback checks, plus A-D account-label, restart, resource, CPU, GPU, and memory evidence; when it fails, it prints each outstanding pending/fail gate and suggested `record` shapes for the missing evidence:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- verify
```

Add `--output .\phase0-verification.txt` to save the exact verification result as a handoff artifact.

Check which external-browser fallback candidates are installed:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- browsers
```

Export a reviewable external-browser fallback script from the last saved session:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- fallback
```

Inspect saved feasibility result history and recorded decision snapshots:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- history
```

List the named playback and isolated-group scenarios before recording manual evidence:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- scenarios
```

Or record a result from the CLI:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- record --count 9 --outcome success --account --account-label main_soop --profile-groups A,B,C --restart --resources --cpu-percent 45 --gpu-percent 60 --memory-mb 12000 --scenario groups_a_b_c_9_slot_threshold --scenario-name "Groups A/B/C, 9-slot success threshold" --notes "manual SOOP test"
```

Add `--dry-run` first to validate and preview the decision/audit output without saving to `feasibility-results.json`.

For isolated A-D profile-group evidence, use `--group` so the CLI records the matching `isolated_group_*` scenario without manually typing the scenario ID:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- record --group D --outcome partial --account --profile-groups D --account-label main_soop --notes "Group D session check"
```

`success` records require `--count` 9 or higher plus `--account`, required `--profile-groups`, `--restart`, `--resources`, `--cpu-percent`, `--gpu-percent`, and `--memory-mb`. Use the same non-sensitive `--account-label <text>` or WPF account-label value for every SOOP account evidence record across A-D; `--account`/`계정 유지` requires at least one verified profile group plus a label, final same-account verification requires one shared non-empty label, and conflicting labels fail the same-account gate. A 9-slot success record proves the threshold scenario for groups A/B/C, but the final `continue_webview2_mvp` recommendation and `verify` pass still require same-account evidence covering A-D. `--resources` and `리소스 OK` always require all three manual resource values. Use `partial` when any success criterion, profile-group account evidence, or resource observation is missing. The WPF buttons fill scenario evidence automatically; CLI records derive the named scenario from `--count` when `--scenario` is omitted, and `--group A-D` derives isolated profile-group scenarios with a 4-slot default count. Profile-group evidence and playback count must match the selected scenario, so Group A evidence cannot be recorded against a Group D scenario and a single-group scenario cannot be recorded as a 16-slot result. Custom evidence can still use `--scenario` and `--scenario-name` when `--group` is not used. Use Task Manager observations for the CPU, GPU, and memory values.

Use `partial` when the requested playback count or isolated group visibly plays but success-only evidence is incomplete; use `failure` when the requested playback count or group does not work. The audit treats `partial` as useful playback evidence for matching playback gates, while reserving `success` for the 9+ Phase 0 success path. A `partial` record without `--account` is playback evidence only and does not invalidate older same-account coverage; a matching `failure` record can invalidate older coverage for the affected profile group.

The WPF app only records a feasibility result after a playback test, group load, or isolated group load has established the current scenario and playback count. This avoids saving an ambiguous result before a test has run.

Before clicking `성공`, `부분`, or `실패`, check the `현재 테스트` label in the WPF feasibility row. It shows the scenario and playback count that will be saved.
Use `감사 복사` to copy the current plan-gate audit text and suggested `record` shapes when you need to paste the verification status into an issue, note, or chat.

Whole-app and group load actions switch to a layout that contains the target slots before loading. This keeps the manual evidence tied to visible playback slots instead of hidden background WebViews.

The `status`, `record`, and `audit` commands print pass, pending, or fail for the manual gates in `docs/plan.md`: 4-slot Group A single-profile playback, distinct 8-slot split-profile, 9-slot threshold, 12-slot, and 16-slot playback evidence, A-D same-account session persistence, restart persistence, resource acceptability, structured resource observations, and the Phase 0 WebView2 success gate. `status` and `record` include suggested `record` shapes directly in their console output, including `--account-label <label>` where same-account evidence is expected; `record --dry-run` validates and previews the same decision/audit without saving; use `audit --output <path>` to save the audit text as a handoff artifact. The 12-slot and 16-slot audit suggestions use copyable `partial` examples for playback-gate evidence, and 9-slot success suggestions are listed after higher-slot playback/account evidence so the latest 9+ result can remain the final success record. Use the `scenarios` command's 9+ `success` shapes when a 12-slot or 16-slot test also has restart, resource, CPU, GPU, and memory evidence.

The `checklist` command prints the ordered manual SOOP verification flow from `docs/plan.md` with current evidence status, outstanding gates, and suggested `record` shapes, and can save the same text with `--output <path>`. The `scenarios` command prints the WPF playback buttons, isolated group tests, scenario IDs, matching CLI `record` shapes, and the partial/failure decision rule, including `--group A-D`, so manual SOOP evidence is named consistently. For 9+ playback, it separates partial/failure records from success records because success requires restart, resource, CPU, GPU, and memory evidence.

The `verify` command prints the same compact plan-gate summary, lists outstanding pending/fail gates, suggests matching `record` shapes, can save the same text with `--output <path>`, and exits non-zero until every Phase 0 plan gate passes.

The `handoff` command saves the preflight, checklist, audit, verification, history, diagnostic report JSON, normalized feasibility-results JSON, and manifest artifacts into one output folder so a manual SOOP run can be reviewed without reconstructing console output from separate commands. The manifest also records preflight profile root, runtime/layout/profile-group status, preflight readiness, verification completion, the current decision, plan-gate pass/pending/fail summary, and artifact hashes; `validate-handoff` checks the handoff folder has no extra files/directories, the canonical manifest JSON, generated-at/path consistency, the required standard artifact list and order, unexpected or duplicate manifest entries, exact A-D manifest profile groups, normalized results snapshot content, preflight/checklist/audit/history/verification content consistency, diagnostic report data/results/profile context including A-D profile groups, snapshot fields and audit items, those hashes, plus recomputed result/report/manifest consistency later. Add `--output <path>` to save that validation text.

Export a diagnostic report from the CLI:

```powershell
dotnet run --project src\StreamOrchestra.Tools -- report
```

The report JSON includes saved workspace/favorite/last-session counts, the latest feasibility result, account-label summary/conflict status, the current decision recommendation, the same plan-gate audit printed by `audit`, and suggested `record` shapes for missing evidence. When the last saved session has active HTTP/HTTPS stream URLs and Chrome, Edge, Whale, Brave, Vivaldi, or a custom browser candidate is installed, the report command also writes an `external-browser-fallback-*.ps1` script with per-slot browser, executable, profile, and URL comments. Blank, malformed, or non-web saved slot URLs are ignored. If no script can be created, the app and CLI print the reason. Review generated scripts before running them; the app does not start fallback browsers automatically.

Use `fallback` when you only need the external-browser script without writing a full diagnostic report. It uses the same visible last-session URLs, mute state, and browser discovery rules, and includes layout-based browser window placement when layout data is available by passing Chromium window-position/window-size arguments and applying a Windows placement fallback. In the WPF app, use `브라우저 스크립트` to export the script directly from the current session.

Optional extra Chromium-compatible fallback browsers can be added in `%LOCALAPPDATA%\StreamOrchestra\Data\external-browsers.json`:

```json
[
  {
    "id": "portable_chrome",
    "name": "Portable Chrome",
    "candidatePaths": ["D:\\Browsers\\PortableChrome\\chrome.exe"]
  }
]
```

## Notes

The app toolbar samples WebView2 process count, CPU, working set, and private memory from Windows `msedgewebview2` processes. Treat the numbers as a practical runtime signal, not a precise profiler. Record GPU and overall system memory from Task Manager in the structured resource fields. If other WebView2 apps are running, close them before recording results.
