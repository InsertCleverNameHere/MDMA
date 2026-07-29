using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Mdma.Core;

/// <summary>
/// Reads NDM's task list from neatdb.db. Per docs/ndm.md §4.3, NDM's DB never
/// stores live progress — the authoritative downloaded-byte count for a task
/// is the sum of its actual seg.xN file sizes on disk. This reader uses that
/// authoritative source when location.InstallOrConfigDir (the temp dir) is
/// available, and falls back to parsing the "Paused ( P% )" status string as
/// an estimate when it isn't (e.g. after manual path validation, which only
/// confirms neatdb.db's folder — see NdmLocator.ValidateManualPath).
/// </summary>
public sealed partial class NdmListReader : IDownloadListReader
{
    private static readonly Regex StatusPercentPattern = StatusPercentRegex();

    public TargetApp App => TargetApp.NDM;

    public Result<IReadOnlyList<DownloadTaskSummary>> ScanTasks(TargetAppLocation location)
    {
        if (location.MetadataDir is null)
        {
            return new MdmaError(
                MdmaErrorCode.ScanFailed,
                "No metadata directory (neatdb.db location) is known for this NDM location.",
                Details: "TargetAppLocation.MetadataDir was null."
            );
        }

        var dbPath = Path.Combine(location.MetadataDir, "neatdb.db");
        if (!File.Exists(dbPath))
        {
            return new MdmaError(
                MdmaErrorCode.ScanFailed,
                "neatdb.db was not found at the expected location.",
                Details: dbPath
            );
        }

        List<(
            long id,
            string url,
            string filename,
            long filesize,
            string status,
            bool resumable
        )> rows;
        try
        {
            rows = ReadRows(dbPath);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.ScanFailed,
                "Failed to read tasks from neatdb.db.",
                Details: dbPath,
                Inner: ex
            );
        }

        var summaries = new List<DownloadTaskSummary>(rows.Count);
        foreach (var row in rows)
        {
            long downloaded = ResolveDownloadedBytes(location, row.id, row.filesize, row.status);

            summaries.Add(
                new DownloadTaskSummary(
                    NativeId: row.id.ToString(),
                    Source: TargetApp.NDM,
                    Filename: row.filename,
                    Url: row.url,
                    TotalBytes: row.filesize,
                    DownloadedBytes: downloaded,
                    StatusText: row.status,
                    Resumable: row.resumable
                )
            );
        }

        return Result<IReadOnlyList<DownloadTaskSummary>>.Ok(summaries);
    }

    private static List<(
        long id,
        string url,
        string filename,
        long filesize,
        string status,
        bool resumable
    )> ReadRows(string dbPath)
    {
        var results = new List<(long, string, string, long, string, bool)>();

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, url, filename, filesize, status, resumable FROM downloads;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var url = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var filename = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var filesize = reader.IsDBNull(3) ? 0L : Convert.ToInt64(reader.GetValue(3));
            var status = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var resumable = !reader.IsDBNull(5) && Convert.ToInt64(reader.GetValue(5)) != 0;

            results.Add((id, url, filename, filesize, status, resumable));
        }

        SqliteConnection.ClearPool(conn);
        return results;
    }

    /// <summary>Authoritative when the temp directory is known: sums real
    /// seg.xN file sizes for the task. Falls back to parsing the status
    /// string's percentage against filesize when the temp dir is unknown
    /// or the task's directory doesn't exist (e.g. task never started).</summary>
    private static long ResolveDownloadedBytes(
        TargetAppLocation location,
        long taskId,
        long totalBytes,
        string status
    )
    {
        // Temp dir is known: this is the authoritative path per docs/ndm.md §4.3.
        // If the task's own folder is missing, that's a real "0 bytes on disk"
        // fact, not a reason to fall back to the estimate below -- falling back
        // here would let a stale/misleading status string override ground truth.
        if (location.InstallOrConfigDir is not null)
        {
            var taskDir = Path.Combine(location.InstallOrConfigDir, taskId.ToString());
            if (!Directory.Exists(taskDir))
                return 0;

            long sum = 0;
            foreach (var segFile in Directory.GetFiles(taskDir, "seg.x*"))
            {
                sum += new FileInfo(segFile).Length;
            }
            return sum;
        }

        // Temp dir itself is unknown (e.g. manual-path-validation location) --
        // only now do we fall back to estimating from the status string.
        var match = StatusPercentPattern.Match(status);
        if (match.Success && long.TryParse(match.Groups[1].Value, out var pct))
        {
            return (long)(totalBytes * (pct / 100.0));
        }

        return 0;
    }

    [GeneratedRegex(@"(\d+)\s*%")]
    private static partial Regex StatusPercentRegex();
}
