using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mdma.Core.Tests.Fixtures;

/// <summary>
/// Builds .mdma package files directly (bypassing the real exporter), so
/// MdmaLoader can be tested in isolation, including against deliberately
/// broken files. Checksum scope here is "SHA-256 over each chunk file's
/// bytes, concatenated in index order" — MUST match whatever MdmaLoader
/// actually implements; update both together if the scheme changes
/// (see architecture plan Open Question #2).
/// </summary>
public sealed class MdmaFixtureBuilder
{
    public sealed record ChunkData(int Index, long StartByte, long EndByte, byte[] Bytes);

    private int _version = 1;
    private string _origin = "NDM";
    private string _url = "https://example.com/file.bin";
    private string _filename = "file.bin";
    private long _totalBytes = 1024;
    private readonly List<ChunkData> _chunks = new();

    public MdmaFixtureBuilder WithVersion(int v) { _version = v; return this; }
    public MdmaFixtureBuilder WithOrigin(string origin) { _origin = origin; return this; }
    public MdmaFixtureBuilder WithTotalBytes(long total) { _totalBytes = total; return this; }

    public MdmaFixtureBuilder WithChunk(int index, long start, long end, byte[] bytes)
    {
        _chunks.Add(new ChunkData(index, start, end, bytes));
        return this;
    }

    /// <summary>Builds a valid, checksum-correct .mdma at destinationPath.</summary>
    public string BuildValid(string destinationPath)
    {
        BuildInternal(destinationPath, corruptChecksum: false, omitManifest: false);
        return destinationPath;
    }

    /// <summary>Builds a .mdma with a checksum that won't match its contents,
    /// for testing MdmaLoader's MdmaChecksumMismatch path.</summary>
    public string BuildWithBadChecksum(string destinationPath)
    {
        BuildInternal(destinationPath, corruptChecksum: true, omitManifest: false);
        return destinationPath;
    }

    /// <summary>Builds a .mdma missing manifest.json entirely, for testing
    /// MdmaLoader's MdmaManifestMalformed path.</summary>
    public string BuildWithoutManifest(string destinationPath)
    {
        BuildInternal(destinationPath, corruptChecksum: false, omitManifest: true);
        return destinationPath;
    }

    private void BuildInternal(string destinationPath, bool corruptChecksum, bool omitManifest)
    {
        using var zipStream = new FileStream(destinationPath, FileMode.Create);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);

        if (!omitManifest)
        {
            var manifest = new
            {
                mdma_version = _version,
                origin = _origin,
                task = new { url = _url, filename = _filename, total_size = _totalBytes, mimetype = (string?)null, headers = Array.Empty<object>(), created = 1785268000000L },
                chunks = _chunks.Select(c => new { index = c.Index, start_byte = c.StartByte, end_byte = c.EndByte, downloaded_bytes = (long)c.Bytes.Length })
            };
            var manifestEntry = zip.CreateEntry("manifest.json");
            using (var w = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
                w.Write(JsonSerializer.Serialize(manifest));
        }

        foreach (var chunk in _chunks.OrderBy(c => c.Index))
        {
            var entry = zip.CreateEntry($"data/chunk_{chunk.Index}.bin");
            using var s = entry.Open();
            s.Write(chunk.Bytes, 0, chunk.Bytes.Length);
        }

        using (var sha = SHA256.Create())
        {
            var combined = _chunks.OrderBy(c => c.Index).SelectMany(c => c.Bytes).ToArray();
            var hash = sha.ComputeHash(combined);
            if (corruptChecksum) hash[0] ^= 0xFF; // flip a bit so it can never match

            var checksumEntry = zip.CreateEntry("checksum.sha256");
            using var w = new StreamWriter(checksumEntry.Open(), Encoding.UTF8);
            w.Write(Convert.ToHexString(hash));
        }
    }
}
