namespace WPCL.Models;

public enum LoaderKind
{
    Vanilla,
    Fabric,
    NeoForge,
    Forge
}

public sealed class LauncherSettings
{
    public const string SnowLauncherClientId = "0dfbc3f7-b930-44a0-b419-3d31b7859659";

    public int SchemaVersion { get; set; } = 5;
    public string Username { get; set; } = "Player";
    public string OfflineUsername { get; set; } = "Player";
    public string Uuid { get; set; } = "";
    public bool UseMicrosoftAccount { get; set; }
    public string SelectedAccountId { get; set; } = "";
    public string GameDirectory { get; set; } = "";
    public string JavaPath { get; set; } = "java";
    public bool AutoMemory { get; set; } = true;
    public string MinimumMemory { get; set; } = "2G";
    public string MaximumMemory { get; set; } = "4G";
    public int DownloadConcurrency { get; set; } = 16;
    public bool IsolateVersions { get; set; } = true;
    public bool Pcl2Compatibility { get; set; } = true;
    public int BackgroundOpacity { get; set; } = 30;
    public string? LastVersion { get; set; }
}

public sealed record VersionManifest(IReadOnlyList<RemoteVersion> Versions, string LatestRelease, string LatestSnapshot);

public sealed record RemoteVersion(string Id, string Type, string Url, DateTimeOffset ReleaseTime);

public sealed record InstalledVersion(
    string Id,
    long Size,
    DateTime LastWriteTime,
    bool Isolated,
    LoaderKind Loader,
    string? LoaderVersion,
    string? BaseVersion,
    bool PclCompatible,
    bool IsLaunchable,
    string? LaunchIssue);

public sealed record LoaderVersion(string Version, string DisplayName, bool Stable);

public sealed record LoaderCatalog(
    LoaderKind Loader,
    string GameVersion,
    IReadOnlyList<LoaderVersion> Versions);

public sealed record InstallRequest(
    string VersionId,
    LoaderKind Loader = LoaderKind.Vanilla,
    string? LoaderVersion = null,
    bool LaunchAfterInstall = false);

public sealed record LaunchRequest(string VersionId);

public sealed record AccountSelectionRequest(string AccountId);

public sealed record OfflineAccountRequest(string Username);

public sealed record AccountSummary(
    string Id,
    string Username,
    string Uuid,
    string? AvatarSvg,
    bool Selected);

public sealed record AccountState(
    string Mode,
    bool SignedIn,
    string Username,
    string Uuid,
    string? AvatarSvg,
    string SelectedAccountId,
    string OfflineUsername,
    IReadOnlyList<AccountSummary> Accounts);

public sealed record GameSessionInfo(
    string Username,
    string Uuid,
    string AccessToken,
    string Xuid,
    string ClientId);

public sealed record LoaderCatalogRequest(string VersionId, LoaderKind Loader);

public sealed record DownloadItem(string Url, string Destination, string Sha1, long Size, string Label);

public sealed record ProgressUpdate(
    string Operation,
    string Stage,
    int Completed,
    int Total,
    long BytesDownloaded,
    long TotalBytes,
    string? CurrentItem = null);

public sealed record BootstrapData(
    LauncherSettings Settings,
    IReadOnlyList<InstalledVersion> InstalledVersions,
    bool GameRunning,
    string AppVersion,
    string ConfigPath,
    AccountState Account);
