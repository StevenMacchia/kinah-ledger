using System.Text.RegularExpressions;

namespace KinahLedger.Platform;

/// <summary>
/// What's new (0.9.12): the release notes of this very version travel inside the exe (whats-new.md, embedded from
/// release-notes/v&lt;Version&gt;.md by the project file), and the window shows their bullets once after an update
/// (Ui/MainWindow.WhatsNew.cs). The notes are written one way: a one-line summary, then bullets
/// "- **Bold lead.** the rest of the sentence.", then "Installed copies update by themselves in the background."
/// Only the bullets are shown; the summary and the closing line are left out.
/// </summary>
public static class WhatsNew
{
    /// <summary>One bullet: the bold lead ("A zone row is a button.") and the rest of the sentence after it (may be empty).</summary>
    public sealed record Note(string Lead, string Rest);

    public const string ResourceName = "whats-new.md";

    static readonly Regex Bullet = new(@"^\s*[-*]\s+(?<body>.+)$", RegexOptions.Compiled);
    static readonly Regex BoldLead = new(@"^\*\*(?<lead>.+?)\*\*\s*(?<rest>.*)$", RegexOptions.Compiled);

    /// <summary>This build's notes, or none in a build made without a notes file (a dev build): then nothing shows.</summary>
    public static IReadOnlyList<Note> Built()
    {
        try
        {
            using var s = typeof(WhatsNew).Assembly.GetManifestResourceStream(ResourceName);
            if (s == null) return Array.Empty<Note>();
            using var r = new StreamReader(s);
            return Parse(r.ReadToEnd());
        }
        catch (Exception) { return Array.Empty<Note>(); }
    }

    /// <summary>
    /// The bullets of a notes file, each split into its bold lead and the rest. A bullet with no bold lead is all Rest; a line that is not a bullet (the summary, the closing line) is left out.
    /// </summary>
    public static IReadOnlyList<Note> Parse(string? text)
    {
        var list = new List<Note>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var m = Bullet.Match(raw);
            if (!m.Success) continue;
            var body = m.Groups["body"].Value.Trim();
            if (body.StartsWith("Installed copies update", StringComparison.OrdinalIgnoreCase)) continue;
            var b = BoldLead.Match(body);
            var note = b.Success ? new Note(Plain(b.Groups["lead"].Value), Plain(b.Groups["rest"].Value)) : new Note("", Plain(body));
            if (note.Lead.Length + note.Rest.Length > 0) list.Add(note);
        }
        return list;
    }

    /// <summary>Markdown a TextBlock can't draw: links keep their text, stray bold and code marks go.</summary>
    static string Plain(string s)
    {
        s = Regex.Replace(s, @"\[(?<t>[^\]]+)\]\([^)]*\)", "${t}");
        s = s.Replace("**", "").Replace("`", "");
        return s.Trim();
    }

    /// <summary>
    /// Whether the card shows at this start, and what is stored as seen. <paramref name="seen"/> is the version whose notes
    /// were put away (ui.json WhatsNewSeen), <paramref name="lastRun"/> the version that last opened the window (ui.json
    /// LastVersion, read before this start writes it). Nothing seen yet: a fresh install (nothing ever ran) shows nothing
    /// and counts this version as seen, the Welcome card is there already; a copy updated from before 0.9.12, which never
    /// had the card, shows it. Something seen: shown when it isn't this version. No notes in this build: never.
    /// </summary>
    public static (bool Show, string Seen) Due(string seen, string lastRun, string current, bool hasNotes)
    {
        if (seen.Length == 0)
        {
            bool updated = lastRun.Length > 0 && lastRun != current;
            if (!updated || !hasNotes) return (false, current);
            return (true, seen);
        }
        if (seen == current) return (false, seen);
        return hasNotes ? (true, seen) : (false, current);
    }
}
