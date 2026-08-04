using Mdma.Core;
using NUnit.Framework;

namespace Mdma.Cli.Tests;

public class CliParserTests
{
    [Test]
    public void Parse_Parses_Command_And_Global_Flags()
    {
        var args = new[] { "scan", "--workdir", @"C:\CustomWork", "--verbose", "--json" };

        var parsed = CliParser.Parse(args);

        Assert.That(parsed.Command, Is.EqualTo("scan"));
        Assert.That(parsed.WorkDir, Is.Not.Null);
        Assert.That(parsed.Verbose, Is.True);
        Assert.That(parsed.Json, Is.True);
    }

    [Test]
    public void Parse_Parses_Short_Option_Flags()
    {
        var args = new[] { "export", "-a", "ndm", "-i", "521", "-o", @"C:\out.mdma", "-h" };

        var parsed = CliParser.Parse(args);

        Assert.That(parsed.Command, Is.EqualTo("export"));
        Assert.That(parsed.App, Is.EqualTo("ndm"));
        Assert.That(parsed.Id, Is.EqualTo("521"));
        Assert.That(parsed.OutPath, Is.Not.Null);
        Assert.That(parsed.Help, Is.True);
    }

    [Test]
    public void Parse_Parses_Convert_Source_And_Dest_Apps()
    {
        var args = new[] { "convert", "-s", "ndm", "-d", "jd2", "-i", "1" };

        var parsed = CliParser.Parse(args);

        Assert.That(parsed.Command, Is.EqualTo("convert"));
        Assert.That(parsed.SourceApp, Is.EqualTo("ndm"));
        Assert.That(parsed.DestApp, Is.EqualTo("jd2"));
        Assert.That(parsed.Id, Is.EqualTo("1"));
    }

    [Test]
    public void Parse_Strips_Surrounding_Quotes_From_Paths()
    {
        var args = new[] { "import", "-f", "\"C:\\Some Path\\file.mdma\"" };

        var parsed = CliParser.Parse(args);

        Assert.That(parsed.FilePath, Does.Not.Contain("\""));
    }

    [Test]
    public void ExitCodes_Maps_MdmaErrorCode_To_Expected_ExitCodes()
    {
        Assert.That(ExitCodes.Map(MdmaErrorCode.TargetAppProcessRunning), Is.EqualTo(1));
        Assert.That(ExitCodes.Map(MdmaErrorCode.TargetAppNotFound), Is.EqualTo(2));
        Assert.That(ExitCodes.Map(MdmaErrorCode.ManualPathInvalid), Is.EqualTo(2));
        Assert.That(ExitCodes.Map(MdmaErrorCode.InsufficientDiskSpaceSource), Is.EqualTo(3));
        Assert.That(ExitCodes.Map(MdmaErrorCode.MdmaChecksumMismatch), Is.EqualTo(4));
        Assert.That(ExitCodes.Map(MdmaErrorCode.BackupFailed), Is.EqualTo(5));
        Assert.That(ExitCodes.Map(MdmaErrorCode.ExportFailed), Is.EqualTo(6));
        Assert.That(ExitCodes.Map(MdmaErrorCode.Unknown), Is.EqualTo(99));
    }
}
