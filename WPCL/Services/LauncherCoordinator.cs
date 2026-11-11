using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WPCL.Bridge;
using WPCL.Models;

namespace WPCL.Services;

public sealed class LauncherCoordinator : IDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly MinecraftApi _api = new();
    private readonly MinecraftLauncher _launcher = new();
    private readonly AccountService _accounts = new();
    private readonly SemaphoreSlim _installLock = new(1, 1);
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private LauncherSettings _settings;
    private CancellationTokenSource? _installCancellation;

    public LauncherCoordinator()
    {
        _settings = _settingsStore.Load();
        _launcher.LogReceived += (level, message) => Raise("log", new { level, message, time = DateTime.Now });
        _launcher.GameExited += exitCode => Raise("gameState", new { running = false, exitCode });
    }

    public event Action<BridgeEvent>? EventRaised;

    public async Task<object?> HandleAsync(BridgeRequest request)
    {
        return request.Action switch
        {
            "bootstrap" => await GetBootstrapDataAsync(),
            "refreshManifest" => await RefreshManifestAsync(),
            "getLoaderVersions" => await GetLoaderVersionsAsync(ReadPayload<LoaderCatalogRequest>(request)),
            "refreshInstalled" => MinecraftInstaller.GetInstalledVersions(_settings.GameDirectory),
            "saveSettings" => SaveSettings(ReadPayload<LauncherSettings>(request)),
            "installVersion" => await InstallAsync(ReadPayload<InstallRequest>(request)),
            "cancelInstall" => CancelInstall(),
            "launchVersion" => await LaunchAsync(ReadPayload<LaunchRequest>(request)),
            "loginMicrosoft" => await LoginMicrosoftAsync(),
            "selectAccount" => await SelectAccountAsync(ReadPayload<AccountSelectionRequest>(request)),
            "useOfflineAccount" => await UseOfflineAccountAsync(ReadPayload<OfflineAccountRequest>(request)),
            "logoutAccount" => await LogoutAccountAsync(ReadPayload<AccountSelectionRequest>(request)),
            "stopGame" => StopGame(),
            "deleteVersion" => DeleteVersion(ReadPayload<LaunchRequest>(request).VersionId),
            "openGameDirectory" => OpenDirectory(_settings.GameDirectory),
            "openVersionDirectory" => OpenDirectory(Path.Combine(_settings.GameDirectory, "versions", ReadPayload<LaunchRequest>(request).VersionId)),
            _ => throw new InvalidOperationException($"不支持的操作：{request.Action}")
        };
    }

    private async Task<BootstrapData> GetBootstrapDataAsync()
    {
        var account = await _accounts.InitializeAsync(_settings, CancellationToken.None);
        _settingsStore.Save(_settings);
        return new BootstrapData(
            _settings,
            MinecraftInstaller.GetInstalledVersions(_settings.GameDirectory),
            _launcher.IsRunning,
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0",
            _settingsStore.ConfigPath,
            account);
    }

    private async Task<VersionManifest> RefreshManifestAsync()
    {
        RaiseLog("info", "正在刷新官方版本列表");
        var manifest = await _api.GetManifestAsync(CancellationToken.None);
        RaiseLog("success", $"已获取 {manifest.Versions.Count} 个官方版本");
        return manifest;
    }

    private object SaveSettings(LauncherSettings settings)
    {
        _settingsStore.Save(settings);
        _settings = settings;
        RaiseLog("success", "启动器设置已保存");
        return new { settings = _settings, installedVersions = MinecraftInstaller.GetInstalledVersions(_settings.GameDirectory) };
    }

    private async Task<LoaderCatalog> GetLoaderVersionsAsync(LoaderCatalogRequest request)
    {
        if (request.Loader == LoaderKind.Vanilla)
        {
            return new LoaderCatalog(request.Loader, request.VersionId, Array.Empty<LoaderVersion>());
        }
        RaiseLog("info", $"正在获取 {request.Loader} 可用版本");
        return await new LoaderCatalogService(_api)
            .GetVersionsAsync(request.VersionId, request.Loader, CancellationToken.None);
    }

    private async Task<object> InstallAsync(InstallRequest request)
    {
        if (!await _installLock.WaitAsync(0))
        {
            throw new InvalidOperationException("已有安装任务正在进行。");
        }
        _installCancellation = new CancellationTokenSource();
        try
        {
            Raise("operationState", new { operation = "install", active = true, versionId = request.VersionId });
            var installer = new MinecraftInstaller(_api);
            var installedVersionId = await installer.InstallAsync(
                request,
                _settings,
                progress => Raise("progress", progress),
                RaiseLog,
                _installCancellation.Token);
            var installed = MinecraftInstaller.GetInstalledVersions(_settings.GameDirectory);
            Raise("installedChanged", installed);
            if (request.LaunchAfterInstall)
            {
                await LaunchVersionAsync(installedVersionId);
            }
            return new { installedVersionId, installedVersions = installed };
        }
        catch (OperationCanceledException)
        {
            RaiseLog("warning", "安装任务已取消，可稍后继续，已校验的文件会被保留");
            throw new InvalidOperationException("安装任务已取消。");
        }
        finally
        {
            Raise("operationState", new { operation = "install", active = false, versionId = request.VersionId });
            _installCancellation.Dispose();
            _installCancellation = null;
            _installLock.Release();
        }
    }

    private object CancelInstall()
    {
        _installCancellation?.Cancel();
        return new { cancelled = _installCancellation is not null };
    }

    private async Task<object> LaunchAsync(LaunchRequest request)
    {
        var processId = await LaunchVersionAsync(request.VersionId);
        return new { processId };
    }

    private async Task<int> LaunchVersionAsync(string versionId)
    {
        if (!await _launchLock.WaitAsync(0))
        {
            throw new InvalidOperationException("启动准备正在进行，请稍候。");
        }
        try
        {
            Raise("operationState", new { operation = "repair", active = true, versionId });
            var repair = new VersionRepairService(_api.Client);
            await repair.RepairAsync(
                versionId,
                _settings,
                progress => Raise("progress", progress),
                RaiseLog,
                CancellationToken.None);
            var session = await _accounts.GetLaunchSessionAsync(_settings, CancellationToken.None);
            var processId = await _launcher.LaunchAsync(versionId, _settings, session, CancellationToken.None);
            _settings.LastVersion = versionId;
            _settingsStore.Save(_settings);
            Raise("gameState", new { running = true, processId, versionId });
            return processId;
        }
        finally
        {
            Raise("operationState", new { operation = "repair", active = false, versionId });
            _launchLock.Release();
        }
    }

    private async Task<AccountState> LoginMicrosoftAsync()
    {
        var state = await _accounts.LoginAsync(_settings, CancellationToken.None);
        return SaveAccountState(state);
    }

    private async Task<AccountState> SelectAccountAsync(AccountSelectionRequest request)
    {
        var state = await _accounts.SelectAsync(_settings, request.AccountId, CancellationToken.None);
        return SaveAccountState(state);
    }

    private async Task<AccountState> UseOfflineAccountAsync(OfflineAccountRequest request)
    {
        var state = await _accounts.UseOfflineAsync(_settings, request.Username, CancellationToken.None);
        return SaveAccountState(state);
    }

    private async Task<AccountState> LogoutAccountAsync(AccountSelectionRequest request)
    {
        var state = await _accounts.LogoutAsync(_settings, request.AccountId, CancellationToken.None);
        return SaveAccountState(state);
    }

    private AccountState SaveAccountState(AccountState state)
    {
        _settingsStore.Save(_settings);
        Raise("accountChanged", state);
        return state;
    }

    private object StopGame()
    {
        _launcher.Stop();
        return new { stopped = true };
    }

    private object DeleteVersion(string versionId)
    {
        if (_launcher.IsRunning && string.Equals(_settings.LastVersion, versionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("游戏运行时不能删除当前版本。");
        }
        MinecraftInstaller.DeleteVersion(_settings.GameDirectory, versionId);
        var installed = MinecraftInstaller.GetInstalledVersions(_settings.GameDirectory);
        Raise("installedChanged", installed);
        RaiseLog("success", $"已删除版本 {versionId}，共享资源和依赖库已保留");
        return new { installedVersions = installed };
    }

    private static object OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        return new { opened = path };
    }

    private T ReadPayload<T>(BridgeRequest request)
    {
        if (request.Payload is not { } payload)
        {
            throw new InvalidOperationException("请求缺少参数。");
        }
        return payload.Deserialize<T>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("请求参数无效。");
    }

    private void RaiseLog(string level, string message) =>
        Raise("log", new { level, message, time = DateTime.Now });

    private void Raise(string name, object? data) =>
        EventRaised?.Invoke(new BridgeEvent("event", name, data));

    public void Dispose()
    {
        _installCancellation?.Cancel();
        _installCancellation?.Dispose();
        _launcher.Dispose();
        _accounts.Dispose();
        _api.Dispose();
        _installLock.Dispose();
        _launchLock.Dispose();
    }
}
