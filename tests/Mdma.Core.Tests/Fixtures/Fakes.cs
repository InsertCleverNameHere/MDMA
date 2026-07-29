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
            ? processName[..^4] : processName;
        return RunningProcesses.Any(p =>
            (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p)
            .Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Fixed-time fake for IClock — deterministic timestamps in tests.</summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
}
