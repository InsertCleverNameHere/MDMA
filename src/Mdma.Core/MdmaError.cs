namespace Mdma.Core;

/// <summary>
/// Every distinguishable failure mode Core can produce. Deliberately granular —
/// CLI maps these to exit codes 1:1, GUI maps these to specific dialogs/copy.
/// Do not collapse similar-looking cases into one value; the whole point of this
/// enum is that "insufficient space" and "path unwritable" need different user-facing text.
/// </summary>
public enum MdmaErrorCode
{
    // Discovery / validation
    TargetAppNotFound,
    ManualPathInvalid,
    TargetAppProcessRunning,

    // Working directory / disk
    WorkingDirectoryUnwritable,
    InsufficientDiskSpaceSource,
    InsufficientDiskSpaceDestination,
    WorkingDirectoryPathConflict,

    // .mdma package
    MdmaFileNotFound,
    MdmaChecksumMismatch,
    MdmaVersionUnsupported,
    MdmaManifestMalformed,

    // Safety-critical operation steps
    BackupFailed,
    AtomicWriteFailed,
    RevertFailed,
    RevertTargetNotFound,

    // Injector / scan
    InjectionFailed,
    ScanFailed,

    // Fallback
    Unknown,
}

/// <summary>
/// Structured failure info. Every field here should be enough, on its own,
/// for a user (or us, reading a bug report) to understand what happened
/// without needing a stack trace.
/// </summary>
public sealed record MdmaError(
    MdmaErrorCode Code,
    string Message,
    string? Details = null,
    string? SuggestedAction = null,
    Exception? Inner = null)
{
    public override string ToString() =>
        Details is null ? $"[{Code}] {Message}" : $"[{Code}] {Message} — {Details}";
}

/// <summary>
/// Result type used across all of Core. Exceptions are reserved for genuinely
/// unexpected/programmer-error conditions (null args, broken invariants) —
/// every *expected* failure (bad path, no space, wrong process running, etc.)
/// comes back as a typed MdmaError instead of being thrown.
/// </summary>
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public MdmaError? Error { get; }

    private Result(bool ok, T? value, MdmaError? error)
    {
        IsSuccess = ok;
        Value = value;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(MdmaError error) => new(false, default, error);
    public static implicit operator Result<T>(MdmaError error) => Fail(error);

    /// <summary>Throws if called on a failed result — use only after checking IsSuccess.</summary>
    public T Unwrap() => IsSuccess
        ? Value!
        : throw new InvalidOperationException($"Attempted to unwrap a failed Result: {Error}");
}

/// <summary>Non-generic variant for operations with no return payload (e.g. Revert, Cleanup).</summary>
public readonly struct Result
{
    public bool IsSuccess { get; }
    public MdmaError? Error { get; }

    private Result(bool ok, MdmaError? error)
    {
        IsSuccess = ok;
        Error = error;
    }

    public static Result Ok() => new(true, null);
    public static Result Fail(MdmaError error) => new(false, error);
    public static implicit operator Result(MdmaError error) => Fail(error);
}
