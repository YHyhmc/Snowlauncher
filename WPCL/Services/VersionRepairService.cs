using System.IO.Compression;
using System.Text.Json;
using WPCL.Models;

namespace WPCL.Services;

public sealed class VersionRepairService
{
    private readonly DownloadService _downloader;

    public VersionRepairService(HttpClient client)
    {
        _downloader = new DownloadService(client);
    }

    public async Task RepairAsync(
        string versionId,
        LauncherSettings settings,
        Action<ProgressUpdate> report,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        var profiles = await ReadVersionChainAsync(settings.GameDirectory, versionId, cancellationToken);
        var downloads = new List<DownloadItem>();
        var nativeJars = new List<string>();
        JsonElement? assetIndex = null;

        foreach (var profile in profiles)
        {
            var root = profile.Document.RootElement;
            if (root.TryGetProperty("downloads", out var rootDownloads) &&
                rootDownloads.TryGetProperty("client", out var client))
            {
                var paths = MinecraftInstaller.GetPaths(settings.GameDirectory, profile.Id);
                downloads.Add(CreateDownload(client, paths.ClientJar, $"{profile.Id} 客户端"));
            }

            CollectLibraries(root, settings.GameDirectory, downloads, nativeJars);
            if (assetIndex is null && root.TryGetProperty("assetIndex", out var index))
            {
                assetIndex = index.Clone();
            }
        }

        if (assetIndex is null)
        {
            throw new InvalidDataException("版本配置缺少资源索引，无法自动修复。");
        }

        var indexId = assetIndex.Value.GetProperty("id").GetString()
            ?? throw new InvalidDataException("资源索引 ID 无效。");
        var indexPath = Path.Combine(settings.GameDirectory, "assets", "indexes", indexId + ".json");
        await _downloader.DownloadManyAsync(
            new[] { CreateDownload(assetIndex.Value, indexPath, "资源索引") },
            1,
            "repair",
            "index",
            report,
            cancellationToken);

        using (var indexDocument = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, cancellationToken)))
        {
            foreach (var entry in indexDocument.RootElement.GetProperty("objects").EnumerateObject())
            {
                var value = entry.Value;
                var hash = value.GetProperty("hash").GetString()
                    ?? throw new InvalidDataException($"资源 {entry.Name} 的哈希无效。");
                downloads.Add(new DownloadItem(
                    $"https://resources.download.minecraft.net/{hash[..2]}/{hash}",
                    Path.Combine(settings.GameDirectory, "assets", "objects", hash[..2], hash),
                    hash,
                    ReadSize(value),
                    entry.Name));
            }
        }

        log("info", $"正在检查 {versionId} 的 {downloads.Count} 个启动文件");
        await _downloader.DownloadManyAsync(
            downloads,
            settings.DownloadConcurrency,
            "repair",
            "files",
            report,
            cancellationToken);

        ExtractNatives(nativeJars, MinecraftInstaller.GetPaths(settings.GameDirectory, versionId).Natives, report);
        log("success", $"{versionId} 缺失文件检查完成");

        foreach (var profile in profiles)
        {
            profile.Document.Dispose();
        }
    }

    private static async Task<IReadOnlyList<VersionProfile>> ReadVersionChainAsync(
        string gameDirectory,
        string versionId,
        CancellationToken cancellationToken)
    {
        var profiles = new List<VersionProfile>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = versionId;
        while (visited.Add(currentId))
        {
            var path = MinecraftInstaller.GetPaths(gameDirectory, currentId).VersionJson;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"缺少版本配置：{path}");
            }

            var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            profiles.Add(new VersionProfile(currentId, document));
            if (!document.RootElement.TryGetProperty("inheritsFrom", out var inherited) ||
                string.IsNullOrWhiteSpace(inherited.GetString()))
            {
                return profiles;
            }
            currentId = inherited.GetString()!;
        }

        throw new InvalidDataException("版本继承关系存在循环。");
    }

    private static void CollectLibraries(
        JsonElement root,
        string gameDirectory,
        ICollection<DownloadItem> downloads,
        ICollection<string> nativeJars)
    {
        if (!root.TryGetProperty("libraries", out var libraries))
        {
            return;
        }

        foreach (var library in libraries.EnumerateArray())
        {
            if (!MinecraftRules.IsAllowed(library))
            {
                continue;
            }

            var coordinate = library.TryGetProperty("name", out var name) ? name.GetString() : null;
            var label = coordinate ?? "依赖库";
            if (library.TryGetProperty("downloads", out var libraryDownloads) &&
                libraryDownloads.TryGetProperty("artifact", out var artifact))
            {
                var path = Path.Combine(gameDirectory, "libraries", artifact.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                downloads.Add(CreateDownload(artifact, path, label));
            }
            else if (!string.IsNullOrWhiteSpace(coordinate))
            {
                var relative = MavenPath(coordinate);
                var baseUrl = library.TryGetProperty("url", out var libraryUrl)
                    ? libraryUrl.GetString()
                    : "https://libraries.minecraft.net/";
                downloads.Add(new DownloadItem(
                    baseUrl!.TrimEnd('/') + "/" + relative.Replace('\\', '/'),
                    Path.Combine(gameDirectory, "libraries", relative),
                    "",
                    0,
                    label));
            }

            if (!library.TryGetProperty("natives", out var natives) ||
                !natives.TryGetProperty("windows", out var windows))
            {
                continue;
            }

            var nativeKey = windows.GetString()!.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
            if (library.TryGetProperty("downloads", out libraryDownloads) &&
                libraryDownloads.TryGetProperty("classifiers", out var classifiers) &&
                classifiers.TryGetProperty(nativeKey, out var classifier))
            {
                var path = Path.Combine(gameDirectory, "libraries", classifier.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                downloads.Add(CreateDownload(classifier, path, label + " (native)"));
                nativeJars.Add(path);
            }
            else if (!string.IsNullOrWhiteSpace(coordinate))
            {
                var relative = MavenPath(coordinate + ":" + nativeKey);
                var baseUrl = library.TryGetProperty("url", out var libraryUrl)
                    ? libraryUrl.GetString()
                    : "https://libraries.minecraft.net/";
                var path = Path.Combine(gameDirectory, "libraries", relative);
                downloads.Add(new DownloadItem(baseUrl!.TrimEnd('/') + "/" + relative.Replace('\\', '/'), path, "", 0, label + " (native)"));
                nativeJars.Add(path);
            }
        }
    }

    private static DownloadItem CreateDownload(JsonElement source, string destination, string label) =>
        new(
            source.GetProperty("url").GetString() ?? throw new InvalidDataException($"{label} 缺少下载地址。"),
            destination,
            source.TryGetProperty("sha1", out var sha1) ? sha1.GetString() ?? "" : "",
            ReadSize(source),
            label);

    private static long ReadSize(JsonElement source) =>
        source.TryGetProperty("size", out var size) && size.TryGetInt64(out var value) ? value : 0;

    private static string MavenPath(string coordinate)
    {
        var extension = "jar";
        var extensionIndex = coordinate.IndexOf('@');
        if (extensionIndex >= 0)
        {
            extension = coordinate[(extensionIndex + 1)..];
            coordinate = coordinate[..extensionIndex];
        }
        var parts = coordinate.Split(':');
        if (parts.Length < 3)
        {
            throw new InvalidDataException("无效 Maven 坐标：" + coordinate);
        }
        var classifier = parts.Length >= 4 ? "-" + parts[3] : "";
        return Path.Combine(parts[0].Replace('.', Path.DirectorySeparatorChar), parts[1], parts[2], $"{parts[1]}-{parts[2]}{classifier}.{extension}");
    }

    private static void ExtractNatives(
        IReadOnlyList<string> archives,
        string destination,
        Action<ProgressUpdate> report)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        for (var index = 0; index < archives.Count; index++)
        {
            using var archive = ZipFile.OpenRead(archives[index]);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }
                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("原生库压缩包包含不安全路径。");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
            report(new ProgressUpdate("repair", "natives", index + 1, archives.Count, 0, 0, Path.GetFileName(archives[index])));
        }
    }

    private sealed record VersionProfile(string Id, JsonDocument Document);
}
