using Mdma.Core;

namespace Mdma.Cli.Handlers;

public static class ScanHandler
{
    public static int Execute(CliArgs args, IRegistryAccessor? registryOverride = null)
    {
        var registry = registryOverride ?? new RegistryAccessor();
        var resolver = new LocationResolver(registry);
        var ndmReader = new NdmListReader();
        var jd2Reader = new Jd2ListReader();

        var targetAppFilter = ParseTargetApp(args.App);
        var allTasks = new List<DownloadTaskSummary>();

        if (targetAppFilter is null or TargetApp.NDM)
        {
            var ndmResult = ScanApp(TargetApp.NDM, resolver, ndmReader, args, allTasks);
            if (!ndmResult.IsSuccess && targetAppFilter == TargetApp.NDM)
            {
                ConsoleFormatter.PrintError(ndmResult.Error!, args.Json, args.Verbose);
                return ExitCodes.Map(ndmResult.Error!.Code);
            }
        }

        if (targetAppFilter is null or TargetApp.JD2)
        {
            var jd2Result = ScanApp(TargetApp.JD2, resolver, jd2Reader, args, allTasks);
            if (!jd2Result.IsSuccess && targetAppFilter == TargetApp.JD2)
            {
                ConsoleFormatter.PrintError(jd2Result.Error!, args.Json, args.Verbose);
                return ExitCodes.Map(jd2Result.Error!.Code);
            }
        }

        ConsoleFormatter.PrintTasksTable(allTasks, args.Json);
        return ExitCodes.Success;
    }

    private static Result<IReadOnlyList<DownloadTaskSummary>> ScanApp(
        TargetApp app,
        ILocationResolver resolver,
        IDownloadListReader reader,
        CliArgs args,
        List<DownloadTaskSummary> allTasks
    )
    {
        var locationResult = resolver.ResolveLocation(
            app,
            manualPathOverride: args.ManualPath,
            metadataDirOverride: args.MetadataDir,
            tempDirOverride: args.TempDir,
            downloadDirOverride: args.DownloadDir
        );

        if (!locationResult.IsSuccess)
            return locationResult.Error!;

        var scanResult = reader.ScanTasks(locationResult.Value!);
        if (!scanResult.IsSuccess)
            return scanResult.Error!;

        allTasks.AddRange(scanResult.Value!);
        return scanResult;
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
