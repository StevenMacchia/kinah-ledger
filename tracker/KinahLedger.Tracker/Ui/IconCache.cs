using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KinahLedger.Ui;

/// <summary>One item's icon as the rows bind it: nothing until the file is on disk, then the image. Every row showing that item shares it.</summary>
public sealed class IconSlot : INotifyPropertyChanged
{
    ImageSource? _image;
    public ImageSource? Image
    {
        get => _image;
        internal set { if (ReferenceEquals(_image, value)) return; _image = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>A fetch is in flight for this slot.</summary>
    internal bool Busy;
    /// <summary>When the last fetch failed (default: never), so a lettered tile is tried again after a while rather than left for the next start.</summary>
    internal DateTime FailedAt;
}

/// <summary>
/// Item icons without shipping any. Each is fetched once from NCSOFT's image server by its file name, the way a browser
/// does on their site, and kept under %LocalAppData%\KinahLedger\icons; a session touches a few dozen files at most, so
/// the exe stays one small download and the cache stays small too. Offline, or with icons off, rows keep a lettered tile.
/// </summary>
public static class IconCache
{
    static readonly Dictionary<string, IconSlot> Slots = new();
    static readonly List<Task> Pending = new();
    /// <summary>How long a failed fetch (offline, a timeout while a table of new rows all asked at once) waits before the tile asks again.</summary>
    public const double RetrySeconds = 60;
    /// <summary>Fetches started, for the self-test.</summary>
    internal static int Fetches;
    static readonly SemaphoreSlim Gate = new(2, 2);          // two downloads at a time; icons are decoration, not the job
    static readonly object Lock = new();
    static HttpClient? _http;
    static Dispatcher? _ui;

    /// <summary>A slot that never gets an image, for rows whose item has no known icon: the tile shows the item's initial.</summary>
    public static readonly IconSlot None = new();

    /// <summary>settings.json ui.icons: off means no request ever leaves the PC for an icon.</summary>
    public static bool Enabled { get; set; } = true;
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "icons");
    const int Pixels = 48;   // decoded size: rows draw them at 24 px, twice that for high-DPI screens

    static HttpClient Http
    {
        get
        {
            if (_http != null) return _http;
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + Upload.RunPayload.AppVersion);
            return _http = h;
        }
    }

    /// <summary>
    /// The slot for an icon URL, made on first use: filled at once when the file is cached, fetched in the background
    /// otherwise. None for an empty URL or with icons off. Call from the UI thread; the image lands there too.
    /// </summary>
    public static IconSlot Get(string url)
    {
        if (!Enabled || string.IsNullOrEmpty(url)) return None;
        _ui ??= Dispatcher.CurrentDispatcher;
        IconSlot slot; bool fetch;
        lock (Lock)
        {
            if (!Slots.TryGetValue(url, out slot!)) Slots[url] = slot = new IconSlot();
            // a tile still lettered asks again: at once when nothing is in flight and nothing failed, or RetrySeconds after
            // a failure (2026-10-25: Expert's gear rows kept their letter all session after one failed download)
            fetch = slot.Image == null && !slot.Busy && (slot.FailedAt == default || (DateTime.UtcNow - slot.FailedAt).TotalSeconds >= RetrySeconds);
            if (fetch) { slot.Busy = true; Fetches++; }
        }
        if (!fetch) return slot;
        var file = PathFor(url);
        // a cached file that won't load is deleted by Load and fetched afresh
        if (File.Exists(file) && Load(file) is { } img) { slot.Image = img; slot.Busy = false; return slot; }
        var t = Task.Run(() => FetchAsync(url, file, slot));   // off the UI thread from the first await, so a render can wait on it
        lock (Lock) Pending.Add(t);
        return slot;
    }

    static async Task FetchAsync(string url, string file, IconSlot slot)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            if (bytes.Length == 0) throw new IOException("empty icon");
            Directory.CreateDirectory(Dir);
            var part = file + ".part";
            await File.WriteAllBytesAsync(part, bytes).ConfigureAwait(false);
            File.Move(part, file, overwrite: true);
            var img = Load(file) ?? throw new IOException("the icon won't load");
            // not awaited: a layout render waits for the fetch on the UI thread and applies the image when it next pumps
            if (_ui != null) _ = _ui.InvokeAsync(() => { slot.Image = img; slot.FailedAt = default; slot.Busy = false; });
            else { slot.FailedAt = default; slot.Busy = false; }
        }
        catch (Exception) { slot.FailedAt = DateTime.UtcNow; slot.Busy = false; }   // 404, offline, a bad file: the tile stays lettered and asks again after RetrySeconds
        finally { Gate.Release(); }
    }

    static ImageSource? Load(string file)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = Pixels;
            bmp.UriSource = new Uri(file);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            try { File.Delete(file); } catch (Exception) { /* a half-written file: gone next time */ }
            return null;
        }
    }

    /// <summary>The cache file is the icon's own file name, which the CDN keeps unique; anything odd is hashed instead.</summary>
    static string PathFor(string url)
    {
        string name;
        try { name = Path.GetFileName(new Uri(url).AbsolutePath); } catch (UriFormatException) { name = ""; }
        if (name.Length == 0 || name.Length > 80 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) name = Fnv(url).ToString("x8") + ".png";
        return Path.Combine(Dir, name);
    }

    static uint Fnv(string s) { uint h = 2166136261; foreach (var c in s) { h ^= c; h *= 16777619; } return h; }

    /// <summary>Waits for the fetches in flight, for a layout render that should show the icons. Never longer than the timeout.</summary>
    public static void Drain(TimeSpan timeout)
    {
        Task[] tasks;
        lock (Lock) tasks = Pending.ToArray();
        if (tasks.Length > 0) { try { Task.WaitAll(tasks, timeout); } catch (AggregateException) { /* a failed fetch is already recorded */ } }
        lock (Lock) Pending.RemoveAll(t => t.IsCompleted);
    }
}
