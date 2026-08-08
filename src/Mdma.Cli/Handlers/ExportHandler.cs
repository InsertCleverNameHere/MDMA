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
        var resolver = new LocationResolver(registry);
        IDownloadListReader reader =
            targetApp == TargetApp.NDM ? new NdmListReader() : new Jd2ListReader();

        var locationResult = resolver.ResolveLocation(
            targetApp.Value,
            manualPathOverride: args.ManualPath,
            metadataDirOverride: args.MetadataDir,
            tempDirOverride: args.TempDir,
            downloadDirOverride: args.DownloadDir
        );

        if (!locationResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(locationResult.Error!, args.Json, args.Verbose);
            return ExitCodes.Map(locationResult.Error!.Code);
        }

        var location = locationResult.Value!;

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

        string destinationMdmaPath;
        var safeFileName = SanitizeFileName(task.Filename);
        var defaultPackageName = $"{safeFileName}.mdma";

        if (string.IsNullOrWhiteSpace(args.OutPath))
        {
            destinationMdmaPath = Path.Combine(Environment.CurrentDirectory, defaultPackageName);
        }
        else if (Directory.Exists(args.OutPath))
        {
            destinationMdmaPath = Path.Combine(args.OutPath, defaultPackageName);
        }
        else
        {
            destinationMdmaPath = Path.GetFullPath(args.OutPath);
        }

        var reporter = new ConsoleProgressReporter(args.Json);
        var exportResult = service.ExportToFile(task, location, destinationMdmaPath, reporter);
        reporter.Complete();

        if (!exportResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(exportResult.Error!, args.Json);
            return ExitCodes.Map(exportResult.Error!.Code);
        }

        ConsoleFormatter.PrintExportResult(exportResult.Value!, args.Json);
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

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
