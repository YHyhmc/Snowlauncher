using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using WPCL.Models;

namespace WPCL.Services;

public sealed class MinecraftApi : IDisposable
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private readonly HttpClient _client;

    public MinecraftApi()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WPCL", "1.0"));
    }

    public async Task<VersionManifest> GetManifestAsync(CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(ManifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var latest = root.GetProperty("latest");
        var versions = root.GetProperty("versions").EnumerateArray()
            .Select(item => new RemoteVersion(
                item.GetProperty("id").GetString()!,
                item.GetProperty("type").GetString()!,
                item.GetProperty("url").GetString()!,
                item.GetProperty("releaseTime").GetDateTimeOffset()))
            .ToArray();
        return new VersionManifest(
            versions,
            latest.GetProperty("release").GetString()!,
            latest.GetProperty("snapshot").GetString()!);
    }

    public async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public HttpClient Client => _client;

    public void Dispose() => _client.Dispose();
}
