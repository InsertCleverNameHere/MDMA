# Mdma.Core — Remaining Work Plan

Status: contracts (`src/Mdma.Core/*.cs`) are written and compiling. Nothing behind them is implemented yet. This document plans everything left to build in Core, in dependency order, plus the test coverage each piece needs.

This is a living document — check items off as they land, and update if a contract changes shape during implementation.

---

## Phase 0 — Test Infrastructure (do this before Phase 1 implementations)

Needed early because every phase below should ship with tests, and several pieces (space checker, registry access, process guard) need fakeable seams designed in from the start rather than retrofitted.

- [ ] `tests/Mdma.Core.Tests` project wired up (already scaffolded — confirm test runner works with a trivial placeholder test).
- [ ] `tests/fixtures/ndm/` — synthetic `neatdb.db` (via a small SQLite script) with a handful of representative rows, plus a synthetic `segments.bin` and matching `seg.x0..N` files at known offsets.
- [ ] `tests/fixtures/jd2/` — synthetic `downloadList<N>.zip` with 1–2 packages, each with 1–2 links, covering both a fully-downloaded and partially-downloaded task.
- [ ] `tests/fixtures/mdma/` — a hand-built valid `.mdma` file, plus deliberately corrupted variants (bad checksum, missing manifest, unsupported `mdma_version`) for negative-path tests.
- [ ] Decide and document the fixture generation approach: committed binary fixtures vs. a fixture-builder helper class that constructs them at test-run time. **Recommendation: builder helper**, since it keeps the repo free of opaque binary blobs and makes it trivial to generate edge cases (zero-byte task, single giant chunk, hundreds of chunks) parametrically.
- [ ] Seam definitions for anything that touches real OS state, so unit tests never depend on the actual machine's disk/registry/processes:
  - [ ] `IDiskSpaceSource` — wraps `DriveInfo.AvailableFreeSpace` lookup, fakeable in tests.
  - [ ] `IRegistryAccessor` — wraps `Microsoft.Win32.Registry` reads/writes for NDM, fakeable in tests (real implementation only exercised in manual/integration passes).
  - [ ] `IProcessLister` — wraps `Process.GetProcessesByName`, fakeable in tests.
  - [ ] `IClock` — wraps `DateTimeOffset.UtcNow`, needed for deterministic backup-id/timestamp tests.

---

## Phase 1 — Foundation (everything else depends on this) — STATUS: COMPLETE

### 1.1 `WorkingDirectoryProvider` (implements `IWorkingDirectoryProvider`) — DONE

- [x] Explicit override path: validate writability via real write+delete probe.
- [x] Portable default: `AppContext.BaseDirectory + "MDMA_Work"`, create if missing, probe writability.
- [x] Fallback: `%LOCALAPPDATA%\MDMA\work`, only reached if portable default fails; surfaces via `IsFallback` flag on a successful `Result` (caller displays the warning).
- [ ] Path-conflict check — **deliberately deferred to Phase 5**, needs `TargetAppLocation` data from discovery that doesn't exist at this layer. Not a gap, a sequencing decision.

**Tests (all passing):**

- [x] Explicit override honored when writable.
- [x] Explicit override rejected (typed error) when unwritable / path cannot be created.
- [x] Falls through to portable default when no override given.
- [x] Falls through to AppData fallback when portable default fails.
- [x] `IsPortableDefault` / `IsFallback` flags correct in each case.
- [x] Idempotent for same override.

### 1.2 `SpaceChecker` (implements `ISpaceChecker`) — DONE

- [x] Uses `IDiskSpaceSource` seam, not `DriveInfo` directly.
- [x] Applies safety margin (15%, `SpaceChecker.SafetyMarginFraction`) on top of `requiredBytes`.
- [x] Populates `MdmaError.Details` with exact required-vs-available numbers, human-readable (GB/MB/KB).
- [x] Correct `MdmaErrorCode` chosen based on `isDestination` flag.

**Tests (all passing):** sufficient space, insufficient source, insufficient destination, margin-boundary math (both sides), shortfall message content, per-path fake behavior, negative-input handling.

### 1.3 `AtomicWriter` (implements `IAtomicWriter`) — DONE

- [x] Temp file created in the **same directory** as destination (guarantees same-volume atomic rename by construction, not by detection).
- [x] Renames over destination only after `writeAction` completes without throwing.
- [x] Exception mid-write: temp file deleted (best-effort), destination untouched, `AtomicWriteFailed` returned.

**Tests (all passing):** successful write (new + pre-existing destination), exception leaves original untouched, no orphaned temp file after failure, no-op writeAction produces typed error, destination directory auto-created, temp-file-same-directory invariant directly asserted, concurrency-out-of-scope documented as an explicit test.

### 1.4 `IProcessGuard` implementation (`ProcessGuard`) — DONE

- [x] Uses `IProcessLister` seam.
- [x] Per-target process name mapping: NDM = `NeatDownloadManager.exe`; JD2 = `JDownloader2.exe` **and** `JDownloader.exe` (both variants checked).

**Tests (all passing):** safe when not running (both targets), blocked when running (both targets, both JD2 variants), case-insensitive matching.

---

## Phase 2 — Discovery & Scanning — STATUS: COMPLETE

**Model change made during this phase:** `TargetAppLocation` (in `Discovery.cs`) gained a `MetadataDir` field and both `InstallOrConfigDir` and `DownloadDirectory` became nullable, because NDM and JD2 both turned out to have split/partial location knowledge depending on auto-detect vs. manual-path validation:

```csharp
public sealed record TargetAppLocation(
    TargetApp App,
    string? InstallOrConfigDir,  // NDM: TempDirectory. JD2: cfg\ folder. Null if unknown.
    string? MetadataDir,         // NDM only: folder containing neatdb.db. Null for JD2.
    string? DownloadDirectory,   // App-level DEFAULT only. NOT authoritative for JD2 (see below).
    bool WasAutoDetected);
```

### 2.1 `NdmLocator` — DONE

- [x] `TryAutoDetect`: reads `HKCU\SOFTWARE\NeatDM` (`TempDirectory`, `DownloadDirectory`) via `IRegistryAccessor`; also sets `MetadataDir` to `%APPDATA%\NeatDM` (injectable for tests).
- [x] `ValidateManualPath`: confirms `neatdb.db` exists, opens read-only, and has the expected `downloads` table. Sets `MetadataDir` only — `InstallOrConfigDir` (temp dir) is intentionally left `null` here since manual validation has no way to know it.

**Tests (all passing):** auto-detect success/failure (missing values, missing directory), manual-path success/failure (missing dir, missing db, corrupt db, missing table), no post-validation file lock.

### 2.2 `Jd2Locator` — DONE

- [x] `TryAutoDetect`: probes `%LOCALAPPDATA%\JDownloader 2\cfg\` (injectable), validates at least one structurally-valid `downloadList*.zip` exists.
- [x] `ValidateManualPath`: same structural validation against a user-supplied path.
- [x] `PickNewest(IEnumerable<string>)`: public static helper, selects highest-numbered `downloadList<N>.zip`. **Must stay `public`** — an earlier draft made it `internal` and broke test visibility.
- [x] **`DownloadDirectory` resolution**: reads `org.jdownloader.settings.GeneralSettings.json`'s `"defaultdownloadfolder"` key from `cfg\` as a soft, app-level default. **Important, discovered during this phase (not in original docs/jd2.md):** this value is NOT authoritative for any individual task — each package (`FilePackageStorable`) carries its own `downloadFolder`, and each link inherits its parent package's folder. `TargetAppLocation.DownloadDirectory` must only ever be treated as a fallback; per-task folder resolution is deferred to `Jd2ListReader`/`Jd2Exporter`/`Jd2Injector` in later phases. **`docs/jd2.md` update for this finding is the user's own action item, not Core's.**

**Tests (all passing):** auto-detect success (with/without settings file present), failure paths (missing cfg dir, no zips), manual-path success/failure (missing dir, no expected entries, corrupt zip), `PickNewest` ordering, newest-zip-used-when-stale-duplicate-present.

### 2.3 `NdmListReader` — DONE

- [x] Reads `downloads` table via `location.MetadataDir` (not `InstallOrConfigDir` — a deliberate distinction now that the two are separate fields).
- [x] **Downloaded-byte resolution, in priority order:**
  1. If `location.InstallOrConfigDir` (temp dir) is known AND the task's own subdirectory exists → sum real `seg.x*` file sizes (authoritative per docs/ndm.md §4.3). If the temp dir is known but the task's subdirectory is missing, this returns **0** (a real fact, not a reason to fall through to step 2 — this was a bug caught by testing and fixed).
  2. Otherwise (temp dir itself unknown, e.g. after manual-path validation) → parse the `"Paused ( P% )"` status string as a best-effort estimate.

**Tests (all passing):** single/multiple task summaries, authoritative byte-sum correctness, missing-task-directory-returns-zero (regression-tested), status-percentage fallback when temp dir unknown, missing metadata dir / missing db file failure paths, no post-scan file lock.

### 2.4 `Jd2ListReader` — DONE

- [x] Picks newest `downloadList<N>.zip` via `Jd2Locator.PickNewest`.
- [x] Flattens every `<PackageID>_<LinkIndex>` entry across every package into a `DownloadTaskSummary`; package entries and `extraInfo` are skipped by regex, not specially parsed.
- [x] `current`/`size` map directly to `DownloadedBytes`/`TotalBytes` — no separate authoritative-byte-counting step needed here, since JD2's own JSON already tracks live progress (unlike NDM).
- [x] `Resumable` read from `properties.PROPERTY_RESUMEABLE`, defaults `false` if absent.

**Known deferred work (not a gap in this phase, just not yet applicable):** per-package `downloadFolder` inheritance is real but has nowhere to live yet — `DownloadTaskSummary` has no folder field. Owed to `Jd2Exporter`/`Jd2Injector` in Phase 4, where actual file placement matters.

**Tests (all passing):** single/multiple-package flattening, newest-zip selection over a stale duplicate, missing-cfg-dir/no-zips failure paths, empty-package-no-links, 100%-complete percent math, resumable-defaults-false-when-property-absent.

---

## Phase 3 — Backup & Revert — STATUS: COMPLETE

**Contract change made during this phase:** `IBackupManager.CreateBackup` (in
`SafetyLayer.cs`) changed signature from `(TargetApp app, WorkingRoot workingRoot)`
to `(TargetAppLocation location, WorkingRoot workingRoot, string? taskNativeId = null)`
— the original signature had no way to know *where* files live or *which*
task's temp folder to include, which is required for the NDM backup scope
decision below.

### 3.1 `BackupManager` — DONE

- [x] NDM scope: `neatdb.db` (via `location.MetadataDir`) + `<TempDirectory>\<taskNativeId>\` (via `location.InstallOrConfigDir`) if `taskNativeId` given and the directory exists. Missing task directory is not an error (task may not have started yet) — only a missing `neatdb.db` is a hard failure.
- [x] JD2 scope (**resolved during this phase**, was an open question): only the current **newest** `downloadList<N>.zip`, not the whole `cfg\` folder. Rationale: injection always creates a new incremented-counter zip rather than overwriting, so the newest-at-backup-time file is the only pre-existing file genuinely at risk.
- [x] Snapshot stored under `<workingRoot>\backups\<timestamp>_<target>_<shortguid>\`, with a `manifest.json` recording each file's original path, its relative path inside the snapshot, and its SHA-256.
- [x] `ListBackups`: newest-first, optional target filter, skips (doesn't fail on) individual corrupt/unreadable snapshot manifests.
- [x] Partial-failure cleanup: a failed `CreateBackup` deletes its own half-created snapshot directory (best-effort).

**Tests (all passing):** NDM db+task-dir capture, byte-for-byte copy verification, db-only backup when no taskNativeId given, missing-db failure, missing-task-dir-is-not-an-error, JD2 newest-zip capture (incl. picking newest over a stale duplicate), JD2 no-zips failure, empty/newest-first/target-filtered listing, no-partial-directory-left-on-failure.

### 3.2 `RevertManager` — DONE

- [x] Reads the backup's `manifest.json`, checks `IProcessGuard` for the target app (blocks with `TargetAppProcessRunning` if running — first real consumer of that error code).
- [x] Verifies **every** entry's SHA-256 against the live snapshot files **before restoring anything** — a tampered/corrupted snapshot is refused wholesale, never partially applied.
- [x] Restores each file via `IAtomicWriter`.

**Tests (all passing):** NDM db restore, NDM task-segment-file restore, JD2 zip restore, blocked-when-process-running (+ confirms original file untouched in that case), tampered-snapshot-restores-nothing, missing-snapshot-file, missing-manifest, multi-file NDM backup restores all files correctly.

**Known limitation, deliberately deferred (not urgent — revisit after Core is otherwise complete and end-to-end tested):** if a multi-file restore fails partway through (e.g. file 2 of 3), files already restored before the failure are **not** rolled back — `Revert` returns a `RevertFailed` error whose message says so explicitly, but the live state is left in a mixed state rather than atomically all-or-nothing. A real fix would need a two-phase restore (stage all restored files first, then commit all at once) or per-entry backup-of-current-state-before-overwrite so a failed revert could itself be reverted. Track this as a hardening pass before v1 ships, not before Phase 4/5/6 proceed.

---

## Phase 4 — Export / Import / Injection

### 4.1 `.mdma` package read/write (shared helper, used by exporter and loader)

- [ ] Zip writer: `manifest.json` + `data/chunk_*.bin` + `checksum.sha256` (SHA-256 over the full payload — decide and document exact hash scope: all chunk files concatenated in index order, or a hash-of-hashes — needs to be unambiguous for the loader to verify).
- [ ] Zip reader: extract to a working-root staging folder, verify checksum, deserialize `manifest.json`, reject on `mdma_version` mismatch.

### 4.2 `NdmExporter` (implements `IMdmaExporter`, `SourceApp = NDM`)

- [ ] Read `segments.bin` records for the task's directory, map each to a `ChunkRange`.
- [ ] Copy each `seg.xN` file's bytes into `data/chunk_N.bin` in the `.mdma` package.
- [ ] Pull `url`/`filename`/headers/etc. from the `downloads`/`headers` tables for the manifest.

### 4.3 `Jd2Exporter` (implements `IMdmaExporter`, `SourceApp = JD2`)

- [ ] Read `chunkProgress` array + link JSON for the task, map to `ChunkRange`s (single sparse file → byte-range slices).
- [ ] Slice the `.part` file at each chunk's start/end into separate `data/chunk_N.bin` files (JD2's storage is single-file; `.mdma`'s is per-chunk — this conversion step is exporter-specific logic, not shared).

### 4.4 `NdmInjector` (implements `IDownloadListInjector`, `TargetApp = NDM`)

- [ ] Compute new Task ID from registry `LastDownloadID + 1`.
- [ ] Write staged chunk files as `seg.x0..N` under `<TempDirectory>\<NewTaskID>\`.
- [ ] Synthesize `segments.bin` from `ChunkRange`s.
- [ ] Insert `downloads` + `headers` rows via `IAtomicWriter`-wrapped SQLite transaction, formatting `status` per the `"Paused ( P% )"` convention in `docs/ndm.md`.
- [ ] Update `LastDownloadID` registry value last, only after DB insert succeeds.

### 4.5 `Jd2Injector` (implements `IDownloadListInjector`, `TargetApp = JD2`)

- [ ] Reconstruct a single sparse `.part` file from staged per-chunk files at their byte offsets.
- [ ] Determine next `downloadList<N+1>.zip` counter.
- [ ] Duplicate existing entries + inject new package/link JSON entries.
- [ ] Write new zip via `IAtomicWriter`.

**Tests:**

- [ ] Round-trip test: export a fixture NDM task → `.mdma` → import into a fixture JD2 environment → resulting JD2 state matches expected byte ranges and metadata.
- [ ] Round-trip test: same in the JD2→NDM direction.
- [ ] Export produces a `.mdma` whose checksum verifies via the loader.
- [ ] Loader rejects a tampered `.mdma` (`MdmaChecksumMismatch`) without staging any files.
- [ ] Loader rejects an `.mdma` with a future/unsupported `mdma_version` (`MdmaVersionUnsupported`).
- [ ] Injector never runs without a prior successful backup — enforced/verified at the orchestrator level (Phase 5), but injector-level tests should confirm it doesn't assume this itself (defense in depth).
- [ ] Partial-download task (not just completed) round-trips correctly — this is the common case, must not be an afterthought in test coverage.
- [ ] Edge cases: zero chunks, single chunk spanning the whole file, very large chunk count.

---

## Phase 5 — Conversion Orchestrator

### 5.1 `ConversionService` (implements `IConversionService`)

- [ ] `ExportToFile`: space check (source) → export. No backup/process-guard needed (read-only against source... **except** confirm: does export require the source app's process guard too, since e.g. NDM's SQLite file could be locked mid-download? Needs explicit decision — see Open Questions below.)
- [ ] `ImportFromFile`: process guard (destination) → space check (destination) → backup (destination) → load `.mdma` → inject → done.
- [ ] `ConvertSameMachine`: process guard (source + destination) → space check (both) → backup (destination) → export to `<workingRoot>\.mdma-tmp\<guid>.mdma.partial` → finalize rename → import → best-effort delete of temp `.mdma`. Must be implemented as literal calls to `ExportToFile`/`ImportFromFile` against the temp path, not parallel logic.
- [ ] Progress reporting threaded through every stage via `IProgress<OperationProgress>`.
- [ ] Enforces `StepCriticality`: Critical step failure aborts immediately and returns without attempting later steps; BestEffort failure (temp cleanup) is caught, logged, and does not affect the returned `Result`'s success state.

**Tests:**

- [ ] Full orchestration order verified via mock call sequence (process guard → space → backup → export/import → cleanup), for all three entry points.
- [ ] Backup failure aborts before any write occurs (mock injector/exporter never invoked).
- [ ] Process-guard failure aborts before backup is even attempted.
- [ ] Insufficient space on either end aborts before backup.
- [ ] Cleanup failure after a successful `ConvertSameMachine` still returns `Result.Ok` (best-effort semantics verified end-to-end, not just at the unit level).
- [ ] Startup orphan sweep (of `.mdma-tmp\`) — decide if this lives in `IConversionService` or a separate `ITempCleanupService`; either way needs a test that leftover `.partial`/`.mdma` files from a simulated crash are detected and reported.

---

## Phase 6 — Logging

- [ ] `ILogger`-style seam (or adopt `Microsoft.Extensions.Logging` — decide) writing structured entries to `<workingRoot>\logs\`, per your instruction that logs live next to the exe / in the portable working root, never AppData, mirroring the working-directory fallback rule if the primary location isn't writable.
- [ ] Every Critical step logs start/success/failure; BestEffort step failures logged at a distinguishable level (warning, not error) since they don't fail the operation.
- [ ] Log format decision: structured (JSON lines) vs. plain text — recommend JSON lines for future tooling/debugging, human-readable enough on its own.

**Tests:**

- [ ] Log file created in the resolved working root, not AppData, under normal conditions.
- [ ] Falls back consistently with `IWorkingDirectoryProvider`'s own fallback behavior when the working root logs subdirectory isn't writable.
- [ ] Critical vs. BestEffort steps produce distinguishable log levels (spot-check a handful, not exhaustive).

---

## Decisions (resolved 2026-07-29)

1. **`ExportToFile` process guard**: **Yes.** Export requires the same process-guard precondition as import, for both source targets.
2. **`.mdma` checksum scope**: **Hash-of-hashes.** Each chunk file gets its own SHA-256; a top-level manifest hash covers the ordered list of per-chunk hashes, so `MdmaLoader` can report which specific chunk is corrupt. Exact on-disk shape (list in `checksum.sha256` vs. folded into `manifest.json`) to be finalized in Phase 4. **`MdmaFixtureBuilder` currently implements single-hash-over-concatenated-bytes — must be updated before Phase 4.**
3. **NDM backup scope**: **`neatdb.db` + the task's temp directory (`<TempDirectory>\<TaskId>\`), if it exists** — scoped to what the operation touches, not the whole `NeatDM` temp tree.
4. **Orphan sweep**: lives in a new **`ITempCleanupService`** interface, separate from `IConversionService`.
5. **Logging**: small custom logger first; fall back to `Microsoft.Extensions.Logging` only if that proves genuinely problematic.

## Phase 0 — Status: COMPLETE

- [x] Test project wired up, NUnit confirmed working.
- [x] `NdmFixtureBuilder`, `Jd2FixtureBuilder`, `MdmaFixtureBuilder` (builder-based, not committed binaries).
- [x] Seams: `IDiskSpaceSource`, `IRegistryAccessor`, `IProcessLister`, `IClock`, with in-memory fakes.
- [x] Smoke tests passing (4/4).

---

## Suggested Build Order (summary)

1. Phase 0 (test seams + fixtures)
2. Phase 1 (working dir, space checker, atomic writer, process guard)
3. Phase 2 (locators + list readers) — first point real NDM/JD2 format knowledge gets exercised
4. Phase 3 (backup + revert) — must exist before Phase 4 injectors are wired into the orchestrator, since injectors assume a prior backup
5. Phase 4 (exporters, loader, injectors) — the core value-delivering logic
6. Phase 5 (orchestrator) — ties everything together behind the single `IConversionService` entry point
7. Phase 6 (logging) — can actually be threaded in parallel with Phases 3–5 once the seam exists, rather than strictly last

Cli and Gui implementation (calling into a finished `IConversionService`) is intentionally out of scope for this document — it's a separate, much smaller planning pass once Core is solid.
