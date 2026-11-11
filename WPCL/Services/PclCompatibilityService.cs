using System.Text;
using System.Text.Json;
using WPCL.Models;

namespace WPCL.Services;

public static class PclCompatibilityService
{
    public static IReadOnlyList<InstalledVersion> ReadInstalledVersions(string gameDirectory)
    {
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
        {
            return Array.Empty<InstalledVersion>();
        }

        var result = new List<InstalledVersion>();
        foreach (var directory in Directory.EnumerateDirectories(versionsDirectory))
        {
            var id = Path.GetFileName(directory);
            var jsonPath = Path.Combine(directory, id + ".json");
            if (!File.Exists(jsonPath))
            {
                continue;
            }

            try
            {
                var metadata = ReadMetadata(directory, jsonPath);
                var launchable = IsLaunchable(versionsDirectory, id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                var jsonInfo = new FileInfo(jsonPath);
                var jarPath = Path.Combine(directory, id + ".jar");
                var size = jsonInfo.Length + (File.Exists(jarPath) ? new FileInfo(jarPath).Length : 0);
                result.Add(new InstalledVersion(
                    id,
                    size,
                    Directory.GetLastWriteTime(directory),
                    metadata.Isolated,
                    metadata.Loader,
                    metadata.LoaderVersion,
                    metadata.BaseVersion,
                    metadata.PclCompatible,
                    launchable,
                    launchable ? null : "版本文件不完整，请重新下载"));
            }
            catch
            {
                // Ignore incomplete version directories.
            }
        }

        return result.OrderByDescending(item => item.LastWriteTime).ToArray();
    }

    public static void WriteVersionSetup(
        string versionDirectory,
        string gameVersion,
        LoaderKind loader,
        string? loaderVersion,
        DateTimeOffset? releaseTime = null)
    {
        var pclDirectory = Path.Combine(versionDirectory, "PCL");
        Directory.CreateDirectory(pclDirectory);
        Directory.CreateDirectory(Path.Combine(versionDirectory, "mods"));
        Directory.CreateDirectory(Path.Combine(versionDirectory, "config"));

        var loaderName = loader == LoaderKind.Vanilla ? "" : loader.ToString();
        var info = loader == LoaderKind.Vanilla
            ? $"正式版 {gameVersion}"
            : $"正式版 {gameVersion}, {loaderName} {loaderVersion}";
        var logo = loader switch
        {
            LoaderKind.Fabric => "Fabric.png",
            LoaderKind.NeoForge => "NeoForge.png",
            LoaderKind.Forge => "Anvil.png",
            _ => "Grass.png"
        };

        var lines = new[]
        {
            "State:9",
            "Info:" + info,
            "Logo:pack://application:,,,/Plain Craft Launcher 2;component/Images/Blocks/" + logo,
            "ReleaseTime:" + (releaseTime ?? DateTimeOffset.Now).LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
            "VersionFabric:" + (loader == LoaderKind.Fabric ? loaderVersion : ""),
            "VersionOptiFine:",
            "VersionLiteLoader:False",
            "VersionForge:" + (loader == LoaderKind.Forge ? loaderVersion : ""),
            "VersionNeoForge:" + (loader == LoaderKind.NeoForge ? loaderVersion : ""),
            "VersionVanillaName:" + gameVersion,
            "VersionVanilla:",
            "VersionArgumentIndieV2:True"
        };
        File.WriteAllLines(Path.Combine(pclDirectory, "Setup.ini"), lines, new UTF8Encoding(false));
    }

    public static bool UsesVersionDirectory(string versionDirectory)
    {
        var setupPath = Path.Combine(versionDirectory, "PCL", "Setup.ini");
        if (!File.Exists(setupPath))
        {
            return false;
        }
        return File.ReadLines(setupPath)
            .Any(line => line.Equals("VersionArgumentIndieV2:True", StringComparison.OrdinalIgnoreCase));
    }

    private static VersionMetadata ReadMetadata(string versionDirectory, string jsonPath)
    {
        var setupPath = Path.Combine(versionDirectory, "PCL", "Setup.ini");
        var values = File.Exists(setupPath) ? ReadIni(setupPath) : new Dictionary<string, string>();
        var loader = LoaderKind.Vanilla;
        string? loaderVersion = null;
        string? baseVersion = values.GetValueOrDefault("VersionVanillaName");

        foreach (var pair in new[]
        {
            (Key: "VersionFabric", Loader: LoaderKind.Fabric),
            (Key: "VersionNeoForge", Loader: LoaderKind.NeoForge),
            (Key: "VersionForge", Loader: LoaderKind.Forge)
        })
        {
            if (values.TryGetValue(pair.Key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                loader = pair.Loader;
                loaderVersion = value;
                break;
            }
        }

        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = document.RootElement;
        var mainClass = root.TryGetProperty("mainClass", out var main) ? main.GetString() ?? "" : "";
        var id = root.TryGetProperty("id", out var jsonId) ? jsonId.GetString() ?? "" : "";
        if (loader == LoaderKind.Vanilla)
        {
            if (mainClass.Contains("fabric", StringComparison.OrdinalIgnoreCase) || id.Contains("fabric", StringComparison.OrdinalIgnoreCase))
            {
                loader = LoaderKind.Fabric;
            }
            else if (mainClass.Contains("neoforge", StringComparison.OrdinalIgnoreCase) || id.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            {
                loader = LoaderKind.NeoForge;
            }
            else if (mainClass.Contains("forge", StringComparison.OrdinalIgnoreCase) || id.Contains("forge", StringComparison.OrdinalIgnoreCase))
            {
                loader = LoaderKind.Forge;
            }
        }
        baseVersion ??= root.TryGetProperty("inheritsFrom", out var inherited) ? inherited.GetString() : null;

        return new VersionMetadata(
            loader,
            loaderVersion,
            baseVersion,
            UsesVersionDirectory(versionDirectory),
            File.Exists(setupPath));
    }

    private static Dictionary<string, string> ReadIni(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }
            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    private static bool IsLaunchable(string versionsDirectory, string versionId, ISet<string> visited)
    {
        if (!visited.Add(versionId))
        {
            return false;
        }
        var versionDirectory = Path.Combine(versionsDirectory, versionId);
        var jsonPath = Path.Combine(versionDirectory, versionId + ".json");
        if (!File.Exists(jsonPath))
        {
            return false;
        }
        if (File.Exists(Path.Combine(versionDirectory, versionId + ".jar")))
        {
            return true;
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (!document.RootElement.TryGetProperty("inheritsFrom", out var inherited))
            {
                return false;
            }
            var parentId = inherited.GetString();
            return !string.IsNullOrWhiteSpace(parentId) && IsLaunchable(versionsDirectory, parentId, visited);
        }
        catch
        {
            return false;
        }
    }

    private sealed record VersionMetadata(
        LoaderKind Loader,
        string? LoaderVersion,
        string? BaseVersion,
        bool Isolated,
        bool PclCompatible);
}
