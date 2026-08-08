using System.Diagnostics;
using Microsoft.Win32;

namespace Mdma.Core;

public sealed class RegistryAccessor : IRegistryAccessor
{
    public string? ReadString(string keyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    public int? ReadDword(string keyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            var val = key?.GetValue(valueName);
            return val is int i ? i : null;
        }
        catch
        {
            return null;
        }
    }

    public void WriteString(string keyPath, string valueName, string value)
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key?.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void WriteDword(string keyPath, string valueName, int value)
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key?.SetValue(valueName, value, RegistryValueKind.DWord);
    }
}

public sealed class ProcessLister : IProcessLister
{
    public bool IsRunning(string processName)
    {
        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        return Process.GetProcessesByName(normalized).Length > 0;
    }
}

public sealed class DiskSpaceSource : IDiskSpaceSource
{
    public long GetAvailableFreeBytes(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath) ?? path;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }
}

public sealed class RealClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
