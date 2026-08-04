using Mdma.Core;

namespace Mdma.Cli.Handlers;

public static class CleanHandler
{
    public static int Execute(
        CliArgs args,
        IWorkingDirectoryProvider? providerOverride = null,
        ITempCleanupService? cleanupOverride = null
    )
    {
        var provider = providerOverride ?? new WorkingDirectoryProvider();
        var cleanupService = cleanupOverride ?? new TempCleanupService();

        var workDirResult = provider.Resolve(args.WorkDir);
        if (!workDirResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(workDirResult.Error!, args.Json);
            return ExitCodes.Map(workDirResult.Error!.Code);
        }

        var result = cleanupService.SweepOrphans(workDirResult.Value!);
        if (!result.IsSuccess)
        {
            ConsoleFormatter.PrintError(result.Error!, args.Json);
            return ExitCodes.Map(result.Error!.Code);
        }

        ConsoleFormatter.PrintCleanResult(result.Value!, args.Json);
        return ExitCodes.Success;
    }
}
