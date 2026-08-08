namespace Mdma.Core.Tests.Fixtures;

/// <summary>Configurable fake for IDiskSpaceSource — set FreeBytesByPath per test.</summary>
public sealed class FakeDiskSpaceSource : IDiskSpaceSource
{
    public long FreeBytes { get; set; } = long.MaxValue;
    public Dictionary<string, long> FreeBytesByPath { get; } = new();

    public long GetAvailableFreeBytes(string path) =>
        FreeBytesByPath.TryGetValue(path, out var v) ? v : FreeBytes;
}

/// <summary>In-memory fake for IRegistryAccessor — no real registry touched in tests.</summary>
public sealed class FakeRegistryAccessor : IRegistryAccessor
{
    private readonly Dictionary<(string key, string value), object> _store = new();

    public FakeRegistryAccessor Seed(string keyPath, string valueName, object value)
    {
        _store[(keyPath, valueName)] = value;
        return this;
    }

    public string? ReadString(string keyPath, string valueName) =>
        _store.TryGetValue((keyPath, valueName), out var v) ? v as string : null;

    public int? ReadDword(string keyPath, string valueName) =>
        _store.TryGetValue((keyPath, valueName), out var v) ? v as int? : null;

    public void WriteString(string keyPath, string valueName, string value) =>
        _store[(keyPath, valueName)] = value;

    public void WriteDword(string keyPath, string valueName, int value) =>
        _store[(keyPath, valueName)] = value;
}

/// <summary>Configurable fake for IProcessLister — set RunningProcesses per test.</summary>
public sealed class FakeProcessLister : IProcessLister
{
    public HashSet<string> RunningProcesses { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning(string processName)
    {
        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        return RunningProcesses.Any(p =>
            (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p).Equals(
                normalized,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }
}

/// <summary>Fake IMdmaExporter for testing ConversionService orchestration --
/// records whether/how many times Export was called, without touching real
/// files. Configure ResultToReturn to simulate success or failure.</summary>
public sealed class FakeMdmaExporter : IMdmaExporter
{
    public TargetApp SourceApp { get; set; }
    public int CallCount { get; private set; }
    public Result<string> ResultToReturn { get; set; } = Result<string>.Ok("fake-output.mdma");

    public Result<string> Export(
        DownloadTaskSummary task,
        TargetAppLocation sourceLocation,
        WorkingRoot workingRoot,
        string destinationMdmaPath,
        IProgress<OperationProgress>? progress = null
    )
    {
        CallCount++;
        return ResultToReturn;
    }
}

/// <summary>Fake IDownloadListInjector for testing ConversionService
/// orchestration -- records call count, configurable result.</summary>
public sealed class FakeDownloadListInjector : IDownloadListInjector
{
    public TargetApp TargetApp { get; set; }
    public int CallCount { get; private set; }
    public Result ResultToReturn { get; set; } = Result.Ok();

    public Result Inject(
        MdmaPackage package,
        TargetAppLocation destinationLocation,
        IProgress<OperationProgress>? progress = null
    )
    {
        CallCount++;
        return ResultToReturn;
    }
}

/// <summary>Fake IMdmaLoader for testing ConversionService orchestration --
/// records call count, configurable result. DefaultPackage() builds a
/// minimal-but-valid MdmaPackage for tests that need Load to succeed.</summary>
public sealed class FakeMdmaLoader : IMdmaLoader
{
    public int CallCount { get; private set; }
    public Result<MdmaPackage> ResultToReturn { get; set; } =
        Result<MdmaPackage>.Ok(DefaultPackage());

    public Result<MdmaPackage> Load(string mdmaFilePath, WorkingRoot workingRoot)
    {
        CallCount++;
        return ResultToReturn;
    }

    public static MdmaPackage DefaultPackage()
    {
        var manifest = new MdmaManifest(
            MdmaVersion: 1,
            Origin: TargetApp.NDM,
            Url: "https://example.com/f.bin",
            Filename: "f.bin",
            TotalBytes: 100,
            MimeType: null,
            Headers: Array.Empty<KeyValuePair<string, string>>(),
            CreatedEpochMillis: 0,
            Chunks: new[] { new ChunkRange(0, 0, 99, 100) }
        );
        return new MdmaPackage(
            manifest,
            new Dictionary<int, string> { [0] = "/fake/chunk_0.bin" },
            "/fake/source.mdma"
        );
    }
}

/// <summary>Fake IBackupManager for testing ConversionService orchestration --
/// records call count, configurable result.</summary>
public sealed class FakeBackupManager : IBackupManager
{
    public int CreateBackupCallCount { get; private set; }
    public Result<BackupHandle> CreateBackupResultToReturn { get; set; } =
        Result<BackupHandle>.Ok(
            new BackupHandle("fake-backup", TargetApp.NDM, DateTimeOffset.UtcNow, "/fake/backup")
        );

    public Result<IReadOnlyList<BackupHandle>> ListBackupsResultToReturn { get; set; } =
        Result<IReadOnlyList<BackupHandle>>.Ok(Array.Empty<BackupHandle>());

    public Result<BackupHandle> CreateBackup(
        TargetAppLocation location,
        WorkingRoot workingRoot,
        string? taskNativeId = null
    )
    {
        CreateBackupCallCount++;
        return CreateBackupResultToReturn;
    }

    public Result<IReadOnlyList<BackupHandle>> ListBackups(
        WorkingRoot workingRoot,
        TargetApp? filterBy = null
    ) => ListBackupsResultToReturn;
}

/// <summary>Fixed-time fake for IClock — deterministic timestamps in tests.</summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } =
        new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
}

/// <summary>In-memory fake for IMdmaLogger — records all log entries for assertions in tests.</summary>
public sealed class FakeMdmaLogger : IMdmaLogger
{
    public List<LogEntry> Entries { get; } = new();
    private readonly object _lock = new();

    public void Log(
        MdmaLogLevel level,
        string component,
        string message,
        string? details = null,
        Exception? exception = null
    )
    {
        lock (_lock)
        {
            Entries.Add(
                new LogEntry(
                    Timestamp: DateTimeOffset.UtcNow,
                    Level: level,
                    Component: component,
                    Message: message,
                    Details: details,
                    Exception: exception?.ToString()
                )
            );
        }
    }
}

/// <summary>In-memory fake for IConversionService — configurable for GUI ViewModel tests.</summary>
public sealed class FakeConversionService : IConversionService
{
    public int ExportToFileCallCount { get; private set; }
    public int ImportFromFileCallCount { get; private set; }
    public int ConvertSameMachineCallCount { get; private set; }

    public Result<string> ExportToFileResultToReturn { get; set; } =
        Result<string>.Ok("fake-export.mdma");
    public Result ImportFromFileResultToReturn { get; set; } = Result.Ok();
    public Result ConvertSameMachineResultToReturn { get; set; } = Result.Ok();

    public Result<string> ExportToFile(
        DownloadTaskSummary task,
        TargetAppLocation sourceLocation,
        string userChosenDestinationPath,
        IProgress<OperationProgress>? progress = null
    )
    {
        ExportToFileCallCount++;
        progress?.Report(new OperationProgress("Exporting", 100, "Done"));
        return ExportToFileResultToReturn;
    }

    public Result ImportFromFile(
        string mdmaFilePath,
        TargetAppLocation destinationLocation,
        IProgress<OperationProgress>? progress = null
    )
    {
        ImportFromFileCallCount++;
        progress?.Report(new OperationProgress("Importing", 100, "Done"));
        return ImportFromFileResultToReturn;
    }

    public Result ConvertSameMachine(
        DownloadTaskSummary task,
        TargetAppLocation sourceLocation,
        TargetAppLocation destinationLocation,
        IProgress<OperationProgress>? progress = null
    )
    {
        ConvertSameMachineCallCount++;
        progress?.Report(new OperationProgress("Converting", 100, "Done"));
        return ConvertSameMachineResultToReturn;
    }
}

/// <summary>In-memory fake for IDownloadListReader — configurable for GUI ViewModel tests.</summary>
public sealed class FakeDownloadListReader : IDownloadListReader
{
    public TargetApp App { get; set; } = TargetApp.NDM;
    public int ScanTasksCallCount { get; private set; }
    public Result<IReadOnlyList<DownloadTaskSummary>> ScanTasksResultToReturn { get; set; } =
        Result<IReadOnlyList<DownloadTaskSummary>>.Ok(Array.Empty<DownloadTaskSummary>());

    public Result<IReadOnlyList<DownloadTaskSummary>> ScanTasks(TargetAppLocation location)
    {
        ScanTasksCallCount++;
        return ScanTasksResultToReturn;
    }
}
