using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// The item list the site publishes (id, name, grade and icon of every item in the game's dictionary), kept under
/// %LocalAppData%\KinahLedger\items.json so that drops leave this PC under their real item ids and show their icons.
/// Checked once per start with the saved ETag: an unchanged list costs one small request, and offline the saved copy
/// is used. Nothing about the player goes up in that request.
/// </summary>
public static class DictionaryCache
{
    public const string Url = "https://kinahledger.com/data/items.json";
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger");
    public static string FilePath => Path.Combine(Dir, "items.json");
    static string EtagPath => FilePath + ".etag";
    public static string Note { get; private set; } = "";

    /// <summary>Fetches a newer list when there is one and merges it into the dictionary. The number of items merged; 0 when unchanged, offline or refused.</summary>
    public static async Task<int> RefreshAsync(ItemDictionary into, string url = Url)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + Upload.RunPayload.AppVersion);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (File.Exists(FilePath) && File.Exists(EtagPath))
            {
                var tag = File.ReadAllText(EtagPath).Trim();
                if (tag.Length > 0 && EntityTagHeaderValue.TryParse(tag, out var etag)) req.Headers.IfNoneMatch.Add(etag);
            }
            using var res = await http.SendAsync(req).ConfigureAwait(false);
            if (res.StatusCode == HttpStatusCode.NotModified) { Note = "item list unchanged"; return 0; }
            if (!res.IsSuccessStatusCode) { Note = $"item list: HTTP {(int)res.StatusCode}"; return 0; }
            var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            // only a real list replaces the saved one
            using (var doc = JsonDocument.Parse(body))
                if (!doc.RootElement.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 100) { Note = "item list: unexpected content"; return 0; }
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, body); File.Move(tmp, FilePath, true);
            File.WriteAllText(EtagPath, res.Headers.ETag?.ToString() ?? "");
            int n = into.Merge(FilePath);
            Note = $"item list updated: {n} items";
            return n;
        }
        catch (Exception ex) { Note = "item list: " + ex.Message; return 0; }
    }
}
