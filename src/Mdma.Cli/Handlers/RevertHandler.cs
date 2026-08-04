using Mdma.Core;

namespace Mdma.Cli.Handlers;

public static class RevertHandler
{
    public static int Execute(
        CliArgs args,
        IWorkingDirectoryProvider? providerOverride = null,
        IBackupManager? backupManagerOverride = null,
        IRevertManager? revertManagerOverride = null
    )
    {
        if (string.IsNullOrWhiteSpace(args.Id))
        {
            var err = new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "Missing required flag: --id <snapshot_id>.",
                SuggestedAction: "Specify --id with the backup snapshot ID to restore."
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.TargetAppNotFoundOrPathInvalid;
        }

        var provider = providerOverride ?? new WorkingDirectoryProvider();
        var workDirResult = provider.Resolve(args.WorkDir);
        if (!workDirResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(workDirResult.Error!, args.Json);
            return ExitCodes.Map(workDirResult.Error!.Code);
        }
        var workingRoot = workDirResult.Value!;

        var backupManager = backupManagerOverride ?? new BackupManager(new RealClock());
        var listResult = backupManager.ListBackups(workingRoot);
        if (!listResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(listResult.Error!, args.Json);
            return ExitCodes.Map(listResult.Error!.Code);
        }

        var backup = listResult.Value!.FirstOrDefault(b =>
            string.Equals(b.Id, args.Id, StringComparison.OrdinalIgnoreCase)
        );
        if (backup is null)
        {
            var err = new MdmaError(
                MdmaErrorCode.RevertTargetNotFound,
                $"Backup snapshot ID '{args.Id}' was not found in working root.",
                Details: workingRoot.Path
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.SafetyOrBackupError;
        }

        var fileLogger = new FileLogger(workingRoot);
        var revertManager =
            revertManagerOverride
            ?? new RevertManager(
                new ProcessGuard(new ProcessLister()),
                new AtomicWriter(),
                fileLogger
            );

        var revertResult = revertManager.Revert(backup);
        if (!revertResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(revertResult.Error!, args.Json);
            return ExitCodes.Map(revertResult.Error!.Code);
        }

        ConsoleFormatter.PrintRevertResult(
            $"Backup snapshot '{backup.Id}' restored successfully.",
            args.Json
        );
        return ExitCodes.Success;
    }
}
