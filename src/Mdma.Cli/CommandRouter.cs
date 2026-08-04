using Mdma.Cli.Handlers;
using Mdma.Core;

namespace Mdma.Cli;

public static class CommandRouter
{
    public static int Route(CliArgs args)
    {
        return args.Command switch
        {
            "scan" => ScanHandler.Execute(args),
            "clean" => CleanHandler.Execute(args),
            "export" => ExportHandler.Execute(args),
            "import" => ImportHandler.Execute(args),
            "convert" => ConvertHandler.Execute(args),
            "backups" => BackupsHandler.Execute(args),
            "revert" => RevertHandler.Execute(args),
            _ => HandleUnknownCommand(args.Command, args.Json),
        };
    }

    private static int HandleNotImplemented(string command, CliArgs args)
    {
        var error = new MdmaError(
            MdmaErrorCode.ScanFailed,
            $"The '{command}' command handler is scheduled for later CLI phases."
        );
        ConsoleFormatter.PrintError(error, args.Json);
        return ExitCodes.OperationFailed;
    }

    private static int HandleUnknownCommand(string command, bool isJson)
    {
        var error = new MdmaError(
            MdmaErrorCode.ManualPathInvalid,
            $"Unrecognized command '{command}'. Run 'mdma --help' for usage info."
        );
        ConsoleFormatter.PrintError(error, isJson);
        return ExitCodes.TargetAppNotFoundOrPathInvalid;
    }
}
