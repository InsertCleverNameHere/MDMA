using System.Text.Json;
using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class FileLoggerTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;
    private FakeClock _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-filelogger-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
        _workingRoot = new WorkingRoot(_testDir, true, false);
        _clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero) };
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private string LogFilePath => Path.Combine(_testDir, "logs", "mdma_20260804.log");

    [Test]
    public void Log_Creates_Log_File_Inside_WorkingRoot_Logs_Folder()
    {
        var logger = new FileLogger(_workingRoot, _clock);

        logger.LogInfo("TestComponent", "Test message");

        Assert.That(File.Exists(LogFilePath), Is.True);
    }

    [Test]
    public void Log_Writes_Valid_Json_Line_With_Correct_Fields()
    {
        var logger = new FileLogger(_workingRoot, _clock);
        var ex = new InvalidOperationException("Something went wrong");

        logger.LogError(
            "TestComponent",
            "An error occurred",
            details: "Extra context",
            exception: ex
        );

        var lines = File.ReadAllLines(LogFilePath);
        Assert.That(lines, Has.Length.EqualTo(1));

        var entry = JsonSerializer.Deserialize<LogEntry>(lines[0]);
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Level, Is.EqualTo(MdmaLogLevel.Error));
        Assert.That(entry.Component, Is.EqualTo("TestComponent"));
        Assert.That(entry.Message, Is.EqualTo("An error occurred"));
        Assert.That(entry.Details, Is.EqualTo("Extra context"));
        Assert.That(
            entry.Exception,
            Does.Contain("System.InvalidOperationException: Something went wrong")
        );
    }

    [Test]
    public void Log_Appends_Multiple_Entries_In_Sequence()
    {
        var logger = new FileLogger(_workingRoot, _clock);

        logger.LogInfo("ComponentA", "First entry");
        logger.LogWarning("ComponentB", "Second entry");

        var lines = File.ReadAllLines(LogFilePath);
        Assert.That(lines, Has.Length.EqualTo(2));

        var entry1 = JsonSerializer.Deserialize<LogEntry>(lines[0])!;
        var entry2 = JsonSerializer.Deserialize<LogEntry>(lines[1])!;

        Assert.That(entry1.Message, Is.EqualTo("First entry"));
        Assert.That(entry2.Message, Is.EqualTo("Second entry"));
    }

    [Test]
    public void Log_Is_ThreadSafe_When_Called_Concurrently()
    {
        var logger = new FileLogger(_workingRoot, _clock);
        const int count = 100;

        Parallel.For(
            0,
            count,
            i =>
            {
                logger.LogInfo("ParallelComponent", $"Message {i}");
            }
        );

        var lines = File.ReadAllLines(LogFilePath);
        Assert.That(lines, Has.Length.EqualTo(count));
    }

    [Test]
    public void Log_Does_Not_Throw_When_Log_Directory_Is_Unwritable()
    {
        // Point working root at a path where 'logs' cannot be created as a subdirectory
        // (a file named 'logs' blocking folder creation).
        var blockedRoot = Path.Combine(_testDir, "blocked");
        Directory.CreateDirectory(blockedRoot);
        File.WriteAllText(Path.Combine(blockedRoot, "logs"), "blocking file");

        var blockedWorkingRoot = new WorkingRoot(blockedRoot, true, false);
        var logger = new FileLogger(blockedWorkingRoot, _clock);

        Assert.DoesNotThrow(() => logger.LogInfo("TestComponent", "Should fail silently"));
    }
}
