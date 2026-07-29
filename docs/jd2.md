# MDMA Technical Specification: JDownloader 2 (JD2)

## 1. Executive Summary

This document provides the complete technical specification for **JDownloader 2 (JD2)** state parsing and task injection in the MDMA (Multiple Download Manager Analogue) conversion system.

JDownloader 2 uses a **Single Sparse File** model for binary download data and a **Rolling ZIP JSON** architecture for persistent state management. Cold injection into JD2 has been empirically tested and verified.

---

## 2. On-Disk Binary Storage Strategy

### 2.1 File Naming & Path Conventions

JDownloader 2 resolves target paths using a two-tier directory lookup:

1. **Global Default Directory:** Stored in `<JD2_Root>/cfg/org.jdownloader.settings.GeneralSettings.json` under key `"defaultdownloadfolder"`.
2. **Package-Level Directory:** Stored inside `cfg/downloadList<N>.zip` in each parent `FilePackageStorable` entry under key `"downloadFolder"`.
3. **Part File Path:** `<downloadFolder>/<Filename>.part`
4. **Final File Path:** `<downloadFolder>/<Filename>`

### 2.2 Sparse File Writing

- JDownloader 2 uses Java's `java.io.RandomAccessFile` in `"rw"` mode.
- All chunk connection threads seek directly into the single `.part` file (`raf.seek(offset)`).
- JD2 sets the OS-level sparse file attribute on NTFS/ext4 filesystems (`SparseFile.createSparseFile()`). Unwritten gaps between parallel chunk boundaries consume zero physical storage on disk.

---

## 3. Metadata State Persistence (`downloadList<N>.zip`)

JDownloader 2 stores its entire UI task tree and chunk state inside zipped JSON archives located in:
`%LOCALAPPDATA%\JDownloader 2\cfg\` (or `<JD2_Root>/cfg/`).

### 3.1 Rolling Counter Mechanism (Crash Protection)

To prevent state corruption during power loss or app crashes, JD2 never overwrites existing state archives. Instead, it uses a rolling integer counter:

- **File Pattern:** `downloadList<COUNTER>.zip` (e.g., `downloadList11283.zip`).
- **Startup Algorithm:** On boot, `DownloadController.java` inspects `cfg/`, sorts all `downloadList*.zip` files in **descending numerical order**, and loads the archive with the **highest counter**.
- **Shutdown Event:** Upon closing, JD2 dumps its memory state into a newly created `downloadList<COUNTER+1>.zip` file.

### 3.2 ZIP Archive Hierarchy

Inside the active `downloadList<N>.zip` file, data is stored in a two-level parent-child hierarchy:

```bash
downloadList11283.zip
├── 00                # FilePackageStorable (Package 0 Metadata)
├── 00_00             # DownloadLinkStorable (Package 0, Child Link 0)
├── 00_01             # DownloadLinkStorable (Package 0, Child Link 1)
├── 01                # FilePackageStorable (Package 1 Metadata)
├── 01_00             # DownloadLinkStorable (Package 1, Child Link 0)
└── extraInfo         # DownloadControllerStorable (JD2 Root & Global Config)
```

---

## 4. JSON Data Schemas

### 4.1 Parent Package Schema (`FilePackageStorable`)

File entry name inside ZIP: `<PackageID>` (e.g., `"99"`).

```json
{
  "uid": 1785268000000,
  "name": "MDMA Injected Downloads",
  "downloadFolder": "D:\\Downloads",
  "created": 1785268000000,
  "enabled": true
}
```

### 4.2 Download Link Schema (`jd.plugins.DownloadLinkStorable`)

File entry name inside ZIP: `<PackageID>_<LinkIndex>` (e.g., `"99_00"`).

```json
{
  "uid": 1785268000001,
  "name": "poc_test_file.bin",
  "url": "https://speed.hetzner.de/100MB.bin",
  "host": "hetzner.de",
  "size": 10485760,
  "current": 2097152,
  "chunkProgress": [2097152],
  "availablestatus": "TRUE",
  "enabled": true,
  "created": 1785268000000,
  "properties": {
    "CHUNKS": 1,
    "PROPERTY_RESUMEABLE": true,
    "URL_CONTENT": "https://speed.hetzner.de/100MB.bin"
  }
}
```

#### Key Fields Description

- **`uid`**: Unique 64-bit epoch millisecond timestamp.

- **`size`**: Total verified byte length of the download.
- **`current`**: Total cumulative bytes downloaded across all chunks.
- **`chunkProgress`**: Array of `long` integers tracking the last successfully written byte offset for each chunk thread.

---

## 5. MDMA Planned Injection Specification

To inject a new or converted download into JDownloader 2:

### Step 1: Process Safety Check (Process Guard)

MDMA **must** verify that `JDownloader2.exe` / `JDownloader.exe` is completely closed before touching `cfg/`. If JD2 is running in the background or minimized to the System Tray, its shutdown hook will overwrite the injection file on exit.

### Step 2: Binary `.part` File Construction

- MDMA creates `<DownloadDirectory>/<Filename>.part`.
- MDMA writes the physical converted byte chunks into their assigned offsets inside the single `.part` file.

### Step 3: Archive Counter Increment ($N+1$)

1. Scan `cfg/` for the highest existing `downloadList<N>.zip` file (e.g., `downloadList11283.zip`).
2. Target output archive name is set to `downloadList<N+1>.zip` (e.g., `downloadList11284.zip`).

### Step 4: ZIP Repacking & Entry Injection

1. MDMA reads `downloadList<N>.zip`.
2. MDMA duplicates all existing package/link JSON files into the new archive buffer.
3. MDMA creates a target package entry (e.g., `"99"`) and inserts the child link JSON entry (e.g., `"99_00"`).
4. MDMA compresses and writes `downloadList<N+1>.zip` to `cfg/`.

---

## 6. Validation Status

- **Status:** **VERIFIED & PASSED**
- **Test Date:** July 28, 2026
- **Result:** Synthetic 10MB test file (2MB downloaded) was injected via Python PoC script (`downloadList11284.zip`). On boot, JDownloader 2 recognized the entry under `"MDMA Injected Downloads"` package at **20% progress** and resumed downloading cleanly.
