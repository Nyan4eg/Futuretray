using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Futuretray.Core;

/// <summary>
/// Значки сайтов для ссылок. Берём у самого сайта (apple-touch-icon, &lt;link rel="icon"&gt;, /favicon.ico),
/// кэшируем PNG в %LOCALAPPDATA%\Futuretray\favicons. Пока значка нет — показывается браузер.
/// </summary>
public static partial class Favicons
{
    private const int MaxBytes = 1_000_000;
    private const int SavePx = 128;

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Futuretray", "favicons");

    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, bool> Known = new();      // host → есть ли файл
    private static readonly ConcurrentDictionary<string, byte> Attempted = new();  // в этой сессии уже пытались

    /// <summary>Разрешено ли ходить в сеть (настройка пользователя).</summary>
    public static Func<bool> Enabled { get; set; } = () => true;

    /// <summary>Скачан значок для сайта (host). Вызывается в потоке интерфейса.</summary>
    public static event Action<string>? Ready;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Futuretray");
        return client;
    }

    public static string? HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.Host.ToLowerInvariant() : null;

    /// <summary>Метка для ключа кэша иконки: меняется, когда значок скачан.</summary>
    public static string Stamp(string url) => HostOf(url) is { } host && Has(host) ? "fav" : "";

    /// <summary>Значок сайта, если он уже есть; иначе null и тихая загрузка в фоне.</summary>
    public static BitmapSource? TryGet(string url, int px)
    {
        var host = HostOf(url);
        if (host == null) return null;
        if (Has(host))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(FilePath(host));
                bmp.DecodePixelWidth = Math.Min(px, SavePx);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                Known[host] = false;
            }
        }
        if (Enabled() && Attempted.TryAdd(host, 0)) _ = Task.Run(() => FetchAsync(url, host));
        return null;
    }

    private static bool Has(string host) => Known.GetOrAdd(host, h => File.Exists(FilePath(h)));

    private static string FilePath(string host) => Path.Combine(Dir, host + ".png");

    private static async Task FetchAsync(string url, string host)
    {
        try
        {
            foreach (var candidate in await CandidatesAsync(url))
            {
                var image = await DownloadImageAsync(candidate);
                if (image == null) continue;
                Save(image, FilePath(host));
                Known[host] = true;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Ready?.Invoke(host));
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Info($"Favicon {host}: {ex.Message}");
        }
    }

    /// <summary>Сначала крупные значки из разметки страницы, затем /favicon.ico.</summary>
    private static async Task<List<Uri>> CandidatesAsync(string url)
    {
        var page = new Uri(url);
        var found = new List<(Uri Uri, int Rank)>();
        try
        {
            using var response = await Http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/html")
            {
                var html = await ReadLimitedAsync(response, 300_000);
                var baseUri = response.RequestMessage?.RequestUri ?? page;
                foreach (Match tag in LinkTag().Matches(html))
                {
                    var rel = Attr(tag.Value, "rel")?.ToLowerInvariant() ?? "";
                    var href = Attr(tag.Value, "href");
                    if (href == null || !rel.Contains("icon") || href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Uri.TryCreate(baseUri, System.Net.WebUtility.HtmlDecode(href), out var iconUri)) continue;
                    int size = Attr(tag.Value, "sizes") is { } sizes && int.TryParse(sizes.Split('x')[0], out var s) ? s : 0;
                    int rank = rel.Contains("apple-touch-icon") ? 1000 + size : size > 0 ? size : 32;
                    found.Add((iconUri, rank));
                }
                page = baseUri;
            }
        }
        catch
        {
            // страница недоступна — попробуем хотя бы /favicon.ico
        }

        var list = found.OrderByDescending(f => f.Rank).Select(f => f.Uri).ToList();
        list.Add(new Uri(page, "/favicon.ico"));
        return list.Distinct().ToList();
    }

    private static async Task<BitmapSource?> DownloadImageAsync(Uri uri)
    {
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0 || bytes.Length > MaxBytes) return null;

            var decoder = BitmapDecoder.Create(new MemoryStream(bytes),
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            // В .ico несколько размеров — берём самый крупный.
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
            if (frame == null || frame.PixelWidth < 16) return null;
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    private static void Save(BitmapSource source, string path)
    {
        BitmapSource image = source;
        if (source.PixelWidth > SavePx)
        {
            double k = (double)SavePx / source.PixelWidth;
            image = new TransformedBitmap(source, new ScaleTransform(k, k));
        }
        Directory.CreateDirectory(Dir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var temp = path + ".tmp";
        using (var fs = File.Create(temp)) encoder.Save(fs);
        File.Move(temp, path, true);
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, int limit)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[limit];
        int total = 0, read;
        while (total < limit && (read = await stream.ReadAsync(buffer.AsMemory(total, limit - total))) > 0) total += read;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\b{name}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) : null;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();
}
