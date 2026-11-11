using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WPCL.Services;

public sealed class PlayerHeadService
{
    private readonly HttpClient _client;

    public PlayerHeadService(HttpClient client)
    {
        _client = client;
    }

    public async Task<string?> CreateSvgDataUriAsync(string? textureUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(textureUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("textures.minecraft.net", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var png = await _client.GetByteArrayAsync(uri, cancellationToken);
        if (png.Length is 0 or > 2_000_000)
        {
            return null;
        }

        using var stream = new MemoryStream(png, writable: false);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        if (bitmap.PixelWidth < 64 || bitmap.PixelHeight < 32 || bitmap.PixelWidth % 64 != 0)
        {
            return null;
        }

        var scale = bitmap.PixelWidth / 64;
        var face = ReadPixels(bitmap, 8 * scale, 8 * scale, 8 * scale, 8 * scale);
        var hat = ReadPixels(bitmap, 40 * scale, 8 * scale, 8 * scale, 8 * scale);
        var composed = Compose(face, hat, 8 * scale, scale);
        var svg = BuildSvg(composed);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    private static byte[] ReadPixels(BitmapSource bitmap, int x, int y, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(x, y, width, height), pixels, width * 4, 0);
        return pixels;
    }

    private static uint[] Compose(byte[] face, byte[] hat, int size, int scale)
    {
        var result = new uint[64];
        for (var py = 0; py < 8; py++)
        for (var px = 0; px < 8; px++)
        {
            var index = ((py * scale) * size + px * scale) * 4;
            var baseColor = ToRgba(face, index);
            var overlay = ToRgba(hat, index);
            result[py * 8 + px] = AlphaBlend(baseColor, overlay);
        }
        return result;
    }

    private static uint ToRgba(byte[] bgra, int index) =>
        ((uint)bgra[index + 2] << 24) | ((uint)bgra[index + 1] << 16) | ((uint)bgra[index] << 8) | bgra[index + 3];

    private static uint AlphaBlend(uint background, uint foreground)
    {
        var alpha = foreground & 0xff;
        if (alpha == 0) return background;
        if (alpha == 255) return foreground;
        var inverse = 255 - alpha;
        uint Blend(int shift) => ((((foreground >> shift) & 0xff) * alpha + ((background >> shift) & 0xff) * inverse) / 255) & 0xff;
        return (Blend(24) << 24) | (Blend(16) << 16) | (Blend(8) << 8) | 0xff;
    }

    private static string BuildSvg(uint[] pixels)
    {
        var builder = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 8 8\" shape-rendering=\"crispEdges\">");
        for (var y = 0; y < 8; y++)
        {
            var x = 0;
            while (x < 8)
            {
                var color = pixels[y * 8 + x];
                var start = x++;
                while (x < 8 && pixels[y * 8 + x] == color) x++;
                builder.Append("<rect x=\"").Append(start).Append("\" y=\"").Append(y)
                    .Append("\" width=\"").Append(x - start).Append("\" height=\"1\" fill=\"#")
                    .Append((color >> 8).ToString("x6")).Append("\"/>");
            }
        }
        return builder.Append("</svg>").ToString();
    }
}
