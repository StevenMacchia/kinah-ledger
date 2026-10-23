using System.Text.RegularExpressions;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// What the chat says about an event, beside the minimap (2026-10-23, from Steven's reads of Accursed Sword, Odyle Flight
/// Frenzy, Incursion of the Spirits, Not This Tile?, Goldrin's Treasure, Jump Jump and Hidden Lugi):
/// <list type="bullet">
/// <item>"&lt;player&gt; joined event." prints in every event's lobby, 0 to 10 s before the minimap names the event. It says an
/// event is starting, not which one. Read garbled often ("joifed event", "Jodned evento"), so it is matched loosely.</item>
/// <item>An event's own NPC speaks in it: "[Lugi Manager] Transform into a Lugi…" is Hidden Lugi; "[Shugo Festival
/// Manager] Welcome to the Jump Jump tournament, nyerk!" is the Shugo Festival, and its words name the game.</item>
/// </list>
/// A player's chat ("[Name] …") never counts, and nothing here keeps a player's name.
/// </summary>
public static class EventWitness
{
    /// <summary>The events' own NPCs as seen in play: the NPC, the event it hosts (or null), the family (or null).</summary>
    static readonly (string Npc, string? Event, string? Family)[] Npcs =
    {
        ("Lugi Manager", "Hidden Lugi", null),
        ("Shugo Festival Manager", null, "Shugo Festival"),
    };

    /// <summary>A "&lt;player&gt; joined event." line: a system line (no "[Name]" in front), short, ending in "joined event" read within two letters.</summary>
    public static bool Joined(string line)
    {
        var t = line.Trim();
        if (t.Length < 12 || t.Length > 48 || t.StartsWith('[')) return false;
        var letters = new string(t.ToLowerInvariant().Where(char.IsLetter).ToArray());
        if (letters.Length < 11) return false;
        // the end of the line, allowing a stray letter or two after it ("evente", "events")
        for (int cut = 0; cut <= 2 && letters.Length - cut >= 11; cut++)
        {
            var end = letters[..(letters.Length - cut)];
            for (int len = 10; len <= 13 && len <= end.Length; len++)
                if (Edit(end[^len..], "joinedevent") <= 2) return true;
        }
        return false;
    }

    /// <summary>The event NPC a line starts with ("[Lugi Manager] …", read within two letters), or null.</summary>
    public static (string Npc, string? Event, string? Family)? Npc(string line)
    {
        var m = Regex.Match(line.TrimStart(), @"^\[([^\]]{4,40})\]");
        if (!m.Success) return null;
        var said = Squash(m.Groups[1].Value);
        foreach (var n in Npcs)
            if (Edit(said, Squash(n.Npc)) <= Math.Max(1, n.Npc.Length / 7)) return n;
        return null;
    }

    /// <summary>
    /// What a read says about the event: each line of an event NPC's message (its wrapped lines too, up to the next
    /// "[…]" line) and each "joined event" line. Returns (source, event or family named, or null for "an event").
    /// </summary>
    public static List<(string Source, string? Name)> Read(IReadOnlyList<string> lines)
    {
        var said = new List<(string, string?)>();
        (string Npc, string? Event, string? Family)? npc = null;
        var text = "";
        void Flush()
        {
            if (npc is not { } n) return;
            // the message's words can name the game ("Welcome to the Jump Jump tournament"): that is more exact than the NPC
            var named = ZoneLocator.EventIn(text);
            if (named != null && n.Family != null && !string.Equals(named, n.Family, StringComparison.OrdinalIgnoreCase) && ZoneLocator.FamilyOf(named) == n.Family)
                said.Add(($"chat: {n.Npc} names it", named));
            else said.Add(($"chat: {n.Npc}", n.Event ?? n.Family));
            npc = null; text = "";
        }
        foreach (var l in lines)
        {
            var t = l.Trim();
            if (t.StartsWith('[')) { Flush(); if (Npc(t) is { } found) { npc = found; text = t[(t.IndexOf(']') + 1)..]; } continue; }
            if (Joined(t)) { Flush(); said.Add(("chat: joined event", null)); continue; }
            if (npc != null) text += " " + t;
        }
        Flush();
        return said;
    }

    static string Squash(string s) => new(s.ToLowerInvariant().Where(char.IsLetter).ToArray());

    static int Edit(string a, string b)
    {
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
