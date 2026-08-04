using System.Text.Json;
using Mdma.Core;

namespace Mdma.Cli;

public static class ConsoleFormatter
{
    public static void PrintError(MdmaError error, bool isJson)
    {
        if (isJson)
        {
            var dto = new ErrorJsonDto(
                Success: false,
                Code: error.Code.ToString(),
                ExitCode: ExitCodes.Map(error.Code),
                Message: error.Message,
                Details: error.Details,
                SuggestedAction: error.SuggestedAction
            );
            Console.WriteLine(JsonSerializer.Serialize(dto, CliJsonContext.Default.ErrorJsonDto));
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[ERROR] [{error.Code}] {error.Message}");
            Console.ResetColor();

            if (!string.IsNullOrEmpty(error.Details))
            {
                Console.Error.WriteLine($"        Details: {error.Details}");
            }
            if (!string.IsNullOrEmpty(error.SuggestedAction))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"        Suggestion: {error.SuggestedAction}");
                Console.ResetColor();
            }
        }
    }

    public static void PrintTasksTable(IReadOnlyList<DownloadTaskSummary> tasks, bool isJson)
    {
        if (isJson)
        {
            var payload = tasks
                .Select(t => new TaskJsonDto(
                    Id: t.NativeId,
                    Source: t.Source.ToString(),
                    Filename: t.Filename,
                    TotalBytes: t.TotalBytes,
                    DownloadedBytes: t.DownloadedBytes,
                    Percent: Math.Round(t.PercentComplete, 1),
                    Resumable: t.Resumable,
                    Status: t.StatusText,
                    Url: t.Url
                ))
                .ToList();

            var dto = new TaskListJsonDto(Success: true, Tasks: payload);
            Console.WriteLine(
                JsonSerializer.Serialize(dto, CliJsonContext.Default.TaskListJsonDto)
            );
            return;
        }

        if (tasks.Count == 0)
        {
            Console.WriteLine("No download tasks found.");
            return;
        }

        Console.WriteLine(
            $"{"ID", -10} {"APP", -6} {"PROGRESS", -10} {"DOWNLOADED", -16} {"FILENAME"}"
        );
        Console.WriteLine(new string('-', 75));

        foreach (var t in tasks)
        {
            var progressText = $"{t.PercentComplete:F1}%";
            var sizeText = $"{FormatBytes(t.DownloadedBytes)} / {FormatBytes(t.TotalBytes)}";
            var filename = t.Filename.Length > 30 ? t.Filename[..27] + "..." : t.Filename;

            Console.WriteLine(
                $"{t.NativeId, -10} {t.Source, -6} {progressText, -10} {sizeText, -16} {filename}"
            );
        }
    }

    public static void PrintBackupsTable(IReadOnlyList<BackupHandle> backups, bool isJson)
    {
        if (isJson)
        {
            var payload = backups
                .Select(b => new BackupJsonDto(
                    Id: b.Id,
                    Target: b.Target.ToString(),
                    CreatedAt: b.CreatedAt.ToString("o"),
                    StoragePath: b.StoragePath
                ))
                .ToList();

            var dto = new BackupListJsonDto(Success: true, Backups: payload);
            Console.WriteLine(
                JsonSerializer.Serialize(dto, CliJsonContext.Default.BackupListJsonDto)
            );
            return;
        }

        if (backups.Count == 0)
        {
            Console.WriteLine("No backup snapshots found.");
            return;
        }

        Console.WriteLine($"{"SNAPSHOT ID", -36} {"TARGET", -8} {"CREATED AT (UTC)"}");
        Console.WriteLine(new string('-', 65));

        foreach (var b in backups)
        {
            Console.WriteLine($"{b.Id, -36} {b.Target, -8} {b.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        }
    }

    public static void PrintHelp(string? subCommand, bool isJson)
    {
        if (isJson)
        {
            var commands = new[]
            {
                new HelpCommandDto("scan", "Scan download tasks from NDM or JD2"),
                new HelpCommandDto("export", "Export a task to a .mdma package file"),
                new HelpCommandDto("import", "Import a .mdma package file into NDM or JD2"),
                new HelpCommandDto("convert", "Migrate a task directly between NDM and JD2"),
                new HelpCommandDto("backups", "List backup snapshots in the working directory"),
                new HelpCommandDto("revert", "Restore a backup snapshot by ID"),
                new HelpCommandDto("clean", "Sweep orphaned temporary files from .mdma-tmp"),
                new HelpCommandDto("help", "Display help information"),
            };

            var dto = new HelpJsonDto("MDMA", "Multi Download Manager Analogue CLI", commands);
            Console.WriteLine(JsonSerializer.Serialize(dto, CliJsonContext.Default.HelpJsonDto));
            return;
        }

        Console.WriteLine("MDMA — Multi Download Manager Analogue CLI");
        Console.WriteLine("Usage: mdma <command> [options]\n");
        Console.WriteLine("Commands:");
        Console.WriteLine("  scan      Scan download tasks from NDM or JD2");
        Console.WriteLine("  export    Export a task to a .mdma package file");
        Console.WriteLine("  import    Import a .mdma package file into NDM or JD2");
        Console.WriteLine("  convert   Migrate a task directly between NDM and JD2");
        Console.WriteLine("  backups   List backup snapshots in the working directory");
        Console.WriteLine("  revert    Restore a backup snapshot by ID");
        Console.WriteLine("  clean     Sweep orphaned temporary files from .mdma-tmp");
        Console.WriteLine("  help      Display help information\n");
        Console.WriteLine("Global Options:");
        Console.WriteLine("  -w, --workdir <path>  Specify working directory");
        Console.WriteLine("  -v, --verbose         Enable detailed logging");
        Console.WriteLine("  --json                Output results in JSON format");
        Console.WriteLine("  -h, --help            Show help usage");
    }

    public static void PrintVersion(bool isJson)
    {
        if (isJson)
        {
            var dto = new VersionJsonDto("MDMA CLI", "1.0.0");
            Console.WriteLine(JsonSerializer.Serialize(dto, CliJsonContext.Default.VersionJsonDto));
            return;
        }
        Console.WriteLine("MDMA CLI v1.0.0");
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024,
            mb = kb * 1024,
            gb = mb * 1024;
        return bytes switch
        {
            < 0 => $"{bytes} B",
            _ when bytes >= gb => $"{bytes / gb:F2} GB",
            _ when bytes >= mb => $"{bytes / mb:F2} MB",
            _ when bytes >= kb => $"{bytes / kb:F2} KB",
            _ => $"{bytes} B",
        };
    }

    public static void PrintCleanResult(TempCleanupReport report, bool isJson)
    {
        if (isJson)
        {
            var dto = new CleanJsonDto(
                true,
                report.Removed.ToArray(),
                report.FailedToRemove.ToArray()
            );
            Console.WriteLine(JsonSerializer.Serialize(dto, CliJsonContext.Default.CleanJsonDto));
            return;
        }

        if (report.Removed.Count == 0 && report.FailedToRemove.Count == 0)
        {
            Console.WriteLine("No orphaned temporary files were found in .mdma-tmp.");
            return;
        }

        if (report.Removed.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Successfully removed {report.Removed.Count} orphaned item(s):");
            Console.ResetColor();
            foreach (var item in report.Removed)
            {
                Console.WriteLine($"  - {item}");
            }
        }

        if (report.FailedToRemove.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Failed to remove {report.FailedToRemove.Count} locked item(s):");
            Console.ResetColor();
            foreach (var item in report.FailedToRemove)
            {
                Console.WriteLine($"  - {item}");
            }
        }
    }

    public static void PrintExportResult(string path, bool isJson)
    {
        if (isJson)
        {
            var dto = new ExportJsonDto(true, path);
            Console.WriteLine(JsonSerializer.Serialize(dto, CliJsonContext.Default.ExportJsonDto));
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] Exported package to: {path}");
        Console.ResetColor();
    }

    public static void PrintImportResult(string message, bool isJson)
    {
        if (isJson)
        {
            var dto = new ImportJsonDto(true, message);
            Console.WriteLine(JsonSerializer.Serialize(dto, CliJsonContext.Default.ImportJsonDto));
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] {message}");
        Console.ResetColor();
    }

    public static void PrintConvertResult(string message, bool isJson)
    {
        if (isJson)
        {
            var dto = new ConvertJsonDto(true, message);
            Console.WriteLine(JsonSerializer.Serialize(dto, CliJsonContext.Default.ConvertJsonDto));
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] {message}");
        Console.ResetColor();
    }

    public static void PrintRevertResult(string message, bool isJson)
    {
        if (isJson)
        {
            var dto = new RevertJsonDto(true, message);
            Console.WriteLine(JsonSerializer.Serialize(dto, CliJsonContext.Default.RevertJsonDto));
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] {message}");
        Console.ResetColor();
    }
}
