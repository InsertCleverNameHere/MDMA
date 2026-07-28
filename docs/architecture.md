# MDMA Architecture

**M**ulti **D**ownload **M**anager **A**nalogue — a tool for migrating in-progress and completed downloads between download managers (e.g. Neat Download Manager, JDownloader 2), including across machines.

Status: design phase. This document captures decisions made so far and the reasoning behind them, so future changes can be checked against original intent rather than re-litigated from scratch.

---

## 1. Design Philosophy

- **Non-destructive by default.** MDMA never mutates a target app's state without a verified-good backup first. If a backup can't be taken, the operation doesn't proceed.
- **One code path, not two.** Same-machine conversion and cross-machine migration are the _same_ operation (export → `.mdma` → import), never a "fast direct path" and a "portable path" that could silently drift apart or get tested unevenly.
- **Fail before writing, not during.** Every precondition (disk space, process guard, path writability, target app validation) is checked up front. Once a destructive write starts, it should be vanishingly unlikely to fail partway.
- **Portable-first, not installed-first.** MDMA assumes it may be run from a USB stick or Desktop folder with no admin rights and no guarantee `C:` has room to spare. Nothing should default to paths outside the app's control (like `%TEMP%`) without explicit fallback logic and a visible warning.
- **Core has no UI opinions.** All real logic lives in a UI-agnostic core library. CLI and GUI are both thin shells over the same contract, so behavior can never diverge between them.

---

## 2. Target Applications (Phase 1)

| App                         | Storage model                                                                                                         | State location                                                                                        |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| Neat Download Manager (NDM) | Multi-file segments (`seg.x0`...`seg.xN`) + fixed 24-byte binary struct array (`segments.bin`) + SQLite (`neatdb.db`) | Registry (`HKCU\SOFTWARE\NeatDM`) points to temp/download dirs; `neatdb.db` under `%APPDATA%\NeatDM\` |
| JDownloader 2 (JD2)         | Single sparse file (`.part`) + rolling ZIP-of-JSON state (`downloadList<N>.zip`)                                      | `%LOCALAPPDATA%\JDownloader 2\cfg\`                                                                   |

Both are fully reverse-engineered and empirically validated as of 2026-07-28 (see `docs/ndm.md`, `docs/jd2.md`). Both directions (NDM↔JD2) are considered proven at the injection level; MDMA formalizes and generalizes this into a real app.

The chunk model both formats reduce to — `(start_byte, end_byte, downloaded_bytes)` — is treated as the universal join point, so future targets (Free Download Manager, DAP, etc.) are expected to slot into the same abstraction without a schema change.

---

## 3. Language & Runtime

**C# / .NET**, self-contained, single-file, trimmed publish (`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true`).

Rationale:

- Native, first-class support for everything the domain needs: registry access, SQLite, ZIP/JSON, and `StructLayout(LayoutKind.Sequential, Pack=1)` for NDM's binary struct.
- Single-file publish satisfies the "minimal dependencies in distribution" requirement — no installer, no runtime prompt, no scattered DLLs.
- One language across Core, CLI, and GUI keeps the whole domain (struct packing, SQLite writes, registry transactions) in a single type system with no FFI boundary.
- GUI via WPF (Windows-only is already a given, since the registry dependency ties NDM support to Windows regardless of language choice).

Rejected/deferred alternatives:

- **Python** — good for one-off reverse-engineering scripts (already used and discarded for exactly that purpose), poor fit for a shipped GUI app.
- **Rust + egui** — smallest possible binary and zero runtime bundling, kept as a fallback option if binary size ever becomes a hard requirement rather than a soft preference. Not chosen now because it slows down GUI development for no proven benefit yet.

---

## 4. Solution Structure

```bash
Mdma.Core      — all logic: discovery, scanning, export, import, injectors, safety layer
Mdma.Cli       — headless wrapper over Core, scriptable, exit-code driven
Mdma.Gui       — WPF wrapper over Core, scan → select → convert flow
```

Core exposes no UI types and makes no assumptions about how it's invoked. Nothing in Cli or Gui should contain logic that isn't a thin call into Core.

---

## 5. The `.mdma` Format

A `.mdma` file is a self-contained ZIP archive — not a metadata pointer to local files — so it can be copied to a different machine and imported there with no dependency on the machine that produced it.

```bash
task.mdma (zip)
├── manifest.json      # task metadata + chunk table
├── data/
│   ├── chunk_0.bin    # actual downloaded bytes for this chunk
│   ├── chunk_1.bin
│   └── ...
└── checksum.sha256    # integrity check over the payload
```

Design notes:

- Chunks are stored as **separate files**, not one reconstructed blob, because target layouts differ (NDM wants per-segment files, JD2 wants offsets into one sparse file). The importer decides how to lay bytes out; the container itself stays layout-agnostic.
- `manifest.json` carries an `origin` tag (source app) for provenance/debugging only — the importer should never need to know where a `.mdma` came from to import it correctly.
- Only _downloaded_ bytes are packaged (`downloaded_bytes`, not `total_size`), keeping partial-download exports proportionally sized.
- ZIP gives free compression via `System.IO.Compression` with no added dependency.

`manifest.json` shape (subject to refinement):

```json
{
  "mdma_version": 1,
  "origin": "NDM",
  "task": {
    "url": "...",
    "filename": "...",
    "total_size": 10485760,
    "mimetype": "...",
    "headers": [{ "name": "Referer", "value": "..." }],
    "created": 1785268000000
  },
  "chunks": [
    {
      "index": 0,
      "start_byte": 0,
      "end_byte": 2097151,
      "downloaded_bytes": 2097152
    }
  ]
}
```

---

## 6. Operation Model

**Every conversion — same-machine or cross-machine — is export-then-import through a real `.mdma` file.** There is no separate "direct" fast path. This guarantees CLI, GUI, same-machine, and cross-machine conversions all exercise identical, well-tested code.

For same-machine conversions, the `.mdma` is temporary:

1. Created under `<workdir>\.mdma-tmp\<guid>.mdma.partial`
2. Finalized via atomic rename to `.mdma` once fully written and checksummed
3. Imported into the target app
4. Deleted on success — **best-effort**, not required for the operation to be considered successful (see §8 on step criticality)

On every startup, Core sweeps `.mdma-tmp\` for orphaned `.partial`/`.mdma` files left by crashed prior runs and surfaces them for cleanup (CLI: `mdma cleanup`; GUI: passive notice with size shown).

---

## 7. Working Directory Resolution

MDMA never assumes `%TEMP%` or any OS default has space to spare. Working root is resolved once at startup, in order:

1. **Explicit override** — `--workdir <path>` (CLI) or a persisted GUI setting.
2. **Portable default** — `MDMA_Work\` next to the running exe (`AppContext.BaseDirectory`), matching the "operate from where the exe is started" requirement.
3. **Fallback** — `%LOCALAPPDATA%\MDMA\work\` if the exe directory isn't writable (e.g. read-only mount, unelevated `Program Files`), with a visible warning since this silently breaks portable-mode expectations.

Resolved once, validated once, shared identically by CLI and GUI via a single `IWorkingDirectoryProvider`.

### Validation before any operation

- **Writability**: probe with an actual write+delete, not a permissions-bit check (junctions, network drives, and AV locks all lie about permission flags).
- **Free space — source side**: compare available bytes at the working root against the task's `downloaded_bytes` (not `total_size`), plus a 10–15% margin for zip/checksum overhead. Fails fast with the exact shortfall stated.
- **Free space — destination side**: the same check applies to the _target_ app's install/temp directory during import, since injection also writes physical chunk files there. A same-machine conversion is therefore validated on both ends before anything is written.
- **Path sanity**: reject a working dir nested inside a source/target app's own directories.

---

## 8. Safety Layer

- **Discovery**: each target app has an `IDownloadManagerLocator` that tries registry/config-based auto-detection first (NDM via `HKCU\SOFTWARE\NeatDM`, JD2 via `%LOCALAPPDATA%\JDownloader 2\cfg\`), and returns a clean "not found" rather than throwing, so CLI/GUI can prompt for a manual path. Manual paths are validated (e.g. `neatdb.db` actually opens, `cfg\` actually contains a `downloadList*.zip`) before being accepted.
- **Process guard**: before any Core operation touches an app's files, verify that app's process isn't running, to avoid SQLite lock conflicts or the app's own shutdown hook overwriting an injected file.
- **Backups**: before any mutation of `neatdb.db`, the registry, or `cfg\`, take a versioned snapshot to `%LOCALAPPDATA%\MDMA\backups\<timestamp>_<target>\`. Backups are enumerable, not just "last one," so revert can target a specific prior state.
- **Atomic writes**: every file mutation goes write-to-temp-then-rename. SQLite and ZIP files use copy-modify-swap; registry writes use a read-all-then-write-then-verify wrapper since multi-key registry edits aren't natively atomic.
- **Revert**: `IRevertManager.Revert(backupId)` restores a specific snapshot, undoing one MDMA operation.

### Step criticality

Steps are explicitly classified so failure handling is consistent rather than ad hoc:

| Step                                    | Class       | On failure                                                 |
| --------------------------------------- | ----------- | ---------------------------------------------------------- |
| Backup before write                     | Critical    | Abort entire operation before any destructive write occurs |
| Disk space check (source & destination) | Critical    | Fail fast, before scan/export/import begins                |
| Process guard                           | Critical    | Fail fast, before backup or write starts                   |
| Injection / write itself                | Critical    | Atomic writer rolls back; original files untouched         |
| Temp `.mdma` cleanup after success      | Best-effort | Logged, non-fatal; operation still reports success         |

This Critical vs. Best-effort distinction is meant to be a first-class concept in Core (not scattered try/catch), so it's applied consistently as new targets are added.

---

## 9. Error Taxonomy (CLI exit codes / GUI messaging)

| Condition                                     | Behavior                                                                     |
| --------------------------------------------- | ---------------------------------------------------------------------------- |
| Working dir unwritable                        | Fail fast at startup                                                         |
| Insufficient space (source or destination)    | Fail fast, states required vs. available bytes                               |
| Space exhausted mid-write                     | Abandon the temp file; source untouched (staging, not moving)                |
| Target app process running                    | Fail fast, before backup starts                                              |
| Backup step fails                             | Abort — never proceed to a destructive write without a confirmed-good backup |
| Import/injection succeeds, temp cleanup fails | Reported as success; cleanup failure logged separately                       |

---

## 10. Open Questions / Deferred Decisions

- Backup retention policy: how many snapshots per target to keep, and where the cutoff/pruning logic lives.
- Whether an explicit "metadata-only" export mode is worth adding later for same-machine conversions where byte-copying is provably wasteful (deferred — full byte export is the correct default for portability and is what's being built first).
- Exact `manifest.json` versioning/migration strategy once `mdma_version` needs to increment.
- Third and later target apps (Free Download Manager, DAP, etc.) — expected to fit the existing chunk abstraction, but not yet scoped.

---

## 11. Non-Goals (Phase 1)

- No macOS/Linux support (NDM's registry dependency makes this Windows-only regardless of the JD2 side).
- No auto-resume orchestration inside MDMA itself — MDMA hands off a correctly-injected task to the target app and stops; the target app resumes it.
- No network transfer of `.mdma` files — cross-machine transfer is the user's responsibility (USB, cloud drive, etc.); MDMA only guarantees the file is self-contained and correct once it arrives.
