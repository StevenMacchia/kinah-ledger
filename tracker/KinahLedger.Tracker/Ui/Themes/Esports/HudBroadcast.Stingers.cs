using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Broadcast's three big moments, a tournament package's transitions (Steven, 2026-11-20, round 2: "my bar is the HIGHEST"):
/// - a fight's start: the branded stinger, a textured cyan plate and a glossy navy plate wiping diagonally across the whole
///   meter with light streaks; on the navy as it covers, FIGHT! and the LIVE tag over the boss's name; then the plates
///   leave and the meter builds in behind them;
/// - a new leader: a NEW LEADER ticker punching in over the HP strip with the leader's player card (their class's plate, art,
///   name and DPS) sliding in beside it; the leader's crown chip pulses, a stinger runs down the rows;
/// - a kill: a VICTORY stinger over the whole meter (DEFEAT in red on a wipe): a mint back plate, a navy front plate, the
///   word big with the fight's time, and the MVP (the top DPS) under it.
/// Each is built once when it starts (its plates and words drawn once, the big words as shapes so they cost no layout), then
/// only transforms and opacities move; it is over in about a second and a half. The overlay is clipped to the meter, so
/// nothing reaches past the window.
/// </summary>
sealed partial class HudBroadcast
{
    /// <summary>A render's hook: KL_BC_MOMENT=leader holds the new leader's ticker in the fight-started frames (no moment in the render set shows it).</summary>
    static readonly string MomentHook = Environment.GetEnvironmentVariable("KL_BC_MOMENT") ?? "";

    /// <summary>Words as a shape in the package's face (condensed bold), drawn once with a plate: no layout, no text to measure.</summary>
    static Geometry Words(string s, double size, FontWeight? w = null, bool tight = true)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Cond, FontStyles.Normal, w ?? FontWeights.Bold, tight ? FontStretches.Condensed : FontStretches.Normal), size, Brushes.White, 1.0);
        var g = ft.BuildGeometry(new Point(0, 0));
        g.Freeze();
        return g;
    }

    /// <summary>A shape drawn with its top-left at (<paramref name="x"/>, <paramref name="y"/>), a hard shadow under it first.</summary>
    static void Put(DrawingContext dc, Geometry g, double x, double y, Brush fill, Brush? shadow = null, double sd = 2)
    {
        if (shadow != null)
        {
            dc.PushTransform(new TranslateTransform(x - g.Bounds.X + sd, y - g.Bounds.Y + sd));
            dc.DrawGeometry(shadow, null, g);
            dc.Pop();
        }
        dc.PushTransform(new TranslateTransform(x - g.Bounds.X, y - g.Bounds.Y));
        dc.DrawGeometry(fill, null, g);
        dc.Pop();
    }

    /// <summary>The package's double chevron ("»"), <paramref name="h"/> tall, at (<paramref name="x"/>, centre <paramref name="y"/>).</summary>
    static void Chevrons(DrawingContext dc, double x, double y, double h, Brush a, Brush b)
    {
        for (int i = 0; i < 2; i++)
        {
            var ch = new StreamGeometry();
            using (var g = ch.Open())
            {
                double x0 = x + i * h * .55, w = h * .34;
                g.BeginFigure(new Point(x0, y - h / 2), true, true);
                g.LineTo(new Point(x0 + w, y - h / 2), true, false); g.LineTo(new Point(x0 + w + h * .36, y), true, false);
                g.LineTo(new Point(x0 + w, y + h / 2), true, false); g.LineTo(new Point(x0, y + h / 2), true, false); g.LineTo(new Point(x0 + h * .36, y), true, false);
            }
            ch.Freeze();
            dc.DrawGeometry(i == 0 ? a : b, null, ch);
        }
    }

    /// <summary>A plate's texture: thin slanted stripes every <paramref name="step"/> px across <paramref name="g"/>.</summary>
    static void Stripes(DrawingContext dc, Geometry g, Size s, double step, Brush ink)
    {
        double l = s.Height * Tan;
        dc.PushClip(g);
        for (double x = -l - 8; x < s.Width + l + 8; x += step) dc.DrawGeometry(ink, null, Cut(new Rect(x - l, 0, 2 + l, s.Height), l));
        dc.Pop();
    }

    /// <summary>The stinger's slashes: thick slanted lights (4 to 7 px) fading in from their trailing side, drawn once.</summary>
    static TacSketch Slashes(TranslateTransform x, params (double At, double W, Color C, double A)[] marks) =>
        new((dc, s) =>
        {
            double l = s.Height * Tan;
            foreach (var (at, w, c, a) in marks)
            {
                double x0 = s.Width * at;
                var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(x0 - 18, 0), EndPoint = new Point(x0 + w, 0) };
                b.GradientStops.Add(new GradientStop(Mod.A(c, 0), 0)); b.GradientStops.Add(new GradientStop(Mod.A(c, a * .5), .7)); b.GradientStops.Add(new GradientStop(Mod.A(c, a), 1));
                b.Freeze();
                dc.DrawGeometry(b, null, Cut(new Rect(x0 - 18 - l, 0, 18 + w + l, s.Height), l));
                dc.DrawGeometry(Frozen(Mod.A(Colors.White, Math.Min(1, a + .1))), null, Cut(new Rect(x0 + w - 1.5 - l, 0, 1.5 + l, s.Height), l));
            }
        }) { RenderTransform = x };

    /// <summary>An eased point that starts slow and leaves fast (a plate leaving).</summary>
    static double In(double p) { p = Math.Clamp(p, 0, 1); return p * p * p; }

    /// <summary>A plate across the whole overlay, slid by <paramref name="x"/>: drawn once.</summary>
    static TacSketch Sheet(Action<DrawingContext, Size, double> draw, TranslateTransform x) =>
        new((dc, s) => { if (s.Width >= 2 && s.Height >= 2) draw(dc, s, s.Height * Tan); }) { RenderTransform = x };

    /// <summary>The overlay over the fight (clipped to it), on top of everything in it.</summary>
    Grid Overlay()
    {
        var fx = new Grid { IsHitTestVisible = false, ClipToBounds = true };
        _fight.Children.Add(fx);
        return fx;
    }

    /// <summary>The fight's size as laid out (the width it was given before its first layout).</summary>
    Size FightSize() => new(_fight.ActualWidth > 0 ? _fight.ActualWidth : _fight.Width, Math.Max(40, _fight.ActualHeight));

    // ================================================================ a fight's start

    sealed record OpenFx(Grid Fx, TranslateTransform A, TranslateTransform B, TranslateTransform St, FrameworkElement Brand, ScaleTransform Punch, TranslateTransform Drift);

    /// <summary>The stinger's course: cyan leads navy in on the slant with streaks, the brand lands on the navy, both leave to the right.</summary>
    void OpenStinger(double t, OpenFx o)
    {
        var s = FightSize();
        double span = s.Width + 2 * s.Height * Tan + 8;
        // (2026-11-21: the plates cover the meter for under half a second, then the rows build in behind them)
        double pa = t < .4 ? .5 * Out(Seg(t, 0, .12)) : .5 + .5 * In(Seg(t, .4, .56));
        double pb = t < .37 ? .5 * Out(Seg(t, .02, .14)) : .5 + .5 * In(Seg(t, .37, .52));
        o.A.X = -span + 2 * span * pa;
        o.B.X = -span + 2 * span * pb;
        o.St.X = -span + 2 * span * (t < .34 ? Seg(t, 0, .2) : Seg(t, .37, .6));
        o.Brand.Opacity = t < .37 ? Seg(t, .07, .11) : 1 - Seg(t, .37, .41);
        o.Punch.ScaleX = o.Punch.ScaleY = t < .07 ? 1.22 : EsportsFx.Punch(Seg(t, .07, .2), 1.22, 2.2);
        o.Drift.X = 14 - 28 * Seg(t, .07, .41);
        o.Fx.Visibility = t >= 1 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Builds the start's overlay: the cyan and navy plates (textured, glossy), the streaks and the brand.</summary>
    OpenFx OpenParts(Encounter e)
    {
        var fx = Overlay();
        var a = new TranslateTransform(-4000, 0); var b = new TranslateTransform(-4000, 0); var st = new TranslateTransform(-4000, 0);
        // the cyan plate: stripes, a big ink chevron watermark, a white leading edge
        fx.Children.Add(Sheet((dc, s, l) =>
        {
            var g = Cut(new Rect(-l - 4, 0, s.Width + 2 * l + 8, s.Height), l);
            dc.DrawGeometry(Mod.Grad(false, (Mod.Mix(CyanC, InkC, .35), 0), (CyanC, .85), (Mod.Mix(CyanC, Colors.White, .55), 1)), null, g);
            Stripes(dc, g, s, 9, Frozen(Mod.A(Colors.White, .12)));
            Chevrons(dc, s.Width * .5 - s.Height * .35, s.Height / 2, s.Height * .8, Frozen(Mod.A(InkC, .22)), Frozen(Mod.A(InkC, .12)));
            dc.DrawGeometry(GlossB, null, g);
            dc.DrawGeometry(White, null, Cut(new Rect(s.Width + 4 - 5, 0, 5 + l, s.Height), l));
        }, a));
        // the navy plate: glossy, faint stripes, two cyan rules, cyan bars at both its ends
        fx.Children.Add(Sheet((dc, s, l) =>
        {
            var g = Cut(new Rect(-l - 4, 0, s.Width + 2 * l + 8, s.Height), l);
            dc.DrawGeometry(Mod.Grad(true, (Color.FromRgb(0x18, 0x25, 0x46), 0), (InkC, .55), (Color.FromRgb(0x03, 0x05, 0x0C), 1)), null, g);
            Stripes(dc, g, s, 14, Frozen(Mod.A(CyanC, .05)));
            dc.DrawGeometry(GlossB, null, g);
            dc.PushClip(g);
            dc.DrawRectangle(Frozen(Mod.A(CyanC, .55)), null, new Rect(-l - 4, s.Height * .5 - 30, s.Width + 2 * l + 8, 1.5));
            dc.DrawRectangle(Frozen(Mod.A(CyanC, .55)), null, new Rect(-l - 4, s.Height * .5 + 29, s.Width + 2 * l + 8, 1.5));
            dc.Pop();
            dc.DrawGeometry(Cyan, null, Cut(new Rect(s.Width + 4 - 10, 0, 10 + l, s.Height), l));
            dc.DrawGeometry(Cyan, null, Cut(new Rect(-l - 4, 0, 7 + l, s.Height), l));
        }, b));
        // the streaks: thin lights on the slant running ahead and behind
        fx.Children.Add(Slashes(st, (.08, 6, CyanC, .85), (.3, 4, Colors.White, .7), (.62, 7, CyanC, .7), (.86, 4, Colors.White, .8)));
        // the brand on the navy: FIGHT! on a cyan tile and the LIVE tag, the boss's name big under them, the chevrons after it
        var title = e.Title;
        var roster = string.Join("  ·  ", Meter?.Members.Select(m => m.Name) ?? Enumerable.Empty<string>());
        var brand = new TacSketch((dc, s) =>
        {
            var fight = Words(L.T("FIGHT!"), 15);
            var live = Words(L.T("LIVE"), 15);
            var name = Words(title, 30, FontWeights.Bold);
            double tagH = 21, fightW = fight.Bounds.Width + 18, liveW = live.Bounds.Width + 26;
            // the party under the name, in cyan, over a rule
            var team = roster.Length > 0 ? Words(roster, 14, FontWeights.SemiBold, tight: true) : null;
            double teamW = team?.Bounds.Width ?? 0;
            double topW = fightW + 4 + liveW, nameW = name.Bounds.Width + 40, total = Math.Max(Math.Max(topW, nameW), teamW);
            double blockH = tagH + 6 + name.Bounds.Height + (team != null ? 9 + 2 + 7 + team.Bounds.Height : 0);
            double k = Math.Min(1, (s.Width - 28) / Math.Max(1, total));
            dc.PushTransform(new TranslateTransform(s.Width / 2 - total * k / 2, s.Height / 2 - blockH * k / 2));
            dc.PushTransform(new ScaleTransform(k, k));
            // FIGHT! and LIVE
            dc.PushTransform(new TranslateTransform(2.5, 2.5));
            dc.DrawGeometry(ShadowB, null, Cut(new Rect(0, 0, fightW, tagH), tagH * Tan));
            dc.Pop();
            dc.DrawGeometry(Cyan, null, Cut(new Rect(0, 0, fightW, tagH), tagH * Tan));
            Put(dc, fight, 9, (tagH - fight.Bounds.Height) / 2, InkB);
            dc.DrawGeometry(Frozen(LiveC), null, Cut(new Rect(fightW + 4 - tagH * Tan + 3, 0, liveW, tagH), tagH * Tan));
            dc.DrawEllipse(White, null, new Point(fightW + 4 - tagH * Tan + 3 + 12, tagH / 2), 3, 3);
            Put(dc, live, fightW + 4 - tagH * Tan + 3 + 19, (tagH - live.Bounds.Height) / 2, White);
            // the boss's name, white with a hard cyan-ink shadow, the chevrons after it
            Put(dc, name, 0, tagH + 6, White, Frozen(Mod.A(Color.FromRgb(0x00, 0x6E, 0x86), .9)), 2.5);
            Chevrons(dc, name.Bounds.Width + 12, tagH + 6 + name.Bounds.Height / 2, 18, Cyan, Frozen(Mod.A(CyanC, .45)));
            if (team != null)
            {
                double ry = tagH + 6 + name.Bounds.Height + 9;
                dc.DrawGeometry(Cyan, null, Cut(new Rect(0, ry, Math.Max(nameW - 10, teamW), 2), 0));
                Put(dc, team, 0, ry + 2 + 7, Frozen(Mod.Mix(CyanC, Colors.White, .35)));
            }
            dc.Pop(); dc.Pop();
        });
        var punch = new ScaleTransform(1, 1);
        var drift = new TranslateTransform();
        var tgp = new TransformGroup(); tgp.Children.Add(punch); tgp.Children.Add(drift);
        brand.RenderTransform = tgp;
        brand.RenderTransformOrigin = new Point(.5, .5);
        brand.Opacity = 0;
        fx.Children.Add(brand);
        return new OpenFx(fx, a, b, st, brand, punch, drift);
    }

    // ================================================================ a kill

    /// <summary>
    /// The kill's stinger over the whole meter: a mint back plate and a navy front plate slam in on the slant with streaks,
    /// VICTORY big with the fight's time beside it, the MVP (the top DPS, their class's colour) under it; DEFEAT in red on a
    /// wipe. Under a second and a half, then away.
    /// </summary>
    void VictoryStinger(Encounter e)
    {
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? LiveC : MintC2;
        var word = (wipe ? L.T("DEFEAT") : e.Outcome == "kill" ? L.T("VICTORY") : OutcomeText(e)).ToUpper(CultureInfo.CurrentCulture);
        var time = Clock(e.Duration(e.End ?? DateTime.UtcNow));
        var mvp = Meter?.Members.FirstOrDefault(m => m.Rank == 1);
        var podium = Meter?.Members.Where(m => m.Rank is 2 or 3).OrderBy(m => m.Rank).ToList() ?? new();
        var fx = Overlay();
        var back = new TranslateTransform(-4000, 0); var front = new TranslateTransform(-4000, 0); var st = new TranslateTransform(-4000, 0);
        fx.Children.Add(Sheet((dc, s, l) =>
        {
            var g = Cut(new Rect(-l - 4, 0, s.Width + 2 * l + 8, s.Height), l);
            dc.DrawGeometry(Mod.Grad(false, (Mod.Mix(c, InkC, .4), 0), (c, .85), (Mod.Mix(c, Colors.White, .55), 1)), null, g);
            Stripes(dc, g, s, 9, Frozen(Mod.A(Colors.White, .12)));
            dc.DrawGeometry(GlossB, null, g);
        }, back));
        fx.Children.Add(Slashes(st, (.12, 6, c, .85), (.36, 4, Colors.White, .7), (.64, 7, c, .7), (.88, 4, Colors.White, .75)));
        fx.Children.Add(Sheet((dc, s, l) =>
        {
            // the front plate sits inside the back one by a band of its colour at the top and the foot
            const double inset = 7;
            var g = Cut(new Rect(-l - 4, inset, s.Width + 2 * l + 8, s.Height - 2 * inset), l);
            dc.DrawGeometry(Mod.Grad(true, (Color.FromRgb(0x18, 0x25, 0x46), 0), (InkC, .55), (Color.FromRgb(0x02, 0x04, 0x0A), 1)), null, g);
            Stripes(dc, g, s, 14, Frozen(Mod.A(c, .05)));
            dc.DrawGeometry(GlossB, null, g);
            // three lines: the word big with the time; the MVP's player card; places two and three
            var big = Words(word, 52);
            var tm = Words(time, 22, FontWeights.SemiBold);
            double l1 = big.Bounds.Width + 14 + tm.Bounds.Width;
            const double cardH = 28, podH = 20;
            Geometry? mv = null, who = null, fig = null, shr = null;
            double l2 = 0;
            if (mvp != null)
            {
                mv = Words(L.T("MVP"), 15); who = Words(mvp.Name, 18, FontWeights.SemiBold); fig = Words(mvp.Figure, 20); shr = Words(mvp.ShareText, 15, FontWeights.SemiBold);
                l2 = (mv.Bounds.Width + 18) + (who.Bounds.Width + 26) + (fig.Bounds.Width + 20) + 10 + shr.Bounds.Width;
            }
            var pods = podium.Select(m => (m, Words(m.Rank.ToString(CultureInfo.InvariantCulture), 13), Words(m.Name, 14, FontWeights.SemiBold), Words(m.Figure, 14))).ToList();
            double l3 = pods.Sum(p => 20 + 6 + p.Item3.Bounds.Width + 8 + p.Item4.Bounds.Width) + Math.Max(0, pods.Count - 1) * 22;
            double total = Math.Max(l1, Math.Max(l2, l3));
            double blockH = big.Bounds.Height + (mvp != null ? 12 + cardH : 0) + (pods.Count > 0 ? 10 + podH : 0);
            double k = Math.Min(1, Math.Min((s.Width - 36) / Math.Max(1, total), (s.Height - 2 * inset - 10) / Math.Max(1, blockH)));
            dc.PushTransform(new TranslateTransform(s.Width / 2 - total * k / 2, s.Height / 2 - blockH * k / 2));
            dc.PushTransform(new ScaleTransform(k, k));
            double y = 0;
            Put(dc, big, (total - l1) / 2, y, White, Frozen(Mod.A(c, .65)), 3.5);
            Put(dc, tm, (total - l1) / 2 + big.Bounds.Width + 14, y + big.Bounds.Height - tm.Bounds.Height, Frozen(Mod.Mix(c, Colors.White, .35)));
            y += big.Bounds.Height;
            if (mvp != null)
            {
                y += 12;
                double x = (total - l2) / 2;
                // MVP on the outcome's colour, the name on the class's plate, the DPS on a cyan tile, the share
                double w0 = mv!.Bounds.Width + 18;
                dc.DrawGeometry(Frozen(c), null, Cut(new Rect(x, y, w0, cardH), cardH * Tan));
                Put(dc, mv, x + 9, y + (cardH - mv.Bounds.Height) / 2, wipe ? White : InkB);
                x += w0 + 3 - cardH * Tan;
                var vcol = Vivid(mvp.ClassColor);
                double w1 = who!.Bounds.Width + 26;
                var cardG = Cut(new Rect(x, y, w1, cardH), cardH * Tan);
                dc.DrawGeometry(Mod.Grad(false, (Mod.Mix(vcol, InkC, .15), 0), (Mod.Mix(vcol, InkC, .45), 1)), null, cardG);
                dc.DrawGeometry(GlossB, null, cardG);
                Put(dc, who, x + 13, y + (cardH - who.Bounds.Height) / 2, White, ShadowB, 1.5);
                x += w1 + 3 - cardH * Tan;
                double w2 = fig!.Bounds.Width + 20;
                dc.DrawGeometry(Cyan, null, Cut(new Rect(x, y, w2, cardH), cardH * Tan));
                Put(dc, fig, x + 10, y + (cardH - fig.Bounds.Height) / 2, InkB);
                Put(dc, shr!, x + w2 + 10, y + (cardH - shr!.Bounds.Height) / 2, Frozen(Color.FromRgb(0xC4, 0xD2, 0xE2)));
                y += cardH;
            }
            if (pods.Count > 0)
            {
                y += 10;
                double x = (total - l3) / 2;
                foreach (var (m, rk, nmG, fgG) in pods)
                {
                    dc.DrawGeometry(Frozen(Ink2C), null, Cut(new Rect(x, y, 20, podH), podH * Tan));
                    Put(dc, rk, x + 10 - rk.Bounds.Width / 2, y + (podH - rk.Bounds.Height) / 2, White);
                    x += 26;
                    Put(dc, nmG, x, y + (podH - nmG.Bounds.Height) / 2, Frozen(Mod.Mix(Vivid(m.ClassColor), Colors.White, .6)));
                    x += nmG.Bounds.Width + 8;
                    Put(dc, fgG, x, y + (podH - fgG.Bounds.Height) / 2, White);
                    x += fgG.Bounds.Width + 22;
                }
            }
            dc.Pop(); dc.Pop();
        }, front));
        var rows = RowsNow();
        // (2026-11-21: the full takeover is a punch, not a hold: the plates slam over the meter, stay under half a second, and
        // leave to the right; the result stays as a ticker over the head while the rows read again under it)
        var ticker = ResultTicker(e, wipe, word, time, mvp);
        Motion.Play(fx, 1700, t =>
        {
            var s = FightSize();
            double span = s.Width + 2 * s.Height * Tan + 8;
            double pb = t < .25 ? .5 * Out(Seg(t, 0, .07)) : .5 + .5 * In(Seg(t, .25, .34));
            double pf = t < .23 ? .5 * Out(Seg(t, .02, .09)) : .5 + .5 * In(Seg(t, .23, .32));
            back.X = -span + 2 * span * pb;
            // a wipe's front plate shudders as it lands (a broken feed); a kill's lands clean
            front.X = -span + 2 * span * pf + (wipe ? EsportsFx.Shake(t, .08, .22, 7, 3.5) : 0);
            st.X = wipe ? -4000 : -span + 2 * span * (t < .2 ? Seg(t, 0, .14) : Seg(t, .22, .4));
            fx.Visibility = t >= .36 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var (r, i) in rows) r.SweepAt(Band(t, .3 + i * .04, .22), c, wipe ? .55 : .9);
            ticker?.Invoke(t);
        }, () => { _fight.Children.Remove(fx); foreach (var (r, _) in rows) r.SweepAt(-1, c); ticker?.Invoke(1); });
    }

    /// <summary>
    /// The result left over the head once the stinger's plates have gone: the word on its colour's tile (VICTORY in mint,
    /// DEFEAT in red), the MVP's player card on a kill (the boss's name on a wipe), the time on a cyan tile. It punches in as
    /// the plates leave, holds, and wipes away; the head's own words come back under it. Its course is driven by the
    /// stinger's (0 to 1, 1: gone).
    /// </summary>
    Action<double>? ResultTicker(Encounter e, bool wipe, string word, string time, MeterMember? mvp)
    {
        if (_head is not { } head) return null;
        const double H = 24;
        var c = wipe ? LiveC : MintC2;
        var tagT = Bc(word, 15, wipe ? White : InkB);
        var tag = new Slab(tagT, c, 1, null, H, padX: 7) { Rule = false, Shadow = 2.5 };
        FrameworkElement mid;
        Slab card;
        if (!wipe && mvp != null)
        {
            var who = new StackPanel { Orientation = Orientation.Horizontal };
            var mv = Bc(L.T("MVP"), 12.5, Frozen(Mod.Mix(MintC2, Colors.White, .4))); mv.Margin = new Thickness(0, 0, 6, 0); mv.VerticalAlignment = VerticalAlignment.Center;
            who.Children.Add(mv);
            var icon = ThemeText.Icon(mvp.Class, 17, mvp.Name);
            icon.Margin = new Thickness(0, 0, 5, 0);
            who.Children.Add(icon);
            var nm = Bc(mvp.Name, 15.5, White); nm.TextTrimming = TextTrimming.CharacterEllipsis; nm.MaxWidth = 100;
            who.Children.Add(nm);
            mid = who;
            var vc = Vivid(mvp.ClassColor);
            card = new Slab(Shaded(who), Mod.Mix(vc, InkC, .3), 1, Colors.White, H, padX: 6, accentW: 3) { Shadow = 2.5, Margin = new Thickness(3 - H * Tan, 0, 0, 0) };
            card.Paint = (dc, s, g, l) => dc.DrawGeometry(Mod.Grad(false, (Mod.A(vc, .95), 0), (Mod.A(vc, .5), .6), (Mod.A(InkC, .8), 1)), null, g);
        }
        else
        {
            var tt = Bc(e.Title, 14, Soft, FontWeights.SemiBold); tt.TextTrimming = TextTrimming.CharacterEllipsis; tt.MaxWidth = 150;
            mid = tt;
            card = new Slab(Shaded(tt), InkC, .96, null, H, padX: 6) { Shadow = 2.5, Margin = new Thickness(3 - H * Tan, 0, 0, 0) };
        }
        var fig = Bc(time, 16, InkB);
        var score = new Slab(fig, CyanC, 1, null, H, padX: 6) { Rule = false, Shadow = 2.5, Margin = new Thickness(3 - H * Tan, 0, 0, 0) };
        var row = new Grid { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var _ in new[] { 0, 1, 2 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(card, 1); Grid.SetColumn(score, 2);
        row.Children.Add(tag); row.Children.Add(card); row.Children.Add(score);
        var slide = new TranslateTransform();
        card.RenderTransform = slide; score.RenderTransform = slide;
        var punch = new ScaleTransform(1, 1, 0, H / 2);
        tag.RenderTransform = punch;
        double y = 0;
        try { y = head.TranslatePoint(new Point(0, 0), _fight).Y + Math.Max(0, (head.ActualHeight - H) / 2); } catch (InvalidOperationException) { }
        row.Margin = new Thickness(0, Math.Max(0, y), 0, 0);
        _fight.Children.Add(row);
        bool gone = false;
        return t =>
        {
            if (gone) return;
            if (t >= 1) { gone = true; _fight.Children.Remove(row); head.Opacity = 1; return; }
            // the head's own words make way under it, and come back as it wipes off
            head.Opacity = t < .9 ? 1 - Seg(t, .2, .26) : Seg(t, .93, 1);
            tag.Wipe = t < .88 ? Out(Seg(t, .22, .3)) : 1 - Seg(t, .88, .95);
            tagT.Opacity = t < .88 ? Seg(t, .26, .3) : 1 - Seg(t, .88, .92);
            punch.ScaleX = punch.ScaleY = t < .26 ? 1.25 : EsportsFx.Punch(Seg(t, .26, .42), 1.25, 2.6);
            tag.SweepAt(Band(t, .3, .2), Colors.White);
            card.Wipe = score.Wipe = t < .86 ? Out(Seg(t, .26, .38)) : 1 - Seg(t, .86, .94);
            slide.X = -40 * (1 - Out(Seg(t, .26, .42)));
            double words = t < .86 ? Seg(t, .32, .4) : 1 - Seg(t, .86, .9);
            mid.Opacity = words; fig.Opacity = words;
            card.SweepAt(Band(t, .4, .24), c);
        };
    }

    // ================================================================ a new leader

    /// <summary>
    /// A new leader: over the HP strip (the boss and the clock stay in view), a NEW LEADER ticker punches in on cyan while the
    /// leader's player card (their class's plate, art, name and DPS) slides in beside it; the leader's crown chip pulses and a
    /// stinger runs down the rows.
    /// </summary>
    void LeaderTicker(long was = 0)
    {
        if (_head is not { } head || Meter is not { } meter || meter.Members.FirstOrDefault(m => m.Rank == 1) is not { } lead) return;
        var prev = meter.Members.FirstOrDefault(m => m.Id == was && m.Id != lead.Id) ?? meter.Members.FirstOrDefault(m => m.Rank == 2);
        const double H = 24;
        var tagT = Bc(L.T("NEW LEADER"), 14.5, InkB);
        var tag = new Slab(tagT, CyanC, 1, null, H, padX: 7) { Rule = false, Shadow = 2.5, Tag = "leader-ticker" };
        var who = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = ThemeText.Icon(lead.Class, 17, lead.Name);
        if (icon is Border ib) { ib.BorderBrush = Frozen(Mod.A(Colors.White, .85)); ib.BorderThickness = new Thickness(1); }
        icon.Margin = new Thickness(0, 0, 6, 0);
        who.Children.Add(icon);
        var nm = Bc(lead.Name, 16, White); nm.TextTrimming = TextTrimming.CharacterEllipsis; nm.MaxWidth = 110;
        who.Children.Add(nm);
        var vc = Vivid(lead.ClassColor);
        var card = new Slab(Shaded(who), Mod.Mix(vc, InkC, .3), 1, Colors.White, H, padX: 6, accentW: 3) { Shadow = 2.5, Margin = new Thickness(3 - H * Tan, 0, 0, 0) };
        card.Paint = (dc, s, g, l) => dc.DrawGeometry(Mod.Grad(false, (Mod.A(vc, .95), 0), (Mod.A(vc, .5), .6), (Mod.A(InkC, .8), 1)), null, g);
        var fig = Bc(lead.Figure, 17, InkB);
        var score = new Slab(fig, CyanC, 1, null, H, padX: 6) { Rule = false, Shadow = 2.5, Margin = new Thickness(3 - H * Tan, 0, 0, 0) };
        // the rest of the strip: who they passed and by how much, on navy
        var pass = new StackPanel { Orientation = Orientation.Horizontal };
        if (prev != null)
        {
            var pw = Bc(L.F("PASSES {0}", prev.Name), 13.5, Soft, FontWeights.SemiBold); pw.TextTrimming = TextTrimming.CharacterEllipsis;
            pass.Children.Add(pw);
            if (!double.IsNaN(lead.FigureValue) && !double.IsNaN(prev.FigureValue) && lead.FigureValue > prev.FigureValue && !lead.CpOnly)
            { var by = Bc("+" + Amounts.Meter(lead.FigureValue - prev.FigureValue), 13.5, Mint2); by.Margin = new Thickness(8, 0, 0, 0); pass.Children.Add(by); }
        }
        var rest = new Slab(Shaded(pass), InkC, .95, null, H, padX: 6) { Shadow = 2.5, Margin = new Thickness(3 - H * Tan, 0, 0, 0) };
        var row = new Grid { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
        foreach (var _ in new[] { 0, 1, 2 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(card, 1); Grid.SetColumn(score, 2); Grid.SetColumn(rest, 3);
        row.Children.Add(tag); row.Children.Add(card); row.Children.Add(score); row.Children.Add(rest);
        var slide = new TranslateTransform();
        card.RenderTransform = slide; score.RenderTransform = slide; rest.RenderTransform = slide;
        var punch = new ScaleTransform(1, 1, 0, H / 2);
        tag.RenderTransform = punch;
        // over the HP strip: just under the head (its place in the fight as last laid out)
        double y = HeadH + 1;
        try { y = head.TranslatePoint(new Point(0, HeadH + 1), _fight).Y; } catch (InvalidOperationException) { }
        row.Margin = new Thickness(0, Math.Max(0, y), 0, 0);
        _fight.Children.Add(row);
        var hp = _hpLine;
        var rows = RowsNow();
        Motion.Play(row, 1600, t =>
        {
            if (hp != null) hp.Opacity = t < .84 ? 1 - Seg(t, 0, .08) : Seg(t, .84, 1);
            tag.Wipe = t < .84 ? Out(Seg(t, 0, .12)) : 1 - Seg(t, .84, .96);
            tagT.Opacity = t < .84 ? Seg(t, .06, .14) : 1 - Seg(t, .84, .9);
            punch.ScaleX = punch.ScaleY = t < .08 ? 1.22 : 1 + .22 * (1 - Out(Seg(t, .08, .24)));
            tag.SweepAt(Band(t, .1, .3), Colors.White);
            card.Wipe = score.Wipe = rest.Wipe = t < .8 ? Out(Seg(t, .08, .28)) : 1 - Seg(t, .8, .94);
            slide.X = -46 * (1 - Out(Seg(t, .08, .34)));
            double words = t < .8 ? Seg(t, .18, .3) : 1 - Seg(t, .8, .86);
            if (card.Content != null) card.Content.Opacity = words;
            fig.Opacity = words; pass.Opacity = words;
            card.SweepAt(Band(t, .28, .3), CyanC);
            foreach (var (r, i) in rows)
            {
                r.SweepAt(Band(t, .14 + i * .05), CyanC);
                if (r.Leader) r.Pulse = Seg(t, .1, .4);
            }
        }, () =>
        {
            _fight.Children.Remove(row);
            if (hp != null) hp.Opacity = 1;
            foreach (var (r, _) in rows) { r.SweepAt(-1, CyanC); r.Crown = 1; }
        });
    }
}
