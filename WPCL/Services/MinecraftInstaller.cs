using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WPCL.Bridge;
using WPCL.Models;

namespace WPCL.Services;

public sealed class MinecraftInstaller
{
    private readonly MinecraftApi _api;
    private readonly LoaderCatalogService _loaders;
    private readonly DownloadService _downloader;

    public MinecraftInstaller(MinecraftApi api)
    {
        _api = api;
        _loaders = new LoaderCatalogService(api);
        _downloader = new DownloadService(api.Client);
    }

    public async Task<string> InstallAsync(
        InstallRequest request,
        LauncherSettings settings,
        Action<ProgressUpdate> report,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, settings);
        EnsureGameRoot(settings.GameDirectory);
        await InstallVanillaAsync(request.VersionId, settings, report, log, cancellationToken);

        return request.Loader switch
        {
            LoaderKind.Fabric => await InstallFabricAsync(request, settings, report, log, cancellationToken),
            LoaderKind.NeoForge => await InstallWithOfficialInstallerAsync(
                request, settings, report, log, cancellationToken, LoaderKind.NeoForge),
            LoaderKind.Forge => await InstallWithOfficialInstallerAsync(
                request, settings, report, log, cancellationToken, LoaderKind.Forge),
            _ => CompleteVanilla(request, settings, log)
        };
    }

    public static IReadOnlyList<InstalledVersion> GetInstalledVersions(string gameDirectory) =>
        PclCompatibilityService.ReadInstalledVersions(gameDirectory);

    public static void DeleteVersion(string gameDirectory, string versionId)
    {
        var versionsRoot = Path.GetFullPath(Path.Combine(gameDirectory, "versions"));
        var target = Path.GetFullPath(Path.Combine(versionsRoot, versionId));
        if (!target.StartsWith(versionsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("版本目录无效。");
        }
        if (Directory.Exists(target))
        {
            Directory.Delete(target, true);
        }
    }

    private async Task InstallVanillaAsync(
        string versionId,
        LauncherSettings settings,
        Action<ProgressUpdate> report,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        log("info", $"正在读取 {versionId} 的官方版本信息");
        var manifest = await _api.GetManifestAsync(cancellationToken);
        var version = manifest.Versions.FirstOrDefault(item => item.Id == versionId)
            ?? throw new InvalidOperationException($"官方版本清单中没有 {versionId}。");
        var detailJson = await _api.GetTextAsync(version.Url, cancellationToken);
        using var document = JsonDocument.Parse(detailJson);
        var detail = document.RootElement;

        var paths = GetPaths(settings.GameDirectory, versionId);
        foreach (var path in new[] { paths.Version, paths.Libraries, paths.Indexes, paths.Objects, paths.Natives })
        {
            Directory.CreateDirectory(path);
        }
        await File.WriteAllTextAsync(paths.VersionJson, detailJson, cancellationToken);

        var assetIndex = detail.GetProperty("assetIndex");
        var assetIndexId = assetIndex.GetProperty("id").GetString()!;
        var indexFile = Path.Combine(paths.Indexes, assetIndexId + ".json");
        await _downloader.DownloadManyAsync(
            new[]
            {
                new DownloadItem(
                    assetIndex.GetProperty("url").GetString()!,
                    indexFile,
                    assetIndex.GetProperty("sha1").GetString()!,
                    GetInt64(assetIndex, "size"),
                    "资源索引")
            },
            1,
            "install",
            "index",
            report,
            cancellationToken);

        var downloads = new List<DownloadItem>();
        var nativeJars = new List<string>();
        var client = detail.GetProperty("downloads").GetProperty("client");
        downloads.Add(new DownloadItem(
            client.GetProperty("url").GetString()!,
            paths.ClientJar,
            client.GetProperty("sha1").GetString()!,
            GetInt64(client, "size"),
            "Minecraft 客户端"));
        CollectLibraries(detail, settings.GameDirectory, downloads, nativeJars);

        using (var indexDocument = JsonDocument.Parse(await File.ReadAllTextAsync(indexFile, cancellationToken)))
        {
            foreach (var entry in indexDocument.RootElement.GetProperty("objects").EnumerateObject())
            {
                var value = entry.Value;
                var hash = value.GetProperty("hash").GetString()!;
                downloads.Add(new DownloadItem(
                    $"https://resources.download.minecraft.net/{hash[..2]}/{hash}",
                    Path.Combine(paths.Objects, hash[..2], hash),
                    hash,
                    GetInt64(value, "size"),
                    entry.Name));
            }
        }

        log("info", $"已收集 {downloads.Count} 个原版文件");
        await _downloader.DownloadManyAsync(
            downloads,
            settings.DownloadConcurrency,
            "install",
            "files",
            report,
            cancellationToken);

        ExtractNatives(nativeJars, paths.Natives, (completed, current) =>
            report(new ProgressUpdate("install", "natives", completed, nativeJars.Count, 0, 0, current)));
        log("success", $"原版 {versionId} 已就绪");
    }

    private async Task<string> InstallFabricAsync(
        InstallRequest request,
        LauncherSettings settings,
        Action<ProgressUpdate> report,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        var loaderVersion = request.LoaderVersion!;
        var targetId = SafeVersionId($"{request.VersionId}-Fabric {loaderVersion}");
        var targetDirectory = Path.Combine(settings.GameDirectory, "versions", targetId);
        Directory.CreateDirectory(targetDirectory);

        log("info", $"正在获取 Fabric {loaderVersion} 安装配置");
        var profileJson = await _loaders.GetFabricProfileAsync(request.VersionId, loaderVersion, cancellationToken);
        var basePath = GetPaths(settings.GameDirectory, request.VersionId);
        var vanilla = JsonNode.Parse(await File.ReadAllTextAsync(basePath.VersionJson, cancellationToken))!.AsObject();
        var profile = JsonNode.Parse(profileJson)!.AsObject();
        var merged = MergeFabricProfile(vanilla, profile, targetId);
        var targetJson = Path.Combine(targetDirectory, targetId + ".json");
        await File.WriteAllTextAsync(targetJson, merged.ToJsonString(JsonDefaults.Options), cancellationToken);
        File.Copy(basePath.ClientJar, Path.Combine(targetDirectory, targetId + ".jar"), true);

        using var profileDocument = JsonDocument.Parse(profileJson);
        var downloads = new List<DownloadItem>();
        CollectLibraries(profileDocument.RootElement, settings.GameDirectory, downloads, null);
        await _downloader.DownloadManyAsync(
            downloads,
            settings.DownloadConcurrency,
            "install",
            "loader",
            report,
            cancellationToken);

        if (settings.Pcl2Compatibility)
        {
            PclCompatibilityService.WriteVersionSetup(
                targetDirectory,
                request.VersionId,
                LoaderKind.Fabric,
                loaderVersion);
        }
        log("success", $"Fabric 版本已安装：{targetId}");
        return targetId;
    }

    private async Task<string> InstallWithOfficialInstallerAsync(
        InstallRequest request,
        LauncherSettings settings,
        Action<ProgressUpdate> report,
        Action<string, string> log,
        CancellationToken cancellationToken,
        LoaderKind loader)
    {
        var loaderVersion = request.LoaderVersion!;
        var installerUrl = loader == LoaderKind.NeoForge
            ? LoaderCatalogService.GetNeoForgeInstallerUrl(loaderVersion)
            : LoaderCatalogService.GetForgeInstallerUrl(loaderVersion);
        var sha1 = (await _api.GetTextAsync(installerUrl + ".sha1", cancellationToken))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WPCL",
            "cache");
        Directory.CreateDirectory(cache);
        var installerPath = Path.Combine(cache, $"{loader.ToString().ToLowerInvariant()}-{SafeVersionId(loaderVersion)}-installer.jar");

        log("info", $"正在下载 {loader} {loaderVersion} 官方安装器");
        await _downloader.DownloadManyAsync(
            new[] { new DownloadItem(installerUrl, installerPath, sha1, 0, $"{loader} 安装器") },
            1,
            "install",
            "loader",
            report,
            cancellationToken);

        var versionsRoot = Path.Combine(settings.GameDirectory, "versions");
        var before = Directory.EnumerateDirectories(versionsRoot)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        log("info", $"正在执行 {loader} 官方客户端安装流程");
        var exitCode = await RunInstallerAsync(
            settings.JavaPath,
            installerPath,
            settings.GameDirectory,
            (level, message) => log(level, message),
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{loader} 安装器退出码为 {exitCode}。");
        }

        var installedId = FindInstalledLoaderVersion(
            versionsRoot,
            loaderVersion,
            loader == LoaderKind.NeoForge ? "neoforge" : "forge",
            before);
        if (installedId is null)
        {
            throw new InvalidOperationException($"{loader} 安装器已退出，但未找到生成的版本配置。");
        }

        if (settings.Pcl2Compatibility)
        {
            PclCompatibilityService.WriteVersionSetup(
                Path.Combine(versionsRoot, installedId),
                request.VersionId,
                loader,
                loaderVersion);
        }
        log("success", $"{loader} 版本已安装：{installedId}");
        return installedId;
    }

    private static string CompleteVanilla(InstallRequest request, LauncherSettings settings, Action<string, string> log)
    {
        if (settings.Pcl2Compatibility)
        {
            PclCompatibilityService.WriteVersionSetup(
                Path.Combine(settings.GameDirectory, "versions", request.VersionId),
                request.VersionId,
                LoaderKind.Vanilla,
                null);
        }
        log("success", $"{request.VersionId} 安装完成");
        return request.VersionId;
    }

    private static JsonObject MergeFabricProfile(JsonObject vanilla, JsonObject fabric, string versionId)
    {
        var merged = vanilla.DeepClone().AsObject();
        merged["id"] = versionId;
        foreach (var key in new[] { "mainClass", "type", "time", "releaseTime" })
        {
            if (fabric[key] is not null)
            {
                merged[key] = fabric[key]!.DeepClone();
            }
        }

        var libraries = new JsonArray();
        AppendClones(libraries, fabric["libraries"] as JsonArray);
        AppendClones(libraries, vanilla["libraries"] as JsonArray);
        merged["libraries"] = libraries;

        var arguments = new JsonObject();
        foreach (var kind in new[] { "game", "jvm" })
        {
            var values = new JsonArray();
            AppendClones(values, vanilla["arguments"]?[kind] as JsonArray);
            AppendClones(values, fabric["arguments"]?[kind] as JsonArray);
            arguments[kind] = values;
        }
        merged["arguments"] = arguments;
        merged.Remove("inheritsFrom");
        return merged;
    }

    private static void AppendClones(JsonArray target, JsonArray? source)
    {
        if (source is null)
        {
            return;
        }
        foreach (var item in source)
        {
            target.Add(item?.DeepClone());
        }
    }

    private static void CollectLibraries(
        JsonElement detail,
        string gameDirectory,
        ICollection<DownloadItem> downloads,
        ICollection<string>? nativeJars)
    {
        if (!detail.TryGetProperty("libraries", out var libraries))
        {
            return;
        }
        foreach (var library in libraries.EnumerateArray())
        {
            if (!MinecraftRules.IsAllowed(library))
            {
                continue;
            }
            var name = library.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "依赖库" : "依赖库";
            if (library.TryGetProperty("downloads", out var libraryDownloads) &&
                libraryDownloads.TryGetProperty("artifact", out var artifact))
            {
                downloads.Add(CreateLibraryItem(artifact, gameDirectory, name));
            }
            else if (library.TryGetProperty("url", out var baseUrlNode) && !string.IsNullOrWhiteSpace(name))
            {
                var relative = MavenPath(name);
                downloads.Add(new DownloadItem(
                    baseUrlNode.GetString()!.TrimEnd('/') + "/" + relative.Replace('\\', '/'),
                    Path.Combine(gameDirectory, "libraries", relative),
                    "",
                    0,
                    name));
            }

            if (nativeJars is null || !library.TryGetProperty("natives", out var natives) ||
                !natives.TryGetProperty("windows", out var windows) ||
                !libraryDownloads.TryGetProperty("classifiers", out var classifiers))
            {
                continue;
            }
            var nativeKey = windows.GetString()!.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
            if (classifiers.TryGetProperty(nativeKey, out var classifier))
            {
                var item = CreateLibraryItem(classifier, gameDirectory, name + " (native)");
                downloads.Add(item);
                nativeJars.Add(item.Destination);
            }
        }
    }

    private static DownloadItem CreateLibraryItem(JsonElement item, string gameDirectory, string label) =>
        new(
            item.GetProperty("url").GetString()!,
            Path.Combine(gameDirectory, "libraries", item.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)),
            item.TryGetProperty("sha1", out var sha1) ? sha1.GetString() ?? "" : "",
            GetInt64(item, "size"),
            label);

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

    private static async Task<int> RunInstallerAsync(
        string java,
        string installerPath,
        string gameDirectory,
        Action<string, string> log,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = java,
            WorkingDirectory = gameDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("--installClient");
        startInfo.ArgumentList.Add(gameDirectory);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) log("info", eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) log("warning", eventArgs.Data);
        };
        try
        {
            process.Start();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("无法启动 Java，请检查 Java 路径。", exception);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static string? FindInstalledLoaderVersion(
        string versionsRoot,
        string loaderVersion,
        string marker,
        ISet<string?> before)
    {
        return Directory.EnumerateDirectories(versionsRoot)
            .Select(path => new DirectoryInfo(path))
            .Where(info => File.Exists(Path.Combine(info.FullName, info.Name + ".json")))
            .Where(info =>
            {
                var content = File.ReadAllText(Path.Combine(info.FullName, info.Name + ".json"));
                return !before.Contains(info.Name) ||
                    (content.Contains(loaderVersion, StringComparison.OrdinalIgnoreCase) &&
                     content.Contains(marker, StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.Name)
            .FirstOrDefault();
    }

    private static long GetInt64(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.TryGetInt64(out var result) ? result : 0;

    private static void ExtractNatives(IReadOnlyList<string> archives, string destination, Action<int, string> report)
    {
        Directory.CreateDirectory(destination);
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        for (var index = 0; index < archives.Count; index++)
        {
            var archivePath = archives[index];
            if (!File.Exists(archivePath))
            {
                continue;
            }
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }
                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("原生库压缩包包含不安全路径。");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
            report(index + 1, Path.GetFileName(archivePath));
        }
    }

    private static void EnsureGameRoot(string gameDirectory)
    {
        foreach (var directory in new[] { "versions", "libraries", "assets" })
        {
            Directory.CreateDirectory(Path.Combine(gameDirectory, directory));
        }
        var profiles = Path.Combine(gameDirectory, "launcher_profiles.json");
        if (!File.Exists(profiles))
        {
            File.WriteAllText(profiles, "{\"profiles\":{},\"settings\":{},\"version\":3}", new UTF8Encoding(false));
        }
    }

    private static void ValidateRequest(InstallRequest request, LauncherSettings settings)
    {
        _ = SafeVersionId(request.VersionId);
        if (request.Loader != LoaderKind.Vanilla && string.IsNullOrWhiteSpace(request.LoaderVersion))
        {
            throw new InvalidOperationException("请选择 Mod 加载器版本。");
        }
        if (string.IsNullOrWhiteSpace(settings.GameDirectory))
        {
            throw new InvalidOperationException("请先设置游戏目录。");
        }
    }

    private static string SafeVersionId(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value is "." or "..")
        {
            throw new InvalidDataException("版本名称包含非法字符：" + value);
        }
        return value;
    }

    internal static LauncherPaths GetPaths(string gameDirectory, string versionId)
    {
        var version = Path.Combine(gameDirectory, "versions", versionId);
        return new LauncherPaths(
            version,
            Path.Combine(gameDirectory, "libraries"),
            Path.Combine(gameDirectory, "assets", "indexes"),
            Path.Combine(gameDirectory, "assets", "objects"),
            Path.Combine(version, "natives"),
            Path.Combine(version, versionId + ".jar"),
            Path.Combine(version, versionId + ".json"));
    }
}

internal sealed record LauncherPaths(
    string Version,
    string Libraries,
    string Indexes,
    string Objects,
    string Natives,
    string ClientJar,
    string VersionJson);
