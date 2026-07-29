namespace Mdma.Core;

/// <summary>
/// Thin wrapper around disk free-space lookup. Exists purely so ISpaceChecker
/// can be unit tested against fake values instead of the real disk the test
/// happens to run on.
/// </summary>
public interface IDiskSpaceSource
{
    /// <summary>Returns available free bytes on the volume containing `path`.</summary>
    long GetAvailableFreeBytes(string path);
}

/// <summary>
/// Thin wrapper around Windows Registry reads/writes MDMA needs (currently only
/// NDM's HKCU\SOFTWARE\NeatDM key). Exists so registry-dependent code (NdmLocator,
/// NdmInjector's LastDownloadID update, BackupManager's registry snapshot) can be
/// unit tested without touching the real registry.
/// </summary>
public interface IRegistryAccessor
{
    /// <summary>Returns null if the key or value doesn't exist, rather than throwing.</summary>
    string? ReadString(string keyPath, string valueName);

    /// <summary>Returns null if the key or value doesn't exist, rather than throwing.</summary>
    int? ReadDword(string keyPath, string valueName);

    void WriteString(string keyPath, string valueName, string value);

    void WriteDword(string keyPath, string valueName, int value);
}

/// <summary>
/// Thin wrapper around process enumeration, so IProcessGuard can be unit tested
/// without depending on what's actually running on the dev/CI machine.
/// </summary>
public interface IProcessLister
{
    /// <summary>Returns true if a process with this executable name (case-insensitive,
    /// with or without .exe) is currently running.</summary>
    bool IsRunning(string processName);
}

/// <summary>
/// Thin wrapper around current time, so anything generating timestamps (backup
/// IDs, log entries) is deterministic in tests.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
