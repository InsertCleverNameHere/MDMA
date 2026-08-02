namespace Mdma.Core;

/// <summary>One file captured in a backup snapshot: where it came from, where
/// it lives inside the snapshot folder, and its hash at backup time (so
/// RevertManager can verify the snapshot hasn't been tampered with/corrupted
/// before restoring anything).</summary>
public sealed record BackupManifestEntry(
    string OriginalPath,
    string BackupRelativePath,
    string Sha256);

/// <summary>Full manifest for one backup snapshot, serialized as
/// manifest.json alongside the copied files inside the snapshot folder.</summary>
public sealed record BackupManifest(
    TargetApp Target,
    DateTimeOffset CreatedAt,
    IReadOnlyList<BackupManifestEntry> Entries);
