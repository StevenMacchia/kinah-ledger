using System.IO;
using System.Windows;
using System.Windows.Media;
using KinahLedger.Packets;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// ui-shot --parser pvp: the PvP panel (Parser/PvpKills.cs) in every meter look, compact and expanded (pvp/ui-pvp-LOOK.png,
/// ui-pvp-LOOK-compact.png), before its first kill (ui-pvp-empty-LOOK.png, a few looks), and a row's skills card
/// (ui-pvp-card*.png). Each frame's spill and cut-off figures go to pvp/spill.txt. The kills are a made-up sample run through
/// the panel's own rules (PvpSample); nothing is tracked, saved or sent.
/// </summary>
public static partial class WpfApp
{
    /// <summary>
    /// Three kills through the real rules, newest first: a kill of Kaelith (your hits over 14 s), an assist on Veyra that Tilly
    /// landed (your hits 6 s before), a kill of Morrigan (a longer fight). Your class is a Templar's, so the skills are named.
    /// </summary>
    internal static PvpKills PvpSample(DateTime now)
    {
        var p = new PvpKills { Self = () => ("", "Templar"), Place = "Abyss" };
        var rnd = new Random(7);
        int[] skills = { 12090000, 12040000, 12100000, 12010000, 12060003 };
        void Fight(uint victim, string name, DateTime end, int seconds, int hitsEach, int lo, int hi)
        {
            for (int s = 0; s < seconds; s++)
                for (int k = 0; k < hitsEach; k++)
                {
                    int sk = skills[(s + k) % skills.Length];
                    p.Take(new FightEvent(end.AddSeconds(-seconds + s + k * 0.3), FightKind.Hit, FightFlags.VictimPlayer | FightFlags.VictimEnemy, victim, 1, (uint)sk, rnd.Next(lo, hi), 0, name, ""));
                }
        }
        var t3 = now.AddMinutes(-6); Fight(3003, "Morrigan", t3, 38, 2, 9_000, 26_000);
        p.Take(new FightEvent(t3, FightKind.Kill, FightFlags.Mine | FightFlags.VictimPlayer | FightFlags.VictimEnemy, 3003, 1, 0, 0, 0, "Morrigan", ""));
        var t2 = now.AddMinutes(-2); Fight(3002, "Veyra", t2.AddSeconds(-6), 7, 1, 8_000, 19_000);
        p.Take(new FightEvent(t2, FightKind.Kill, FightFlags.OtherKiller | FightFlags.VictimPlayer | FightFlags.VictimEnemy, 3002, 4004, 0, 0, 0, "Veyra", "Tilly"));
        var t1 = now.AddSeconds(-20); Fight(3001, "Kaelith", t1, 14, 2, 10_000, 24_000);
        p.Take(new FightEvent(t1, FightKind.Kill, FightFlags.Mine | FightFlags.VictimPlayer | FightFlags.VictimEnemy, 3001, 1, 0, 0, 0, "Kaelith", ""));
        return p;
    }

    static void ShotPvp(string dir)
    {
        var now = new DateTime(2026, 11, 20, 21, 30, 0, DateTimeKind.Utc);
        string sub = Path.Combine(dir, "pvp");
        Directory.CreateDirectory(sub);
        try { File.Delete(Path.Combine(sub, "spill.txt")); } catch (IOException) { }
        var board = PvpSample(now).Board(now);
        var empty = PvpKills.BoardOf(Array.Empty<PvpKill>(), "Abyss", now);
        bool still0 = Motion.Still, exp0 = ParserRows.ExpandedPref;
        double width0 = HudStackLook.FightWidth;
        var only = (Environment.GetEnvironmentVariable("KL_THEME_LOOK") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            Motion.Still = true;
            foreach (var look in MeterLooks.Where(l => only.Length == 0 || only.Contains(l)))
            {
                foreach (var expanded in new[] { true, false })
                {
                    ParserRows.ExpandedPref = expanded; HudStackLook.FightWidth = width0 + (expanded ? 70 : 0);
                    var name = $"ui-pvp-{look}{(expanded ? "" : "-compact")}.png";
                    var shot = PvpFrame(board, now, look, sub, name);
                    Save(shot, Path.Combine(sub, name), 0, ShotGround);
                }
                if (look is ParserPrefs.StandardLook or HudThemes.ModernGlass or HudThemes.Neon or HudThemes.Fighter or HudThemes.Terminal)
                {
                    ParserRows.ExpandedPref = true; HudStackLook.FightWidth = width0 + 70;
                    var name = $"ui-pvp-empty-{look}.png";
                    Save(PvpFrame(empty, now, look, sub, name), Path.Combine(sub, name), 0, ShotGround);
                }
            }
            // a row's skills card: the Parser's own, and a theme's (Modern Glass, the default look)
            var f = board.Roster[0];
            if (ParserRows.HoverCard(board, f, now) is { } card) Save(card, Path.Combine(sub, "ui-pvp-card.png"), 0, ShotGround);
            if (HudThemes.Make(HudThemes.ModernGlass, true) is IThemeCard tc && MemberCard.Of(board, f, now) is { } mc) Save(tc.DrawCard(mc), Path.Combine(sub, "ui-pvp-card-glass.png"), 0, ShotGround);
            var assist = board.Roster[1];
            if (ParserRows.HoverCard(board, assist, now) is { } card2) Save(card2, Path.Combine(sub, "ui-pvp-card-assist.png"), 0, ShotGround);
        }
        finally { Motion.Still = still0; ParserRows.ExpandedPref = exp0; HudStackLook.FightWidth = width0; }
        Console.WriteLine($"wrote {Path.GetFullPath(sub)}");
    }

    /// <summary>
    /// options-test (KL_OPTIONS_ONLY=pvp alone): the PvP panel in every meter look, compact and expanded: three rows newest
    /// first, each with its name, its damage, Kill or Assist (an assist's "by Tilly", never as Combat Power), no group DPS
    /// and no clock, nothing spilled or cut; a row's skills card is your fight with that player; and when the window shows it
    /// (in PvP with the switch on, not over a boss pull, before a kill only where the meter shows anyway).
    /// </summary>
    static void PvpChecks(MainWindow w, Action<bool, string> check)
    {
        var now = new DateTime(2026, 11, 20, 21, 30, 0, DateTimeKind.Utc);
        var kills = PvpSample(now);
        var board = kills.Board(now);
        var last = kills.Last;
        check(last.Select(k => k.Name).SequenceEqual(new[] { "Kaelith", "Veyra", "Morrigan" }) && last[1] is { Assist: true, Killer: "Tilly" } && last.All(k => k.Damage > 0),
            $"PvP panel sample: Kaelith, Veyra (assist, Tilly's kill), Morrigan, each with damage ({string.Join(", ", last.Select(k => $"{k.Name} {k.Damage:N0}"))})");
        bool still0 = Motion.Still, exp0 = ParserRows.ExpandedPref;
        double width0 = HudStackLook.FightWidth;
        var wrong = new List<string>();
        int looks = 0;
        try
        {
            Motion.Still = true;
            foreach (var look in MeterLooks)
                foreach (var expanded in new[] { true, false })
                {
                    ParserRows.ExpandedPref = expanded; HudStackLook.FightWidth = width0 + (expanded ? 70 : 0);
                    Motion.ForgetFigures();
                    var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = ParserMode.Fights, ShowActivated = false };
                    if (look != ParserPrefs.StandardLook) o.Look = look;
                    o.Draw(board, now);
                    o.Show(); DoEvents();
                    var root = (FrameworkElement)o.Content;
                    var texts = new List<(string S, double Y)>();
                    void Walk(DependencyObject d)
                    {
                        if (d is UIElement u && !u.IsVisible) return;
                        if (d is System.Windows.Controls.TextBlock t && BlockText(t).Trim() is { Length: > 0 } s && t.ActualWidth > 0) texts.Add((s, t.TranslatePoint(new Point(0, 0), root).Y));
                        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
                    }
                    Walk(root);
                    string tag = look + (expanded ? "" : " compact");
                    bool Has(string s) => texts.Any(x => x.S.Contains(s, StringComparison.OrdinalIgnoreCase));
                    double YOf(string s) => texts.Where(x => x.S.Contains(s, StringComparison.OrdinalIgnoreCase)).Select(x => x.Y).DefaultIfEmpty(double.NaN).Min();
                    var miss = new List<string>();
                    foreach (var k in last)
                    {
                        if (!Has(k.Name)) miss.Add(k.Name);
                        if (!Has(Encounter.PvpFigure(k))) miss.Add(Encounter.PvpFigure(k));
                    }
                    if (!Has("Kill")) miss.Add("Kill");
                    if (!Has("Assist")) miss.Add("Assist");
                    if (!Has("by Tilly")) miss.Add("by Tilly");
                    if (!(YOf("Kaelith") < YOf("Veyra") && YOf("Veyra") < YOf("Morrigan"))) miss.Add("newest first");
                    if (texts.Any(x => x.S.Contains("Group DPS", StringComparison.OrdinalIgnoreCase) || x.S.Contains("Uptime", StringComparison.OrdinalIgnoreCase))) miss.Add("no group DPS");
                    if (texts.Any(x => x.S.Contains("CP by", StringComparison.OrdinalIgnoreCase) || x.S.Equals("CP", StringComparison.OrdinalIgnoreCase))) miss.Add("no CP word");
                    if (texts.Any(x => System.Text.RegularExpressions.Regex.IsMatch(x.S, @"(^|\s|T\+)\d{1,2}:\d\d(\s|$)"))) miss.Add("no clock");
                    var spill = Spill(root);
                    var cut = CutOffs(root);
                    if (spill.Length > 0) miss.Add("spill " + spill);
                    if (cut.Count > 0) miss.Add("cut " + string.Join("; ", cut.Take(2)));
                    if (miss.Count > 0) wrong.Add($"{tag}: {string.Join(", ", miss)}");
                    looks++;
                    o.Close();
                }
        }
        finally { Motion.Still = still0; ParserRows.ExpandedPref = exp0; HudStackLook.FightWidth = width0; }
        check(looks >= 40 && wrong.Count == 0, $"PvP panel in every meter look, compact and expanded ({looks} drawn): names newest first, damage, Kill/Assist, by Tilly, no group DPS, no CP word, no clock, nothing spilled or cut{(wrong.Count > 0 ? ":\n        " + string.Join("\n        ", wrong.Take(12)) : "")}");

        // a row's skills card: your fight with that player (your damage on them by skill)
        var row = board.Roster[0];
        var card = ParserRows.HoverCard(board, row, now);
        var cardTexts = card == null ? new List<string>() : FindTexts(card).Select(BlockText).ToList();
        var fight = last[0].Fight;
        var top = fight.AbilityRows(fight.You!).First();
        string topName = KinahLedger.Parser.Abilities.Name(top.Skill, "Templar");
        check(card != null && cardTexts.Any(s => s.Contains(Amounts.Short(last[0].Damage))) && cardTexts.Any(s => s.Contains(topName)),
            $"PvP panel: a row's skills card is your fight with that player: {Amounts.Short(last[0].Damage)} and its top skill {topName} ({string.Join(" | ", cardTexts.Take(8))})");
        check(MemberCard.Of(board, row, now) is { Abilities.Count: > 0 } mcard && mcard.Damage == Amounts.Short(last[0].Damage), "...a theme's card the same figures");

        // when the window shows it
        var pvp0 = w.PvpSourceForTest; bool on0 = w.PvpPanelForTest;
        try
        {
            w.PvpPanelForTest = true;
            w.PvpSourceForTest = (kills, true);
            check(w.PvpBoard(null, now) is { Pvp.Count: 3 }, "PvP panel: in PvP with the switch on the meter is the panel");
            var pull = new Encounter { Mode = "boss", Boss = "Abyss Guardian", Kind = "World boss", Start = now, LastAt = now, HasDamage = true };
            check(w.PvpBoard(pull, now) == null, "...a boss pull there keeps the DPS meter");
            var world = new Encounter { Mode = "fight", Kind = "Abyss", Start = now, LastAt = now, HasDamage = true };
            check(w.PvpBoard(world, now) is { Pvp: not null }, "...an Abyss fight (players or monsters) is the panel's place");
            w.PvpSourceForTest = (kills, false);
            check(w.PvpBoard(world, now) == null, "...out of PvP, the DPS meter");
            w.PvpSourceForTest = (new PvpKills(), true);
            check(w.PvpBoard(null, now) == null && w.PvpBoard(world, now) is { Waiting: true }, "...before a kill: nothing where the meter shows nothing, the waiting card where it shows (Always on)");
            w.PvpSourceForTest = (kills, true); w.PvpPanelForTest = false;
            check(w.PvpBoard(null, now) == null, "...Settings › DPS meter › More › PvP kills in PvP off: the DPS meter");
        }
        finally { w.PvpSourceForTest = pvp0; w.PvpPanelForTest = on0; }
    }

    /// <summary>The panel in one look of the Parser's own window, still: the picture, its window closed; its spill noted.</summary>
    static FrameworkElement PvpFrame(Encounter e, DateTime at, string look, string dir, string name)
    {
        Motion.ForgetFigures();
        var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = ParserMode.Fights };
        if (look != ParserPrefs.StandardLook) o.Look = look;
        o.Draw(e, at);
        o.Show(); DoEvents();
        SpillNote(dir, name, (FrameworkElement)o.Content);
        var shot = Snapshot((FrameworkElement)o.Content);
        o.Close();
        return shot;
    }
}
