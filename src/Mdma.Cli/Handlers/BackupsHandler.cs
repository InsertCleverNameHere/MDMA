using Mdma.Core;

namespace Mdma.Cli.Handlers;

public static class BackupsHandler
{
    public static int Execute(
        CliArgs args,
        IWorkingDirectoryProvider? providerOverride = null,
        IBackupManager? backupManagerOverride = null
    )
    {
        var provider = providerOverride ?? new WorkingDirectoryProvider();
        var workDirResult = provider.Resolve(args.WorkDir);
        if (!workDirResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(workDirResult.Error!, args.Json);
            return ExitCodes.Map(workDirResult.Error!.Code);
        }
        var workingRoot = workDirResult.Value!;

        var backupManager = backupManagerOverride ?? new BackupManager(new RealClock());
        var targetAppFilter = ParseTargetApp(args.App);

        var listResult = backupManager.ListBackups(workingRoot, targetAppFilter);
        if (!listResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(listResult.Error!, args.Json);
            return ExitCodes.Map(listResult.Error!.Code);
        }

        ConsoleFormatter.PrintBackupsTable(listResult.Value!, args.Json);
        return ExitCodes.Success;
    }

    private static TargetApp? ParseTargetApp(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        return input.Trim().ToLowerInvariant() switch
        {
            "ndm" => TargetApp.NDM,
            "jd2" => TargetApp.JD2,
            _ => null,
        };
    }
}
