using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class FixtureSmokeTests
{
    [Test]
    public void NdmFixture_Builds_Without_Throwing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mdma-fixture-smoke-" + Guid.NewGuid());
        var builder = new NdmFixtureBuilder(dir)
            .WithTask(521, "poc_ndm_perfect.bin", "https://example.com/f.bin", 10_485_760,
                (0, 10_485_759, 2_097_152));
        builder.Build();

        Assert.That(File.Exists(builder.NeatDbPath), Is.True);
        Assert.That(File.Exists(Path.Combine(builder.TempDirectory, "521", "segments.bin")), Is.True);
        Assert.That(File.Exists(Path.Combine(builder.TempDirectory, "521", "seg.x0")), Is.True);

        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public void Jd2Fixture_Builds_Without_Throwing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mdma-fixture-smoke-" + Guid.NewGuid());
        var builder = new Jd2FixtureBuilder(dir)
            .WithLink("99", "00", "poc_test_file.bin", "https://speed.hetzner.de/100MB.bin",
                10_485_760, 2_097_152, 2_097_152);
        var zipPath = builder.Build();

        Assert.That(File.Exists(zipPath), Is.True);

        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public void MdmaFixture_Builds_Valid_And_Corrupt_Variants()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mdma-fixture-smoke-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);

        var chunkBytes = new byte[1024];
        new Random(1).NextBytes(chunkBytes);

        var valid = new MdmaFixtureBuilder().WithChunk(0, 0, 1023, chunkBytes)
            .BuildValid(Path.Combine(dir, "valid.mdma"));
        var badChunk = new MdmaFixtureBuilder().WithChunk(0, 0, 1023, chunkBytes)
            .BuildWithCorruptChunk(Path.Combine(dir, "bad-chunk.mdma"), chunkIndex: 0);
        var noManifest = new MdmaFixtureBuilder().WithChunk(0, 0, 1023, chunkBytes)
            .BuildWithoutManifest(Path.Combine(dir, "no-manifest.mdma"));

        Assert.That(File.Exists(valid), Is.True);
        Assert.That(File.Exists(badChunk), Is.True);
        Assert.That(File.Exists(noManifest), Is.True);

        Directory.Delete(dir, recursive: true);
    }
}
