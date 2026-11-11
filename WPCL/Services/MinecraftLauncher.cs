using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WPCL.Models;

namespace WPCL.Services;

public sealed class MinecraftLauncher : IDisposable
{
    private readonly object _sync = new();
    private Process? _process;

    public event Action<string, string>? LogReceived;
    public event Action<int>? GameExited;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public async Task<int> LaunchAsync(
        string versionId,
        LauncherSettings settings,
        GameSessionInfo? session,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_process is { HasExited: false })
            {
                throw new InvalidOperationException("已有 Minecraft 实例正在运行。");
            }
        }

        SettingsStore.Normalize(settings);
        var paths = MinecraftInstaller.GetPaths(settings.GameDirectory, versionId);
        if (!File.Exists(paths.VersionJson))
        {
            throw new FileNotFoundException("版本文件不完整，请重新安装该版本。");
        }

        var resolved = await ResolveVersionAsync(settings.GameDirectory, versionId, cancellationToken);
        using var document = JsonDocument.Parse(resolved.Profile.ToJsonString());
        var detail = document.RootElement;
        var pclIsolation = settings.Pcl2Compatibility && PclCompatibilityService.UsesVersionDirectory(paths.Version);
        var gameDirectory = pclIsolation
            ? paths.Version
            : settings.IsolateVersions
                ? Path.Combine(paths.Version, "game")
                : settings.GameDirectory;
        PrepareGameDirectory(gameDirectory);

        var classpath = BuildClasspath(detail, resolved.ClientJar, settings.GameDirectory);
        var assetIndex = detail.GetProperty("assetIndex").GetProperty("id").GetString()!;
        var assetsDirectory = Path.Combine(settings.GameDirectory, "assets");
        var username = session?.Username ?? settings.Username;
        var uuid = session?.Uuid ?? settings.Uuid;
        var accessToken = session?.AccessToken ?? "0";
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["${classpath}"] = classpath,
            ["${classpath_separator}"] = Path.PathSeparator.ToString(),
            ["${natives_directory}"] = paths.Natives,
            ["${launcher_name}"] = "WPCL",
            ["${launcher_version}"] = "1.0",
            ["${auth_player_name}"] = username,
            ["${version_name}"] = versionId,
            ["${game_directory}"] = gameDirectory,
            ["${assets_root}"] = assetsDirectory,
            ["${assets_index_name}"] = assetIndex,
            ["${auth_uuid}"] = uuid,
            ["${auth_access_token}"] = accessToken,
            ["${auth_session}"] = accessToken,
            ["${user_type}"] = session is null ? "legacy" : "msa",
            ["${user_properties}"] = "{}",
            ["${version_type}"] = detail.TryGetProperty("type", out var type) ? type.GetString() ?? "release" : "release",
            ["${clientid}"] = session?.ClientId ?? "",
            ["${auth_xuid}"] = session?.Xuid ?? ""
        };

        var arguments = new List<string>();
        arguments.AddRange(GetMemoryArguments(settings));
        arguments.Add("-Djava.library.path=" + paths.Natives);
        arguments.Add("-Dminecraft.launcher.brand=WPCL");
        arguments.Add("-Dminecraft.launcher.version=1.0");
        arguments.Add("-cp");
        arguments.Add(classpath);
        AddDefaultOpens(detail, arguments);

        if (detail.TryGetProperty("arguments", out var modernArguments))
        {
            AddArgumentSection(modernArguments.GetProperty("jvm"), arguments, values, isGame: false);
            arguments.Add(detail.GetProperty("mainClass").GetString()!);
            AddArgumentSection(modernArguments.GetProperty("game"), arguments, values, isGame: true);
        }
        else
        {
            arguments.Add(detail.GetProperty("mainClass").GetString()!);
            var legacy = detail.GetProperty("minecraftArguments").GetString() ?? "";
            arguments.AddRange(SplitArguments(Expand(legacy, values)));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = settings.JavaPath,
            WorkingDirectory = gameDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null) LogReceived?.Invoke("game", eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null) LogReceived?.Invoke("warning", eventArgs.Data);
        };
        process.Exited += ProcessExited;

        LogReceived?.Invoke("info", $"正在使用 {settings.JavaPath} 启动 {versionId}");
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Java 进程未能启动。");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_sync)
        {
            _process = process;
        }
        LogReceived?.Invoke("success", $"Minecraft 已启动，进程号 {process.Id}");
        return process.Id;
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_process is not { HasExited: false } process)
            {
                return;
            }
            process.Kill(true);
            LogReceived?.Invoke("warning", "已请求结束 Minecraft 进程");
        }
    }

    private void ProcessExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not Process process)
        {
            return;
        }
        var exitCode = process.ExitCode;
        LogReceived?.Invoke(exitCode == 0 ? "success" : "warning", $"Minecraft 已退出，退出码 {exitCode}");
        GameExited?.Invoke(exitCode);
        lock (_sync)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }
        }
        process.Dispose();
    }

    private static string BuildClasspath(JsonElement detail, string clientJar, string gameDirectory)
    {
        var entries = new List<string>();
        foreach (var library in detail.GetProperty("libraries").EnumerateArray())
        {
            if (!MinecraftRules.IsAllowed(library))
            {
                continue;
            }
            string? relative = null;
            if (library.TryGetProperty("downloads", out var downloads) &&
                downloads.TryGetProperty("artifact", out var artifact))
            {
                relative = artifact.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar);
            }
            else if (library.TryGetProperty("name", out var name) && !string.IsNullOrWhiteSpace(name.GetString()))
            {
                relative = MavenPath(name.GetString()!);
            }
            if (relative is null)
            {
                continue;
            }
            var path = Path.Combine(gameDirectory, "libraries", relative);
            if (File.Exists(path))
            {
                entries.Add(path);
            }
        }
        entries.Add(clientJar);
        return string.Join(Path.PathSeparator, entries);
    }

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

    private static async Task<ResolvedVersion> ResolveVersionAsync(
        string gameDirectory,
        string versionId,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return await ResolveVersionCoreAsync(gameDirectory, versionId, visited, cancellationToken);
    }

    private static async Task<ResolvedVersion> ResolveVersionCoreAsync(
        string gameDirectory,
        string versionId,
        ISet<string> visited,
        CancellationToken cancellationToken)
    {
        if (!visited.Add(versionId))
        {
            throw new InvalidDataException("版本继承关系存在循环。");
        }
        var paths = MinecraftInstaller.GetPaths(gameDirectory, versionId);
        if (!File.Exists(paths.VersionJson))
        {
            throw new FileNotFoundException($"缺少版本配置：{versionId}");
        }
        var child = JsonNode.Parse(await File.ReadAllTextAsync(paths.VersionJson, cancellationToken))!.AsObject();
        if (child["inheritsFrom"]?.GetValue<string>() is not { Length: > 0 } parentId)
        {
            if (!File.Exists(paths.ClientJar))
            {
                throw new FileNotFoundException($"缺少 Minecraft 客户端：{paths.ClientJar}");
            }
            return new ResolvedVersion(child, paths.ClientJar);
        }

        var parent = await ResolveVersionCoreAsync(gameDirectory, parentId, visited, cancellationToken);
        var merged = parent.Profile.DeepClone().AsObject();
        foreach (var pair in child)
        {
            if (pair.Key is "libraries" or "arguments" or "inheritsFrom")
            {
                continue;
            }
            merged[pair.Key] = pair.Value?.DeepClone();
        }

        var libraries = new JsonArray();
        AppendJson(libraries, child["libraries"] as JsonArray);
        AppendJson(libraries, parent.Profile["libraries"] as JsonArray);
        merged["libraries"] = libraries;

        var arguments = new JsonObject();
        foreach (var kind in new[] { "jvm", "game" })
        {
            var values = new JsonArray();
            AppendJson(values, parent.Profile["arguments"]?[kind] as JsonArray);
            AppendJson(values, child["arguments"]?[kind] as JsonArray);
            arguments[kind] = values;
        }
        merged["arguments"] = arguments;
        merged.Remove("inheritsFrom");

        return new ResolvedVersion(merged, File.Exists(paths.ClientJar) ? paths.ClientJar : parent.ClientJar);
    }

    private static void AppendJson(JsonArray target, JsonArray? source)
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

    private static void AddArgumentSection(
        JsonElement section,
        List<string> target,
        IReadOnlyDictionary<string, string> values,
        bool isGame)
    {
        foreach (var item in section.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                AddArgument(item.GetString()!, target, values, isGame);
                continue;
            }
            if (!MinecraftRules.IsAllowed(item) || (isGame && MinecraftRules.ContainsQuickPlayRule(item)) ||
                !item.TryGetProperty("value", out var value))
            {
                continue;
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in value.EnumerateArray())
                {
                    AddArgument(part.GetString()!, target, values, isGame);
                }
            }
            else
            {
                AddArgument(value.GetString()!, target, values, isGame);
            }
        }
    }

    private static void AddArgument(
        string value,
        List<string> target,
        IReadOnlyDictionary<string, string> values,
        bool isGame)
    {
        if (value is "-cp" or "-classpath" or "${classpath}" ||
            (isGame && (value == "--demo" || value.Contains("quickPlay", StringComparison.OrdinalIgnoreCase))))
        {
            return;
        }
        var expanded = Expand(value, values);
        if (target.Count >= 2 && target[^2] == "--add-opens" && target[^1] == expanded)
        {
            return;
        }
        target.Add(expanded);
    }

    private static string Expand(string value, IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            value = value.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        }
        return value;
    }

    private static void AddDefaultOpens(JsonElement detail, List<string> arguments)
    {
        if (!detail.TryGetProperty("javaVersion", out var javaVersion) ||
            !javaVersion.TryGetProperty("majorVersion", out var majorVersion))
        {
            return;
        }
        var major = majorVersion.GetInt32();
        if (major >= 17)
        {
            arguments.Add("--add-opens");
            arguments.Add("java.base/java.lang=ALL-UNNAMED");
        }
        if (major >= 21)
        {
            arguments.Add("--add-opens");
            arguments.Add("java.base/java.util=ALL-UNNAMED");
        }
    }

    private static IReadOnlyList<string> GetMemoryArguments(LauncherSettings settings)
    {
        if (!settings.AutoMemory)
        {
            return new[] { "-Xms" + settings.MinimumMemory, "-Xmx" + settings.MaximumMemory };
        }
        var gigabytes = GetTotalMemory() / 1024d / 1024d / 1024d;
        return gigabytes switch
        {
            <= 4 => new[] { "-Xms512M", "-Xmx1G" },
            <= 8 => new[] { "-Xms1G", "-Xmx2G" },
            <= 16 => new[] { "-Xms2G", "-Xmx4G" },
            <= 32 => new[] { "-Xms3G", "-Xmx6G" },
            _ => new[] { "-Xms4G", "-Xmx8G" }
        };
    }

    private static ulong GetTotalMemory()
    {
        var status = new MemoryStatusEx();
        return GlobalMemoryStatusEx(status) ? status.TotalPhysical : 8UL * 1024 * 1024 * 1024;
    }

    private static void PrepareGameDirectory(string gameDirectory)
    {
        foreach (var name in new[] { "logs", "saves", "crash-reports", "resourcepacks", "screenshots", "config", "mods" })
        {
            Directory.CreateDirectory(Path.Combine(gameDirectory, name));
        }
    }

    private static IEnumerable<string> SplitArguments(string commandLine)
    {
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < commandLine.Length; index++)
        {
            var character = commandLine[index];
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }
        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _process?.Dispose();
            _process = null;
        }
    }

    private sealed record ResolvedVersion(JsonObject Profile, string ClientJar);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
