using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mdma.Core.Tests.Fixtures;

/// <summary>
/// Builds .mdma package files directly (bypassing MdmaPackageWriter), so
/// MdmaLoader can be tested in isolation, including against deliberately
/// broken files. MUST mirror the exact on-disk format MdmaPackageWriter/
/// MdmaLoader use (see MdmaPackageFormat.cs): nested manifest.json shape,
/// and the hash-of-hashes checksum.sha256 scheme (mdma-core-plan.md
/// Decision #2). If that format ever changes, this file needs to change
/// with it -- it is deliberately NOT sharing code with the production
/// writer, so a drift between the two here is a real bug to fix, not
/// expected duplication.
/// </summary>
public sealed class MdmaFixtureBuilder
{
    public sealed record ChunkData(int Index, long StartByte, long EndByte, byte[] Bytes);

    private int _version = MdmaChecksumHelper.CurrentMdmaVersion;
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
        BuildInternal(destinationPath, corruptChunkIndex: null, corruptManifestHash: false,
            omitManifest: false, omitChecksum: false, dropChunkFromChecksum: null);
        return destinationPath;
    }

    /// <summary>Builds a .mdma where one specific chunk's bytes don't match its
    /// recorded hash, for testing MdmaLoader's per-chunk MdmaChecksumMismatch path.</summary>
    public string BuildWithCorruptChunk(string destinationPath, int chunkIndex)
    {
        BuildInternal(destinationPath, corruptChunkIndex: chunkIndex, corruptManifestHash: false,
            omitManifest: false, omitChecksum: false, dropChunkFromChecksum: null);
        return destinationPath;
    }

    /// <summary>Builds a .mdma whose manifest_hash doesn't match its recorded
    /// per-chunk hashes (chunk hashes themselves are fine), for testing the
    /// whole-package hash-of-hashes check independent of per-chunk checks.</summary>
    public string BuildWithBadManifestHash(string destinationPath)
    {
        BuildInternal(destinationPath, corruptChunkIndex: null, corruptManifestHash: true,
            omitManifest: false, omitChecksum: false, dropChunkFromChecksum: null);
        return destinationPath;
    }

    /// <summary>Builds a .mdma missing manifest.json entirely.</summary>
    public string BuildWithoutManifest(string destinationPath)
    {
        BuildInternal(destinationPath, corruptChunkIndex: null, corruptManifestHash: false,
            omitManifest: true, omitChecksum: false, dropChunkFromChecksum: null);
        return destinationPath;
    }

    /// <summary>Builds a .mdma missing checksum.sha256 entirely.</summary>
    public string BuildWithoutChecksum(string destinationPath)
    {
        BuildInternal(destinationPath, corruptChunkIndex: null, corruptManifestHash: false,
            omitManifest: false, omitChecksum: true, dropChunkFromChecksum: null);
        return destinationPath;
    }

    /// <summary>Builds a .mdma where checksum.sha256's chunk_hashes is missing
    /// an entry that manifest.json's chunks list still has, for testing the
    /// structural-consistency check.</summary>
    public string BuildWithMismatchedChunkLists(string destinationPath, int chunkIndexToDropFromChecksum)
    {
        BuildInternal(destinationPath, corruptChunkIndex: null, corruptManifestHash: false,
            omitManifest: false, omitChecksum: false, dropChunkFromChecksum: chunkIndexToDropFromChecksum);
        return destinationPath;
    }

    private void BuildInternal(
        string destinationPath,
        int? corruptChunkIndex,
        bool corruptManifestHash,
        bool omitManifest,
        bool omitChecksum,
        int? dropChunkFromChecksum)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var zipStream = new FileStream(destinationPath, FileMode.Create);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);

        var orderedChunks = _chunks.OrderBy(c => c.Index).ToList();
        var chunkHashes = new Dictionary<string, string>();

        foreach (var chunk in orderedChunks)
        {
            var bytesToWrite = chunk.Bytes;
            var entry = zip.CreateEntry($"data/chunk_{chunk.Index}.bin");
            using (var s = entry.Open())
            {
                s.Write(bytesToWrite, 0, bytesToWrite.Length);
            }

            var actualHash = Convert.ToHexString(SHA256.HashData(bytesToWrite));
            var recordedHash = chunk.Index == corruptChunkIndex
                ? FlipHexHash(actualHash) // deliberately wrong, so the file doesn't match what's recorded
                : actualHash;

            chunkHashes[chunk.Index.ToString()] = recordedHash;
        }

        if (!omitManifest)
        {
            var manifestJson = JsonSerializer.Serialize(new
            {
                mdma_version = _version,
                origin = _origin,
                task = new
                {
                    url = _url,
                    filename = _filename,
                    total_size = _totalBytes,
                    mimetype = (string?)null,
                    headers = Array.Empty<object>(),
                    created = 1785268000000L,
                },
                chunks = orderedChunks
                    .Select(c => new
                    {
                        index = c.Index,
                        start_byte = c.StartByte,
                        end_byte = c.EndByte,
                        downloaded_bytes = (long)c.Bytes.Length,
                    }),
            });

            var manifestEntry = zip.CreateEntry("manifest.json");
            using var w = new StreamWriter(manifestEntry.Open(), Encoding.UTF8);
            w.Write(manifestJson);
        }

        if (!omitChecksum)
        {
            var effectiveChunkHashes = dropChunkFromChecksum is int dropIndex
                ? chunkHashes.Where(kv => kv.Key != dropIndex.ToString()).ToDictionary(kv => kv.Key, kv => kv.Value)
                : chunkHashes;

            var manifestHash = MdmaChecksumHelper.ComputeManifestHash(
                effectiveChunkHashes.Select(kv => new KeyValuePair<int, string>(int.Parse(kv.Key), kv.Value)));

            if (corruptManifestHash)
            {
                manifestHash = FlipHexHash(manifestHash);
            }

            var checksumJson = JsonSerializer.Serialize(new
            {
                chunk_hashes = effectiveChunkHashes,
                manifest_hash = manifestHash,
            });

            var checksumEntry = zip.CreateEntry("checksum.sha256");
            using var w = new StreamWriter(checksumEntry.Open(), Encoding.UTF8);
            w.Write(checksumJson);
        }
    }

    private static string FlipHexHash(string hexHash)
    {
        var chars = hexHash.ToCharArray();
        chars[0] = chars[0] == 'F' ? '0' : 'F';
        return new string(chars);
    }
}
