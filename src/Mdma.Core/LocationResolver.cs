namespace Mdma.Core;

public interface ILocationResolver
{
    Result<TargetAppLocation> ResolveLocation(
        TargetApp app,
        string? manualPathOverride = null,
        string? metadataDirOverride = null,
        string? tempDirOverride = null,
        string? downloadDirOverride = null
    );
}

public sealed class LocationResolver : ILocationResolver
{
    private readonly IReadOnlyDictionary<TargetApp, IDownloadManagerLocator> _locators;

    public LocationResolver(IReadOnlyDictionary<TargetApp, IDownloadManagerLocator> locators)
    {
        _locators = locators;
    }

    public LocationResolver(
        IRegistryAccessor registry,
        string? appDataDirectory = null,
        string? localAppDataDirectory = null
    )
    {
        _locators = new Dictionary<TargetApp, IDownloadManagerLocator>
        {
            [TargetApp.NDM] = new NdmLocator(registry, appDataDirectory),
            [TargetApp.JD2] = new Jd2Locator(localAppDataDirectory),
        };
    }

    public Result<TargetAppLocation> ResolveLocation(
        TargetApp app,
        string? manualPathOverride = null,
        string? metadataDirOverride = null,
        string? tempDirOverride = null,
        string? downloadDirOverride = null
    )
    {
        if (!_locators.TryGetValue(app, out var locator))
        {
            return new MdmaError(
                MdmaErrorCode.TargetAppNotFound,
                $"No locator is registered for target app {app}.",
                Details: app.ToString()
            );
        }

        Result<TargetAppLocation> locationResult = !string.IsNullOrWhiteSpace(manualPathOverride)
            ? locator.ValidateManualPath(manualPathOverride)
            : locator.TryAutoDetect();

        if (!locationResult.IsSuccess)
        {
            return locationResult.Error!;
        }

        var location = locationResult.Value!;

        if (!string.IsNullOrWhiteSpace(metadataDirOverride))
        {
            location = location with { MetadataDir = metadataDirOverride };
        }

        if (!string.IsNullOrWhiteSpace(tempDirOverride))
        {
            location = location with { InstallOrConfigDir = tempDirOverride };
        }

        if (!string.IsNullOrWhiteSpace(downloadDirOverride))
        {
            location = location with { DownloadDirectory = downloadDirOverride };
        }

        return Result<TargetAppLocation>.Ok(location);
    }
}
