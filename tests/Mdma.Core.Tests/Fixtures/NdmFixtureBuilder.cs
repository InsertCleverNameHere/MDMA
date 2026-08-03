using Microsoft.Data.Sqlite;

namespace Mdma.Core.Tests.Fixtures;

/// <summary>
/// Builds a synthetic, on-disk NDM task directory + neatdb.db, matching the
/// real format documented in docs/ndm.md, so tests never depend on a real
/// NDM install. Call Build() to get everything on disk under a temp root,
/// then point NdmLocator/NdmListReader/NdmExporter/NdmInjector at it.
/// </summary>
public sealed class NdmFixtureBuilder
{
    public string TempDirectory { get; }
    public string DownloadDirectory { get; }
    public string NeatDbPath { get; }

    private readonly List<(int TaskId, string Filename, string Url, long TotalBytes, List<(long start, long end, long downloaded)> chunks)> _tasks = new();
    private readonly Dictionary<int, (string? MimeType, List<(string Name, string Value)> Headers)> _metadataByTaskId = new();

    public NdmFixtureBuilder(string rootDir)
    {
        TempDirectory = Path.Combine(rootDir, "NDM Temp");
        DownloadDirectory = Path.Combine(rootDir, "Downloads");
        NeatDbPath = Path.Combine(rootDir, "neatdb.db");
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(DownloadDirectory);
    }

    /// <summary>Adds a task. Each chunk tuple is (startByte, endByte, downloadedBytes).
    /// downloadedBytes controls how many bytes are actually written to seg.xN,
    /// letting you construct partial-download fixtures.</summary>
    public NdmFixtureBuilder WithTask(
        int taskId,
        string filename,
        string url,
        long totalBytes,
        params (long start, long end, long downloaded)[] chunks)
    {
        _tasks.Add((taskId, filename, url, totalBytes, chunks.ToList()));
        return this;
    }

    /// <summary>Attaches mimetype and/or headers to the task with the given id
    /// (must have already been added via WithTask). Optional -- only needed
    /// by tests exercising NdmExporter's metadata-reading path.</summary>
    public NdmFixtureBuilder WithMetadata(int taskId, string? mimeType = null, params (string Name, string Value)[] headers)
    {
        _metadataByTaskId[taskId] = (mimeType, headers.ToList());
        return this;
    }

    public void Build()
    {
        BuildDb();
        foreach (var task in _tasks)
        {
            BuildTaskDirectory(task.TaskId, task.chunks);
        }
    }

    private void BuildDb()
    {
        // Pooling=False: without this, Microsoft.Data.Sqlite can keep a native
        // handle to NeatDbPath open in its connection pool even after this
        // `using` block disposes the connection, which then breaks any test
        // that tries to delete the fixture directory right after Build().
        var connectionString = $"Data Source={NeatDbPath};Pooling=False";
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE downloads (
                id INTEGER PRIMARY KEY, url TEXT, method TEXT, filename TEXT,
                ltype TEXT, filesize NUMERIC, category TEXT, status TEXT,
                bandwidthlimit NUMERIC, connections NUMERIC, lasttry NUMERIC,
                firsttry NUMERIC, useragent TEXT, resumable NUMERIC, pageurl TEXT,
                pagetitle TEXT, hittitle TEXT, mimetype TEXT, errortext TEXT,
                urla TEXT, postdata TEXT, folderpath TEXT, temppath TEXT
            );
            CREATE TABLE headers (id INTEGER, header TEXT);
            """;
        cmd.ExecuteNonQuery();

        foreach (var task in _tasks)
        {
            long downloaded = task.chunks.Sum(c => c.downloaded);
            int pct = task.TotalBytes <= 0 ? 0 : (int)(downloaded * 100 / task.TotalBytes);
            var status = $"Paused ( {pct}% )";

            var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO downloads (id, url, filename, filesize, status, resumable, folderpath, temppath, mimetype)
                VALUES ($id, $url, $filename, $filesize, $status, 1, $folderpath, $temppath, $mimetype);
                """;
            insert.Parameters.AddWithValue("$id", task.TaskId);
            insert.Parameters.AddWithValue("$url", task.Url);
            insert.Parameters.AddWithValue("$filename", task.Filename);
            insert.Parameters.AddWithValue("$filesize", task.TotalBytes);
            insert.Parameters.AddWithValue("$status", status);
            insert.Parameters.AddWithValue("$folderpath", DownloadDirectory);
            insert.Parameters.AddWithValue("$temppath", TempDirectory);
            var mimeType = _metadataByTaskId.TryGetValue(task.TaskId, out var meta) ? meta.MimeType : null;
            insert.Parameters.AddWithValue("$mimetype", (object?)mimeType ?? DBNull.Value);
            insert.ExecuteNonQuery();

            if (_metadataByTaskId.TryGetValue(task.TaskId, out var metaForHeaders))
            {
                foreach (var (name, value) in metaForHeaders.Headers)
                {
                    var headerInsert = conn.CreateCommand();
                    headerInsert.CommandText = "INSERT INTO headers (id, header) VALUES ($id, $header);";
                    headerInsert.Parameters.AddWithValue("$id", task.TaskId);
                    headerInsert.Parameters.AddWithValue("$header", $"{name}: {value}");
                    headerInsert.ExecuteNonQuery();
                }
            }
        }

        SqliteConnection.ClearPool(conn);
    }

    private void BuildTaskDirectory(int taskId, List<(long start, long end, long downloaded)> chunks)
    {
        var taskDir = Path.Combine(TempDirectory, taskId.ToString());
        Directory.CreateDirectory(taskDir);

        // segments.bin: 24-byte records, little-endian, per docs/ndm.md §4.2
        using (var fs = new FileStream(Path.Combine(taskDir, "segments.bin"), FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                var (start, end, _) = chunks[i];
                bw.Write((ushort)i);                              // segment_id
                bw.Write((ushort)i);                               // segment_index
                bw.Write(i == chunks.Count - 1 ? -1 : i + 1);      // next_segment_id
                bw.Write((ulong)start);                            // start_byte
                bw.Write((ulong)end);                              // end_byte
            }
        }

        // seg.xN files: filled with deterministic dummy bytes up to `downloaded` length
        for (int i = 0; i < chunks.Count; i++)
        {
            var (_, _, downloaded) = chunks[i];
            var bytes = new byte[downloaded];
            new Random(taskId * 1000 + i).NextBytes(bytes); // deterministic per task+chunk
            File.WriteAllBytes(Path.Combine(taskDir, $"seg.x{i}"), bytes);
        }
    }
}
