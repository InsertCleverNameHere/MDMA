using Mdma.Core;

namespace Mdma.Cli.Handlers;

public static class ScanHandler
{
    public static int Execute(CliArgs args, IRegistryAccessor? registryOverride = null)
    {
        var registry = registryOverride ?? new RegistryAccessor();
        var ndmLocator = new NdmLocator(registry);
        var jd2Locator = new Jd2Locator();
        var ndmReader = new NdmListReader();
        var jd2Reader = new Jd2ListReader();

        var targetAppFilter = ParseTargetApp(args.App);
        var allTasks = new List<DownloadTaskSummary>();

        if (targetAppFilter is null or TargetApp.NDM)
        {
            var ndmResult = ScanApp(TargetApp.NDM, ndmLocator, ndmReader, args);
            if (!ndmResult.IsSuccess)
            {
                if (targetAppFilter == TargetApp.NDM)
                {
                    ConsoleFormatter.PrintError(ndmResult.Error!, args.Json, args.Verbose);
                    return ExitCodes.Map(ndmResult.Error!.Code);
                }
            }
            else
            {
                allTasks.AddRange(ndmResult.Value!);
            }
        }

        if (targetAppFilter is null or TargetApp.JD2)
        {
            var jd2Result = ScanApp(TargetApp.JD2, jd2Locator, jd2Reader, args);
            if (!jd2Result.IsSuccess)
            {
                if (targetAppFilter == TargetApp.JD2)
                {
                    ConsoleFormatter.PrintError(jd2Result.Error!, args.Json, args.Verbose);
                    return ExitCodes.Map(jd2Result.Error!.Code);
                }
            }
            else
            {
                allTasks.AddRange(jd2Result.Value!);
            }
        }

        ConsoleFormatter.PrintTasksTable(allTasks, args.Json);
        return ExitCodes.Success;
    }

    private static Result<IReadOnlyList<DownloadTaskSummary>> ScanApp(
        TargetApp app,
        IDownloadManagerLocator locator,
        IDownloadListReader reader,
        CliArgs args
    )
    {
        Result<TargetAppLocation> locationResult;

        if (!string.IsNullOrEmpty(args.ManualPath))
        {
            locationResult = locator.ValidateManualPath(args.ManualPath);
        }
        else
        {
            locationResult = locator.TryAutoDetect();
        }

        if (!locationResult.IsSuccess)
            return locationResult.Error!;

        var location = locationResult.Value!;

        if (app == TargetApp.NDM && !string.IsNullOrEmpty(args.MetadataDir))
        {
            location = new TargetAppLocation(
                App: app,
                InstallOrConfigDir: location.InstallOrConfigDir,
                MetadataDir: args.MetadataDir,
                DownloadDirectory: location.DownloadDirectory,
                WasAutoDetected: location.WasAutoDetected
            );
        }
        if (app == TargetApp.NDM && !string.IsNullOrEmpty(args.TempDir))
        {
            location = new TargetAppLocation(
                App: app,
                InstallOrConfigDir: args.TempDir,
                MetadataDir: location.MetadataDir,
                DownloadDirectory: location.DownloadDirectory,
                WasAutoDetected: location.WasAutoDetected
            );
        }

        return reader.ScanTasks(location);
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
