using Mdma.Core;

namespace Mdma.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var cliArgs = CliParser.Parse(args);

        if (cliArgs.Help || string.IsNullOrEmpty(cliArgs.Command) || cliArgs.Command == "help")
        {
            ConsoleFormatter.PrintHelp(cliArgs.Command, cliArgs.Json);
            return ExitCodes.Success;
        }

        if (cliArgs.Command == "version")
        {
            ConsoleFormatter.PrintVersion(cliArgs.Json);
            return ExitCodes.Success;
        }

        try
        {
            return CommandRouter.Route(cliArgs);
        }
        catch (Exception ex)
        {
            var err = new MdmaError(
                MdmaErrorCode.Unknown,
                "An unexpected error occurred during command execution.",
                Inner: ex
            );
            ConsoleFormatter.PrintError(err, cliArgs.Json);
            return ExitCodes.UnknownError;
        }
    }
}
