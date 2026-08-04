using Mdma.Core;

namespace Mdma.Cli.Handlers;

public static class ExportHandler
{
    public static int Execute(
        CliArgs args,
        IRegistryAccessor? registryOverride = null,
        IWorkingDirectoryProvider? providerOverride = null
    )
    {
        if (string.IsNullOrWhiteSpace(args.App))
        {
            var err = new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "Missing required flag: --app (ndm or jd2).",
                SuggestedAction: "Specify --app ndm or --app jd2."
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.TargetAppNotFoundOrPathInvalid;
        }

        if (string.IsNullOrWhiteSpace(args.Id))
        {
            var err = new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "Missing required flag: --id <task_id>.",
                SuggestedAction: "Specify --id with the task ID to export."
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.TargetAppNotFoundOrPathInvalid;
        }

        if (string.IsNullOrWhiteSpace(args.OutPath))
        {
            var err = new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "Missing required flag: --out <path>.",
                SuggestedAction: "Specify --out with the destination .mdma file path."
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.TargetAppNotFoundOrPathInvalid;
        }

        var targetApp = ParseTargetApp(args.App);
        if (targetApp is null)
        {
            var err = new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                $"Unsupported app target '{args.App}'. Must be 'ndm' or 'jd2'."
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
        IDownloadManagerLocator locator =
            targetApp == TargetApp.NDM ? new NdmLocator(registry) : new Jd2Locator();
        IDownloadListReader reader =
            targetApp == TargetApp.NDM ? new NdmListReader() : new Jd2ListReader();

        Result<TargetAppLocation> locationResult = !string.IsNullOrEmpty(args.ManualPath)
            ? locator.ValidateManualPath(args.ManualPath)
            : locator.TryAutoDetect();

        if (!locationResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(locationResult.Error!, args.Json);
            return ExitCodes.Map(locationResult.Error!.Code);
        }

        var location = locationResult.Value!;
        if (!string.IsNullOrEmpty(args.MetadataDir))
        {
            location = new TargetAppLocation(
                location.App,
                location.InstallOrConfigDir,
                args.MetadataDir,
                location.DownloadDirectory,
                location.WasAutoDetected
            );
        }
        if (!string.IsNullOrEmpty(args.TempDir))
        {
            location = new TargetAppLocation(
                location.App,
                args.TempDir,
                location.MetadataDir,
                location.DownloadDirectory,
                location.WasAutoDetected
            );
        }

        var tasksResult = reader.ScanTasks(location);
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
                $"Task ID '{args.Id}' was not found in {targetApp}."
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
        var exportResult = service.ExportToFile(task, location, args.OutPath, reporter);
        reporter.Complete();

        if (!exportResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(exportResult.Error!, args.Json);
            return ExitCodes.Map(exportResult.Error!.Code);
        }

        ConsoleFormatter.PrintExportResult(exportResult.Value!, args.Json);
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
