# 整机功耗监控实现计划

## Goal
实现已确认的 `SYS.Power` 标准监控项：按可用的 `CPU.Power` 与 `GPU.Power` 求和，默认显示在主监控面板和任务栏，接入现有功耗趋势，并兼容旧版 settings。

## Architecture
- `HardwareValueProvider`：`SYS.Power` 组合值的唯一取值 owner，消费已有 CPU/GPU 功耗值。
- `SettingsHelper`：标准监控项清单与旧配置迁移 owner。
- `HardwareHistoryLogger` / `HardwareTrendForm`：历史记录与功耗趋势 owner，沿用现有 key 集合。
- `MetricUtils` / 语言资源：单位、颜色和显示名称复用现有功耗逻辑，补充“软件估算”文案。

## Tech Stack
.NET 8 WinForms、C#、LibreHardwareMonitorLib 0.9.6、现有 JSON settings 与硬件历史记录。

## Baseline/Authority Refs
- `docs/aegis/specs/2026-08-16-system-power-monitoring-design.md`
- `docs/aegis/BASELINE-GOVERNANCE.md`
- `src/System/HardwareServices/HardwareValueProvider.cs`
- `src/Core/SettingsHelper.cs`
- `src/Core/HardwareHistoryLogger.cs`
- `src/Core/MetricUtils.cs`

BaselineUsageDraft:
- Required baseline refs: approved design spec, baseline governance, listed owner files
- Delivered context refs: current repository source inspection and approved user decisions
- Acknowledged before plan refs: design spec and current runtime owners
- Cited in plan refs: design spec and owner files
- Missing refs: no existing automated test project was found in the initial source map
- Decision: continue

Requirement Ready Check:
- Requirement source refs: approved design specification and user confirmations
- Goals and scope refs: design spec Goal, Scope, Non-goals
- User / scenario refs: panel and taskbar monitoring scenario
- Requirement item refs: SYS.Power formula, missing-sensor behavior, default visibility, trend integration
- Acceptance / verification criteria refs: design spec Acceptance and Verification
- Open blocker questions: none
- Decision: ready

## Compatibility Boundary
- Existing `CPU.Power`, `GPU.Power`, `BAT.Power` behavior remains unchanged.
- Existing settings fields and user history files remain readable.
- Adding `SYS.Power` is additive; migration preserves user custom visibility/labels for existing items.
- Missing component sensors return no data for the missing component and never silently contribute zero as a complete reading.

## TDD Route
- Mode: off
- Decision: skipped
- Strict authority: not applicable
- Test posture: post-change regression and build verification
- Reason: user did not request strict TDD and the repository has no identified test project for this WinForms slice.
- Verification: focused source inspection plus `dotnet build -c Release` and runtime/config checks where available.

## Change Necessity
- User-visible need: expose a new combined power metric.
- No-change / non-code option: configuration-only cannot compute a runtime sum or feed history.
- Why code change is necessary: the existing provider only exposes component keys and the history list is explicit.
- Minimum change boundary: provider combination branch, standard item list, history key list, labels/formatting/max-record mapping, and focused verification.
- Decision: code-change

## Existence Check
- Proposed new surface: standard metric key `SYS.Power`.
- Existing owner / reuse candidate: existing monitor key registry and `HardwareValueProvider`.
- Why existing surface is insufficient: no existing key represents a combined CPU/GPU value.
- Creation proof: user-approved acceptance requires a selectable panel/taskbar metric and trend series.
- Entropy / retirement impact: additive key only; no duplicate scanner, fallback owner, adapter, or new persistence schema.
- Decision: add-with-proof

## Architecture Integrity Lens
- Invariant: all consumers receive one value for one canonical key.
- Canonical owner / contract: `HardwareValueProvider.GetValue("SYS.Power")`.
- Responsibility overlap: none; CPU/GPU sensors continue to be read by existing logic.
- Higher-level simplification: combine already-normalized component values in provider rather than duplicating sensor matching in UI or history.
- Retirement / falsifier: no retirement required; revisit only if a native system-power sensor becomes an approved source of truth.
- Verdict: proceed with existing owner.

## Plan-Time Complexity Check
- Target files: `HardwareValueProvider.cs`, `SettingsHelper.cs`, `HardwareHistoryLogger.cs`, `MetricUtils.cs`, language resources or resolver, and focused tests if an existing test harness is available.
- Existing pressure: provider and settings helper are large but already own the relevant switch/key registries.
- Owner fit: edit in place is consistent with current key-based architecture.
- Add-in-place risk: avoid refactoring unrelated branches; add one small combination branch and explicit mappings.
- Better file boundary: no new file is justified for one metric.
- Recommendation: edit-in-place.

## Tasks

### 1. Add the runtime combined metric
Files: `src/System/HardwareServices/HardwareValueProvider.cs`.

Why: the user-visible value must be calculated from existing canonical CPU/GPU power readings.

Change Necessity: no configuration or UI-only change can provide a runtime sum; the minimum owner is the provider switch branch.

Steps:
1. Add a `SYS.Power` case before the generic `Power` fallback.
2. Read `CPU.Power` and `GPU.Power` through the existing provider paths without recursively re-entering the public lock path; use the existing component processor/sensor cache paths or a small private helper that preserves current filtering.
3. Sum only non-null, finite, non-negative component values; return null when both are unavailable.
4. Apply a conservative upper bound consistent with existing power validation so bad sensor spikes do not become a false system reading.
5. Preserve the existing `_lastValidMap` and tick-cache behavior.

Impact/Compatibility: CPU/GPU/BAT keys are unchanged; only the new key is added.

Verification: inspect the branch for null/empty behavior and run the project build after all related mappings are added.

### 2. Register the metric and default visibility
Files: `src/Core/SettingsHelper.cs`.

Why: old and new settings need the new standard item, with the approved panel/taskbar defaults.

Change Necessity: the standard item registry is the existing migration source of truth.

Steps:
1. Add `SYS.Power` in the power/system section with a stable sort index that does not reorder existing user items unexpectedly.
2. Set `VisibleInPanel = true` and `VisibleInTaskbar = true`.
3. Ensure standard-key filtering and missing-item migration automatically retain the new item.
4. Do not alter persisted settings field names or remove existing orphan/plugin preservation behavior.

Impact/Compatibility: additive settings migration; existing user overrides are copied by current migration code.

Verification: inspect `RebuildAndMigrateSettings` and `CheckAndAppendMissingItems`; run build and a settings migration check if available.

### 3. Integrate history, display metadata, and explanatory label
Files: `src/Core/HardwareHistoryLogger.cs`, `src/Core/MetricUtils.cs`, relevant language JSON or existing label resolver files discovered during implementation.

Why: the metric must format as watts, participate in power trends, and be understandable as an estimate.

Change Necessity: the trend key collection is explicit and display labels are not inferred for every surface.

Steps:
1. Append `SYS.Power` to `HardwareHistoryLogger.PowerKeys`.
2. Reuse existing `MetricUtils` power classification and W formatting; add explicit max-record handling only if the current max-record path requires a key-specific branch.
3. Add localized labels for `Items.SYS.Power` and taskbar short label using existing language conventions.
4. Add “software estimate” wording in the nearest existing tooltip/help surface without changing unrelated UI copy.
5. Confirm `HardwareTrendForm` automatically picks the new key through `PowerKeys` and needs no duplicate list.

Impact/Compatibility: additive history series; existing history entries remain readable.

Verification: inspect power category selection, label resolution, and W formatting; build the application.

### 4. Verify, package, and deliver
Files: no source changes unless verification finds a scoped defect; generated outputs remain outside source ownership.

Why: the project delivery convention requires a release build and replacement of the actual running copy.

Steps:
1. Check git diff and ensure only approved source/docs files changed.
2. Run `dotnet build -c Release` or the repository’s exact Release publish command if available.
3. Run focused runtime/config checks for both component-present and component-missing behavior. Do not claim wall-power accuracy.
4. Stop the old LiteMonitor process, publish Release output, and replace program files in `D:\Onedrive\桌面\LiteMonitor_v1.3.6-win-x64\LiteMonitor_v1.3.6-win-x64`, preserving `settings.json`, `TrafficHistory.json`, and `HardwareHistory.json`.
5. Start the new version and verify the application launches.
6. Commit with a Chinese `feat:` message describing `SYS.Power`, visibility, trend integration, and migration.
7. Push `origin/master` and perform the project’s release asset update if the build and credentials are available; report any unavailable external step explicitly.

Verification: record build output, launch result, git status, commit id, push result, and package/runtime path.

## Plan Pressure Test
- Owner / contract / retirement: one additive standard key, provider remains canonical, no old path retirement.
- Architecture integrity / higher-level path: no duplicate sensor collection or UI-side calculation.
- Verification scope: value combinations, settings migration, formatting, trend key registration, Release build, launch.
- Task executability: exact files and commands are identified; repository test harness availability is an explicit check.
- Pressure result: proceed

## Execution Readiness View
- Intent Lock: implement approved `SYS.Power` software estimate only.
- Scope Fence: CPU/GPU sum, panel/taskbar defaults, trend, migration, build/package; no wall-power meter or PSU adapter.
- Baseline Lock: existing provider, settings, history, and metric metadata owners remain canonical.
- Approved Behavior: available values sum; both missing means no data; defaults visible in panel and taskbar.
- Owner / Contract Constraints: standard monitor key and existing provider API; no new public API.
- Compatibility Boundary: additive key; preserve old settings and history files.
- Retirement Boundary: none; no fallback or duplicate owner introduced.
- Task Batches: runtime key, registry/migration, history/display, verification/release.
- Test Obligations: null combinations, valid sum, invalid spikes, settings migration, trend registration, Release build.
- Review Gates: inspect diff before build; verify launch before closeout.
- Drift / Rewind Rules: stop and return to design if requested wall-power measurement or a new hardware source is introduced.
- Evidence Required Before Completion: build result, focused behavior evidence, launch result, git/push/package status.
- Advisory Boundary: planning guidance only; not completion authority.

## Risks
- Some systems expose no GPU or CPU power sensor; accepted partial-estimate behavior must remain explicit.
- Existing source contains signs of generated/redacted key text in a few branches; preserve surrounding code and verify compilation before making assumptions.
- .NET SDK, publish target, git push, and release upload may be unavailable; report rather than fabricate success.

## Retirement
No old owner, fallback, adapter, or compatibility path is retired by this feature. If a future native system-power sensor is adopted, it requires a new reviewed source-of-truth decision before changing `SYS.Power` precedence.
