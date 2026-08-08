using Mdma.Core;

namespace Mdma.Cli.Handlers;

public static class ConvertHandler
{
    public static int Execute(
        CliArgs args,
        IRegistryAccessor? registryOverride = null,
        IWorkingDirectoryProvider? providerOverride = null
    )
    {
        if (string.IsNullOrWhiteSpace(args.SourceApp))
        {
            var err = new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "Missing required flag: --source (ndm or jd2).",
                SuggestedAction: "Specify --source ndm or --source jd2."
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.TargetAppNotFoundOrPathInvalid;
        }

        if (string.IsNullOrWhiteSpace(args.DestApp))
        {
            var err = new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "Missing required flag: --dest (ndm or jd2).",
                SuggestedAction: "Specify --dest ndm or --dest jd2."
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.TargetAppNotFoundOrPathInvalid;
        }

        if (string.IsNullOrWhiteSpace(args.Id))
        {
            var err = new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "Missing required flag: --id <task_id>.",
                SuggestedAction: "Specify --id with the task ID to convert."
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.TargetAppNotFoundOrPathInvalid;
        }

        var sourceApp = ParseTargetApp(args.SourceApp);
        var destApp = ParseTargetApp(args.DestApp);
        if (sourceApp is null || destApp is null)
        {
            var err = new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "Source and destination target apps must be 'ndm' or 'jd2'."
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

        var registry = registryOverride ?? new RegistryAccessor();

        var resolver = new LocationResolver(registry);
        IDownloadListReader sourceReader =
            sourceApp == TargetApp.NDM ? new NdmListReader() : new Jd2ListReader();

        // Resolve source location
        var sourceLocationResult = resolver.ResolveLocation(
            sourceApp.Value,
            manualPathOverride: args.ManualPath,
            metadataDirOverride: args.MetadataDir,
            tempDirOverride: args.TempDir
        );

        if (!sourceLocationResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(sourceLocationResult.Error!, args.Json, args.Verbose);
            return ExitCodes.Map(sourceLocationResult.Error!.Code);
        }

        var sourceLocation = sourceLocationResult.Value!;

        // Resolve destination location
        var destLocationResult = resolver.ResolveLocation(
            destApp.Value,
            downloadDirOverride: args.DownloadDir
        );

        if (!destLocationResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(destLocationResult.Error!, args.Json, args.Verbose);
            return ExitCodes.Map(destLocationResult.Error!.Code);
        }

        var destLocation = destLocationResult.Value!;

        // Scan source tasks
        var tasksResult = sourceReader.ScanTasks(sourceLocation);
        if (!tasksResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(tasksResult.Error!, args.Json);
            return ExitCodes.Map(tasksResult.Error!.Code);
        }

        var task = tasksResult.Value!.FirstOrDefault(t =>
            string.Equals(t.NativeId, args.Id, StringComparison.OrdinalIgnoreCase)
        );
        if (task is null)
        {
            var err = new MdmaError(
                MdmaErrorCode.ScanFailed,
                $"Task ID '{args.Id}' was not found in {sourceApp}."
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.OperationFailed;
        }

        var processGuard = new ProcessGuard(new ProcessLister());
        var spaceChecker = new SpaceChecker(new DiskSpaceSource());
        var backupManager = new BackupManager(new RealClock());
        var fileLogger = new FileLogger(workingRoot);

        var service = new ConversionService(
            workingRoot,
            processGuard,
            spaceChecker,
            backupManager,
            exporters: new Dictionary<TargetApp, IMdmaExporter>
            {
                [TargetApp.NDM] = new NdmExporter(),
                [TargetApp.JD2] = new Jd2Exporter(),
            },
            injectors: new Dictionary<TargetApp, IDownloadListInjector>
            {
                [TargetApp.NDM] = new NdmInjector(registry, new AtomicWriter()),
                [TargetApp.JD2] = new Jd2Injector(new AtomicWriter()),
            },
            mdmaLoader: new MdmaLoader(),
            logger: fileLogger
        );

        var reporter = new ConsoleProgressReporter(args.Json);
        var convertResult = service.ConvertSameMachine(
            task,
            sourceLocation,
            destLocation,
            reporter
        );
        reporter.Complete();

        if (!convertResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(convertResult.Error!, args.Json);
            return ExitCodes.Map(convertResult.Error!.Code);
        }

        ConsoleFormatter.PrintConvertResult(
            $"Task '{task.Filename}' converted successfully from {sourceApp} to {destApp}.",
            args.Json
        );
        return ExitCodes.Success;
    }

    private static TargetApp? ParseTargetApp(string input) =>
        input.Trim().ToLowerInvariant() switch
        {
            "ndm" => TargetApp.NDM,
            "jd2" => TargetApp.JD2,
            _ => null,
        };
}
