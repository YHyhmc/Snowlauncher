using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using WPCL.Bridge;
using WPCL.Models;

namespace WPCL.Services;

public sealed class SettingsStore
{
    private static readonly Regex MemoryPattern = new("^[1-9][0-9]*[MG]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly string _settingsPath;

    public SettingsStore()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WPCL");
        Directory.CreateDirectory(dataDirectory);
        _settingsPath = Path.Combine(dataDirectory, "WPCL.PCLjson");
        var legacyPath = Path.Combine(dataDirectory, "settings.json");
        if (!File.Exists(_settingsPath) && File.Exists(legacyPath))
        {
            File.Copy(legacyPath, _settingsPath);
        }
    }

    public string ConfigPath => _settingsPath;

    public LauncherSettings Load()
    {
        LauncherSettings settings;
        try
        {
            settings = File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_settingsPath), JsonDefaults.Options) ?? new LauncherSettings()
                : new LauncherSettings();
        }
        catch
        {
            settings = new LauncherSettings();
        }

        settings.GameDirectory = string.IsNullOrWhiteSpace(settings.GameDirectory)
            ? Path.Combine(AppContext.BaseDirectory, ".minecraft")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.GameDirectory));
        settings.OfflineUsername = string.IsNullOrWhiteSpace(settings.OfflineUsername)
            ? settings.Username
            : settings.OfflineUsername;
        Normalize(settings);
        Directory.CreateDirectory(settings.GameDirectory);
        Save(settings);
        return settings;
    }

    public void Save(LauncherSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(settings.GameDirectory);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonDefaults.Options));
        File.Move(temporary, _settingsPath, true);
    }

    public static void Normalize(LauncherSettings settings)
    {
        settings.SchemaVersion = 5;
        settings.OfflineUsername = string.IsNullOrWhiteSpace(settings.OfflineUsername) ? "Player" : settings.OfflineUsername.Trim();
        if (!Regex.IsMatch(settings.OfflineUsername, "^[A-Za-z0-9_]{3,16}$"))
        {
            throw new InvalidOperationException("离线用户名需要为 3–16 位字母、数字或下划线。");
        }

        if (!settings.UseMicrosoftAccount)
        {
            settings.Username = settings.OfflineUsername;
            settings.Uuid = CreateOfflineUuid(settings.OfflineUsername);
        }
        else
        {
            settings.Username = string.IsNullOrWhiteSpace(settings.Username) ? settings.OfflineUsername : settings.Username.Trim();
            settings.Uuid = IsUuid(settings.Uuid) ? settings.Uuid : CreateOfflineUuid(settings.OfflineUsername);
        }

        settings.SelectedAccountId = settings.SelectedAccountId.Trim();

        settings.GameDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.GameDirectory));
        settings.JavaPath = string.IsNullOrWhiteSpace(settings.JavaPath) ? "java" : settings.JavaPath.Trim();
        settings.MinimumMemory = settings.MinimumMemory.Trim().ToUpperInvariant();
        settings.MaximumMemory = settings.MaximumMemory.Trim().ToUpperInvariant();
        if (!MemoryPattern.IsMatch(settings.MinimumMemory) || !MemoryPattern.IsMatch(settings.MaximumMemory))
        {
            throw new InvalidOperationException("内存格式应类似 2G 或 1024M。");
        }

        settings.DownloadConcurrency = Math.Clamp(settings.DownloadConcurrency, 1, 32);
        settings.BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0, 100);
        settings.Uuid = IsUuid(settings.Uuid) ? settings.Uuid : CreateOfflineUuid(settings.OfflineUsername);
    }

    private static bool IsUuid(string? value) =>
        value is not null && Regex.IsMatch(value, "^[0-9a-f]{32}$", RegexOptions.IgnoreCase);

    internal static string CreateOfflineUuid(string username)
    {
        var bytes = MD5.HashData(System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
