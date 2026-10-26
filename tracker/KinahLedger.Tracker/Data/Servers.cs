using KinahLedger.Upload;

namespace KinahLedger.Data;

/// <summary>One of the game's regions. The key is the first part of every server key ("na_e-siel-elyos") and never changes once data has been shared under it.</summary>
public sealed record GameRegion(string Key, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One home server: a region and a faction's world name. The same names exist in every region, so the key carries both.</summary>
public sealed record GameServer(GameRegion Region, string Name, string Faction)
{
    /// <summary>As the window shows it and the prefs keep it: "Siel (Elyos)".</summary>
    public string Label => $"{Name} ({Faction})";
    /// <summary>What prices and runs are filed under on the site: "na_e-siel-elyos".</summary>
    public string Key => ServerKeys.Build(Region.Key, Label);
    public override string ToString() => Label;
}

/// <summary>
/// The global service's home servers: 53 Elyos/Asmodian pairs in five regions, each pair one world with a name per
/// faction. Checked 26 October 2026 against NCSOFT's "New Server and Matchmaking Information" notice (kept current;
/// Steam mirror of the plaync notice board) and the aion2.exchange server feed. The 5 October launch had 43 pairs; ten
/// opened later (Europe 5, NA East 1, Asia 4). Every shared number is per server (prices outright, drops once the site splits them),
/// so the window asks for this before anything else. Kept as data: a new server is one entry here.
/// </summary>
public static class Servers
{
    public static readonly GameRegion[] Regions =
    {
        new("na_e", "NA East"), new("na_w", "NA West"), new("eu", "Europe"), new("latam", "Latin America"), new("asia", "Asia")
    };

    // (Elyos name, Asmodian name) per region, Advanced Access servers first, then the ones added at launch, then later
    // openings. Append new pairs at the end: a key, once data is filed under it, never changes.
    static readonly Dictionary<string, (string elyos, string asmodian)[]> Pairs = new()
    {
        ["eu"] = new[]
        {
            ("Siel", "Israphel"), ("Nezekan", "Zikel"), ("Vaizel", "Triniel"), ("Kaisinel", "Lumiel"), ("Yustiel", "Marchutan"),
            ("Ariel", "Azphel"), ("Fregion", "Ereshkigal"), ("Meslamtaeda", "Beritra"), ("Hithanya", "Nemon"),
            ("Nania", "Hadala"), ("Tahavatha", "Ludra"), ("Luteros", "Ulgorn"), ("Phernos", "Munin"), ("Daminu", "Odar"),
            ("Kasaka", "Zemurru"), ("Bakarma", "Kromede"), ("Tsenka", "Quai"), ("Kochi", "Baba"),
            ("Ishtar", "Fafnir"), ("Tiamat", "Indnath"), ("Gauss", "Agnita"), ("Lamuatan", "Atiel"), ("Nathara", "Tassin")
        },
        ["na_e"] = new[]
        {
            ("Siel", "Israphel"), ("Nezekan", "Zikel"), ("Vaizel", "Triniel"), ("Kaisinel", "Lumiel"), ("Yustiel", "Marchutan"),
            ("Ariel", "Azphel"), ("Fregion", "Ereshkigal"), ("Meslamtaeda", "Beritra"), ("Hithanya", "Nemon")
        },
        ["na_w"] = new[] { ("Siel", "Israphel"), ("Nezekan", "Zikel"), ("Vaizel", "Triniel"), ("Kaisinel", "Lumiel"), ("Yustiel", "Marchutan") },
        ["latam"] = new[] { ("Siel", "Israphel"), ("Nezekan", "Zikel"), ("Vaizel", "Triniel"), ("Kaisinel", "Lumiel"), ("Yustiel", "Marchutan"), ("Ariel", "Azphel") },
        ["asia"] = new[]
        {
            ("Siel", "Israphel"), ("Nezekan", "Zikel"), ("Vaizel", "Triniel"), ("Kaisinel", "Lumiel"), ("Yustiel", "Marchutan"),
            ("Ariel", "Azphel"), ("Fregion", "Ereshkigal"), ("Meslamtaeda", "Beritra"), ("Hithanya", "Nemon"), ("Nania", "Hadala")
        },
    };

    public static GameRegion? Region(string? key) => Regions.FirstOrDefault(r => r.Key == key);

    /// <summary>The servers of one region as the pickers show them: Elyos A to Z, then Asmodian A to Z (Steven, 2026-10-26:
    /// the pair order "looks messy"). Pairs above stay in the game's order; keys don't depend on either.</summary>
    public static IReadOnlyList<GameServer> In(string regionKey)
    {
        var region = Region(regionKey);
        if (region == null || !Pairs.TryGetValue(regionKey, out var pairs)) return Array.Empty<GameServer>();
        var list = new List<GameServer>(pairs.Length * 2);
        foreach (var (elyos, asmodian) in pairs) { list.Add(new GameServer(region, elyos, "Elyos")); list.Add(new GameServer(region, asmodian, "Asmodian")); }
        return list.OrderBy(s => s.Faction == "Elyos" ? 0 : 1).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IEnumerable<GameServer> All => Regions.SelectMany(r => In(r.Key));

    /// <summary>The server for a kept label ("Siel (Elyos)") in a region, or null when it isn't one of the known ones.</summary>
    public static GameServer? Find(string regionKey, string? label)
    {
        var want = (label ?? "").Trim();
        return want.Length == 0 ? null : In(regionKey).FirstOrDefault(s => string.Equals(s.Label, want, StringComparison.OrdinalIgnoreCase));
    }
}
