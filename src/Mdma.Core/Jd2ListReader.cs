using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mdma.Core;

/// <summary>
/// Reads JD2's task list from the newest downloadList<N>.zip in cfg\, per
/// docs/jd2.md §3-4. Entry naming: "<PackageID>" is a FilePackageStorable,
/// "<PackageID>_<LinkIndex>" is a DownloadLinkStorable child. This reader
/// flattens every link across every package into DownloadTaskSummary objects.
///
/// current/size map directly to DownloadedBytes/TotalBytes -- unlike NDM, JD2's
/// own JSON already tracks live progress (chunkProgress/current), so no
/// separate "read real file sizes" step is needed here the way NdmListReader
/// requires for seg.x* files.
/// </summary>
public sealed partial class Jd2ListReader : IDownloadListReader
{
    private static readonly Regex LinkEntryPattern = LinkEntryRegex();

    public TargetApp App => TargetApp.JD2;

    public Result<IReadOnlyList<DownloadTaskSummary>> ScanTasks(TargetAppLocation location)
    {
        if (location.InstallOrConfigDir is null)
        {
            return new MdmaError(
                MdmaErrorCode.ScanFailed,
                "No cfg\\ directory is known for this JD2 location.",
                Details: "TargetAppLocation.InstallOrConfigDir was null.");
        }

        var cfgDir = location.InstallOrConfigDir;
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(cfgDir, "downloadList*.zip");
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.ScanFailed,
                "Could not enumerate downloadList*.zip files.",
                Details: cfgDir,
                Inner: ex);
        }

        if (candidates.Length == 0)
        {
            return new MdmaError(
                MdmaErrorCode.ScanFailed,
                "No downloadList*.zip file was found.",
                Details: cfgDir);
        }

        var newestZip = Jd2Locator.PickNewest(candidates);

        try
        {
            return Result<IReadOnlyList<DownloadTaskSummary>>.Ok(ParseZip(newestZip));
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.ScanFailed,
                "Failed to parse the JD2 task list archive.",
                Details: newestZip,
                Inner: ex);
        }
    }

    private static List<DownloadTaskSummary> ParseZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        var links = new List<DownloadTaskSummary>();

        foreach (var entry in zip.Entries)
        {
            var linkMatch = LinkEntryPattern.Match(entry.Name);
            if (!linkMatch.Success) continue; // skip package entries and "extraInfo"

            var packageId = linkMatch.Groups[1].Value;
            var linkIndex = linkMatch.Groups[2].Value;

            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            var filename = GetString(root, "name") ?? entry.Name;
            var url = GetString(root, "url") ?? "";
            var size = GetInt64(root, "size") ?? 0;
            var current = GetInt64(root, "current") ?? 0;

            bool resumable = false;
            if (root.TryGetProperty("properties", out var props) &&
                props.TryGetProperty("PROPERTY_RESUMEABLE", out var resumeProp) &&
                resumeProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                resumable = resumeProp.GetBoolean();
            }

            int pct = size <= 0 ? 0 : (int)(current * 100 / size);
            var status = $"Paused ( {pct}% )"; // mirrors NDM's convention for consistent CLI/GUI display

            links.Add(new DownloadTaskSummary(
                NativeId: $"{packageId}_{linkIndex}",
                Source: TargetApp.JD2,
                Filename: filename,
                Url: url,
                TotalBytes: size,
                DownloadedBytes: current,
                StatusText: status,
                Resumable: resumable));
        }

        return links;
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long? GetInt64(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt64()
            : null;

    // "99_00" -> DownloadLinkStorable, captures (packageId, linkIndex).
    // Package entries ("99") and "extraInfo" simply don't match this and are skipped.
    [GeneratedRegex(@"^(\d+)_(\d+)$")]
    private static partial Regex LinkEntryRegex();
}
