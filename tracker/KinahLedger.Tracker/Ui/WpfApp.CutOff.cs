using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The meter's figures never cut (Steven, 2026-11-17, playing 1.0.5: "a lot of numbers in all the meters you can't see, it's
/// cut off!"). The demo fights draw small figures (CP 4.8K, DPS 4.1K); a real fight has CP 150-170K, DPS up to 200K, totals in
/// the hundreds of millions and heals of 12K a second. <see cref="WorstSample"/> is a fight with every figure at its widest real
/// form; <see cref="CutOffs"/> finds every text drawn short of its words; the options test runs it over every look of the DPS
/// meter in both its windows (<c>KL_OPTIONS_ONLY=cutoff</c> alone), and <c>ui-shot DIR --parser worst</c> draws them.
/// </summary>
public static partial class WpfApp
{
    /// <summary>The worst case's members: a 10-character name with TOP DPS, 16-character names, a healer with TOP HEALS and two deaths, you last (pinned).</summary>
    static readonly (long Id, string Name, string Class, double Dps, int Cp, double Hps, int Deaths, bool You)[] WorstGroup =
    {
        (11, "Mordwulfen", "Assassin", 199_940, 167_548, 0, 0, false),
        (12, "Thornecrestwarde", "Templar", 188_840, 158_812, 0, 1, false),
        (13, "Kaelith", "Sorcerer", 177_740, 162_340, 0, 0, false),
        (14, "Aerinwynne", "Cleric", 166_640, 151_906, 12_340, 2, false),
        (15, "Seraphimdawnlite", "Chanter", 148_840, 165_432, 4_140, 0, false),
        (1, "Swoly", "Gladiator", 117_040, 154_120, 0, 0, true),
    };

    /// <summary>The names in the worst case (the cut-off check lets a long one trim, never a short one).</summary>
    internal static IReadOnlyList<string> WorstNames => WorstGroup.Select(g => g.Name).ToList();

    /// <summary>
    /// A boss fight with every figure at its widest real form: CP "167.5K", DPS "199.9K", the group's "998.8K", damage in the
    /// hundreds of millions ("124.56M"), a heal of "12.3K/s", shares of "20.0%", names of 16 characters next to short ones, six
    /// members (you pinned below the party), TOP DPS and TOP HEALS once it is over, two members with deaths, crit and back rates,
    /// the boss's HP "47% · 12.84M left" and clocks past 10:00. Live (10:23 in), killed (12:47) and on your own (100%), and the
    /// party before the first pull (CP "167,548").
    /// </summary>
    internal static (Encounter Live, Encounter Killed, Encounter Solo, Encounter Ready) WorstSample(DateTime now)
    {
        var (_, demo) = ParserDemo.Sample(now, live: true, damage: true);
        Encounter Make(int secs, bool alive, bool solo)
        {
            var e = BossParser.Copy(demo!);
            e.Id = $"worst-{(alive ? "live" : "kill")}{(solo ? "-solo" : "")}";
            e.Boss = "Ferocious Horn Nuakum";
            e.Start = now.AddSeconds(-secs);
            e.End = alive ? null : now; e.LastAt = now;
            e.Outcome = alive ? "" : "kill";
            e.BossMaxHp = 27_320_000; e.BossHp = alive ? 12_840_000 : 0;
            var templates = e.Roster.Where(f => !f.Other).ToList();
            var roster = new List<Fighter>();
            int i = 0;
            foreach (var g in WorstGroup.Where(g => !solo || g.You))
            {
                var t = templates[i++ % templates.Count];
                var f = JsonSerializer.Deserialize<Fighter>(JsonSerializer.Serialize(t))!;
                double dps = solo ? 199_940 : g.Dps;
                long dmg = (long)Math.Round(dps * secs);
                double k = t.Damage > 0 ? dmg / (double)t.Damage : 1;
                f.Id = g.Id; f.Name = g.Name; f.Class = g.Class; f.ClassNumber = 0; f.You = g.You; f.Server = "Israphel";
                f.CombatPower = solo ? 167_548 : g.Cp; f.ItemLevel = 5_640;
                f.Damage = f.BossDamage = dmg;
                f.Late = f.Gone = false; f.JoinedAt = f.LeftAt = null; f.FromDamage = false;
                f.Deaths = g.Deaths;
                f.FirstHit = e.Start.AddSeconds(2); f.LastHit = now; f.ActiveSeconds = secs - 4;
                long heal = (long)Math.Round(g.Hps * secs);
                f.Healing = (long)(heal * 1.4); f.HealingEffective = heal; f.HealingEstimated = 0; f.HealingSelf = heal > 0 ? heal / 20 : 0; f.HealingPotions = 0;
                foreach (var a in f.Abilities) { a.Damage = (long)(a.Damage * k); a.BossDamage = (long)(a.BossDamage * k); a.Uncounted = (long)(a.Uncounted * k); a.BossUncounted = (long)(a.BossUncounted * k); }
                if (f.PerSecond != null) f.PerSecond = f.PerSecond.Select(x => (long)(x * k)).ToList();
                if (f.BossPerSecond != null) f.BossPerSecond = f.BossPerSecond.Select(x => (long)(x * k)).ToList();
                roster.Add(f);
            }
            e.Roster = roster;
            e.Uncredited = e.UncreditedBoss = 0;
            return WithKinds(e);
        }
        var ready = ReadyWithParty(Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now), Make(623, true, false));
        return (Make(623, true, false), Make(767, false, false), Make(623, true, true), ready);
    }

    /// <summary>
    /// Every text under <paramref name="root"/> drawn short of its words, each said as "what: why" (empty when none):
    /// <list type="bullet">
    /// <item>trimmed (its "…" shown) or cut by its own box: wider laid out on its own than the room it was given;</item>
    /// <item>its words ending in "…" (cut before they were drawn);</item>
    /// <item>clipped by a part round it (ClipToBounds, a Clip, a layout clip) or by the window's edge;</item>
    /// <item>two different texts drawn over each other.</item>
    /// </list>
    /// A figure (anything with a digit) and a name of up to 10 characters must always show in full; a longer name may trim with
    /// "…", as may a title or a sentence (over 18 characters, no digit). Texts faded out (under 15%) are effects, not read.
    /// </summary>
    /// <param name="names">The fight's names (the long ones may trim); null: any text without a digit may trim.</param>
    internal static List<string> CutOffs(FrameworkElement root, IReadOnlyCollection<string>? names = null)
    {
        root.UpdateLayout();
        var top = (Visual?)Window.GetWindow(root) ?? root;
        Size topSize = top is FrameworkElement tf ? new Size(tf.ActualWidth, tf.ActualHeight) : root.RenderSize;
        var found = new List<string>();
        var boxes = new List<(string Text, Rect Box, bool Strict)>();
        void Walk(DependencyObject d, double opacity)
        {
            if (d is UIElement u) { if (!u.IsVisible) return; opacity *= u.Opacity; }
            if (opacity < 0.15) return;
            if (d is TextBlock t && t.ActualWidth > 0 && BlockText(t) is { } s && s.Trim().Length > 0) JudgeText(t, s.Trim());
            else if (d is Odometer o && o.Text.Length > 0 && o.ActualWidth > 0) JudgeOdometer(o);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i), opacity);
        }

        bool IsName(string s, out string name)
        {
            name = "";
            if (names == null) return false;
            // a look's own joiners (VHS writes "A⁠P" so no "AP" is read in its capitals) are no letters
            s = new string(s.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.Format).ToArray());
            var bare = s.EndsWith('…') ? s[..^1].TrimEnd() : s;
            foreach (var n in names)
                if (string.Equals(n, s, StringComparison.OrdinalIgnoreCase) || (s.EndsWith('…') && bare.Length > 0 && n.StartsWith(bare, StringComparison.OrdinalIgnoreCase))) { name = n; return true; }
            return false;
        }

        void JudgeText(TextBlock t, string s)
        {
            bool figure = s.Any(char.IsDigit);
            bool isName = IsName(s, out var name);
            bool mayTrim = isName ? name.Length > 10 : !figure && (names == null || s.Length > 18);
            bool strict = figure || isName;
            double has = t.ActualWidth;
            if (s.EndsWith('…') && !mayTrim) found.Add($"\"{s}\": its words end in \"…\"");
            if (t.TextWrapping == TextWrapping.NoWrap)
            {
                double needs = NaturalWidth(t);
                if (needs > has + 0.75)
                {
                    bool ellipsis = t.TextTrimming != TextTrimming.None;
                    if (!(ellipsis && mayTrim)) found.Add($"\"{s}\": {(ellipsis ? "trimmed" : "cut by its box")} ({needs:0.#} px wide in {has:0.#})");
                    return;   // said once
                }
                has = needs;
            }
            // the words' own box: where its lines sit in the block, its ink's height
            var pad = t.Padding;
            double w = Math.Min(has, t.ActualWidth) - pad.Left - pad.Right;
            double x = t.TextAlignment switch { TextAlignment.Right => t.ActualWidth - pad.Right - w, TextAlignment.Center => (t.ActualWidth - w) / 2, _ => pad.Left };
            var (inkTop, inkH) = Ink(t, s);
            var box = new Rect(x, pad.Top + inkTop, Math.Max(0, w), Math.Max(0, inkH));
            Clipped(t, s, box, strict || !mayTrim);
        }

        void JudgeOdometer(Odometer o)
        {
            o.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double needs = o.DesiredSize.Width - o.Margin.Left - o.Margin.Right;
            o.InvalidateMeasure();
            if (needs > o.ActualWidth + 0.75) { found.Add($"\"{o.Text}\": cut by its box ({needs:0.#} px wide in {o.ActualWidth:0.#})"); return; }
            Clipped(o, o.Text, new Rect(o.ActualWidth - needs, 0, needs, o.ActualHeight), true);
        }

        // the words' box against every clip round it, up to the window's edge
        void Clipped(FrameworkElement el, string s, Rect box, bool strict)
        {
            if (box.Width < 0.5 || box.Height < 0.5) return;
            Rect onTop;
            try { onTop = el.TransformToAncestor(top).TransformBounds(box); } catch (InvalidOperationException) { return; }
            // what is left of it on screen (each clip round it, in the window's terms): what another text can be drawn over
            var seen = Rect.Intersect(onTop, new Rect(topSize));
            string? cut = null;
            for (var a = VisualTreeHelper.GetParent(el); a != null; a = VisualTreeHelper.GetParent(a))
            {
                if (a is not UIElement ue) continue;
                Rect? clip = null;
                if (ue.ClipToBounds) clip = new Rect(ue.RenderSize);
                if (ue.Clip is { } g && !g.Bounds.IsEmpty) clip = clip is { } c0 ? Rect.Intersect(c0, g.Bounds) : g.Bounds;
                if (ue is FrameworkElement fe && System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(fe) is { } lg && !lg.Bounds.IsEmpty) clip = clip is { } c1 ? Rect.Intersect(c1, lg.Bounds) : lg.Bounds;
                if (clip is { } r)
                {
                    try
                    {
                        var inA = el.TransformToAncestor((Visual)a).TransformBounds(box);
                        if (cut == null && Outside(inA, r, out var by)) cut = $"\"{s}\": clipped by a {a.GetType().Name} ({by})";
                        if (!ReferenceEquals(a, top)) seen.Intersect(((Visual)a).TransformToAncestor(top).TransformBounds(r)); else seen.Intersect(r);
                    }
                    catch (InvalidOperationException) { }
                }
                if (ReferenceEquals(a, top)) break;
            }
            if (cut == null && Outside(onTop, new Rect(topSize), out var past)) cut = $"\"{s}\": past the window's edge ({past})";
            if (cut != null) found.Add(cut);
            if (!seen.IsEmpty) boxes.Add((s, seen, strict));
        }

        // a pixel either side is antialiasing; a pixel and a half off a descender's tip doesn't hide a letter
        static bool Outside(Rect b, Rect clip, out string by)
        {
            const double tol = 1.0, tolV = 1.5;
            var parts = new List<string>();
            if (b.Left < clip.Left - tol) parts.Add($"left {clip.Left - b.Left:0.#}");
            if (b.Right > clip.Right + tol) parts.Add($"right {b.Right - clip.Right:0.#}");
            if (b.Top < clip.Top - tolV) parts.Add($"top {clip.Top - b.Top:0.#}");
            if (b.Bottom > clip.Bottom + tolV) parts.Add($"bottom {b.Bottom - clip.Bottom:0.#}");
            by = string.Join(", ", parts) + (parts.Count > 0 ? " px" : "");
            return parts.Count > 0;
        }

        Walk(top, 1);
        root.UpdateLayout();   // the measures above undone: laid out as before
        // two different texts over each other (a figure or a name in either)
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var (s1, b1, st1) = boxes[i]; var (s2, b2, st2) = boxes[j];
                // a mark alone (▶, ✕, †) is drawn from a fallback font whose box this can't know: words and figures only
                if (!(st1 || st2) || s1 == s2 || !s1.Any(char.IsLetterOrDigit) || !s2.Any(char.IsLetterOrDigit)) continue;
                var x = Rect.Intersect(b1, b2);
                if (!x.IsEmpty && x.Width > 1.5 && x.Height > 1.5) found.Add($"\"{s1}\" and \"{s2}\" drawn over each other ({x.Width:0.#} x {x.Height:0.#} px)");
            }
        return found.Distinct().ToList();
    }

    /// <summary>A text block's width laid out on its own: no trimming, no Width or MaxWidth of its own (put back after).</summary>
    static double NaturalWidth(TextBlock t)
    {
        var props = new DependencyProperty[] { FrameworkElement.MaxWidthProperty, FrameworkElement.WidthProperty, TextBlock.TextTrimmingProperty };
        var kept = props.Select(p => t.ReadLocalValue(p)).ToArray();
        t.MaxWidth = double.PositiveInfinity; t.Width = double.NaN; t.TextTrimming = TextTrimming.None;
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = t.DesiredSize.Width - t.Margin.Left - t.Margin.Right;
        for (int i = 0; i < props.Length; i++)
        {
            if (kept[i] == DependencyProperty.UnsetValue) t.ClearValue(props[i]);
            else if (kept[i] is BindingExpressionBase be) BindingOperations.SetBinding(t, props[i], be.ParentBindingBase);
            else t.SetValue(props[i], kept[i]);
        }
        t.InvalidateMeasure();
        return w;
    }

    /// <summary>Where a text's ink sits in its line, top and height (its own font; a block of runs by the block's).</summary>
    static (double Top, double Height) Ink(TextBlock t, string s)
    {
        try
        {
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, t.FlowDirection, new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch),
                t.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(t).PixelsPerDip);
            double bottom = ft.Height + ft.OverhangAfter, h = ft.Extent;   // OverhangAfter: the ink's bottom from the line's (negative: above it)
            double lineH = double.IsNaN(t.LineHeight) || t.LineHeight <= 0 ? ft.Height : t.LineHeight;
            double shift = (lineH - ft.Height) / 2;
            return (Math.Max(0, bottom - h + shift), h);
        }
        catch (Exception) { return (0, t.ActualHeight); }
    }

    /// <summary>The DPS meter's looks, as Settings › DPS meter offers them: Standard, the fight looks and every theme's three.</summary>
    internal static IReadOnlyList<string> MeterLooks => HudThemes.All.SelectMany(t => OptionsWindow.ParserLooksOf(t.Key)).Distinct().ToList();

    /// <summary>
    /// options-test (<c>KL_OPTIONS_ONLY=cutoff</c> alone): the worst case (<see cref="WorstSample"/>) in every look of the DPS
    /// meter, in the Parser's own window and inside the Kinah overlay, live, killed, on your own and before the first pull; at
    /// width 350 and the widest (640), compact and expanded, at 100% and 110%; in English, Korean and Chinese. Fails on any
    /// figure or short name cut (<see cref="CutOffs"/>).
    /// </summary>
    static void CutOffChecks(Action<bool, string> check)
    {
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        var defCulture = CultureInfo.DefaultThreadCurrentCulture; var defUi = CultureInfo.DefaultThreadCurrentUICulture;
        string? ck0 = Encounter.CritKey, bk0 = Encounter.BackKey;
        double fw0 = HudStackLook.FightWidth; bool exp0 = ParserRows.ExpandedPref;
        string lang0 = L.Code;
        try
        {
            Encounter.CritKey = Encounter.CritKeyOf("result3"); Encounter.BackKey = Encounter.BackKeyOf("side1");
            foreach (var lang in new[] { "en", "ko", "zh-TW" })
            {
                L.Init(lang);
                var cuts = CutOffSweep(lang == "en" ? new[] { 1.0, 1.1 } : new[] { 1.1 }, lang == "en" ? new[] { 350.0, HudStackLook.FightWidthMax } : new[] { 350.0 }, null);
                foreach (var look in MeterLooks.Where(k => (Environment.GetEnvironmentVariable("KL_WORST_LOOK") ?? "").Length == 0 || Environment.GetEnvironmentVariable("KL_WORST_LOOK")!.Split(',').Contains(k)))
                {
                    var list = cuts.Where(c => c.Look == look).Select(c => $"{c.Where}: {c.What}").Distinct().ToList();
                    check(list.Count == 0, $"the meter's figures in full ({lang}), {look}: " + (list.Count == 0 ? "nothing cut in either window, at any width, size or state"
                        : $"{list.Count} cut" + Environment.NewLine + "        " + string.Join(Environment.NewLine + "        ", list.Take(14)) + (list.Count > 14 ? $"{Environment.NewLine}        … and {list.Count - 14} more" : "")));
                }
            }
        }
        finally
        {
            L.Init(lang0);
            CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui;
            CultureInfo.DefaultThreadCurrentCulture = defCulture; CultureInfo.DefaultThreadCurrentUICulture = defUi;
            Encounter.CritKey = ck0; Encounter.BackKey = bk0;
            HudStackLook.FightWidth = fw0; ParserRows.ExpandedPref = exp0;
        }
    }

    /// <summary>
    /// The worst case through every meter look at each size and width given, compact and expanded, in the Parser's window and the
    /// Kinah overlay: each cut as (look, where, what). With <paramref name="shots"/>, each frame is also saved there (ui-worst-*.png).
    /// </summary>
    static List<(string Look, string Where, string What)> CutOffSweep(double[] scales, double[] widths, string? shots)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (live, killed, solo, ready) = WorstSample(now);
        var fixtures = new[] { ("live", live, now), ("killed", killed, now), ("solo", solo, now), ("ready", ready, now) };
        var names = WorstNames.Append(ready.Title).Append(live.Title).ToList();
        var cuts = new List<(string, string, string)>();
        // KL_WORST_LOOK=neon,sticker: those looks only (a quick run while fixing them; the test runs them all)
        var only = (Environment.GetEnvironmentVariable("KL_WORST_LOOK") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Wanted(string look) => only.Length == 0 || only.Contains(look);
        var meterLooks = MeterLooks.Where(Wanted).ToList();
        var kinahLooks = HudLooks.All.Select(l => l.Key).Where(k => Wanted(HudThemes.Themed(k) || HudLooks.FightLooks.Contains(k) ? k : ParserPrefs.StandardLook)).ToList();
        void Frame(string look, string where, FrameworkElement el)
        {
            foreach (var c in CutOffs(el, names)) cuts.Add((look, where, c));
            if (shots != null) { var f = Path.Combine(shots, $"ui-worst-{where}.png"); Save(el, f, 0, ShotGround); SpillNote(shots, Path.GetFileName(f), el); }
        }
        foreach (var scale in scales)
            foreach (var look in meterLooks)
            {
                // the Parser's own window
                var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Look = look, ShowDetails = true };
                o.SetScale(scale);
                bool shown = false;
                foreach (var width in widths)
                    foreach (var expanded in new[] { false, true })
                    {
                        HudStackLook.FightWidth = width + (expanded ? 70 : 0); ParserRows.ExpandedPref = expanded;
                        o.ApplyFightWidth();
                        foreach (var (tag, e, at) in fixtures)
                        {
                            o.Mode = e.ModeKind;
                            o.Draw(e, at);
                            if (!shown) { o.Show(); shown = true; }
                            DoEvents();
                            Frame(look, $"{look}-parser-{tag}-{width:0}{(expanded ? "-expanded" : "")}-{scale * 100:0}", RootOf(o));
                        }
                    }
                o.Close();
            }
        foreach (var scale in scales)
            foreach (var look in kinahLooks)
            {
                // inside the Kinah overlay (its fight in the look's own meter, or the Parser's panel above the Kinah line)
                var h = new HudWindow(null, look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                h.SetScale(scale);
                h.Update(new Dictionary<string, string>());
                string owner = HudThemes.Themed(look) || HudLooks.FightLooks.Contains(look) ? look : ParserPrefs.StandardLook;
                bool shown = false;
                foreach (var width in widths)
                    foreach (var expanded in new[] { false, true })
                    {
                        HudStackLook.FightWidth = width + (expanded ? 70 : 0); ParserRows.ExpandedPref = expanded;
                        h.ApplyFightWidth();
                        foreach (var (tag, e, at) in fixtures)
                        {
                            h.ShowParser(e, at, e.ModeKind, details: true); h.SetKinahHidden(true);
                            if (!shown) { h.Show(); shown = true; }
                            DoEvents();
                            Frame(owner, $"{look}-kinah-{tag}-{width:0}{(expanded ? "-expanded" : "")}-{scale * 100:0}", RootOf(h));
                        }
                    }
                h.Close();
            }
        return cuts;
    }

    /// <summary>A window's own root: its size is the scaled content's (the content's own is the size before the Size's scale).</summary>
    static FrameworkElement RootOf(Window win) => VisualTreeHelper.GetChildrenCount(win) > 0 && VisualTreeHelper.GetChild(win, 0) is FrameworkElement r ? r : (FrameworkElement)win.Content;

    /// <summary>
    /// ui-shot DIR --parser worst: the worst case (<see cref="WorstSample"/>) in every meter look at Steven's settings (width 350,
    /// 110%, details on), compact and expanded, in both windows (ui-worst-LOOK-WINDOW-STATE-WIDTH[-expanded]-110.png); cut.txt
    /// lists every text cut (empty: none), spill.txt anything drawn past an edge. KL_WORST_SCALE / KL_WORST_WIDTH change them.
    /// </summary>
    static void ShotWorst(string dir)
    {
        try { File.Delete(Path.Combine(dir, "spill.txt")); } catch (IOException) { }
        double scale = double.TryParse(Environment.GetEnvironmentVariable("KL_WORST_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1.1;
        double width = double.TryParse(Environment.GetEnvironmentVariable("KL_WORST_WIDTH"), NumberStyles.Float, CultureInfo.InvariantCulture, out var wd) ? wd : 350;
        string? ck0 = Encounter.CritKey, bk0 = Encounter.BackKey;
        double fw0 = HudStackLook.FightWidth; bool exp0 = ParserRows.ExpandedPref;
        try
        {
            Encounter.CritKey = Encounter.CritKeyOf("result3"); Encounter.BackKey = Encounter.BackKeyOf("side1");
            var cuts = CutOffSweep(new[] { scale }, new[] { width }, dir);
            File.WriteAllLines(Path.Combine(dir, "cut.txt"), cuts.Select(c => $"{c.Where}: {c.What}"));
            Console.WriteLine(cuts.Count == 0 ? "nothing cut" : $"{cuts.Count} cut (cut.txt):" + Environment.NewLine + string.Join(Environment.NewLine, cuts.Select(c => $"  {c.Where}: {c.What}").Take(60)));
        }
        finally { Encounter.CritKey = ck0; Encounter.BackKey = bk0; HudStackLook.FightWidth = fw0; ParserRows.ExpandedPref = exp0; }
    }
}
