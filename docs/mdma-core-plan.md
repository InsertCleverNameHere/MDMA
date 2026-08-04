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

## Phase 4 — Export / Import / Injection — STATUS: COMPLETE

**Contract change made during this phase:** `IMdmaExporter.Export` (in
`Conversion.cs`) gained a `WorkingRoot workingRoot` parameter — needed
because `Jd2Exporter` must slice a single sparse source file into per-chunk
temp files before packaging, and that staging must happen under the
portable working root, never a hardcoded temp/AppData path. `NdmExporter`
accepts but ignores the parameter (it doesn't need staging, since NDM's
segments are already separate physical files).

### 4.1 `.mdma` package format (shared writer/reader) — DONE

- [x] `MdmaPackageFormat.cs`: on-disk DTOs (`MdmaManifestDto`/`MdmaTaskDto`/`MdmaChunkDto`/`MdmaHeaderDto`, nested per architecture.md) kept deliberately separate from the domain `MdmaManifest` record. `MdmaChecksumDto` + `MdmaChecksumHelper` implement the **final hash-of-hashes shape**: `checksum.sha256` is JSON `{"chunk_hashes": {"<index>": "<hex sha256>"}, "manifest_hash": "<hex sha256 of chunk hashes ordered by index, joined by \n>"}`.
- [x] `MdmaPackageWriter`: computes `downloaded_bytes` from the **actual file length on disk**, never a caller-supplied value — verified by a dedicated test.
- [x] `MdmaLoader`: verifies version, structural consistency (manifest chunk list == checksum chunk list), **every** per-chunk hash, then the manifest hash — all before extracting a single byte to disk. Stages chunks under `<workingRoot>\.mdma-tmp\extracted-<guid>\`.
- [x] `MdmaFixtureBuilder` (Phase 0) rewritten to match this exact format — the earlier single-hash version was replaced (this was the flagged deferred item from Phase 0/2, now resolved).

### 4.2 `NdmExporter` — DONE

- [x] Parses `segments.bin` (24-byte records), packages each `seg.xN` as a chunk. Chunk identity/ordering comes from `segment_id`, not the `next_segment_id` linked-list pointer.
- [x] Pulls `mimetype` + `headers` from `neatdb.db` directly (not on `DownloadTaskSummary`).
- [x] Needs both `location.InstallOrConfigDir` (temp dir) and `location.MetadataDir` (db) — fails cleanly if either is null.

### 4.3 `Jd2Exporter` — DONE

- [x] Resolves per-task download folder: **package-level `downloadFolder` always wins if the package entry exists at all**, even if that folder doesn't resolve to a real path; app-level `location.DownloadDirectory` fallback only applies when the package entry itself is missing from the zip. (Explicitly tested both ways — this is a real priority-order decision, not an accident.)
- [x] Slices the single sparse `.part`/completed file into per-chunk staged temp files under `workingRoot`.
- [x] `mimeType` always `null`, `headers` always empty — `docs/jd2.md`'s link JSON schema has no fields for either (unlike NDM's separate headers table).
- [x] **Documented assumption, not confirmed in docs/jd2.md** (which only shows a `CHUNKS=1` example): multi-chunk files split into `CHUNKS` equal-size contiguous ranges (remainder on the last chunk), and `chunkProgress[i]` is an **absolute file offset**. If a real multi-chunk JD2 capture ever contradicts this, `Jd2Exporter.SliceChunks` and the two tests exercising it (`Export_MultiChunk_Slices_Correct_Byte_Ranges`, `Export_MultiChunk_Handles_Partial_Progress_Per_Chunk`) are where to start.

### 4.4 `NdmInjector` — DONE

- [x] `NewTaskID = LastDownloadID (registry) + 1`, defaults to `1` if the registry value is absent.
- [x] Refuses to write into a pre-existing task directory at the computed ID (safety guard, not explicitly required by the plan but added deliberately).
- [x] `neatdb.db` mutated via `IAtomicWriter` copy-modify-swap (copy → open copy → INSERT → atomic rename over original).
- [x] Registry `LastDownloadID` update is **last**, only after the db write succeeds.
- [x] **Known limitation, same class as RevertManager's (Phase 3.2):** if the registry update fails *after* the db insert already succeeded, the db row is not rolled back. Documented in the class doc comment; not fixed now, same "revisit after Core is otherwise complete" deferral.

### 4.5 `Jd2Injector` — DONE

- [x] Reconstructs a sparse `.part` file by seeking to each chunk's `StartByte` and writing its staged bytes (mirrors JD2's own `RandomAccessFile.seek()` model).
- [x] New package ID = `max(existing top-level numeric entries) + 1`, explicitly excluding `extraInfo` and link entries (anything containing `_`).
- [x] Creates `downloadList<N+1>.zip` duplicating every existing entry, **never deletes the old zip** (matches docs/jd2.md's crash-protection design).
- [x] Unlike NDM, **no separate external bookkeeping/counter update step** — JD2's own boot logic just picks the highest-numbered zip, so writing the new file is the entire commit. No cross-store atomicity gap here (contrast with NdmInjector's known limitation above).
- [x] `chunkProgress` written as absolute offsets, consistent with the same assumption documented in `Jd2Exporter`.

**All tests passing: 143/143** (test-by-test counts, not estimates, were used throughout this phase after an earlier miscounting incident — see workflow note below).

**Process note for future phases:** during this phase, a file (`Jd2FixtureBuilder.cs`) was accidentally reverted to a stale version because a change described in prose in an earlier turn was never mirrored into the assistant's own local working copy, then got overwritten when the file was later rewritten wholesale. Lesson: any time a file is fully rewritten, diff it against the most recent known-good version first, not just against what's in local memory. Caught and fixed same-day; flagged here so it doesn't repeat.

**Test coverage gap, honestly noted:** the plan's original checklist called for full round-trip tests (NDM export → JD2 import, and the reverse) and "injector never runs without a prior successful backup." What was actually built and tested is each exporter and each injector **independently** (exporter output verified via `MdmaLoader`, injector input built via `MdmaPackageWriter`+`MdmaLoader`) — not a single end-to-end test chaining `NdmExporter → Jd2Injector` or `Jd2Exporter → NdmInjector` in one flow, and not yet anything enforcing the backup precondition (that's inherently an orchestrator-level concern, Phase 5). Worth adding true cross-target round-trip tests once `ConversionService` exists in Phase 5, since that's naturally where they'd live and be meaningful.

---

## Phase 5 — Conversion Orchestrator

Split into sub-phases (this phase was originally one large block — divided per the same incremental, one-piece-at-a-time pattern used in Phases 1–4). `ITempCleanupService` and the Phase 1 path-conflict check are folded in here since they only became implementable once discovery (Phase 2) and injection (Phase 4) existed.

### 5.1 Path-conflict check (deferred from Phase 1)

- [x] Add the check to `WorkingDirectoryProvider.Resolve` (or a variant called with known `TargetAppLocation`s): reject a working root nested inside a source or destination app's own directory (`InstallOrConfigDir`/`MetadataDir`). Returns `WorkingDirectoryPathConflict` (error code already exists in `MdmaErrorCode`, unused until now).
- [x] Decide exact call shape: does `Resolve` gain an optional `IEnumerable<TargetAppLocation>` parameter, or is this a separate method called by `ConversionService` after both locations are known? Lean toward the latter — `WorkingDirectoryProvider` shouldn't need to know about discovery types just for this one check.

**Tests:** working root nested inside source location's temp dir → rejected; nested inside destination's metadata dir → rejected; sibling (not nested) directory → allowed; no locations provided → behaves exactly as before (backward compatible).

### 5.2 `ITempCleanupService` (new interface + implementation)

- [x] New interface: sweeps `<workingRoot>\.mdma-tmp\` for orphaned `.mdma`/`.mdma.partial`/extraction folders left by a crashed prior run. Returns what it found/removed so CLI/GUI can report it.
- [x] Decide exact contract shape (e.g. `Result<IReadOnlyList<string>> SweepOrphans(WorkingRoot workingRoot)`), matching the `Result`-based pattern used everywhere else in Core.
- [x] Best-effort by nature — a file that can't be deleted (locked, in use) is skipped and reported, not a hard failure of the sweep.

**Tests:** sweep removes orphaned temp files/folders and reports them; a locked/undeletable file is skipped without failing the whole sweep; empty `.mdma-tmp\` (or missing entirely) returns cleanly with nothing to report.

### 5.3 `ConversionService.ExportToFile`

- [x] Order: process guard (source) → space check (working root, using the task's `DownloadedBytes` as the required size) → call the source's `IMdmaExporter` → return the written `.mdma` path.
- [x] No backup needed here — export never mutates the source app's own state (`NdmExporter`/`Jd2Exporter` are read-only against NDM/JD2's files).
- [x] Progress reporting threaded through via `IProgress<OperationProgress>`.

**Tests:** full order verified via mock/spy sequence; process-guard failure aborts before space check or export; insufficient space aborts before export is attempted; successful export returns the correct path.

### 5.4 `ConversionService.ImportFromFile`

- [x] Order: process guard (destination) → space check (destination's `DownloadDirectory`/`InstallOrConfigDir`, using the `.mdma` manifest's `TotalBytes` or summed `DownloadedBytes` as required size) → backup (destination, **Critical** — abort before any write if this fails) → `MdmaLoader.Load` → destination's `IDownloadListInjector.Inject`.
- [x] Progress reporting threaded through.

**Tests:** full order verified via mock/spy sequence; backup failure aborts before `MdmaLoader.Load` or injection ever run; process-guard failure aborts before backup is attempted; insufficient space aborts before backup.

### 5.5 `ConversionService.ConvertSameMachine`

- [x] Order: process guard (source **and** destination) → space checks (both sides) → backup (destination, Critical) → `ExportToFile` against a temp path under `<workingRoot>\.mdma-tmp\<guid>.mdma` → `ImportFromFile` against that temp path → best-effort delete of the temp `.mdma` (failure here is logged, does **not** fail the overall `Result`).
- [x] Must literally call the `ExportToFile`/`ImportFromFile` methods just built in 5.3/5.4 — no parallel/duplicated logic, per the standing "always round-trip through a real `.mdma`, even same-machine" design decision.
- [x] Enforces `StepCriticality` consistently: Critical failures abort immediately without attempting later steps; the cleanup step is explicitly BestEffort.

**Tests:** full order verified via mock/spy sequence; cleanup failure after an otherwise-successful conversion still returns `Result.Ok` (this is the concrete test for the Critical vs. BestEffort distinction promised all the way back in architecture.md §8); backup failure aborts before export is attempted; confirms `ExportToFile`/`ImportFromFile` are the actual methods invoked (not reimplemented logic) — e.g. via a spy/count assertion.

### 5.6 True end-to-end round-trip tests (the coverage gap flagged at the end of Phase 4)

- [x] `NdmExporter` → `.mdma` → `Jd2Injector`, using `ConversionService.ConvertSameMachine` (or the two steps manually chained), asserting the resulting JD2 state (zip entries, reconstructed `.part` file bytes) matches the original NDM task.
- [x] `Jd2Exporter` → `.mdma` → `NdmInjector`, same idea in reverse.
- [x] At least one partial-download (not fully-completed) task in each direction, since that's the common real-world case per the plan's original Phase 4 test checklist.

**Tests:** the above, using real fixture-built NDM/JD2 environments on both the source and destination side (two separate fixture roots standing in for "two machines" conceptually, even though same-machine mechanically) — byte-for-byte verification of the final injected state against the original source bytes.

---

## Phase 6 — Logging

- [x] `ILogger`-style seam (or adopt `Microsoft.Extensions.Logging` — decide) writing structured entries to `<workingRoot>\logs\`, per your instruction that logs live next to the exe / in the portable working root, never AppData, mirroring the working-directory fallback rule if the primary location isn't writable.
- [x] Every Critical step logs start/success/failure; BestEffort step failures logged at a distinguishable level (warning, not error) since they don't fail the operation.
- [x] Log format decision: structured (JSON lines) vs. plain text — recommend JSON lines for future tooling/debugging, human-readable enough on its own.

**Tests:**

- [x] Log file created in the resolved working root, not AppData, under normal conditions.
- [x] Falls back consistently with `IWorkingDirectoryProvider`'s own fallback behavior when the working root logs subdirectory isn't writable.
- [x] Critical vs. BestEffort steps produce distinguishable log levels (spot-check a handful, not exhaustive).

## Mdma.Cli — Command-Line Interface — STATUS: COMPLETE

- [x] Phase 1: CLI Infrastructure, Argument Parser (`CliParser`), Exit Codes (`0`–`6`, `99`), Trim-Safe JSON Source Generation (`CliJsonContext`), Production OS Seams (`RealSeams.cs`), Command Router & Entry Point (`Program.cs`).
- [x] Phase 2: Discovery Commands (`scan` task table / JSON output, `clean` orphan sweep).
- [x] Phase 3: Conversion Commands (`export` with default current directory output, `import`, `convert`, live progress reporter `ConsoleProgressReporter`).
- [x] Phase 4: Safety Commands (`backups` snapshot table, `revert` snapshot restore).
- [x] Phase 5: End-to-End CLI Tests (`EndToEndCliTests.cs`) & `.NET 10` Single-File Trimmed Publish (`mdma.exe`).

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
