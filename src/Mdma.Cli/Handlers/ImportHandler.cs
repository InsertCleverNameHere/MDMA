using Mdma.Core;

namespace Mdma.Cli.Handlers;

public static class ImportHandler
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

        if (string.IsNullOrWhiteSpace(args.FilePath))
        {
            var err = new MdmaError(
                MdmaErrorCode.ManualPathInvalid,
                "Missing required flag: --file <path>.",
                SuggestedAction: "Specify --file with the path to the .mdma package file."
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.TargetAppNotFoundOrPathInvalid;
        }

        if (!File.Exists(args.FilePath))
        {
            var err = new MdmaError(
                MdmaErrorCode.MdmaFileNotFound,
                "The specified .mdma package file was not found.",
                Details: args.FilePath
            );
            ConsoleFormatter.PrintError(err, args.Json);
            return ExitCodes.PackageOrChecksumError;
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
        var importResult = service.ImportFromFile(args.FilePath, location, reporter);
        reporter.Complete();

        if (!importResult.IsSuccess)
        {
            ConsoleFormatter.PrintError(importResult.Error!, args.Json);
            return ExitCodes.Map(importResult.Error!.Code);
        }

        ConsoleFormatter.PrintImportResult(
            $"Package '{Path.GetFileName(args.FilePath)}' imported successfully into {targetApp}.",
            args.Json
        );
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
