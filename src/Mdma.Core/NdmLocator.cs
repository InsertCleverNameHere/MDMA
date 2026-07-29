using Microsoft.Data.Sqlite;

namespace Mdma.Core;

/// <summary>
/// Locates NDM via its registry configuration (HKCU\SOFTWARE\NeatDM), per
/// docs/ndm.md §2. Falls back to manual path validation when auto-detect fails.
/// </summary>
public sealed class NdmLocator : IDownloadManagerLocator
{
    private const string RegistryKeyPath = @"SOFTWARE\NeatDM"; // HKCU-relative; IRegistryAccessor is assumed HKCU-scoped
    private const string TempDirectoryValue = "TempDirectory";
    private const string DownloadDirectoryValue = "DownloadDirectory";

    private readonly IRegistryAccessor _registry;
    private readonly string _appDataDirectory;

    public TargetApp App => TargetApp.NDM;

    public NdmLocator(IRegistryAccessor registry, string? appDataDirectory = null)
    {
        _registry = registry;
        _appDataDirectory =
            appDataDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    public Result<TargetAppLocation> TryAutoDetect()
    {
        var tempDir = _registry.ReadString(RegistryKeyPath, TempDirectoryValue);
        var downloadDir = _registry.ReadString(RegistryKeyPath, DownloadDirectoryValue);

        if (string.IsNullOrWhiteSpace(tempDir) || string.IsNullOrWhiteSpace(downloadDir))
        {
            return new MdmaError(
                MdmaErrorCode.TargetAppNotFound,
                "Could not find Neat Download Manager via the registry.",
                Details: $@"Expected values at HKCU\{RegistryKeyPath}\{TempDirectoryValue} and \{DownloadDirectoryValue}.",
                SuggestedAction: "If NDM is installed, point MDMA at its temp folder manually. Otherwise install NDM first."
            );
        }

        if (!Directory.Exists(tempDir))
        {
            return new MdmaError(
                MdmaErrorCode.TargetAppNotFound,
                "Registry pointed at an NDM temp directory that no longer exists.",
                Details: tempDir,
                SuggestedAction: "Point MDMA at the correct NDM temp folder manually."
            );
        }

        var metadataDir = Path.Combine(_appDataDirectory, "NeatDM");

        return Result<TargetAppLocation>.Ok(
            new TargetAppLocation(
                App: TargetApp.NDM,
                InstallOrConfigDir: tempDir,
                MetadataDir: metadataDir,
                DownloadDirectory: downloadDir,
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

        // The user may point at the temp directory itself, or at the folder
        // containing neatdb.db (e.g. %APPDATA%\NeatDM). Check both: NDM's temp
        // directory and neatdb.db location are not necessarily the same folder
        // (see docs/ndm.md §5: neatdb.db lives under %APPDATA%\NeatDM\, separate
        // from TempDirectory). So a "manual path" validation for NDM specifically
        // needs the neatdb.db location, not the temp directory, since that's
        // where scanning/injection actually reads and writes task rows.
        var dbPath = Path.Combine(path, "neatdb.db");
        if (!File.Exists(dbPath))
        {
            return new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "No neatdb.db was found at the specified path.",
                Details: dbPath,
                SuggestedAction: "Point MDMA at the folder containing neatdb.db (typically %APPDATA%\\NeatDM)."
            );
        }

        var schemaCheck = ValidateDbSchema(dbPath);
        if (!schemaCheck.IsSuccess)
        {
            return schemaCheck.Error!;
        }

        // Manual validation only confirms neatdb.db's folder. It cannot know
        // the real TempDirectory without the registry — InstallOrConfigDir is
        // left null rather than guessed, so callers (Cli/Gui) know to prompt
        // for it separately if the operation actually needs it (e.g. export,
        // which reads seg.x* files from the temp directory, not from here).
        return Result<TargetAppLocation>.Ok(
            new TargetAppLocation(
                App: TargetApp.NDM,
                InstallOrConfigDir: null,
                MetadataDir: path,
                DownloadDirectory: path,
                WasAutoDetected: false
            )
        );
    }

    private static Result ValidateDbSchema(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection(
                $"Data Source={dbPath};Mode=ReadOnly;Pooling=False"
            );
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name='downloads';";
            var result = cmd.ExecuteScalar();

            SqliteConnection.ClearPool(conn);

            if (result is null)
            {
                return new MdmaError(
                    MdmaErrorCode.ManualPathInvalid,
                    "neatdb.db was found but does not contain the expected 'downloads' table.",
                    Details: dbPath
                );
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "neatdb.db could not be opened as a valid SQLite database.",
                Details: dbPath,
                Inner: ex
            );
        }
    }
}
