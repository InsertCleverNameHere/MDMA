using Mdma.Core;

namespace Mdma.Cli;

public static class ExitCodes
{
    public const int Success = 0;
    public const int TargetAppProcessRunning = 1;
    public const int TargetAppNotFoundOrPathInvalid = 2;
    public const int DiskOrWorkingDirError = 3;
    public const int PackageOrChecksumError = 4;
    public const int SafetyOrBackupError = 5;
    public const int OperationFailed = 6;
    public const int UnknownError = 99;

    public static int Map(MdmaErrorCode code) =>
        code switch
        {
            MdmaErrorCode.TargetAppProcessRunning => TargetAppProcessRunning,

            MdmaErrorCode.TargetAppNotFound or MdmaErrorCode.ManualPathInvalid =>
                TargetAppNotFoundOrPathInvalid,

            MdmaErrorCode.WorkingDirectoryUnwritable
            or MdmaErrorCode.InsufficientDiskSpaceSource
            or MdmaErrorCode.InsufficientDiskSpaceDestination
            or MdmaErrorCode.WorkingDirectoryPathConflict => DiskOrWorkingDirError,

            MdmaErrorCode.MdmaFileNotFound
            or MdmaErrorCode.MdmaChecksumMismatch
            or MdmaErrorCode.MdmaVersionUnsupported
            or MdmaErrorCode.MdmaManifestMalformed => PackageOrChecksumError,

            MdmaErrorCode.BackupFailed
            or MdmaErrorCode.AtomicWriteFailed
            or MdmaErrorCode.RevertFailed
            or MdmaErrorCode.RevertTargetNotFound => SafetyOrBackupError,

            MdmaErrorCode.ExportFailed
            or MdmaErrorCode.InjectionFailed
            or MdmaErrorCode.ScanFailed => OperationFailed,

            _ => UnknownError,
        };
}
