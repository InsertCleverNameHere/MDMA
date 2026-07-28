# MDMA Technical Specification: Neat Download Manager (NDM)

## 1. Executive Summary

This document details the complete reverse-engineered architecture of **Neat Download Manager (NDM)** for the MDMA system.

NDM uses a **Multi-File Segment Storage** model for temporary chunk data, a **24-byte Little Endian C-struct Blueprint (`segments.bin`)** for range partitioning, an **SQLite 3 database (`neatdb.db`)** for global UI task indexing, and **Windows Registry keys** for path and Task ID tracking. Cold injection into NDM has been empirically tested and verified.

---

## 2. Windows Registry Configuration

NDM stores user settings and task counters under the Windows Registry key:
`HKEY_CURRENT_USER\SOFTWARE\NeatDM`

| Key Name                | Type        | Description                                     | Example Value                 |
| :---------------------- | :---------- | :---------------------------------------------- | :---------------------------- |
| **`TempDirectory`**     | `REG_SZ`    | Path to active NDM temp working directory       | `"D:\\Downloads\\NDM Temp\\"` |
| **`DownloadDirectory`** | `REG_SZ`    | Default output directory for finished downloads | `"D:\\Downloads\\"`           |
| **`LastDownloadID`**    | `REG_DWORD` | Auto-incrementing integer Task ID counter       | `521` (`0x00000209`)          |

---

## 3. Directory & On-Disk Layout

NDM isolates each download task inside a subfolder named after its numeric Task ID:
`<TempDirectory>/<TaskID>/` (e.g., `D:\Downloads\NDM Temp\521\`).

### Files Inside Task Directory

- **`seg.x0`, `seg.x1`, ..., `seg.xN`**: Raw sequential binary data files for each segment connection.
- **`segments.bin`**: Fixed 24-byte binary C-struct array tracking segment range boundaries and linked-list chains.
- **`LogFile.txt`**: Execution and HTTP request/response log.

---

## 4. Master Binary State Blueprint (`segments.bin`)

### 4.1 Overview

`segments.bin` acts as the master segment partition map. It consists of an array of 24-byte (`0x18`) records written in **Little Endian** format:

$$\text{FileSize of segments.bin} = \text{NumSegments} \times 24 \text{ bytes}$$

### 4.2 C Struct Specification

```c
#pragma pack(push, 1) // 1-byte alignment
typedef struct {
    uint16_t segment_id;      // Matches file extension seg.xN (e.g., 0 for seg.x0)
    uint16_t segment_index;   // Internal connection slot index
    int32_t  next_segment_id; // Singly-linked list pointer to adjacent segment (-1 if last)
    uint64_t start_byte;      // Absolute starting byte offset in target file (Little Endian)
    uint64_t end_byte;        // Absolute ending byte offset in target file (Little Endian)
} NDMSegmentRecord; // Exactly 24 bytes (0x18)
#pragma pack(pop)
```

### 4.3 Progress & Resume Range Formulas

Progress for segment $N$ is **not** written to `segments.bin` during runtime. Progress is measured dynamically from the physical file length of `seg.xN` on disk:

$$\text{DownloadedBytes}_N = \text{FileSize}(\text{"seg.x"} + N)$$

When resuming, NDM transmits the HTTP Range header:

$$\text{HTTP Range} = \text{bytes } (\text{start\_byte}_N + \text{FileSize}(\text{"seg.x"} + N)) - \text{end\_byte}_N$$

---

## 5. Global Task Index (`neatdb.db`)

NDM maintains its main UI list in an SQLite 3 database located at:
`%APPDATA%\NeatDM\neatdb.db`

### 5.1 `downloads` Table Schema

Every row represents a task displayed in NDM's main UI window:

```sql
CREATE TABLE downloads (
    id INTEGER PRIMARY KEY,
    url TEXT,
    method TEXT,
    filename TEXT,
    ltype TEXT,
    filesize NUMERIC,
    category TEXT,
    status TEXT,          -- UI Status String: "Paused ( 20% )"
    bandwidthlimit NUMERIC,
    connections NUMERIC,
    lasttry NUMERIC,      -- Epoch timestamp
    firsttry NUMERIC,     -- Epoch timestamp
    useragent TEXT,
    resumable NUMERIC,    -- 1 (True), 0 (False)
    pageurl TEXT,
    pagetitle TEXT,
    hittitle TEXT,
    mimetype TEXT,
    errortext TEXT,
    urla TEXT,
    postdata TEXT,
    folderpath TEXT,      -- Destination directory
    temppath TEXT         -- Temp root directory
);
```

#### Crucial Discovery: The `status` Column

NDM's UI table renders status directly from the text in the `status` column. When MDMA injects a paused task, it must format the percentage string explicitly:

$$\text{Percentage} = \left\lfloor \left( \frac{\text{DownloadedBytes}}{\text{TotalBytes}} \right) \times 100 \right\rfloor$$

$$\text{status} = \text{f"Paused ( \{\text{Percentage}\}\% )"}$$

### 5.2 `headers` Table Schema

Stores custom HTTP request headers associated with task `id`:

- **`id`**: INTEGER (Task ID matching `downloads.id`)
- **`header`**: TEXT (e.g., `'Referer: https://example.com'`)

---

## 6. MDMA Planned Injection Specification

To inject a converted download into Neat Download Manager:

### Step 1: Process Guard

Verify `NeatDownloadManager.exe` is closed to avoid SQLite file-lock conflicts.

### Step 2: Query Registry Configuration

1. Read `TempDirectory`, `DownloadDirectory`, and `LastDownloadID` from `HKCU\SOFTWARE\NeatDM`.
2. Compute new Task ID:
   $$\text{NewTaskID} = \text{LastDownloadID} + 1$$

### Step 3: Create Temp Directory & Chunk Files

1. Create directory `<TempDirectory>/<NewTaskID>/`.
2. Write physical chunk files (`seg.x0`, `seg.x1` ... `seg.xN`).
3. Synthesize `segments.bin` with $N$ packed 24-byte `NDMSegmentRecord` structs.

### Step 4: Insert Record into SQLite (`neatdb.db`)

1. Open `%APPDATA%\NeatDM\neatdb.db`.
2. Compute percentage $P$ and format status: `"Paused ( P% )"`.
3. Execute `INSERT INTO downloads` with `id = NewTaskID`, `status`, `filesize`, `filename`, `url`, `folderpath`, `temppath`.
4. Execute `INSERT INTO headers` with `id = NewTaskID`.

### Step 5: Update Registry Counter

Update `HKCU\SOFTWARE\NeatDM\LastDownloadID` to `NewTaskID`.

---

## 7. Validation Status

- **Status:** **VERIFIED & PASSED**
- **Test Date:** July 28, 2026
- **Result:** Synthetic 10MB test file (2MB downloaded) was injected via Python PoC script (`ndm_poc_inject_v2.py`). On boot, Neat Download Manager 1.4 immediately rendered `poc_ndm_perfect.bin` displaying **`Paused ( 20% )`** in the UI list.
