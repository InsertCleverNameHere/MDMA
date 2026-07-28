namespace Mdma.Core;

/// <summary>
/// Reads a task's physical chunk data from a source app's native storage and
/// packages it into a .mdma file. One implementation per target (reads NDM's
/// seg.x* files, or JD2's sparse .part file offsets).
/// </summary>
public interface IMdmaExporter
{
    TargetApp SourceApp { get; }

    /// <summary>Exports the given task to a .mdma file at destinationPath (inside
    /// the working root; caller decides temp vs. user-chosen final location).
    /// Requires ISpaceChecker to have already passed for the source side.</summary>
    Result<string> Export(
        DownloadTaskSummary task,
        TargetAppLocation sourceLocation,
        string destinationMdmaPath,
        IProgress<OperationProgress>? progress = null);
}

/// <summary>Opens and verifies a .mdma file: checksum, manifest version, and
/// stages chunk files into the working root ready for an injector to consume.</summary>
public interface IMdmaLoader
{
    /// <summary>On checksum mismatch or unsupported version, returns a typed
    /// MdmaChecksumMismatch / MdmaVersionUnsupported error — caller (CLI/GUI) is
    /// expected to inform the user and point at the revert option per
    /// architecture.md conventions; this method itself does not revert anything.</summary>
    Result<MdmaPackage> Load(string mdmaFilePath, WorkingRoot workingRoot);
}

/// <summary>
/// Writes a loaded MdmaPackage into a target app's native storage (registry,
/// SQLite, segment files / sparse file + JSON-zip, as appropriate). One
/// implementation per target. Must use IAtomicWriter for every file mutation
/// and must only run after IBackupManager.CreateBackup has succeeded and
/// IProcessGuard has confirmed it's safe.
/// </summary>
public interface IDownloadListInjector
{
    TargetApp TargetApp { get; }

    Result Inject(
        MdmaPackage package,
        TargetAppLocation destinationLocation,
        IProgress<OperationProgress>? progress = null);
}

/// <summary>
/// Top-level orchestrator both Cli and Gui call into. Encodes the fixed operation
/// order from architecture.md §6–8: process guard -> space checks (both ends) ->
/// backup -> export -> import -> best-effort cleanup. This is the ONLY entry point
/// for a conversion; there is no separate "direct convert" path, per design decision.
/// </summary>
public interface IConversionService
{
    /// <summary>Full source-app -> .mdma file export, for cross-machine migration
    /// or standalone backup. Does not touch any destination app.</summary>
    Result<string> ExportToFile(
        DownloadTaskSummary task,
        TargetAppLocation sourceLocation,
        string userChosenDestinationPath,
        IProgress<OperationProgress>? progress = null);

    /// <summary>Imports an existing .mdma file (e.g. one carried over from another
    /// machine) into a target app.</summary>
    Result ImportFromFile(
        string mdmaFilePath,
        TargetAppLocation destinationLocation,
        IProgress<OperationProgress>? progress = null);

    /// <summary>Same-machine conversion: always round-trips through a temporary
    /// .mdma under <workingRoot>\.mdma-tmp\, deleted best-effort on success.
    /// Internally this is just ExportToFile + ImportFromFile against a temp path —
    /// no separate logic path, per design decision in architecture.md §6.</summary>
    Result ConvertSameMachine(
        DownloadTaskSummary task,
        TargetAppLocation sourceLocation,
        TargetAppLocation destinationLocation,
        IProgress<OperationProgress>? progress = null);
}
