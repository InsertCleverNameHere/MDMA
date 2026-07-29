using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mdma.Core;

/// <summary>
/// Locates JD2 by probing its default cfg\ directory, per docs/jd2.md §3.
/// Unlike NDM, JD2 has no registry footprint — auto-detect is a filesystem
/// probe, not a registry read.
/// </summary>
public sealed partial class Jd2Locator : IDownloadManagerLocator
{
    private static readonly Regex DownloadListPattern = DownloadListRegex();

    private readonly string _localAppDataDirectory;

    public TargetApp App => TargetApp.JD2;

    public Jd2Locator(string? localAppDataDirectory = null)
    {
        _localAppDataDirectory =
            localAppDataDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public Result<TargetAppLocation> TryAutoDetect()
    {
        var cfgDir = Path.Combine(_localAppDataDirectory, "JDownloader 2", "cfg");

        if (!Directory.Exists(cfgDir))
        {
            return new MdmaError(
                MdmaErrorCode.TargetAppNotFound,
                "Could not find JDownloader 2's configuration folder.",
                Details: cfgDir,
                SuggestedAction: "If JD2 is installed at a custom location, point MDMA at its cfg\\ folder manually."
            );
        }

        var validation = ValidateCfgDirectory(cfgDir);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        return Result<TargetAppLocation>.Ok(
            new TargetAppLocation(
                App: TargetApp.JD2,
                InstallOrConfigDir: cfgDir,
                MetadataDir: null, // JD2 has no separate metadata location — cfg\ is everything
                DownloadDirectory: ReadDefaultDownloadFolder(cfgDir),
                WasAutoDetected: true
            )
        );
    }

    public Result<TargetAppLocation> ValidateManualPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "The specified path does not exist.",
                Details: path
            );
        }

        var validation = ValidateCfgDirectory(path);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        return Result<TargetAppLocation>.Ok(
            new TargetAppLocation(
                App: TargetApp.JD2,
                InstallOrConfigDir: path,
                MetadataDir: null,
                DownloadDirectory: ReadDefaultDownloadFolder(path),
                WasAutoDetected: false
            )
        );
    }

    /// <summary>Confirms at least one downloadList*.zip exists, is a valid zip,
    /// and contains at least one entry matching JD2's naming convention
    /// (a package id, a package_link id, or the "extraInfo" entry).</summary>
    private static Result ValidateCfgDirectory(string cfgDir)
    {
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(cfgDir, "downloadList*.zip");
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "Could not enumerate files in the specified folder.",
                Details: cfgDir,
                Inner: ex
            );
        }

        if (candidates.Length == 0)
        {
            return new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "No downloadList*.zip file was found in the specified folder.",
                Details: cfgDir,
                SuggestedAction: "Point MDMA at JD2's cfg\\ folder (typically %LOCALAPPDATA%\\JDownloader 2\\cfg)."
            );
        }

        // Only the newest needs to be structurally valid for locate/validate
        // purposes — Jd2ListReader is responsible for picking the newest one
        // for actual scanning; this just proves the folder is genuinely JD2's.
        var newest = PickNewest(candidates);

        try
        {
            using var zip = ZipFile.OpenRead(newest);
            var hasExpectedEntry = zip.Entries.Any(e =>
                e.Name.Equals("extraInfo", StringComparison.OrdinalIgnoreCase)
                || DownloadListPattern.IsMatch(e.Name)
            );

            if (!hasExpectedEntry)
            {
                return new MdmaError(
                    MdmaErrorCode.ManualPathInvalid,
                    "Found a downloadList*.zip file, but it doesn't contain the expected JD2 entry structure.",
                    Details: newest
                );
            }
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "downloadList*.zip could not be opened as a valid zip archive.",
                Details: newest,
                Inner: ex
            );
        }

        return Result.Ok();
    }

    /// <summary>Reads the app-level default download folder from
    /// org.jdownloader.settings.GeneralSettings.json ("defaultdownloadfolder" key),
    /// which lives alongside downloadList*.zip in cfg\. Returns null if the
    /// settings file is missing or doesn't contain the key — this is a soft
    /// read, not a validation requirement, since a missing/renamed settings
    /// file shouldn't make an otherwise-valid JD2 install undetectable.</summary>
    private static string? ReadDefaultDownloadFolder(string cfgDir)
    {
        var settingsPath = Path.Combine(cfgDir, "org.jdownloader.settings.GeneralSettings.json");
        if (!File.Exists(settingsPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (
                doc.RootElement.TryGetProperty("defaultdownloadfolder", out var value)
                && value.ValueKind == JsonValueKind.String
            )
            {
                return value.GetString();
            }
        }
        catch
        {
            // Malformed settings file — treated the same as "missing", not a
            // hard failure, since it's only ever used as a soft default.
        }

        return null;
    }

    /// <summary>Picks the highest-numbered downloadList<N>.zip, per docs/jd2.md
    /// §3.1's "descending numerical order" boot algorithm.</summary>
    public static string PickNewest(IEnumerable<string> candidatePaths)
    {
        return candidatePaths
            .Select(p => (Path: p, Counter: ExtractCounter(p)))
            .OrderByDescending(x => x.Counter)
            .First()
            .Path;
    }

    private static long ExtractCounter(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path); // "downloadList11283"
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var n) ? n : -1;
    }

    // Matches a package entry ("99"), a package_link entry ("99_00"), for
    // the structural sanity check in ValidateCfgDirectory.
    [GeneratedRegex(@"^\d+(_\d+)?$")]
    private static partial Regex DownloadListRegex();
}
