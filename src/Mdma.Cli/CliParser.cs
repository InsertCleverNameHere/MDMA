namespace Mdma.Cli;

public sealed record CliArgs(
    string Command,
    string? WorkDir,
    bool Verbose,
    bool Json,
    bool Help,
    string? App,
    string? SourceApp,
    string? DestApp,
    string? Id,
    string? OutPath,
    string? FilePath,
    string? ManualPath,
    string? TempDir,
    string? MetadataDir,
    string? DownloadDir
);

public static class CliParser
{
    public static CliArgs Parse(string[] args)
    {
        string command = "";
        string? workDir = null;
        bool verbose = false;
        bool json = false;
        bool help = false;
        string? app = null;
        string? sourceApp = null;
        string? destApp = null;
        string? id = null;
        string? outPath = null;
        string? filePath = null;
        string? manualPath = null;
        string? tempDir = null;
        string? metadataDir = null;
        string? downloadDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim();
            if (string.IsNullOrEmpty(arg))
                continue;

            if (i == 0 && !arg.StartsWith('-'))
            {
                command = arg.ToLowerInvariant();
                continue;
            }

            if (string.IsNullOrEmpty(command) && !arg.StartsWith('-'))
            {
                command = arg.ToLowerInvariant();
                continue;
            }

            switch (arg.ToLowerInvariant())
            {
                case "-h" or "--help":
                    help = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "--json":
                    json = true;
                    break;

                case "-w"
                or "--workdir":
                    workDir = GetNextPathValue(args, ref i);
                    break;
                case "-a" or "--app":
                    app = GetNextStringValue(args, ref i);
                    break;
                case "-s" or "--source":
                    sourceApp = GetNextStringValue(args, ref i);
                    break;
                case "-d" or "--dest":
                    destApp = GetNextStringValue(args, ref i);
                    break;
                case "-i" or "--id":
                    id = GetNextStringValue(args, ref i);
                    break;
                case "-o" or "--out":
                    outPath = GetNextPathValue(args, ref i);
                    break;
                case "-f" or "--file":
                    filePath = GetNextPathValue(args, ref i);
                    break;
                case "-p" or "--path":
                    manualPath = GetNextPathValue(args, ref i);
                    break;
                case "--temp-dir":
                    tempDir = GetNextPathValue(args, ref i);
                    break;
                case "--metadata-dir":
                    metadataDir = GetNextPathValue(args, ref i);
                    break;
                case "--download-dir":
                    downloadDir = GetNextPathValue(args, ref i);
                    break;
            }
        }

        if (string.IsNullOrEmpty(command) && help)
            command = "help";

        return new CliArgs(
            Command: command,
            WorkDir: workDir,
            Verbose: verbose,
            Json: json,
            Help: help,
            App: app,
            SourceApp: sourceApp,
            DestApp: destApp,
            Id: id,
            OutPath: outPath,
            FilePath: filePath,
            ManualPath: manualPath,
            TempDir: tempDir,
            MetadataDir: metadataDir,
            DownloadDir: downloadDir
        );
    }

    private static string? GetNextStringValue(string[] args, ref int index)
    {
        if (index + 1 < args.Length && !args[index + 1].StartsWith('-'))
        {
            index++;
            return args[index].Trim('"', '\'');
        }
        return null;
    }

    private static string? GetNextPathValue(string[] args, ref int index)
    {
        var raw = GetNextStringValue(args, ref index);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var cleaned = raw.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(cleaned);
        }
        catch
        {
            return cleaned;
        }
    }
}
