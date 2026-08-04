namespace Mdma.Core;

/// <summary>
/// Top-level orchestrator both Cli and Gui call into, per Conversion.cs's
/// IConversionService contract. Constructed once per run with an already-
/// resolved WorkingRoot (resolution itself happens once at startup via
/// IWorkingDirectoryProvider, per architecture.md §7 -- this class does not
/// re-resolve it per call).
///
/// Implementation status: ExportToFile is complete (Phase 5.3). ImportFromFile
/// and ConvertSameMachine are explicit NotImplementedException stubs until
/// Phase 5.4/5.5 -- they throw rather than return a Result failure, so a test
/// written before they're implemented can't mistake "not built yet" for a
/// legitimate business failure.
/// </summary>
public sealed class ConversionService : IConversionService
{
    private readonly WorkingRoot _workingRoot;
    private readonly IProcessGuard _processGuard;
    private readonly ISpaceChecker _spaceChecker;
    private readonly IBackupManager _backupManager;
    private readonly IReadOnlyDictionary<TargetApp, IMdmaExporter> _exporters;
    private readonly IReadOnlyDictionary<TargetApp, IDownloadListInjector> _injectors;
    private readonly IMdmaLoader _mdmaLoader;

    public ConversionService(
        WorkingRoot workingRoot,
        IProcessGuard processGuard,
        ISpaceChecker spaceChecker,
        IBackupManager backupManager,
        IReadOnlyDictionary<TargetApp, IMdmaExporter> exporters,
        IReadOnlyDictionary<TargetApp, IDownloadListInjector> injectors,
        IMdmaLoader mdmaLoader
    )
    {
        _workingRoot = workingRoot;
        _processGuard = processGuard;
        _spaceChecker = spaceChecker;
        _backupManager = backupManager;
        _exporters = exporters;
        _injectors = injectors;
        _mdmaLoader = mdmaLoader;
    }

    public Result<string> ExportToFile(
        DownloadTaskSummary task,
        TargetAppLocation sourceLocation,
        string userChosenDestinationPath,
        IProgress<OperationProgress>? progress = null
    )
    {
        var guardResult = _processGuard.IsSafeToProceed(task.Source);
        if (!guardResult.IsSuccess)
            return guardResult.Error!;
        if (!guardResult.Value)
        {
            return new MdmaError(
                MdmaErrorCode.TargetAppProcessRunning,
                $"{task.Source} must be closed before exporting this task.",
                Details: task.Source.ToString(),
                SuggestedAction: "Close the application and try again."
            );
        }

        // Space check against the working root (staging), sized to the task's
        // actual downloaded bytes -- not total_size, per architecture.md §7.
        var spaceResult = _spaceChecker.HasSufficientSpace(
            _workingRoot.Path,
            task.DownloadedBytes,
            isDestination: false
        );
        if (!spaceResult.IsSuccess)
            return spaceResult.Error!;

        if (!_exporters.TryGetValue(task.Source, out var exporter))
        {
            return new MdmaError(
                MdmaErrorCode.ExportFailed,
                $"No exporter is registered for {task.Source}.",
                Details: task.Source.ToString()
            );
        }

        return exporter.Export(
            task,
            sourceLocation,
            _workingRoot,
            userChosenDestinationPath,
            progress
        );
    }

    public Result ImportFromFile(
        string mdmaFilePath,
        TargetAppLocation destinationLocation,
        IProgress<OperationProgress>? progress = null
    )
    {
        var guardResult = _processGuard.IsSafeToProceed(destinationLocation.App);
        if (!guardResult.IsSuccess)
            return guardResult.Error!;
        if (!guardResult.Value)
        {
            return new MdmaError(
                MdmaErrorCode.TargetAppProcessRunning,
                $"{destinationLocation.App} must be closed before importing.",
                Details: destinationLocation.App.ToString(),
                SuggestedAction: "Close the application and try again."
            );
        }

        // Where the BULK of the imported data actually lands differs per app:
        // NDM writes chunk files into InstallOrConfigDir (the temp dir); JD2
        // writes its sparse file into DownloadDirectory (the download folder),
        // with InstallOrConfigDir only receiving the small zip. Pick accordingly.
        var spaceCheckPath =
            destinationLocation.App == TargetApp.NDM
                ? destinationLocation.InstallOrConfigDir
                : destinationLocation.DownloadDirectory;

        if (spaceCheckPath is null)
        {
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                $"No known destination path to write into for {destinationLocation.App}.",
                Details: destinationLocation.App.ToString()
            );
        }

        var manifestPeek = PeekManifestTotalBytes(mdmaFilePath);
        if (!manifestPeek.IsSuccess)
            return manifestPeek.Error!;

        var spaceResult = _spaceChecker.HasSufficientSpace(
            spaceCheckPath,
            manifestPeek.Value,
            isDestination: true
        );
        if (!spaceResult.IsSuccess)
            return spaceResult.Error!;

        // Critical step: must succeed before any destructive write is attempted.
        var backupResult = _backupManager.CreateBackup(destinationLocation, _workingRoot);
        if (!backupResult.IsSuccess)
            return backupResult.Error!;

        var loadResult = _mdmaLoader.Load(mdmaFilePath, _workingRoot);
        if (!loadResult.IsSuccess)
            return loadResult.Error!;

        if (!_injectors.TryGetValue(destinationLocation.App, out var injector))
        {
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                $"No injector is registered for {destinationLocation.App}.",
                Details: destinationLocation.App.ToString()
            );
        }

        return injector.Inject(loadResult.Value!, destinationLocation, progress);
    }

    /// <summary>Lightweight peek at manifest.json's total_size, WITHOUT the
    /// full verification MdmaLoader.Load performs -- used only to size the
    /// pre-flight space check before committing to the heavier verified load.
    /// If the .mdma is actually corrupt, MdmaLoader.Load will catch that
    /// properly later in the sequence; this peek is deliberately cheap.</summary>
    private static Result<long> PeekManifestTotalBytes(string mdmaFilePath)
    {
        if (!File.Exists(mdmaFilePath))
        {
            return new MdmaError(
                MdmaErrorCode.MdmaFileNotFound,
                "The specified .mdma file does not exist.",
                Details: mdmaFilePath
            );
        }

        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(mdmaFilePath);
            var entry = zip.GetEntry("manifest.json");
            if (entry is null)
            {
                return new MdmaError(
                    MdmaErrorCode.MdmaManifestMalformed,
                    "manifest.json is missing from the .mdma package.",
                    Details: mdmaFilePath
                );
            }

            using var stream = entry.Open();
            var manifest = System.Text.Json.JsonSerializer.Deserialize<MdmaManifestDto>(stream);
            if (manifest is null)
            {
                return new MdmaError(
                    MdmaErrorCode.MdmaManifestMalformed,
                    "manifest.json deserialized to nothing.",
                    Details: mdmaFilePath
                );
            }

            return Result<long>.Ok(manifest.Task.TotalSize);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.MdmaManifestMalformed,
                "manifest.json could not be read.",
                Details: mdmaFilePath,
                Inner: ex
            );
        }
    }

    public Result ConvertSameMachine(
        DownloadTaskSummary task,
        TargetAppLocation sourceLocation,
        TargetAppLocation destinationLocation,
        IProgress<OperationProgress>? progress = null
    )
    {
        // Source-side process guard. (Destination-side guard + space check +
        // backup all happen naturally inside ImportFromFile below -- no need
        // to duplicate them here, only what's specific to the source side.)
        var sourceGuardResult = _processGuard.IsSafeToProceed(task.Source);
        if (!sourceGuardResult.IsSuccess)
            return sourceGuardResult.Error!;
        if (!sourceGuardResult.Value)
        {
            return new MdmaError(
                MdmaErrorCode.TargetAppProcessRunning,
                $"{task.Source} must be closed before converting this task.",
                Details: task.Source.ToString(),
                SuggestedAction: "Close the application and try again."
            );
        }

        // Destination guard checked here too (cheap, not a duplication of
        // meaningful logic) so a blocked destination fails fast, before
        // wasting time/disk writing a temp export that ImportFromFile would
        // just reject anyway via its own (redundant but harmless) guard check.
        var destGuardResult = _processGuard.IsSafeToProceed(destinationLocation.App);
        if (!destGuardResult.IsSuccess)
            return destGuardResult.Error!;
        if (!destGuardResult.Value)
        {
            return new MdmaError(
                MdmaErrorCode.TargetAppProcessRunning,
                $"{destinationLocation.App} must be closed before converting this task.",
                Details: destinationLocation.App.ToString(),
                SuggestedAction: "Close the application and try again."
            );
        }

        var tempMdmaDir = Path.Combine(_workingRoot.Path, ".mdma-tmp");
        try
        {
            Directory.CreateDirectory(tempMdmaDir);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.Unknown,
                "Could not create the temp .mdma staging directory.",
                Details: tempMdmaDir,
                Inner: ex
            );
        }

        var tempMdmaPath = Path.Combine(tempMdmaDir, $"{Guid.NewGuid():N}.mdma");

        progress?.Report(new OperationProgress("Exporting to temporary package", null, null));

        // Literal call to the already-built method -- no parallel export logic,
        // per the standing design decision (architecture.md §6): same-machine
        // conversion always round-trips through a real .mdma, even internally.
        var exportResult = ExportToFile(task, sourceLocation, tempMdmaPath, progress);
        if (!exportResult.IsSuccess)
            return exportResult.Error!;

        progress?.Report(new OperationProgress("Importing from temporary package", null, null));

        // Literal call to the already-built method -- same reasoning as above.
        var importResult = ImportFromFile(tempMdmaPath, destinationLocation, progress);

        // Best-effort cleanup regardless of import outcome -- a leftover temp
        // .mdma is not itself a failure of the conversion, and orphan sweeps
        // (ITempCleanupService, Phase 5.2) are the backstop if this fails.
        TryDeleteBestEffort(tempMdmaPath);

        return importResult;
    }

    private static void TryDeleteBestEffort(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        { /* best-effort -- ITempCleanupService is the backstop */
        }
    }
}
