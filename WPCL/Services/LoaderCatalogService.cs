using System.Xml.Linq;
using WPCL.Models;

namespace WPCL.Services;

public sealed class LoaderCatalogService
{
    private const string FabricMeta = "https://meta.fabricmc.net/v2";
    private const string NeoForgeMetadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    private const string ForgeMetadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private readonly MinecraftApi _api;

    public LoaderCatalogService(MinecraftApi api)
    {
        _api = api;
    }

    public async Task<LoaderCatalog> GetVersionsAsync(
        string gameVersion,
        LoaderKind loader,
        CancellationToken cancellationToken)
    {
        var versions = loader switch
        {
            LoaderKind.Fabric => await GetFabricAsync(gameVersion, cancellationToken),
            LoaderKind.NeoForge => await GetNeoForgeAsync(gameVersion, cancellationToken),
            LoaderKind.Forge => await GetForgeAsync(gameVersion, cancellationToken),
            _ => Array.Empty<LoaderVersion>()
        };
        return new LoaderCatalog(loader, gameVersion, versions);
    }

    public Task<string> GetFabricProfileAsync(
        string gameVersion,
        string loaderVersion,
        CancellationToken cancellationToken) =>
        _api.GetTextAsync(
            $"{FabricMeta}/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json",
            cancellationToken);

    public static string GetNeoForgeInstallerUrl(string version)
    {
        var safe = Uri.EscapeDataString(version);
        return $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{safe}/neoforge-{safe}-installer.jar";
    }

    public static string GetForgeInstallerUrl(string version)
    {
        var safe = Uri.EscapeDataString(version);
        return $"https://maven.minecraftforge.net/net/minecraftforge/forge/{safe}/forge-{safe}-installer.jar";
    }

    private async Task<IReadOnlyList<LoaderVersion>> GetFabricAsync(
        string gameVersion,
        CancellationToken cancellationToken)
    {
        using var json = await _api.GetJsonAsync(
            $"{FabricMeta}/versions/loader/{Uri.EscapeDataString(gameVersion)}",
            cancellationToken);
        var result = new List<LoaderVersion>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("loader", out var loader))
            {
                continue;
            }
            var version = loader.GetProperty("version").GetString();
            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }
            var stable = !loader.TryGetProperty("stable", out var stableValue) || stableValue.GetBoolean();
            result.Add(new LoaderVersion(version, version, stable));
        }
        return result;
    }

    private async Task<IReadOnlyList<LoaderVersion>> GetNeoForgeAsync(
        string gameVersion,
        CancellationToken cancellationToken)
    {
        var prefix = GetNeoForgePrefix(gameVersion);
        var xml = await _api.GetTextAsync(NeoForgeMetadata, cancellationToken);
        return XDocument.Parse(xml).Descendants("version")
            .Select(item => item.Value.Trim())
            .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Reverse()
            .Select(version => new LoaderVersion(version, version, IsStable(version)))
            .ToArray();
    }

    private async Task<IReadOnlyList<LoaderVersion>> GetForgeAsync(
        string gameVersion,
        CancellationToken cancellationToken)
    {
        var prefix = gameVersion.Trim() + "-";
        var xml = await _api.GetTextAsync(ForgeMetadata, cancellationToken);
        return XDocument.Parse(xml).Descendants("version")
            .Select(item => item.Value.Trim())
            .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Reverse()
            .Select(version => new LoaderVersion(version, version[prefix.Length..], IsStable(version)))
            .ToArray();
    }

    private static string GetNeoForgePrefix(string gameVersion)
    {
        var parts = gameVersion.Split('.');
        if (parts.Length < 2)
        {
            return "__unsupported__";
        }
        if (parts[0] == "1")
        {
            return parts[1] + "." + (parts.Length >= 3 ? parts[2] : "0") + ".";
        }
        return string.Join('.', parts.Take(3)) + ".";
    }

    private static bool IsStable(string version) =>
        !version.Contains("alpha", StringComparison.OrdinalIgnoreCase) &&
        !version.Contains("beta", StringComparison.OrdinalIgnoreCase);
}
