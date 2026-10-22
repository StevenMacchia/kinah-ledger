using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// Each enemy's picture: data/mob-images.json, built into the exe, a link per name on the enemy list to MetaBot.GG's
/// copy of the game's portrait. The app fetches a picture once, when the enemy first shows in Kinah by enemy, and keeps
/// it with the item icons. MetaBot.GG's terms ask for a link back, which the window shows beside the pictures.
/// </summary>
public static class MobImages
{
    public const string CreditUrl = "https://metabot.gg/en/aion-2/monsters";
    static readonly Dictionary<string, string> Urls = Load();

    static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var s = KinahLedger.Data.DataFiles.Open("mob-images.json");
            if (s == null) return d;
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Object) return d;
            foreach (var p in images.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { } u && u.StartsWith("https://", StringComparison.Ordinal)) d[p.Name] = u;
        }
        catch (Exception) { /* no pictures: rows keep a lettered tile */ }
        return d;
    }

    /// <summary>The picture's link for an enemy on the list, or "" when there is none.</summary>
    public static string Url(string name) => Urls.GetValueOrDefault(name.Trim()) ?? "";
    public static int Count => Urls.Count;
}
