# MDMA Technical Specification: Neat Download Manager (NDM) Analysis

## 1. Overview

This document outlines the reverse-engineered architecture of **Neat Download Manager (NDM)** regarding partial download storage, binary state serialization (`segments.bin`), dynamic segment allocation, and final file assembly. This specification forms the basis for MDMA’s NDM import/export module.

---

## 2. Directory & On-Disk Layout

NDM uses a **multi-file segment storage strategy**. Each download task is isolated inside a dedicated temporary directory named after NDM's internal Task ID (e.g., `516`).

- **Temp Directory:** `<NDM_Temp_Path>/<TaskID>/` (e.g., `D:/Downloads/NDM Temp/516/`)
- **Segment Files:** `seg.x0`, `seg.x1`, `seg.x2`, ..., `seg.xN`
- **Binary Master State File:** `segments.bin` (1 KB binary file)
- **Log File:** `LogFile.txt` (Contains diagnostic HTTP request/response logs and state transitions)

---

## 3. Master Binary State Format (`segments.bin`)

### 3.1 Structure Overview

`segments.bin` acts as the Master Partition Blueprint for a download task. It consists of an array of fixed 24-byte (`0x18`) records stored in **Little Endian** format.

$$\text{Total Size of segments.bin} = \text{NumSegments} \times 24 \text{ bytes}$$

### 3.2 C Struct Definition

```c
#pragma pack(push, 1) // 1-byte byte alignment
typedef struct {
    uint16_t segment_id;      // Segment identifier (e.g., 0 for seg.x0, 1 for seg.x1)
    uint16_t segment_index;   // Internal connection slot index
    int32_t  next_segment_id; // ID of adjacent downstream segment (-1 / 0xFFFFFFFF if last)
    uint64_t start_byte;      // Absolute starting byte index in complete file (Little Endian)
    uint64_t end_byte;        // Absolute ending byte index in complete file (Little Endian)
} NDMSegmentRecord; // Exactly 24 bytes (0x18)
#pragma pack(pop)
```

### 3.3 Field Description

| Field Name        | Type       | Description                                                                                                             |
| :---------------- | :--------- | :---------------------------------------------------------------------------------------------------------------------- |
| `segment_id`      | `uint16_t` | Matches the extension of `seg.xN` (e.g., `0` maps to `seg.x0`).                                                         |
| `segment_index`   | `uint16_t` | Identifies thread/slot position in the engine manager.                                                                  |
| `next_segment_id` | `int32_t`  | Singly-linked list pointer to the logically adjacent segment. Value is `-1` (`0xFFFFFFFF`) for the file's tail segment. |
| `start_byte`      | `uint64_t` | Fixed assigned start boundary (0-indexed).                                                                              |
| `end_byte`        | `uint64_t` | Fixed assigned target end byte boundary.                                                                                |

---

## 4. Progress & Resume Range Algorithms

### 4.1 Progress Calculation

NDM does **not** update byte offsets in `segments.bin` while downloading. Instead, progress is measured directly by checking the physical file length of `seg.xN` on disk:

$$\text{DownloadedBytes}_N = \text{FileSize}(\text{"seg.x"} + N)$$

$$\text{CurrentByteOffset}_N = \text{start\_byte}_N + \text{FileSize}(\text{"seg.x"} + N)$$

### 4.2 Resume Header Formula

When resuming a paused download, NDM reads `start_byte` and `end_byte` from `segments.bin`, inspects `seg.xN`'s length, and transmits HTTP Range headers:

$$\text{HTTP Range} = \text{bytes } (\text{start\_byte}_N + \text{FileSize}(\text{"seg.x"} + N)) - \text{end\_byte}_N$$

---

## 5. Dynamic Segment Allocation & Rollbacks

NDM dynamically optimizes network utilization during runtime:

1. **Dynamic Segment Splitting:**
   - If a fast connection completes early, NDM splits an unfinished byte range from a slower segment.
   - NDM spawns a new segment file (`seg.x8`, `seg.x9` ... `seg.xN`) and appends a new 24-byte `NDMSegmentRecord` to `segments.bin`.
   - The `next_segment_id` field is updated to maintain the singly-linked list chain across dynamic sub-segments.

2. **Error Recovery & Rollbacks:**
   - If a sub-segment encounters an HTTP error (e.g., Cloudflare `403 Forbidden` or timeout), NDM cancels that sub-segment and **merges its byte range back into the parent segment**, updating `segments.bin` accordingly.

---

## 6. Final File Assembly Phase (`Merging...`)

When all active segment files (`seg.x0` through `seg.xN`) reach `100%` downloaded length (`FileSize == end_byte - start_byte + 1`):

1. **State Transition:** NDM transitions engine state from `Downloading...` to `Merging...`.
2. **Linked-List Traversal:** Starting at `segment_id == 0` (`start_byte == 0`), NDM traverses the in-order segment chain following `next_segment_id`.
3. **Sequential Concatenation:** NDM opens the final destination file and streams/appends the contents of `seg.x0`, `seg.x1` ... `seg.xN` sequentially into the output file.
4. **Cleanup:** Upon successful assembly, NDM deletes the entire `<TaskID>/` directory (`segments.bin`, `seg.xN`, `LogFile.txt`).

---

## 7. MDMA Intermediate Format Mapping for NDM

```json
{
  "manager_type": "NeatDownloadManager",
  "task_id": "516",
  "temp_folder_path": "<NDM_Temp_Path>/516/",
  "total_bytes": 932158148,
  "downloaded_bytes": 182740992,
  "segment_count": 8,
  "segments": [
    {
      "segment_id": 0,
      "segment_file": "seg.x0",
      "start_byte": 0,
      "end_byte": 117006200,
      "downloaded_bytes": 31690176,
      "next_segment_id": 7
    },
    {
      "segment_id": 7,
      "segment_file": "seg.x7",
      "start_byte": 117006201,
      "end_byte": 233389810,
      "downloaded_bytes": 32307200,
      "next_segment_id": 3
    }
  ]
}
```

---

## 8. Future Implementation Considerations

- **Task ID Folder Resolution:** In future phases, MDMA will map how NDM maps its main UI task list (stored in Windows Registry / `%APPDATA%` / SQLite) to the numeric folder IDs (e.g. `516`).
- **File Reconstruction for Conversion:** When converting JD2’s single sparse `.part` file to NDM, MDMA must slice byte ranges from `.part` into individual `seg.xN` files and synthesize a valid `segments.bin` binary file.
