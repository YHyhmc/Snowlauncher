using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using System.Net;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Msal;
using WPCL.Models;

namespace WPCL.Services;

public sealed class AccountService : IDisposable
{
    private readonly HttpClient _client = new();
    private readonly PlayerHeadService _heads;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataDirectory;
    private readonly string _accountsPath;
    private JELoginHandler? _handler;
    private GameSessionInfo? _currentSession;

    public AccountService()
    {
        _dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WPCL");
        Directory.CreateDirectory(_dataDirectory);
        _accountsPath = Path.Combine(_dataDirectory, "microsoft-accounts.json");
        _heads = new PlayerHeadService(_client);
    }

    public async Task<AccountState> InitializeAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (settings.UseMicrosoftAccount &&
                !string.IsNullOrWhiteSpace(settings.SelectedAccountId))
            {
                try
                {
                    await AuthenticateSelectedAsync(settings, cancellationToken);
                }
                catch
                {
                    _currentSession = null;
                }
            }
            return await CreateStateAsync(settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountState> LoginAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var handler = await GetHandlerAsync(cancellationToken);
            var newAccount = handler.AccountManager.NewAccount();
            var session = await handler.AuthenticateInteractively(newAccount, cancellationToken);
            var sessionUuid = session.UUID ?? throw new InvalidOperationException("Microsoft 登录未返回玩家 UUID。");
            var account = !string.IsNullOrWhiteSpace(newAccount.Identifier)
                ? newAccount
                : FindAccountByUuid(handler.AccountManager.GetAccounts(), sessionUuid)
                    ?? throw new InvalidOperationException("登录成功，但未能保存 Microsoft 账户。");
            handler.AccountManager.SaveAccounts();
            ApplySession(settings, account.Identifier ?? throw new InvalidOperationException("Microsoft 账户标识无效。"), session);
            return await CreateStateAsync(settings, cancellationToken);
        }
        catch (Exception exception) when (ContainsLoginError(exception, "unauthorized_client"))
        {
            throw new InvalidOperationException(
                "Microsoft 登录应用未对个人账户启用。请在 Azure 门户将受支持的账户类型改为“个人 Microsoft 账户”，添加 http://localhost 公共客户端重定向 URI，并启用公共客户端流。",
                exception);
        }
        catch (Exception exception) when (
            ContainsLoginError(exception, "AADSTS50011") ||
            ContainsLoginError(exception, "redirect_uri"))
        {
            throw new InvalidOperationException(
                "Microsoft 登录回调地址不匹配。请在 Azure 门户的“移动和桌面应用程序”中添加 http://localhost。",
                exception);
        }
        catch (Exception exception) when (IsMinecraftForbidden(exception))
        {
            throw new InvalidOperationException(
                "Microsoft 授权已完成，但 Snow launcher 尚未获得 Minecraft 登录权限（403）。账号未保存，请先将当前 Client ID 加入 Minecraft API 允许列表。",
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountState> SelectAsync(
        LauncherSettings settings,
        string accountId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var handler = await GetHandlerAsync(cancellationToken);
            var account = handler.AccountManager.GetAccounts().GetAccount(accountId)
                ?? throw new InvalidOperationException("所选 Microsoft 账户不存在。");
            var session = await handler.AuthenticateSilently(account, cancellationToken);
            handler.AccountManager.SaveAccounts();
            ApplySession(settings, account.Identifier ?? throw new InvalidOperationException("Microsoft 账户标识无效。"), session);
            return await CreateStateAsync(settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountState> UseOfflineAsync(
        LauncherSettings settings,
        string username,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            settings.OfflineUsername = username.Trim();
            settings.UseMicrosoftAccount = false;
            settings.Username = settings.OfflineUsername;
            settings.Uuid = SettingsStore.CreateOfflineUuid(settings.OfflineUsername);
            _currentSession = null;
            SettingsStore.Normalize(settings);
            return await CreateStateAsync(settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountState> LogoutAsync(
        LauncherSettings settings,
        string accountId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var handler = await GetHandlerAsync(cancellationToken);
            var account = handler.AccountManager.GetAccounts().GetAccount(accountId);
            if (account is not null)
            {
                await handler.Signout(account, cancellationToken);
                handler.AccountManager.SaveAccounts();
            }
            if (string.Equals(settings.SelectedAccountId, accountId, StringComparison.Ordinal))
            {
                settings.UseMicrosoftAccount = false;
                settings.SelectedAccountId = "";
                settings.Username = settings.OfflineUsername;
                settings.Uuid = SettingsStore.CreateOfflineUuid(settings.OfflineUsername);
                _currentSession = null;
            }
            return await CreateStateAsync(settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GameSessionInfo?> GetLaunchSessionAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.UseMicrosoftAccount)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_currentSession is null ||
                !string.Equals(_currentSession.Uuid, settings.Uuid, StringComparison.OrdinalIgnoreCase))
            {
                await AuthenticateSelectedAsync(settings, cancellationToken);
            }
            return _currentSession;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task AuthenticateSelectedAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        var handler = await GetHandlerAsync(cancellationToken);
        var account = handler.AccountManager.GetAccounts().GetAccount(settings.SelectedAccountId)
            ?? throw new InvalidOperationException("已选择的 Microsoft 账户不存在，请重新登录。");
        var session = await handler.AuthenticateSilently(account, cancellationToken);
        handler.AccountManager.SaveAccounts();
        ApplySession(settings, account.Identifier ?? throw new InvalidOperationException("Microsoft 账户标识无效。"), session);
    }

    private async Task<JELoginHandler> GetHandlerAsync(CancellationToken cancellationToken)
    {
        if (_handler is not null)
        {
            return _handler;
        }

        var cache = new MsalCacheSettings
        {
            CacheDir = _dataDirectory,
            CacheFileName = "msal.cache"
        };
        var application = await MsalClientHelper.BuildApplicationWithCache(LauncherSettings.SnowLauncherClientId, cache);
        cancellationToken.ThrowIfCancellationRequested();
        _handler = new JELoginHandlerBuilder()
            .WithOAuthProvider(new MultiAccountMsalProvider(application))
            .WithAccountManager(_accountsPath)
            .Build();
        return _handler;
    }

    private async Task<AccountState> CreateStateAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        var accounts = new List<AccountSummary>();
        var handler = await GetHandlerAsync(cancellationToken);
        foreach (var account in handler.AccountManager.GetAccounts().OfType<JEGameAccount>())
        {
            try
            {
                var profile = account.Profile;
                var identifier = account.Identifier;
                if (profile is null || string.IsNullOrWhiteSpace(identifier) ||
                    string.IsNullOrWhiteSpace(profile.Username) || string.IsNullOrWhiteSpace(profile.UUID))
                {
                    continue;
                }
                var skin = profile.Skins?.FirstOrDefault(item =>
                    string.Equals(item.State, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    ?? profile.Skins?.FirstOrDefault();
                string? avatar = null;
                try
                {
                    avatar = await _heads.CreateSvgDataUriAsync(skin?.Url, cancellationToken);
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException)
                {
                    // Account switching must remain available when the skin service is offline.
                }
                accounts.Add(new AccountSummary(
                    identifier,
                    profile.Username,
                    profile.UUID,
                    avatar,
                    settings.UseMicrosoftAccount && string.Equals(settings.SelectedAccountId, identifier, StringComparison.Ordinal)));
            }
            catch
            {
                // Ignore incomplete cached accounts. A new login will recreate them.
            }
        }

        var selected = accounts.FirstOrDefault(item => item.Selected);
        return new AccountState(
            selected is null ? "offline" : "microsoft",
            selected is not null,
            selected?.Username ?? settings.OfflineUsername,
            selected?.Uuid ?? SettingsStore.CreateOfflineUuid(settings.OfflineUsername),
            selected?.AvatarSvg,
            settings.SelectedAccountId,
            settings.OfflineUsername,
            accounts);
    }

    private static IXboxGameAccount? FindAccountByUuid(XboxGameAccountCollection accounts, string uuid) =>
        accounts.FirstOrDefault(account => account is JEGameAccount game &&
            string.Equals(NormalizeUuid(game.Profile?.UUID), NormalizeUuid(uuid), StringComparison.OrdinalIgnoreCase));

    private static string NormalizeUuid(string? uuid) =>
        (uuid ?? "").Replace("-", "", StringComparison.Ordinal);

    private static bool ContainsLoginError(Exception exception, string value)
    {
        foreach (var current in EnumerateExceptions(exception))
        {
            if (current.Message.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsJeStatusCode(Exception exception, int statusCode) =>
        EnumerateExceptions(exception)
            .OfType<JEAuthException>()
            .Any(item => item.StatusCode == statusCode);

    private static bool IsMinecraftForbidden(Exception exception) =>
        ContainsJeStatusCode(exception, 403) ||
        EnumerateExceptions(exception).Any(item =>
            item is HttpRequestException { StatusCode: HttpStatusCode.Forbidden } ||
            item.Message.Contains("403", StringComparison.OrdinalIgnoreCase) &&
            item.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);
        while (pending.TryPop(out var current))
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push(inner);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }
    }

    private void ApplySession(LauncherSettings settings, string accountId, MSession session)
    {
        var username = session.Username ?? throw new InvalidOperationException("Microsoft 登录未返回玩家名称。");
        var uuid = session.UUID ?? throw new InvalidOperationException("Microsoft 登录未返回玩家 UUID。");
        var accessToken = session.AccessToken ?? throw new InvalidOperationException("Microsoft 登录未返回访问令牌。");
        settings.UseMicrosoftAccount = true;
        settings.SelectedAccountId = accountId;
        settings.Username = username;
        settings.Uuid = uuid.Replace("-", "", StringComparison.Ordinal);
        _currentSession = new GameSessionInfo(
            username,
            settings.Uuid,
            accessToken,
            session.Xuid ?? "",
            LauncherSettings.SnowLauncherClientId);
    }

    public void Dispose()
    {
        _client.Dispose();
        _gate.Dispose();
    }
}
