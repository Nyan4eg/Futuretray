using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Futuretray.Core;

/// <summary>
/// Достаёт иконки через IShellItemImageFactory — тот же механизм, что у Проводника:
/// работает для exe, ярлыков, папок, любых файлов и приложений из Microsoft Store.
/// </summary>
public sealed class IconService
{
    public const int TilePx = 96;

    private const int SIIGBF_ICONONLY = 0x4;
    private readonly ConcurrentDictionary<string, BitmapSource> _cache = new();

    public IconService() => Current = this;

    public static IconService? Current { get; private set; }

    public BitmapSource Get(LaunchItem item, int px) =>
        _cache.GetOrAdd($"{item.IconKey}#{px}", _ => Load(item, px));

    private readonly BlockingCollection<(LaunchItem Item, Func<bool> Needed, Action<BitmapSource> Done)> _queue = new();
    private Thread? _worker;

    /// <summary>
    /// Иконка без задержки интерфейса: из кэша сразу, иначе — в фоновом потоке, и <paramref name="done"/>
    /// придёт в потоке интерфейса. Пока очередь дошла, иконка могла стать не нужна (<paramref name="needed"/>).
    /// </summary>
    public void GetAsync(LaunchItem item, Action<BitmapSource> done, Func<bool> needed)
    {
        if (_cache.TryGetValue($"{item.IconKey}#{TilePx}", out var cached))
        {
            done(cached);
            return;
        }
        if (_worker == null)
        {
            _worker = new Thread(() =>
            {
                foreach (var (queued, stillNeeded, callback) in _queue.GetConsumingEnumerable())
                {
                    if (!stillNeeded()) continue;
                    try
                    {
                        var image = Get(queued, TilePx);
                        Application.Current?.Dispatcher.BeginInvoke(() => callback(image));
                    }
                    catch (Exception ex) { Log.Error(ex, "Icon " + queued.Name); }
                }
            })
            { IsBackground = true, Name = "Icons on demand", Priority = ThreadPriority.BelowNormal };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }
        _queue.Add((item, needed, done));
    }

    public BitmapSource? GetShell(string parsingName, int px)
    {
        var key = $"shell|{parsingName}#{px}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var image = Shell(parsingName, px);
        if (image != null) _cache[key] = image;
        return image;
    }

    /// <summary>Иконка/картинка из файла, который пользователь выбрал вручную.</summary>
    public BitmapSource? LoadCustom(string path, int px)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff")
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.UriSource = new Uri(path);
                bmp.DecodePixelWidth = Math.Max(px, 64);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            return Shell(path, px);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Custom icon " + path);
            return null;
        }
    }

    public void Prewarm(IEnumerable<LaunchItem> items)
    {
        var list = items.ToList();
        var thread = new Thread(() =>
        {
            foreach (var item in list)
            {
                try { Get(item, TilePx); } catch { }
            }
        })
        { IsBackground = true, Name = "Icon prewarm", Priority = ThreadPriority.BelowNormal };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private BitmapSource Load(LaunchItem item, int px)
    {
        try
        {
            if (item.CustomIconPath is { } custom && LoadCustom(custom, px) is { } customImage)
                return customImage;

            var image = item.Kind switch
            {
                ItemKind.ShellApp => Shell(@"shell:AppsFolder\" + item.Target, px),
                ItemKind.Url => Favicons.TryGet(item.Target, px) ?? BrowserIcon(px),
                _ => Shell(Environment.ExpandEnvironmentVariables(item.Target), px),
            };
            if (image != null) return image;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Icon " + item.Target);
        }
        return Fallback(item.Kind == ItemKind.Url ? "" : "", px);
    }

    private BitmapSource? BrowserIcon(int px)
    {
        uint length = 1024;
        var sb = new StringBuilder((int)length);
        if (Native.AssocQueryString(0, 2 /* ASSOCSTR_EXECUTABLE */, "http", "open", sb, ref length) == 0 && File.Exists(sb.ToString()))
            return GetShell(sb.ToString(), px);
        return null;
    }

    private static BitmapSource? Shell(string parsingName, int px)
    {
        if (string.IsNullOrWhiteSpace(parsingName)) return null;
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory) != 0 || factory == null)
            return null;
        try
        {
            if (factory.GetImage(new SIZE { cx = px, cy = px }, SIIGBF_ICONONLY, out var hbm) != 0 || hbm == IntPtr.Zero)
                return null;
            return FromHBitmap(hbm);
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    private static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        try
        {
            if (Native.GetObject(hbm, Marshal.SizeOf<Native.BITMAP>(), out var bm) == 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0) return null;

            var header = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // сверху вниз
                biPlanes = 1,
                biBitCount = 32,
            };
            var pixels = new byte[w * h * 4];
            var hdc = Native.GetDC(IntPtr.Zero);
            try
            {
                if (Native.GetDIBits(hdc, hbm, 0, (uint)h, pixels, ref header, 0) == 0) return null;
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, hdc);
            }

            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha)
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
            source.Freeze();
            return source;
        }
        finally
        {
            Native.DeleteObject(hbm);
        }
    }

    /// <summary>Нейтральная заглушка, если файл пропал или иконку не удалось получить.</summary>
    public static BitmapSource Fallback(string glyph, int px)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(px * 0.08, px * 0.08, px * 0.84, px * 0.84);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x5B, 0x63, 0x70)), null, rect, px * 0.2, px * 0.2);
            var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                px * 0.42, Brushes.White, 1.0);
            dc.DrawText(text, new Point((px - text.Width) / 2, (px - text.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? factory);
}
