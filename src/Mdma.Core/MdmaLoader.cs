using System.IO.Compression;
using System.Text.Json;

namespace Mdma.Core;

/// <summary>
/// Opens and verifies a .mdma file per architecture.md §5 and the hash-of-hashes
/// checksum scheme (mdma-core-plan.md Decision #2). Verification happens BEFORE
/// any chunk file is staged to disk -- a corrupt/tampered/wrong-version package
/// is rejected wholesale, matching the same "verify everything before touching
/// anything" pattern used by RevertManager.
/// </summary>
public sealed class MdmaLoader : IMdmaLoader
{
    private const string StagingSubfolder = ".mdma-tmp";

    public Result<MdmaPackage> Load(string mdmaFilePath, WorkingRoot workingRoot)
    {
        if (!File.Exists(mdmaFilePath))
        {
            return new MdmaError(
                MdmaErrorCode.MdmaFileNotFound,
                "The specified .mdma file does not exist.",
                Details: mdmaFilePath);
        }

        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(mdmaFilePath);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.MdmaManifestMalformed,
                "The .mdma file could not be opened as a valid zip archive.",
                Details: mdmaFilePath,
                Inner: ex);
        }

        using (zip)
        {
            var manifestResult = ReadManifest(zip, mdmaFilePath);
            if (!manifestResult.IsSuccess) return manifestResult.Error!;
            var manifest = manifestResult.Value!;

            if (manifest.MdmaVersion > MdmaChecksumHelper.CurrentMdmaVersion)
            {
                return new MdmaError(
                    MdmaErrorCode.MdmaVersionUnsupported,
                    $"This .mdma file was created with a newer format version ({manifest.MdmaVersion}) than this version of MDMA supports ({MdmaChecksumHelper.CurrentMdmaVersion}).",
                    Details: mdmaFilePath,
                    SuggestedAction: "Update MDMA to the latest version.");
            }

            var checksumResult = ReadChecksum(zip, mdmaFilePath);
            if (!checksumResult.IsSuccess) return checksumResult.Error!;
            var checksum = checksumResult.Value!;

            var consistencyCheck = VerifyStructuralConsistency(manifest, checksum, mdmaFilePath);
            if (!consistencyCheck.IsSuccess) return consistencyCheck.Error!;

            // Verify EVERY chunk's hash against the zip entry BEFORE extracting
            // anything to disk -- a corrupt package must be rejected wholesale.
            var perChunkVerify = VerifyAllChunkHashes(zip, manifest, checksum, mdmaFilePath);
            if (!perChunkVerify.IsSuccess) return perChunkVerify.Error!;

            var manifestHashCheck = VerifyManifestHash(checksum, mdmaFilePath);
            if (!manifestHashCheck.IsSuccess) return manifestHashCheck.Error!;

            // All verification passed -- now stage the chunk files to disk.
            return ExtractChunks(zip, manifest, workingRoot, mdmaFilePath);
        }
    }

    private static Result<MdmaManifestDto> ReadManifest(ZipArchive zip, string mdmaFilePath)
    {
        var entry = zip.GetEntry("manifest.json");
        if (entry is null)
        {
            return new MdmaError(
                MdmaErrorCode.MdmaManifestMalformed,
                "manifest.json is missing from the .mdma package.",
                Details: mdmaFilePath);
        }

        try
        {
            using var stream = entry.Open();
            var manifest = JsonSerializer.Deserialize<MdmaManifestDto>(stream);
            if (manifest is null)
            {
                return new MdmaError(MdmaErrorCode.MdmaManifestMalformed, "manifest.json deserialized to nothing.", Details: mdmaFilePath);
            }
            return Result<MdmaManifestDto>.Ok(manifest);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.MdmaManifestMalformed,
                "manifest.json could not be parsed.",
                Details: mdmaFilePath,
                Inner: ex);
        }
    }

    private static Result<MdmaChecksumDto> ReadChecksum(ZipArchive zip, string mdmaFilePath)
    {
        var entry = zip.GetEntry("checksum.sha256");
        if (entry is null)
        {
            return new MdmaError(
                MdmaErrorCode.MdmaManifestMalformed,
                "checksum.sha256 is missing from the .mdma package.",
                Details: mdmaFilePath);
        }

        try
        {
            using var stream = entry.Open();
            var checksum = JsonSerializer.Deserialize<MdmaChecksumDto>(stream);
            if (checksum is null)
            {
                return new MdmaError(MdmaErrorCode.MdmaManifestMalformed, "checksum.sha256 deserialized to nothing.", Details: mdmaFilePath);
            }
            return Result<MdmaChecksumDto>.Ok(checksum);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.MdmaManifestMalformed,
                "checksum.sha256 could not be parsed.",
                Details: mdmaFilePath,
                Inner: ex);
        }
    }

    private static Result VerifyStructuralConsistency(MdmaManifestDto manifest, MdmaChecksumDto checksum, string mdmaFilePath)
    {
        var manifestIndices = manifest.Chunks.Select(c => c.Index).OrderBy(i => i).ToList();
        var checksumIndices = checksum.ChunkHashes.Keys.Select(k => int.Parse(k)).OrderBy(i => i).ToList();

        if (!manifestIndices.SequenceEqual(checksumIndices))
        {
            return new MdmaError(
                MdmaErrorCode.MdmaManifestMalformed,
                "manifest.json's chunk list and checksum.sha256's chunk list do not match.",
                Details: mdmaFilePath);
        }

        return Result.Ok();
    }

    private static Result VerifyAllChunkHashes(ZipArchive zip, MdmaManifestDto manifest, MdmaChecksumDto checksum, string mdmaFilePath)
    {
        foreach (var chunk in manifest.Chunks)
        {
            var entryName = $"data/chunk_{chunk.Index}.bin";
            var entry = zip.GetEntry(entryName);
            if (entry is null)
            {
                return new MdmaError(
                    MdmaErrorCode.MdmaManifestMalformed,
                    $"Chunk {chunk.Index} is listed in the manifest but its data file is missing from the package.",
                    Details: mdmaFilePath);
            }

            var expectedHash = checksum.ChunkHashes[chunk.Index.ToString()];

            string actualHash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = entry.Open())
            {
                actualHash = Convert.ToHexString(sha.ComputeHash(stream));
            }

            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return new MdmaError(
                    MdmaErrorCode.MdmaChecksumMismatch,
                    $"Chunk {chunk.Index} failed checksum verification. The package may be corrupt.",
                    Details: mdmaFilePath);
            }
        }

        return Result.Ok();
    }

    private static Result VerifyManifestHash(MdmaChecksumDto checksum, string mdmaFilePath)
    {
        var recomputed = MdmaChecksumHelper.ComputeManifestHash(
            checksum.ChunkHashes.Select(kv => new KeyValuePair<int, string>(int.Parse(kv.Key), kv.Value)));

        if (!string.Equals(recomputed, checksum.ManifestHash, StringComparison.OrdinalIgnoreCase))
        {
            return new MdmaError(
                MdmaErrorCode.MdmaChecksumMismatch,
                "The package's overall manifest hash does not match its recorded chunk hashes. The checksum file may be corrupt or truncated.",
                Details: mdmaFilePath);
        }

        return Result.Ok();
    }

    private static Result<MdmaPackage> ExtractChunks(ZipArchive zip, MdmaManifestDto manifest, WorkingRoot workingRoot, string mdmaFilePath)
    {
        var stagingDir = Path.Combine(workingRoot.Path, StagingSubfolder, $"extracted-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDir);
        }
        catch (Exception ex)
        {
            return new MdmaError(MdmaErrorCode.Unknown, "Could not create staging directory for chunk extraction.", Details: stagingDir, Inner: ex);
        }

        var chunkFilePaths = new Dictionary<int, string>();
        try
        {
            foreach (var chunk in manifest.Chunks)
            {
                var entry = zip.GetEntry($"data/chunk_{chunk.Index}.bin")!; // presence already verified
                var destPath = Path.Combine(stagingDir, $"chunk_{chunk.Index}.bin");
                entry.ExtractToFile(destPath, overwrite: true);
                chunkFilePaths[chunk.Index] = destPath;
            }
        }
        catch (Exception ex)
        {
            TryDeleteDirectoryBestEffort(stagingDir);
            return new MdmaError(MdmaErrorCode.Unknown, "Failed to extract chunk data from the .mdma package.", Details: mdmaFilePath, Inner: ex);
        }

        var domainManifest = new MdmaManifest(
            MdmaVersion: manifest.MdmaVersion,
            Origin: Enum.Parse<TargetApp>(manifest.Origin),
            Url: manifest.Task.Url,
            Filename: manifest.Task.Filename,
            TotalBytes: manifest.Task.TotalSize,
            MimeType: manifest.Task.MimeType,
            Headers: manifest.Task.Headers.Select(h => new KeyValuePair<string, string>(h.Name, h.Value)).ToList(),
            CreatedEpochMillis: manifest.Task.Created,
            Chunks: manifest.Chunks.Select(c => new ChunkRange(c.Index, c.StartByte, c.EndByte, c.DownloadedBytes)).ToList());

        return Result<MdmaPackage>.Ok(new MdmaPackage(domainManifest, chunkFilePaths, mdmaFilePath));
    }

    private static void TryDeleteDirectoryBestEffort(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup of a failed extraction attempt */ }
    }
}
