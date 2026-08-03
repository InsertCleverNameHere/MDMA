using System.IO.Compression;
using System.Text.Json;

namespace Mdma.Core.Tests;

public class MdmaPackageWriterTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-packagewriter-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private string WriteSourceChunk(string name, byte[] bytes)
    {
        var path = Path.Combine(_testDir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Test]
    public void WritePackage_Produces_A_Valid_Zip_With_Expected_Entries()
    {
        var chunk0 = WriteSourceChunk("c0.bin", new byte[] { 1, 2, 3, 4 });
        var chunk1 = WriteSourceChunk("c1.bin", new byte[] { 5, 6 });

        var writer = new MdmaPackageWriter();
        var destPath = Path.Combine(_testDir, "out.mdma");

        var result = writer.WritePackage(
            TargetApp.NDM, "https://example.com/f.bin", "f.bin", 6, null,
            Array.Empty<KeyValuePair<string, string>>(), 1785268000000L,
            new[]
            {
                new MdmaChunkSource(0, 0, 3, chunk0),
                new MdmaChunkSource(1, 4, 5, chunk1),
            },
            destPath);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.Exists(destPath), Is.True);

        using var zip = ZipFile.OpenRead(destPath);
        Assert.That(zip.GetEntry("manifest.json"), Is.Not.Null);
        Assert.That(zip.GetEntry("checksum.sha256"), Is.Not.Null);
        Assert.That(zip.GetEntry("data/chunk_0.bin"), Is.Not.Null);
        Assert.That(zip.GetEntry("data/chunk_1.bin"), Is.Not.Null);
    }

    [Test]
    public void WritePackage_DownloadedBytes_Reflects_Actual_File_Length_Not_Caller_Claim()
    {
        // Even though StartByte/EndByte imply a 100-byte range, the real file
        // is only 4 bytes (a partial download) -- manifest must record the
        // TRUE file length, not derive it from the byte range.
        var chunk0 = WriteSourceChunk("c0.bin", new byte[] { 1, 2, 3, 4 });

        var writer = new MdmaPackageWriter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        writer.WritePackage(
            TargetApp.NDM, "https://example.com/f.bin", "f.bin", 1000, null,
            Array.Empty<KeyValuePair<string, string>>(), 0,
            new[] { new MdmaChunkSource(0, 0, 99, chunk0) },
            destPath);

        using var zip = ZipFile.OpenRead(destPath);
        var manifestEntry = zip.GetEntry("manifest.json")!;
        using var reader = new StreamReader(manifestEntry.Open());
        var manifest = JsonSerializer.Deserialize<MdmaManifestDto>(reader.ReadToEnd())!;

        Assert.That(manifest.Chunks[0].DownloadedBytes, Is.EqualTo(4));
    }

    [Test]
    public void WritePackage_Fails_Cleanly_When_Chunk_Source_File_Missing()
    {
        var writer = new MdmaPackageWriter();
        var destPath = Path.Combine(_testDir, "out.mdma");

        var result = writer.WritePackage(
            TargetApp.NDM, "https://example.com/f.bin", "f.bin", 100, null,
            Array.Empty<KeyValuePair<string, string>>(), 0,
            new[] { new MdmaChunkSource(0, 0, 99, Path.Combine(_testDir, "does-not-exist.bin")) },
            destPath);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(File.Exists(destPath), Is.False);
    }

    [Test]
    public void WritePackage_Roundtrips_Correctly_Through_MdmaLoader()
    {
        var chunk0Bytes = new byte[] { 10, 20, 30 };
        var chunk0 = WriteSourceChunk("c0.bin", chunk0Bytes);

        var writer = new MdmaPackageWriter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        writer.WritePackage(
            TargetApp.JD2, "https://example.com/f.bin", "f.bin", 3, "application/octet-stream",
            new[] { new KeyValuePair<string, string>("Referer", "https://example.com") }, 123456,
            new[] { new MdmaChunkSource(0, 0, 2, chunk0) },
            destPath);

        var loader = new MdmaLoader();
        var workRootPath = Path.Combine(_testDir, "workroot");
        Directory.CreateDirectory(workRootPath);
        var workingRoot = new WorkingRoot(workRootPath, true, false);

        var loadResult = loader.Load(destPath, workingRoot);

        Assert.That(loadResult.IsSuccess, Is.True);
        var package = loadResult.Value!;
        Assert.That(package.Manifest.Origin, Is.EqualTo(TargetApp.JD2));
        Assert.That(package.Manifest.Url, Is.EqualTo("https://example.com/f.bin"));
        Assert.That(package.Manifest.Headers.Single().Key, Is.EqualTo("Referer"));
        Assert.That(File.ReadAllBytes(package.ChunkFilePaths[0]), Is.EqualTo(chunk0Bytes));
    }
}
