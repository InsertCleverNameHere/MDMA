using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class MdmaLoaderTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-loader-test-" + Guid.NewGuid());
        var workRootPath = Path.Combine(_testDir, "workroot");
        Directory.CreateDirectory(workRootPath);
        _workingRoot = new WorkingRoot(workRootPath, true, false);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private static byte[] Bytes(params byte[] b) => b;

    [Test]
    public void Load_Succeeds_For_Valid_Package_And_Stages_Chunk_Files()
    {
        var chunkBytes = Bytes(1, 2, 3, 4, 5);
        var mdmaPath = new MdmaFixtureBuilder()
            .WithOrigin("NDM").WithTotalBytes(5)
            .WithChunk(0, 0, 4, chunkBytes)
            .BuildValid(Path.Combine(_testDir, "valid.mdma"));

        var loader = new MdmaLoader();
        var result = loader.Load(mdmaPath, _workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        var package = result.Value!;
        Assert.That(package.Manifest.Origin, Is.EqualTo(TargetApp.NDM));
        Assert.That(File.Exists(package.ChunkFilePaths[0]), Is.True);
        Assert.That(File.ReadAllBytes(package.ChunkFilePaths[0]), Is.EqualTo(chunkBytes));
    }

    [Test]
    public void Load_Succeeds_For_Multiple_Chunks_In_Correct_Order()
    {
        var mdmaPath = new MdmaFixtureBuilder()
            .WithChunk(0, 0, 1, Bytes(1, 1))
            .WithChunk(1, 2, 3, Bytes(2, 2))
            .WithChunk(2, 4, 5, Bytes(3, 3))
            .BuildValid(Path.Combine(_testDir, "multi.mdma"));

        var loader = new MdmaLoader();
        var result = loader.Load(mdmaPath, _workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ChunkFilePaths, Has.Count.EqualTo(3));
        Assert.That(File.ReadAllBytes(result.Value.ChunkFilePaths[1]), Is.EqualTo(Bytes(2, 2)));
    }

    [Test]
    public void Load_Fails_With_ChecksumMismatch_When_A_Chunk_Is_Corrupt()
    {
        var mdmaPath = new MdmaFixtureBuilder()
            .WithChunk(0, 0, 4, Bytes(1, 2, 3, 4, 5))
            .BuildWithCorruptChunk(Path.Combine(_testDir, "corrupt.mdma"), chunkIndex: 0);

        var loader = new MdmaLoader();
        var result = loader.Load(mdmaPath, _workingRoot);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.MdmaChecksumMismatch));
    }

    [Test]
    public void Load_Does_Not_Stage_Any_Files_When_A_Chunk_Is_Corrupt()
    {
        var mdmaPath = new MdmaFixtureBuilder()
            .WithChunk(0, 0, 4, Bytes(1, 2, 3, 4, 5))
            .WithChunk(1, 5, 9, Bytes(6, 7, 8, 9, 10)) // this one is fine
            .BuildWithCorruptChunk(Path.Combine(_testDir, "corrupt.mdma"), chunkIndex: 0);

        var loader = new MdmaLoader();
        loader.Load(mdmaPath, _workingRoot);

        var stagingRoot = Path.Combine(_workingRoot.Path, ".mdma-tmp");
        if (Directory.Exists(stagingRoot))
        {
            // No extraction folder should have any files in it -- verification
            // happens before extraction starts, so nothing should be staged.
            var anyFiles = Directory.GetFiles(stagingRoot, "*", SearchOption.AllDirectories);
            Assert.That(anyFiles, Is.Empty);
        }
    }

    [Test]
    public void Load_Fails_With_ChecksumMismatch_When_ManifestHash_Is_Wrong()
    {
        var mdmaPath = new MdmaFixtureBuilder()
            .WithChunk(0, 0, 4, Bytes(1, 2, 3, 4, 5))
            .BuildWithBadManifestHash(Path.Combine(_testDir, "bad-manifest-hash.mdma"));

        var loader = new MdmaLoader();
        var result = loader.Load(mdmaPath, _workingRoot);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.MdmaChecksumMismatch));
    }

    [Test]
    public void Load_Fails_When_Manifest_Missing()
    {
        var mdmaPath = new MdmaFixtureBuilder()
            .WithChunk(0, 0, 4, Bytes(1, 2, 3, 4, 5))
            .BuildWithoutManifest(Path.Combine(_testDir, "no-manifest.mdma"));

        var loader = new MdmaLoader();
        var result = loader.Load(mdmaPath, _workingRoot);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.MdmaManifestMalformed));
    }

    [Test]
    public void Load_Fails_When_Checksum_File_Missing()
    {
        var mdmaPath = new MdmaFixtureBuilder()
            .WithChunk(0, 0, 4, Bytes(1, 2, 3, 4, 5))
            .BuildWithoutChecksum(Path.Combine(_testDir, "no-checksum.mdma"));

        var loader = new MdmaLoader();
        var result = loader.Load(mdmaPath, _workingRoot);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.MdmaManifestMalformed));
    }

    [Test]
    public void Load_Fails_When_Manifest_And_Checksum_Chunk_Lists_Disagree()
    {
        var mdmaPath = new MdmaFixtureBuilder()
            .WithChunk(0, 0, 4, Bytes(1, 2, 3, 4, 5))
            .WithChunk(1, 5, 9, Bytes(6, 7, 8, 9, 10))
            .BuildWithMismatchedChunkLists(Path.Combine(_testDir, "mismatched.mdma"), chunkIndexToDropFromChecksum: 1);

        var loader = new MdmaLoader();
        var result = loader.Load(mdmaPath, _workingRoot);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.MdmaManifestMalformed));
    }

    [Test]
    public void Load_Fails_When_File_Does_Not_Exist()
    {
        var loader = new MdmaLoader();
        var result = loader.Load(Path.Combine(_testDir, "nope.mdma"), _workingRoot);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.MdmaFileNotFound));
    }

    [Test]
    public void Load_Fails_When_File_Is_Not_A_Valid_Zip()
    {
        var badPath = Path.Combine(_testDir, "not-a-zip.mdma");
        File.WriteAllText(badPath, "definitely not a zip file");

        var loader = new MdmaLoader();
        var result = loader.Load(badPath, _workingRoot);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.MdmaManifestMalformed));
    }

    [Test]
    public void Load_Fails_With_VersionUnsupported_When_Manifest_Version_Too_New()
    {
        var mdmaPath = new MdmaFixtureBuilder()
            .WithVersion(MdmaChecksumHelper.CurrentMdmaVersion + 1)
            .WithChunk(0, 0, 4, Bytes(1, 2, 3, 4, 5))
            .BuildValid(Path.Combine(_testDir, "future-version.mdma"));

        var loader = new MdmaLoader();
        var result = loader.Load(mdmaPath, _workingRoot);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.MdmaVersionUnsupported));
    }

    [Test]
    public void Load_Stages_Files_Under_The_Working_Root_MdmaTmp_Folder()
    {
        var mdmaPath = new MdmaFixtureBuilder()
            .WithChunk(0, 0, 4, Bytes(1, 2, 3, 4, 5))
            .BuildValid(Path.Combine(_testDir, "valid.mdma"));

        var loader = new MdmaLoader();
        var result = loader.Load(mdmaPath, _workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        var stagedPath = result.Value!.ChunkFilePaths[0];
        Assert.That(stagedPath, Does.StartWith(Path.Combine(_workingRoot.Path, ".mdma-tmp")));
    }
}
