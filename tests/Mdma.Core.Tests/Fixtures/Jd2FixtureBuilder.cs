using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Mdma.Core.Tests.Fixtures;

/// <summary>
/// Builds a synthetic, on-disk JD2 cfg\ directory with a downloadList<N>.zip,
/// matching the real format documented in docs/jd2.md, so tests never depend
/// on a real JD2 install.
/// </summary>
public sealed class Jd2FixtureBuilder
{
    public string CfgDirectory { get; }

    private readonly List<(
        string packageId,
        string linkIndex,
        string filename,
        string url,
        long size,
        long current,
        long[] chunkProgress
    )> _links = new();
    private readonly int _counter;

    public Jd2FixtureBuilder(string rootDir, int counter = 11283)
    {
        CfgDirectory = Path.Combine(rootDir, "cfg");
        Directory.CreateDirectory(CfgDirectory);
        _counter = counter;
    }

    public Jd2FixtureBuilder WithLink(
        string packageId,
        string linkIndex,
        string filename,
        string url,
        long size,
        long current,
        params long[] chunkProgress
    )
    {
        _links.Add((packageId, linkIndex, filename, url, size, current, chunkProgress));
        return this;
    }

    private string? _defaultDownloadFolder;

    public Jd2FixtureBuilder WithDefaultDownloadFolder(string folder)
    {
        _defaultDownloadFolder = folder;
        return this;
    }

    /// <summary>Builds downloadList<_counter>.zip. Returns the full path so tests
    /// can point Jd2ListReader/Jd2Exporter at CfgDirectory and expect this file
    /// to be picked up as the newest.</summary>
    public string Build()
    {
        var zipPath = Path.Combine(CfgDirectory, $"downloadList{_counter}.zip");
        using var zipStream = new FileStream(zipPath, FileMode.Create);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);

        var packageIds = _links.Select(l => l.packageId).Distinct();
        foreach (var pkgId in packageIds)
        {
            var pkgJson = JsonSerializer.Serialize(
                new
                {
                    uid = 1785268000000L,
                    name = "MDMA Injected Downloads",
                    downloadFolder = "D:\\Downloads",
                    created = 1785268000000L,
                    enabled = true,
                }
            );
            WriteEntry(zip, pkgId, pkgJson);
        }

        foreach (var link in _links)
        {
            var linkJson = JsonSerializer.Serialize(
                new
                {
                    uid = 1785268000001L,
                    name = link.filename,
                    url = link.url,
                    host = new Uri(link.url).Host,
                    size = link.size,
                    current = link.current,
                    chunkProgress = link.chunkProgress,
                    availablestatus = "TRUE",
                    enabled = true,
                    created = 1785268000000L,
                    properties = new
                    {
                        CHUNKS = link.chunkProgress.Length,
                        PROPERTY_RESUMEABLE = true,
                        URL_CONTENT = link.url,
                    },
                }
            );
            WriteEntry(zip, $"{link.packageId}_{link.linkIndex}", linkJson);
        }

        WriteEntry(zip, "extraInfo", JsonSerializer.Serialize(new { version = 2 }));

        if (_defaultDownloadFolder is not null)
        {
            var settingsPath = Path.Combine(
                CfgDirectory,
                "org.jdownloader.settings.GeneralSettings.json"
            );
            File.WriteAllText(
                settingsPath,
                JsonSerializer.Serialize(new { defaultdownloadfolder = _defaultDownloadFolder })
            );
        }

        return zipPath;
    }

    /// <summary>Adds an older downloadList<N>.zip in the same folder, to test
    /// that the reader correctly picks the highest-numbered file, not just any.</summary>
    public string BuildStaleDuplicate(int staleCounter)
    {
        var stale = new Jd2FixtureBuilder(Path.GetDirectoryName(CfgDirectory)!, staleCounter);
        foreach (var l in _links)
            stale.WithLink(
                l.packageId,
                l.linkIndex,
                l.filename,
                l.url,
                l.size,
                l.current,
                l.chunkProgress
            );
        return stale.Build();
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string json)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(json);
    }
}
