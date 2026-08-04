using Mdma.Core;
using NUnit.Framework;

namespace Mdma.Cli.Tests;

public class CommandRouterTests
{
    [Test]
    public void Route_UnknownCommand_Returns_TargetAppNotFoundOrPathInvalid_ExitCode()
    {
        var args = new CliArgs(
            "unknown-cmd",
            null,
            false,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );

        var exitCode = CommandRouter.Route(args);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.TargetAppNotFoundOrPathInvalid));
    }

    [Test]
    public void Program_Main_Help_Returns_Success_ExitCode()
    {
        var exitCode = Program.Main(new[] { "--help" });

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
    }

    [Test]
    public void Program_Main_Version_Returns_Success_ExitCode()
    {
        var exitCode = Program.Main(new[] { "version" });

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
    }
}
