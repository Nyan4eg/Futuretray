using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Futuretray.Core;

/// <summary>
/// Рисует иконку группы: «папку» из иконок приложений на цветной подложке
/// (как на iOS), либо иконку выбранного приложения, либо свою картинку.
/// </summary>
public static class GroupIconRenderer
{
    public static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 256 };
    private static readonly Dictionary<string, BitmapSource> Cache = new();

    public static string Signature(Group g)
    {
        var sb = new StringBuilder();
        sb.Append(g.IconMode).Append('|').Append(g.ShowIconBackground).Append('|')
          .Append(ColorUtil.ToHex(ColorUtil.Resolve(g.Color))).Append('|');
        switch (g.IconMode)
        {
            case GroupIconMode.Custom:
                sb.Append(g.CustomIconPath);
                break;
            case GroupIconMode.Item:
                sb.Append(SelectedItem(g)?.IconKey);
                break;
            default:
                foreach (var item in g.Items.Take(4)) sb.Append(item.IconKey).Append(';');
                break;
        }
        return sb.ToString();
    }

    public static string Hash(Group g) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Signature(g))))[..10].ToLowerInvariant();

    public static BitmapSource Get(Group g, int px)
    {
        var key = Signature(g) + "#" + px;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }
        var image = Render(g, px);
        lock (Cache)
        {
            if (Cache.Count > 400) Cache.Clear();
            Cache[key] = image;
        }
        return image;
    }

    public static void WriteIco(Group g, string path)
    {
        var frames = IcoSizes.Select(size => (size, data: EncodePng(Render(g, size)))).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var (size, data) in frames)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(data.Length);
            w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in frames) w.Write(data);
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static LaunchItem? SelectedItem(Group g) =>
        g.Items.FirstOrDefault(i => i.Id == g.IconItemId) ?? g.Items.FirstOrDefault();

    private static BitmapSource Render(Group g, int px)
    {
        var icons = Images(g, px <= 48 ? IconService.TilePx : 256);
        bool background = g.ShowIconBackground;
        bool single = g.IconMode != GroupIconMode.Auto || icons.Count == 1;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            double s = px;
            if (background) DrawBackground(dc, s, ColorUtil.Resolve(g.Color));

            double pad = background ? s * (single ? 0.17 : 0.15) : s * 0.02;
            var area = new Rect(pad, pad, s - 2 * pad, s - 2 * pad);

            if (icons.Count == 0)
            {
                DrawEmpty(dc, area, background);
            }
            else if (single)
            {
                dc.DrawImage(icons[0], area);
            }
            else
            {
                double gap = area.Width * 0.1;
                double cell = (area.Width - gap) / 2;
                var slots = icons.Count switch
                {
                    2 => new[] { new Point(area.Left, area.Top + (area.Height - cell) / 2), new Point(area.Left + cell + gap, area.Top + (area.Height - cell) / 2) },
                    3 => new[] { new Point(area.Left, area.Top), new Point(area.Left + cell + gap, area.Top), new Point(area.Left + (area.Width - cell) / 2, area.Top + cell + gap) },
                    _ => new[] { new Point(area.Left, area.Top), new Point(area.Left + cell + gap, area.Top), new Point(area.Left, area.Top + cell + gap), new Point(area.Left + cell + gap, area.Top + cell + gap) },
                };
                for (int i = 0; i < slots.Length && i < icons.Count; i++)
                    dc.DrawImage(icons[i], new Rect(slots[i], new Size(cell, cell)));
            }
        }

        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static List<ImageSource> Images(Group g, int srcPx)
    {
        var icons = IconService.Current;
        var list = new List<ImageSource>();
        if (icons == null) return list;
        switch (g.IconMode)
        {
            case GroupIconMode.Custom when g.CustomIconPath != null:
                if (icons.LoadCustom(g.CustomIconPath, Math.Max(srcPx, 256)) is { } custom) list.Add(custom);
                break;
            case GroupIconMode.Item:
                if (SelectedItem(g) is { } item) list.Add(icons.Get(item, srcPx));
                break;
            default:
                list.AddRange(g.Items.Take(4).Select(i => (ImageSource)icons.Get(i, srcPx)));
                break;
        }
        return list;
    }

    private static void DrawBackground(DrawingContext dc, double s, Color color)
    {
        var rect = new Rect(0, 0, s, s);
        double r = s * 0.225;
        var brush = new LinearGradientBrush(ColorUtil.Lighten(color, 0.2), ColorUtil.Darken(color, 0.08), 90);
        brush.Freeze();
        dc.DrawRoundedRectangle(brush, null, rect, r, r);

        double t = Math.Max(1, s / 48.0);
        var pen = new Pen(ColorUtil.Brush(Color.FromArgb(56, 255, 255, 255)), t);
        pen.Freeze();
        var inner = rect;
        inner.Inflate(-t / 2, -t / 2);
        dc.DrawRoundedRectangle(null, pen, inner, r - t / 2, r - t / 2);
    }

    private static void DrawEmpty(DrawingContext dc, Rect area, bool background)
    {
        double gap = area.Width * 0.12;
        double cell = (area.Width - gap) / 2;
        double r = cell * 0.28;
        var baseColor = background ? Colors.White : Color.FromRgb(0x8A, 0x8F, 0x99);
        double[] alpha = { 0.95, 0.7, 0.7, 0.45 };
        for (int i = 0; i < 4; i++)
        {
            double x = area.Left + (i % 2) * (cell + gap);
            double y = area.Top + (i / 2) * (cell + gap);
            dc.DrawRoundedRectangle(ColorUtil.Brush(ColorUtil.WithAlpha(baseColor, alpha[i])), null, new Rect(x, y, cell, cell), r, r);
        }
    }
}
