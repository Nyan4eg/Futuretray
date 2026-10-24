using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;

namespace Futuretray.Core;

public sealed record UpdateInfo(Version Version, string Notes, string PageUrl, string AssetName, string AssetUrl, long Size, string? Sha256);

/// <summary>
/// Обновления из GitHub Releases: ищем установщик FuturetraySetup-*.exe в последнем релизе,
/// а портативная версия — архив Futuretray.zip, из которого подменяет свой exe.
/// </summary>
public static class Updater
{
    public const string Repository = "Nyan4eg/Futuretray";

    // Порядок важен: клиент при создании читает версию.
    public static Version CurrentVersion { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0));

    private static readonly HttpClient Http = CreateClient();

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Futuretray", CurrentVersionText));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <returns>Информация о более новой версии или null, если обновлений нет.</returns>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // релизов ещё нет
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        version = Normalize(version);
        if (version <= CurrentVersion) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!IsOurAsset(name, AppPaths.Portable)) continue;

            string? sha = asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d &&
                          d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? d[7..]
                : null;
            return new UpdateInfo(
                version,
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                root.GetProperty("html_url").GetString() ?? $"https://github.com/{Repository}/releases",
                name,
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                sha);
        }
        return null;
    }

    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Futuretray-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, update.AssetName);

        using (var response = await Http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? update.Size;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        if (update.Sha256 != null)
        {
            await using var file = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidDataException(Loc.T("Скачанный файл повреждён (контрольная сумма не совпала). Попробуйте ещё раз."));
            }
        }
        return path;
    }

    /// <summary>Установщик — для обычной версии, архив — для портативной.</summary>
    public static bool IsOurAsset(string name, bool portable) => portable
        ? name.Equals("Futuretray.zip", StringComparison.OrdinalIgnoreCase)
        : name.StartsWith("FuturetraySetup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static string OldExePath => AppPaths.ExePath + ".old";

    /// <summary>
    /// Портативное обновление: работающий exe Windows разрешает переименовать — отодвигаем его в .old,
    /// кладём новый на его место и запускаем. Старый удаляется при следующем запуске.
    /// </summary>
    public static void ReplacePortableExe(string zipPath)
    {
        SwapPortableExe(zipPath);
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.ExePath, "--restart --updated") { UseShellExecute = false, WorkingDirectory = AppPaths.ExeDir })?.Dispose();
        }
        catch
        {
            // Новый exe не запустился (например, его забрал антивирус) — возвращаем рабочий.
            RollBackPortableExe();
            throw;
        }
    }

    /// <summary>Подмена exe без запуска новой версии (отдельно — чтобы её можно было проверить).</summary>
    internal static void SwapPortableExe(string zipPath)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Futuretray-update", "portable");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ZipFile.ExtractToDirectory(zipPath, dir);
        var fresh = Path.Combine(dir, "Futuretray.exe");
        if (!File.Exists(fresh)) throw new InvalidDataException(Loc.T("В архиве обновления нет Futuretray.exe"));

        var exe = AppPaths.ExePath;
        if (File.Exists(OldExePath)) File.Delete(OldExePath);
        File.Move(exe, OldExePath);
        try
        {
            File.Copy(fresh, exe);
        }
        catch
        {
            RollBackPortableExe(); // не вышло — возвращаем как было
            throw;
        }
    }

    /// <summary>Убрать недописанный новый exe и вернуть старый на место.</summary>
    private static void RollBackPortableExe()
    {
        try
        {
            if (!File.Exists(OldExePath)) return;
            if (File.Exists(AppPaths.ExePath)) File.Delete(AppPaths.ExePath);
            File.Move(OldExePath, AppPaths.ExePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Portable update rollback");
        }
    }

    /// <summary>Убрать exe, оставшийся от прошлого портативного обновления.</summary>
    public static void CleanupOldExe()
    {
        try { if (File.Exists(OldExePath)) File.Delete(OldExePath); }
        catch { /* ещё занят — удалим в следующий раз */ }
    }

    /// <summary>Тихая установка: установщик сам закроет программу и запустит новую версию.</summary>
    public static void RunInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true })?.Dispose();

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}

/// <summary>Состояние обновлений для интерфейса + периодическая проверка.</summary>
public sealed class UpdateManager : Observable
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(12) };
    private UpdateInfo? _available;
    private string _status = Loc.F("Версия {0}", Updater.CurrentVersionText);
    private bool _busy;

    public UpdateManager(AppSettings settings)
    {
        _settings = settings;
        _timer.Tick += async (_, _) => await CheckAsync(manual: false);
    }

    public UpdateInfo? Available { get => _available; private set => Set(ref _available, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    /// <summary>Новая версия найдена (для уведомления в редакторе и трее).</summary>
    public event Action<UpdateInfo>? Found;

    public void Start()
    {
        _timer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            await CheckAsync(manual: false);
        };
        first.Start();
    }

    public async Task CheckAsync(bool manual)
    {
        if (Busy || (!manual && !_settings.CheckUpdates)) return;
        Busy = true;
        if (manual) Status = Loc.T("Проверяю…");
        try
        {
            var update = await Updater.CheckAsync();
            _settings.LastUpdateCheck = DateTime.Now;
            bool isNew = update != null && update.Version != Available?.Version;
            Available = update;
            Status = update == null
                ? Loc.F("У вас последняя версия — {0}", Updater.CurrentVersionText)
                : Loc.F("Доступна версия {0} (у вас {1})", update.Version.ToString(3), Updater.CurrentVersionText);
            if (isNew) Found?.Invoke(update!);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update check");
            if (manual) Status = Loc.T("Не удалось проверить обновления: нет связи с GitHub");
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task InstallAsync()
    {
        if (Available is not { } update || Busy) return;
        Busy = true;
        try
        {
            var progress = new Progress<double>(p => Status = Loc.F("Скачиваю версию {0}… {1:P0}", update.Version.ToString(3), p));
            var path = await Updater.DownloadAsync(update, progress);
            Status = Loc.T("Устанавливаю…");
            if (AppPaths.Portable)
            {
                App.Host.Store.SaveNow();
                Updater.ReplacePortableExe(path);
            }
            else
            {
                Updater.RunInstaller(path);
            }
            App.Host.ExitApp();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update install");
            Status = ex switch
            {
                InvalidDataException => ex.Message,
                UnauthorizedAccessException or IOException when AppPaths.Portable =>
                    Loc.T("Нет права записи в папку программы — скачайте новую версию вручную"),
                _ => Loc.T("Не удалось скачать обновление. Попробуйте позже."),
            };
            Busy = false;
        }
    }
}
