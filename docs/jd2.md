# MDMA Technical Specification: JDownloader 2 Analysis

## 1. Overview

This document details the reverse-engineered architecture of **JDownloader 2 (JD2)** regarding partial download storage, metadata serialization, and multi-chunk offset tracking. This specification serves as the foundation for the MDMA (Multiple Download Manager Analogue) conversion layer for JDownloader 2.

---

## 2. On-Disk Binary Storage Scheme

### 2.1 File Naming & Path Conventions

When JDownloader 2 initiates a download task, it writes bytes to a temporary part file inside the destination directory:

- **Target File Path:** `<DownloadDirectory>/<Filename>`
- **Part File Path:** `<DownloadDirectory>/<Filename>.part`
- _Custom/Tmp Filename Overrides:_ Stored under properties `PROPERTY_CUSTOM_LOCALFILENAME` and `PROPERTY_CUSTOM_LOCALFILENAMEAPPEND`.

### 2.2 Sparse Single-File Architecture

Unlike managers that write chunks into separate temporary files (`.part0`, `.part1`), JDownloader 2 writes all chunks into **a single contiguous `.part` file**:

- **API Used:** `java.io.RandomAccessFile` in read/write mode (`"rw"`).
- **Multi-Threading:** Multiple chunk threads write concurrently into the same `.part` file by seeking (`raf.seek(writePos)`) to their assigned byte offset boundaries.
- **Sparse Allocation:** On Windows (NTFS) and modern Unix filesystems (ext4/APFS), JDownloader 2 enables OS-level sparse file flags (`SparseFile.createSparseFile(outputPartFile)` / `FSCTL_SET_SPARSE`). Unwritten byte gaps between chunks occupy zero physical block storage on disk until written.

### 2.3 Completion & Finalization Workflow

When all chunk threads complete and pass integrity checks:

1. `DownloadLinkDownloadable.rename()` attempts an atomic file rename from `<Filename>.part` to `<Filename>`.
2. **Fallback Strategy:** If file locks prevent atomic renaming (e.g., antivirus locking), JD2 allocates target disk space, copies bytes via stream buffers (`IO.copyFile`), and deletes the original `.part` file.
3. Timestamp attributes (`Last-Modified`) are applied to the final file if enabled in settings.

---

## 3. Metadata Persistence & Serialization

### 3.1 Global State Container Location

JDownloader 2 maintains state persistence across app restarts using zipped JSON archives located in the application configuration directory:

- **Active State File:** `<JDownloader_Root>/cfg/downloadList.zip`
- **Backup Archives:** `downloadList1.zip`, `downloadList2.zip`, etc. (managed by `DownloadController.java`).

### 3.2 Internal ZIP Hierarchy

Inside `downloadList.zip`, files are stored as serialized JSON entries:

```bash
downloadList.zip
├── 00                # FilePackageStorable (Package 0 Metadata)
├── 00_00             # DownloadLinkStorable (Package 0, Link 0 Metadata)
├── 00_01             # DownloadLinkStorable (Package 0, Link 1 Metadata)
├── 01                # FilePackageStorable (Package 1 Metadata)
├── 01_00             # DownloadLinkStorable (Package 1, Link 0 Metadata)
└── extraInfo         # DownloadControllerStorable (Root Path & Global Config)
```

### 3.3 Key Properties Map (`DownloadLinkStorable` / `Property`)

Each `DownloadLink` node encapsulates internal properties serialized into the JSON payload:

| Key Name                       | Type    | Description                                                  |
| :----------------------------- | :------ | :----------------------------------------------------------- |
| `urlDownload` / `URL_CONTENT`  | String  | Direct content download URL / pattern                        |
| `URL_REFERRER`                 | String  | HTTP Referer header                                          |
| `URL_ORIGIN` / `URL_CONTAINER` | String  | Source page or container URL                                 |
| `VERIFIEDFILESIZE`             | Long    | Total verified file size in bytes (-1 if unknown)            |
| `downloadCurrent`              | Long    | Total cumulative bytes downloaded across all chunks          |
| `CHUNKS`                       | Integer | Total configured/allocated chunk count                       |
| `PROPERTY_RESUMEABLE`          | Boolean | Whether server range requests are supported                  |
| `HASHINFO`                     | String  | Hash type and expected hash value (MD5, SHA1, SHA256, CRC32) |
| `chunksProgress`               | Long[]  | Array tracking the last written byte position per chunk      |

---

## 4. Multi-Chunk Division & Resume Algorithms

### 4.1 Virgin Download Initialization (`setupVirginStart`)

When starting a new download with $N$ requested chunks and total file size $S$:

$$\text{partSize} = \lfloor S / N \rfloor$$

Chunk boundaries are partitioned sequentially:

- **Chunk $0$:** Range $[0, \text{partSize} - 1]$
- **Chunk $i$:** Range $[i \cdot \text{partSize}, (i + 1) \cdot \text{partSize} - 1]$
- **Final Chunk ($N-1$):** Range $[(N - 1) \cdot \text{partSize}, S - 1]$

_Note: If HTTP connection 0 returns an initial byte range response, range positions are dynamically adjusted relative to the active content length._

### 4.2 Resume State Calculation (`setupResume`)

JD2 inspects the `chunksProgress` array (length $N$) to resume an interrupted task:

1. `chunksProgress[i]` holds the **absolute byte offset** last successfully written by Chunk $i$.
2. The resume start byte for Chunk $i$ is calculated as:

$$\text{ResumeStartByte}_i = \begin{cases} 0 & \text{if } \text{chunksProgress}[i] == 0 \\ \text{chunksProgress}[i] + 1 & \text{if } \text{chunksProgress}[i] > 0 \end{cases}$$

1. The HTTP request for Chunk $i$ issues the header: `Range: bytes=<ResumeStartByte_i>-<ChunkEndByte_i>`

---

## 5. MDMA Intermediate Format Mapping for JD2

To import/export a JDownloader 2 task into MDMA, map the fields as follows:

```json
{
  "manager_type": "JDownloader2",
  "target_path": "<DownloadDirectory>",
  "target_filename": "<Filename>",
  "part_file_path": "<DownloadDirectory>/<Filename>.part",
  "file_size": {
    "verified_bytes": "VERIFIEDFILESIZE",
    "downloaded_bytes": "downloadCurrent"
  },
  "http_metadata": {
    "content_url": "URL_CONTENT",
    "referrer_url": "URL_REFERRER",
    "container_url": "URL_CONTAINER",
    "is_resumable": "PROPERTY_RESUMEABLE"
  },
  "integrity": {
    "hash_type": "HASHINFO.type",
    "hash_value": "HASHINFO.value"
  },
  "chunk_mapping": {
    "count": "CHUNKS",
    "progress_array": "chunksProgress"
  }
}
```

---

## 6. Guidelines for MDMA Converter Development

1. **Reading JD2 State:**
   - Unzip `cfg/downloadList.zip` in memory or temp storage.
   - Locate the target `DownloadLinkStorable` entry by filename/URL.
   - Extract URLs, total size, hash, and `chunksProgress` values.
   - Read physical `.part` file to verify actual file length on disk matches `max(chunksProgress)`.

2. **Writing/Injecting into JD2:**
   - Create single contiguous `<Filename>.part` file at target destination.
   - Write physical chunk byte data at calculated offsets.
   - Construct a `DownloadLinkStorable` JSON with mapped properties and correct `chunksProgress` array.
   - Pack into `downloadList.zip` (or append to active `DownloadController` queue).
