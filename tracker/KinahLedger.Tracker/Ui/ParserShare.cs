using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// Every view of a fight on the Parser page as a picture to paste (Steven, 2026-11-17, a kept pull open: "make everything
/// from here copyable", "make timeline sharable as well as a copy/paste picture"): the hero card, the group's table, the
/// timeline, the damage taken, the compare, or all of them stacked, each in the meter share picture's frame (Ui/MeterShare.cs:
/// the slim line with the boss's portrait, place, mode and day; the mark and kinahledger.com at the foot), drawn at twice its
/// size, settled, never a screenshot. The views are the page's own (Ui/ParserCommand.cs), drawn for a picture (no tabs, no
/// chevrons) in Night's colours whatever look the window has, so they sit right on the dark frame. The tables also copy as
/// text, tab-separated, for Discord or a spreadsheet. Other members' names follow Settings › In game › Parser › Hide other
/// names (off by default). Nothing is uploaded.
/// </summary>
public static class ParserShare
{
    /// <summary>The views a picture can hold: the page's tabs, the hero, everything stacked, and a member's abilities (their inspect view).</summary>
    public const string Hero = "hero", Group = "group", Compare = "compare", Timeline = "timeline", Taken = "taken", All = "all", Abilities = "abilities";
    /// <summary>The Timeline tab's view (Ui/ParserTimeline.cs): <see cref="Timeline"/> is the Graphs tab's, named so before the tab was.</summary>
    public const string Track = "track";

    /// <summary>The views' width in the picture, in DIPs: the group's every column with room for a name and its tags ("Member 5 joined late left").</summary>
    public const double ContentWidth = 700;
    /// <summary>The PNG's width: the views, the frame's sides and its edges, at twice the size (1564 px).</summary>
    public const int PixelWidth = (int)((ContentWidth + 82) * 2);

    /// <summary>What the window lends the views: its Parser switches, the group's coverage line, and the member whose abilities are copied (their id).</summary>
    public sealed record Parts(bool Details, bool Kills, bool Deaths, bool HideNames, Func<Encounter, FrameworkElement?> GroupFoot, long? Member = null,
        string? CompareWith = null, string? CompareMember = null);

    /// <summary>The member <see cref="Abilities"/> is about, when they are in the fight and have abilities.</summary>
    static MemberView? MemberOf(FightView v, Parts p) => p.Member is long id ? v.Members.FirstOrDefault(m => m.F.Id == id && m.Abilities.Count > 0) : null;

    /// <summary>The page's tab as a view ("Group" is "group").</summary>
    public static string ViewOfTab(string tab) => tab switch { "Compare" => Compare, "Timeline" => Timeline, "Track" => Track, "Taken" => Taken, _ => Group };

    /// <summary>A view the fight has something for (Timeline, Taken and Compare only once its record carries them).</summary>
    public static bool Has(FightView v, string view) => view switch
    {
        Compare => v.HasCompare,
        Timeline => v.HasTimeline && v.IsBossPull,
        Track => v.HasPullTimeline,
        Taken => v.HasTaken,
        Group => v.Members.Count > 0,
        Abilities => v.Members.Any(m => m.Abilities.Count > 0),
        _ => true,
    };

    /// <summary>The views Everything stacks: the hero, the group, the timeline and the damage taken (the ones the fight has).</summary>
    public static List<string> Stack(FightView v) => new[] { Hero, Group, Timeline, Taken }.Where(x => Has(v, x)).ToList();

    /// <summary>The fight as the picture shows it: the other members' names hidden when the setting says so (a copy).</summary>
    static Encounter Shown(Encounter e, Parts p) => p.HideNames ? MeterShare.Hidden(e) : e;

    /// <summary>The words a view is called by, in the language in use ("the Group table").</summary>
    public static string Name(string view) => view switch
    {
        Hero => L.T("Summary card"),
        Group => L.T("Group table"),
        Compare => L.T("Compare"),
        Timeline => L.T("Graphs"),
        Track => L.T("Timeline"),
        Taken => L.T("Damage taken"),
        Abilities => L.T("Abilities table"),
        _ => L.T("Everything"),
    };

    static CommandCtx Ctx(Parts p) => new()
    {
        Share = true, Details = p.Details, Kills = p.Kills, Deaths = p.Deaths, CompareWith = p.CompareWith, CompareMember = p.CompareMember,
        GroupHead = _ => null, GroupFoot = p.GroupFoot, Tip = _ => null,
    };

    /// <summary>One view's face at the picture's width, tagged "share-view-VIEW".</summary>
    static FrameworkElement Face(FightView v, string view, bool live, Parts p)
    {
        var ctx = Ctx(p);
        FrameworkElement body = view switch
        {
            Hero => ParserCommand.Hero(v, ctx, live, Array.Empty<FrameworkElement>()),
            Compare => ParserCommand.Compare(v, null, ctx),
            Track => (FrameworkElement?)ParserTimeline.View(v, ctx, null, ContentWidth) ?? ParserCommand.Group(v, ctx, live, detail: !live),
            Timeline => ParserCommand.Timeline(v, ContentWidth - 34),   // the glass's sides and edges
            Taken => ParserCommand.Taken(v),
            // the member's card and their abilities, as the page shows them opened (no way back drawn)
            Abilities when MemberOf(v, p) is { } m => ParserCommand.Inspect(v, m, ctx),
            _ => ParserCommand.Group(v, ctx, live, detail: !live),
        };
        body.Tag = "share-view-" + view;
        return body;
    }

    /// <summary>The face of <paramref name="view"/> (Everything: each view the fight has, stacked).</summary>
    static FrameworkElement Faces(FightView v, string view, bool live, Parts p)
    {
        var box = new StackPanel { Width = ContentWidth };
        var views = view == All ? Stack(v) : new List<string> { view };
        foreach (var x in views)
        {
            var f = Face(v, x, live, p);
            f.Margin = new Thickness(0, box.Children.Count > 0 ? 12 : 0, 0, 0);
            box.Children.Add(f);
        }
        return box;
    }

    /// <summary>
    /// The window's skin is the frame's for the moment the picture is drawn: the views read the window's brushes, and the
    /// picture is dark in every look (Night, unless the window already has a dark look of its own; the themed looks draw their
    /// own cards, so they take Night too). Put back at once; nothing is drawn on screen between.
    /// </summary>
    static T InDark<T>(Func<T> draw)
    {
        var was = Skins.Current;
        bool swap = was is not ("night" or "abyss");
        if (swap) Skins.Apply("night");
        try { return draw(); }
        finally { if (swap) Skins.Apply(was); }
    }

    static FightView ViewOf(Encounter e, DateTime now, List<Encounter> past, Action<FightView>? sample, string? against = null)
    {
        var v = FightView.Of(e, now, past, against);
        sample?.Invoke(v);
        return v;
    }

    static T With<T>(Encounter e, DateTime now, List<Encounter> past, string view, Parts p, Action<FightView>? sample, Func<Border, T> use)
    {
        var shown = Shown(e, p);
        var v = ViewOf(shown, now, past, sample, p.CompareWith);
        if (view == Abilities ? MemberOf(v, p) == null : view != All && !Has(v, view)) view = Group;
        return MeterShare.Framed(shown, now, () => Faces(v, view, e.Live, p), "parser-share-" + view, use);
    }

    /// <summary>The picture laid out at its size (for the tests): the frame round the view's face, in the window's current look.</summary>
    public static Border Laid(Encounter e, DateTime now, List<Encounter> past, string view, Parts p, Action<FightView>? sample = null) =>
        With(e, now, past, view, p, sample, b => b);

    /// <summary>The PNG: the view in the share frame at twice its size (1564 px wide), in the dark look.</summary>
    public static BitmapSource Render(Encounter e, DateTime now, List<Encounter> past, string view, Parts p, Action<FightView>? sample = null) =>
        InDark(() => With(e, now, past, view, p, sample, b => MeterShare.Snap(b, "parser share picture")));

    /// <summary>"kinah-ledger-2026-11-17-2130-fight-Talisra-of-the-Void-timeline.png": the meter picture's name with the view's.</summary>
    public static string FileName(Encounter e, string view) => MeterShare.FileName(e)[..^4] + "-" + view + ".png";

    // ---- as text (right-click › Copy as text on a table, and the Share menu's own line) ----

    /// <summary>A view that copies as text: the group's table, the damage taken, the compare, a member's abilities.</summary>
    public static bool HasText(FightView v, string view) => view is Group or Taken or Compare or Abilities && Has(v, view);

    /// <summary>
    /// The table as text: a line naming the fight, then a head row and a row a member (or an ability), the cells split by
    /// tabs, so it pastes into a spreadsheet's columns as it does into Discord. Figures whole (no 1.2M), so a sheet can sum them.
    /// </summary>
    public static string Text(Encounter e, DateTime now, List<Encounter> past, string view, Parts p, Action<FightView>? sample = null)
    {
        var shown = Shown(e, p);
        var v = ViewOf(shown, now, past, sample, p.CompareWith);
        var sb = new StringBuilder();
        var bits = new List<string> { shown.Title };
        var member = view == Abilities ? MemberOf(v, p) : null;
        if (member != null) bits.Add(ParserRows.NameOf(member.F));
        if (shown.Live) bits.Add($"{L.T("Fighting")} {BossParser.Clock(shown.Duration(now))}");
        else bits.Add($"{shown.OutcomeLabel} · {BossParser.Clock(shown.Duration(now))}");
        if (PK.WhereOf(shown) is { Length: > 0 } where) bits.Add(where);
        bits.Add(L.Day(shown.Start.ToLocalTime()));
        sb.Append(string.Join(" · ", bits)).Append("\r\n");
        void Row(IEnumerable<string> cells) => sb.Append(string.Join("\t", cells.Select(c => c.Replace('\t', ' ').Replace("\r", "").Replace('\n', ' ')))).Append("\r\n");
        static string N(double x) => Math.Round(x).ToString("0", CultureInfo.InvariantCulture);
        static string P(double x) => PK.Pct(x);
        string Cls(Fighter f) => f.Class.Length > 0 ? L.T(ClassArt.Title(f.Class)) : "";
        switch (view)
        {
            case Taken:
            {
                Row(new[] { "#", L.T("Member"), L.T("Class"), L.T("Taken"), L.T("Per second"), L.T("Deaths") });
                int i = 0;
                foreach (var m in v.Members.Where(m => m.Taken != null).OrderByDescending(m => m.Taken))
                    Row(new[] { (++i).ToString(CultureInfo.InvariantCulture), ParserRows.NameOf(m.F), Cls(m.F), N(m.Taken ?? 0), N((m.Taken ?? 0) / Math.Max(1, v.Seconds)), m.Deaths.ToString(CultureInfo.InvariantCulture) });
                break;
            }
            case Compare when v.Diff is { You: { } you0 } diff:
            {
                // the member picked on the tab (else you), against the pull picked (else your best kill): both pulls' figures, whole
                var cm = (p.CompareMember is { } who ? diff.Members.FirstOrDefault(x => !x.You && x.A.Name.Equals(who, StringComparison.OrdinalIgnoreCase)) : null) ?? you0;
                string cls = cm.A.Class.Length > 0 ? cm.A.Class : cm.B.Class;
                // one table: DPS this pull and the other (its day and length in the head), the change, then damage, casts, crit
                // and share in both; the member's total first, then each ability
                bool casts = cm.HasCasts, crit = cm.HasCrit;
                static string Other(string col) => L.F("{0}, other pull", L.T(col));
                var head = new List<string> { L.T("Ability"), L.T("This pull"), L.F("vs {0}", $"{L.DayTime(diff.B.Start.ToLocalTime())} · {BossParser.Clock(TimeSpan.FromSeconds(diff.BLength))}"), L.T("DPS change"), L.T("Damage"), Other("Damage") };
                if (casts) { head.Add(L.T("Casts")); head.Add(Other("Casts")); }
                if (crit) { head.Add(L.T("Crit")); head.Add(Other("Crit")); }
                head.Add(L.T("Share")); head.Add(Other("Share"));
                Row(head);
                var total = new List<string> { cm.You ? ParserRows.NameOf(cm.A) : cm.A.Name, N(cm.DpsA), N(cm.DpsB), cm.DpsChange is { } dc ? PK.Signed(dc) : "", N(cm.DamageA), N(cm.DamageB) };
                if (casts) { total.Add(cm.Skills.Sum(x => x.CastsA ?? 0).ToString(CultureInfo.InvariantCulture)); total.Add(cm.Skills.Sum(x => x.CastsB ?? 0).ToString(CultureInfo.InvariantCulture)); }
                if (crit) { total.Add(""); total.Add(""); }
                total.Add(P(cm.ShareA)); total.Add(P(cm.ShareB));
                Row(total);
                foreach (var x in cm.Skills)
                {
                    var cells = new List<string> { Parser.Abilities.Name(x.Skill, cls), x.UsedA ? N(x.DpsA) : "", x.UsedB ? N(x.DpsB) : "",
                        x.UsedA && x.UsedB && x.DpsChange is { } c ? PK.Signed(c) : x.UsedA ? L.T("new") : L.T("not used"), x.UsedA ? N(x.DamageA) : "", x.UsedB ? N(x.DamageB) : "" };
                    if (casts) { cells.Add(x.CastsA?.ToString(CultureInfo.InvariantCulture) ?? ""); cells.Add(x.CastsB?.ToString(CultureInfo.InvariantCulture) ?? ""); }
                    if (crit) { cells.Add(x.CritA is { } a1 && x.UsedA ? P(a1) : ""); cells.Add(x.CritB is { } b1 && x.UsedB ? P(b1) : ""); }
                    cells.Add(x.UsedA ? P(x.ShareA) : ""); cells.Add(x.UsedB ? P(x.ShareB) : "");
                    Row(cells);
                }
                break;
            }
            case Abilities when member != null:
            {
                // the member's abilities by damage, every column the record has (none made up: a missing figure is an empty cell)
                var ab = member.Abilities;
                bool casts = ab.Any(a => a.Casts != null), crit = ab.Any(a => a.Crit != null), max = ab.Any(a => a.Max != null), min = ab.Any(a => a.Min != null);
                var head = new List<string> { L.T("Ability"), L.T("Damage"), L.T("Share"), L.T("DPS") };
                if (casts) head.Add(L.T("Casts"));
                head.Add(L.T("Hits"));
                if (crit) head.Add(L.T("Crit"));
                if (max) head.Add(L.T("Max"));
                if (min) head.Add(L.T("Min"));
                head.Add(L.T("Average"));
                Row(head);
                foreach (var a in ab.OrderByDescending(a => a.Damage))
                {
                    var cells = new List<string> { a.Name, a.Damage.ToString(CultureInfo.InvariantCulture), P(a.Share), a.DpsOrNull is { } d ? N(d) : "" };
                    if (casts) cells.Add(a.Casts?.ToString(CultureInfo.InvariantCulture) ?? "");
                    cells.Add(a.Hits > 0 ? a.Hits.ToString(CultureInfo.InvariantCulture) : "");
                    if (crit) cells.Add(a.Crit is { } c ? P(c) : "");
                    if (max) cells.Add(a.Max is { } x ? x.ToString(CultureInfo.InvariantCulture) : "");
                    if (min) cells.Add(a.Min is { } y ? y.ToString(CultureInfo.InvariantCulture) : "");
                    cells.Add(a.Avg is { } g ? N(g) : "");
                    Row(cells);
                }
                break;
            }
            default:
            {
                bool dmg = v.HasDamage, crit = v.HasCrit, max = v.HasMax;
                var head = new List<string> { "#", L.T("Member"), L.T("Class"), L.T("CP") };
                if (dmg) head.AddRange(new[] { L.T("Damage"), L.T("DPS"), L.T("Share") });
                if (crit) head.Add(L.T("Crit"));
                if (max) head.Add(L.T("Max hit"));
                head.Add(L.T("Deaths"));
                Row(head);
                int i = 0;
                foreach (var m in dmg ? v.Members.OrderBy(m => m.Rank) : v.Members.AsEnumerable())
                {
                    var cells = new List<string> { (dmg ? m.Rank : ++i).ToString(CultureInfo.InvariantCulture), ParserRows.NameOf(m.F), Cls(m.F), m.F.CombatPower > 0 ? m.F.CombatPower.ToString(CultureInfo.InvariantCulture) : "" };
                    if (dmg) cells.AddRange(new[] { m.Damage.ToString(CultureInfo.InvariantCulture), m.DpsOrNull is { } d ? N(d) : "", P(m.Share) });
                    if (crit) cells.Add(m.Crit is { } c ? P(c) : "");
                    if (max) cells.Add(m.MaxHit is { } x ? x.ToString(CultureInfo.InvariantCulture) : "");
                    cells.Add(m.Deaths.ToString(CultureInfo.InvariantCulture));
                    Row(cells);
                }
                break;
            }
        }
        return sb.ToString();
    }
}
