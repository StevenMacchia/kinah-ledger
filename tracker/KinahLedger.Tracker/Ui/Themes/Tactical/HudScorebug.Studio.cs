using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Studio (Steven picked it, 2026-11-17): the Scorebug's fight as a daytime studio package. White glossy cards floating on
/// soft shadows, dark crisp type, a red spine down the title and the foot, the clock inset on black glass, and each member
/// a card of their own with a thick glossy band in their class's colour along its foot. The Kinah side is the Scorebug's bug.
/// </summary>
sealed partial class HudScorebug
{
    static readonly Color PaperTop = Colors.White, PaperBot = Color.FromRgb(0xE6, 0xE9, 0xEF), PaperInk = Color.FromRgb(0x12, 0x15, 0x1C), PaperSub = Color.FromRgb(0x5D, 0x64, 0x74),
        PaperGroove = Color.FromRgb(0xC3, 0xC8, 0xD2), PaperGroove2 = Color.FromRgb(0xDC, 0xE0, 0xE7), PaperRed = Color.FromRgb(0xC8, 0x1E, 0x30), PaperGreen = Color.FromRgb(0x14, 0x93, 0x5A);

    /// <summary>Studio's pickup strip under the bug: on a light card, the rule the cards' grey groove, the star the gold.</summary>
    static readonly PickStrip.Look StudioStrip = new(Size: 24, Gap: 4, Rule: Frozen(PaperGroove));

    /// <summary>The title card: the red spine, the boss in dark type, the outcome, the clock on black glass (in a dungeon the next timer after it).</summary>
    FrameworkElement StudioTitle(Encounter e, DateTime now)
    {
        var head = new Grid { Height = 32 };
        head.Children.Add(new TacSketch((dc, s) =>
        {
            var g = Slant(new Rect(s), 0, 9);
            Gloss(dc, g, PaperTop, PaperBot, 0, 0);
            dc.PushClip(g);
            dc.DrawRectangle(Tac.Grad(true, (Tac.Mix(RedC, Colors.White, .15), 0), (RedDeep, 1)), null, new Rect(0, 0, 7, s.Height));
            dc.DrawRectangle(Frozen(Colors.White), null, new Rect(0, 0, s.Width, 1));
            dc.Pop();
        }) { Effect = Glow(12, 3, .5) });
        var hg = new Grid { Margin = new Thickness(17, 0, 9, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = C(e.Title, 16, PaperInk, FontWeights.Bold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        hg.Children.Add(title);
        if (OutcomeText(e) is { Length: > 0 } ow)
        {
            var o = C(Up(ow), 12, e.Outcome == "wipe" ? PaperRed : PaperGreen, FontWeights.Bold, Semi); o.Margin = new Thickness(10, 1, 9, 0);
            Grid.SetColumn(o, 1); hg.Children.Add(o);
        }
        var clockT = e.Waiting ? C(L.T("Ready"), 13.5, GreyOf, FontWeights.SemiBold) : C(Clock(e.Duration(now)), 18, e.Live ? YellowC : Colors.White, FontWeights.Bold);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var clock = GCell((dc, s) => Gloss(dc, Slant(new Rect(s), 6, 6), Color.FromRgb(0x2A, 0x2E, 0x38), Color.FromRgb(0x0B, 0x0C, 0x10), .18, .35), clockT, 24, new Thickness(13, 0, 13, 0));
        right.Children.Add(clock);
        if (KinahHidden && _timer is { } t)
        {
            var tb = TimerPlate(t.Text, t.Tone, 24, "");
            tb.Root.Margin = new Thickness(-Lean + 2.5, 0, 0, 0);
            right.Children.Add(tb.Root);
        }
        Grid.SetColumn(right, 2); hg.Children.Add(right);
        head.Children.Add(hg);
        return head;
    }

    static readonly Color GreyOf = Color.FromRgb(0xA9, 0xAF, 0xBE);

    /// <summary>The boss's HP on a white strip: a rounded red bar in its groove, white notches at the thirds, "47%" and what's left.</summary>
    FrameworkElement StudioHp(Encounter e, double hp, DateTime now)
    {
        var strip = new Grid { Height = 20, Margin = new Thickness(0, 3, 12, 0) };
        strip.Children.Add(new TacSketch((dc, s) => Gloss(dc, Slant(new Rect(s), 0, 7), Tac.A(PaperTop, .97), Tac.A(PaperBot, .97), 0, 0)) { Effect = Glow(10, 2, .4) });
        var g = new Grid { Margin = new Thickness(12, 0, 14, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var track = new Grid { Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        track.Children.Add(new TacSketch((dc, s) =>
        {
            dc.DrawRoundedRectangle(Tac.Grad(true, (PaperGroove, 0), (PaperGroove2, 1)), null, new Rect(s), 3.5, 3.5);
            dc.DrawRoundedRectangle(Tac.Grad(true, (Tac.Mix(RedC, Colors.White, .25), 0), (RedDeep, 1)), null, new Rect(0, 0, Math.Max(7, s.Width * hp), s.Height), 3.5, 3.5);
            for (int k = 1; k <= 2; k++) dc.DrawRectangle(Frozen(Colors.White), null, new Rect(Math.Round(s.Width * k / 3) - 1, -1, 2, s.Height + 2));
        }) { Tag = "hp-ticks" });
        HpGhost(track, e, hp, now, 3.5);
        g.Children.Add(track);
        var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(C($"{Math.Round(hp * 100):0}%", 13, hp < .34 ? PaperRed : PaperInk, FontWeights.Bold));
        if (HpLeftText(e) is { Length: > 0 } left) words.Children.Add(C("  " + left, 11.5, PaperSub, FontWeights.SemiBold, Semi));
        Grid.SetColumn(words, 1); g.Children.Add(words);
        strip.Children.Add(g);
        return strip;
    }

    /// <summary>Before the first pull: a white card with the ready line.</summary>
    static FrameworkElement StudioReady(Encounter e)
    {
        var wt = C(e.ReadyText, 14, PaperSub, FontWeights.SemiBold, Semi); wt.Margin = new Thickness(16, 0, 14, 0); wt.TextTrimming = TextTrimming.CharacterEllipsis;
        var card = new Grid { Height = 28, Margin = new Thickness(0, 4, 0, 0) };
        card.Children.Add(new TacSketch((dc, s) => Gloss(dc, Slant(new Rect(s), 0, 8), PaperTop, PaperBot, 0, 0)) { Effect = Glow(10, 2, .4) });
        card.Children.Add(wt);
        return card;
    }

    /// <summary>The foot: a dark glossy card with the red spine, TOTAL and the fight's figure, the group's word and figure (yellow), the AVG. DPS badge.</summary>
    static FrameworkElement StudioFoot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var foot = new Grid { Height = 26, Margin = new Thickness(0, 2, 0, 0) };
        foot.Children.Add(new TacSketch((dc, s) =>
        {
            Gloss(dc, Slant(new Rect(s), 0, 9), Color.FromRgb(0x1C, 0x1F, 0x27), Color.FromRgb(0x0A, 0x0B, 0x0F), .14, .3);
            dc.DrawRectangle(Tac.Grad(true, (Tac.Mix(RedC, Colors.White, .15), 0), (RedDeep, 1)), null, new Rect(0, 0, 7, s.Height));
        }) { Effect = Glow(12, 3, .5) });
        var fg = new Grid { Margin = new Thickness(17, 0, 14, 0) };
        fg.ColumnDefinitions.Add(new ColumnDefinition()); fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tl.Children.Add(C(Up(L.T("Total")), 11, GreyOf, FontWeights.Bold, Semi));
        var tv = C(total, 16, Colors.White, FontWeights.Bold); tv.Margin = new Thickness(8, 0, 0, 0); tl.Children.Add(tv);
        EnergyMark.After(tl, 12.5, Frozen(GreyOf));   // Odyle Energy (Ui/EnergyMark.cs)
        fg.Children.Add(tl);
        var rt = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (word.Length > 0) { rt.Children.Add(C(Up(word), 11, GreyOf, FontWeights.Bold, Semi)); var gv = C(group, 16, YellowC, FontWeights.Bold); gv.Margin = new Thickness(8, 0, 0, 0); rt.Children.Add(gv); }
        if (held)
            rt.Children.Add(new Border { Background = Frozen(GreenC), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Tag = "avg-badge", Child = C(Up(L.T(Encounter.AvgBadge)), 10, InkC0, FontWeights.Bold, Semi) });
        Grid.SetColumn(rt, 1); fg.Children.Add(rt);
        foot.Children.Add(fg);
        return foot;
    }

    /// <summary>Studio's line between the party and you pinned below it: "▼ 1 more" on a white pill.</summary>
    static FrameworkElement StudioGap(int left)
    {
        var t = C(left > 0 ? "▼  " + L.F("{0} more", left) : "▼", 10, Color.FromRgb(0x3A, 0x40, 0x4E), FontWeights.SemiBold, Semi);
        t.Margin = new Thickness(0, -1, 0, 0);
        return new Border { Padding = new Thickness(9, 0, 9, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = t,
            Background = Frozen(Tac.A(Colors.White, .94)), CornerRadius = new CornerRadius(8), Effect = Glow(6, 1, .45) };
    }

    /// <summary>
    /// Studio's row: a white glossy card floating on a soft shadow (yours blush pink with a red glow), the rank block down its
    /// left (gold, silver, bronze, slate; red for you), the emblem, the name in dark type, Combat Power quiet beside it, a
    /// healer's healing in green, the marks as small tags; the DPS big at the right; along the card's foot the bar, a thick
    /// glossy band in the class's colour in the card's groove, the share at its end. With Details, the damage and crit before
    /// the DPS. Short of room the name trims first (never a 10-letter name), then the TOP tags go to their glyphs, then the CP.
    /// </summary>
    sealed class StudioRow : BoardRow
    {
        /// <summary>The card's height and pitch, the band's thickness and its room under the words, the share's room at its end.</summary>
        public const double H = 32, Pitch = 36, Band = 7, BandFoot = 5, ShareW = 40;
        readonly TacSketch _card;
        readonly Grid _rank = new() { Width = 28, HorizontalAlignment = HorizontalAlignment.Left };
        readonly TacSketch _rankArt;
        readonly TextBlock _rankT = C("", 17, Colors.White, FontWeights.Bold);
        readonly Border _iconHost = new() { Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _name = C("", 15, PaperInk, FontWeights.Bold, Semi);
        readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _lineHost = new();
        readonly TextBlock _dps = C("", 19, PaperInk, FontWeights.Bold), _pct = C("", 10.5, PaperSub, FontWeights.Bold, Semi),
            _dmg = C("", 13.5, PaperInk, FontWeights.SemiBold), _crit = C("", 10.5, PaperSub, FontWeights.Bold, Semi);
        readonly Grid _top = new() { Height = H - Band - BandFoot - 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(35, 1, 16, 0) };
        readonly StackPanel _more;
        readonly double _moreW;
        readonly List<FrameworkElement> _keep = new();
        readonly List<(FrameworkElement, FrameworkElement?)> _give = new();
        string _cls = "\0";
        Color _rc;
        bool _expanded;

        public StudioRow() : base(H)
        {
            _card = new TacSketch(DrawCard);
            RootG.Children.Insert(0, _card);
            _rankArt = new TacSketch((dc, s) => Gloss(dc, new RectangleGeometry(new Rect(s)), Tac.Mix(_rc, Colors.White, .15), Tac.Mix(_rc, Colors.Black, .22), .25, .5));
            _rank.Children.Add(_rankArt);
            _rankT.HorizontalAlignment = HorizontalAlignment.Center;
            _rank.Children.Add(_rankT);
            Words.Children.Add(_rank);
            _top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _top.ColumnDefinitions.Add(new ColumnDefinition());
            _top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            _top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tac.FigCol(_dps, "888.8K", "888,888") + 8) });
            _top.Children.Add(_iconHost);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _lineHost.Child = _line;
            Grid.SetColumn(_lineHost, 1); _top.Children.Add(_lineHost);
            _dmg.VerticalAlignment = _crit.VerticalAlignment = VerticalAlignment.Center;
            _crit.Margin = new Thickness(5, 2, 0, 0);
            _more = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), Children = { _dmg, _crit } };
            _moreW = Tac.FigCol(_dmg, "888.88M") + Tac.FigCol(_crit, "88%") + 13;
            Grid.SetColumn(_more, 2); _top.Children.Add(_more);
            // the figure's line box overhangs the slim words' line a little (never laid out cut): its digits sit on the name's line
            _dps.Tag = "row-dps"; _dps.HorizontalAlignment = HorizontalAlignment.Right; _dps.Margin = new Thickness(0, -3, 0, -3);
            Grid.SetColumn(_dps, 3); _top.Children.Add(_dps);
            Words.Children.Add(_top);
            _pct.HorizontalAlignment = HorizontalAlignment.Right; _pct.VerticalAlignment = VerticalAlignment.Bottom; _pct.Margin = new Thickness(0, 0, 18, 1);
            Words.Children.Add(_pct);
            Refit = () => FitLine(_lineHost, _name, _keep, _give);
        }

        protected override bool EnterWhole => true;

        static FrameworkElement Tag(string s, Color bg, Color fg, string? tag = null) => new Border
        {
            Background = Tac.Grad(true, (Tac.Mix(bg, Colors.White, .2), 0), (bg, 1)), CornerRadius = new CornerRadius(2.5), Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(6, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Tag = tag, Child = C(s, 9.5, fg, FontWeights.Bold, Semi),
        };

        public override void Set(MeterMember m, MeterContext c, bool fresh)
        {
            M = m;
            if (_expanded != c.Expanded) { _expanded = c.Expanded; _top.ColumnDefinitions[2].Width = new GridLength(c.Expanded ? _moreW : 0); }
            _rc = m.You ? RedC : RankColor(m) == SlateC ? Color.FromRgb(0x2A, 0x2E, 0x38) : RankColor(m);
            _rankArt.InvalidateVisual();
            _rankT.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            _rankT.Foreground = Frozen(RankColor(m) == SlateC || m.You ? Colors.White : InkC0);
            if (_cls != m.Class + "|" + m.Name) { _cls = m.Class + "|" + m.Name; _iconHost.Child = ThemeText.Icon(m.Class, 18, m.Name); }
            _name.Text = m.Name;
            _line.Children.Clear(); _keep.Clear(); _give.Clear();
            _line.Children.Add(_name);
            TextBlock Small(string s, Color col) { var t = C(s, 11, col, FontWeights.SemiBold, Semi); t.Margin = new Thickness(6, 2, 0, 0); _line.Children.Add(t); return t; }
            var cp = m.Cp.Length > 0 ? Small(m.Cp, PaperSub) : null;
            if (m.Heal.Length > 0) { var h = Small("+" + m.Heal, Color.FromRgb(0x0F, 0x8A, 0x4F)); h.FontWeight = FontWeights.Bold; h.Tag = "heal-figures"; _keep.Add(h); }
            if (m.Deaths > 0) { var d = Tag(m.Deaths > 1 ? "✕" + m.Deaths : "✕", Color.FromRgb(0xD6, 0x25, 0x38), Colors.White); _line.Children.Add(d); _keep.Add(d); }
            if (m.TopDps) { var f = Tag(Up(L.T("TOP DPS")), GoldTab, InkC0, "top-dps"); var s = Tag("★", GoldTab, InkC0, "top-dps"); s.ToolTip = L.T("Top DPS"); _line.Children.Add(f); _line.Children.Add(s); _give.Add((f, s)); }
            if (m.TopHeals) { var f = Tag(Up(L.T("TOP HEALS")), Color.FromRgb(0x1F, 0xB8, 0x6A), Colors.White, "top-heals"); var s = Tag("✚", Color.FromRgb(0x1F, 0xB8, 0x6A), Colors.White, "top-heals"); s.ToolTip = L.T("Top heals"); _line.Children.Add(f); _line.Children.Add(s); _give.Add((f, s)); }
            if (cp != null) _give.Add((cp, null));
            _dps.Text = m.Figure; _dps.FontSize = m.CpOnly ? 16 : 19;
            _pct.Text = m.ShareText;
            _dmg.Text = c.Expanded ? m.Damage : ""; _crit.Text = c.Expanded ? m.Crit : "";
            _card.Effect = m.You ? Glow(14, 2, .6, RedC) : Glow(10, 3, .45);
            Refit?.Invoke();
            SlideBar(_card, c, "bar", Tac.BarOf(m, c), v => { Bar = v; _card.InvalidateVisual(); });
            _card.InvalidateVisual();
        }

        void DrawCard(DrawingContext dc, Size s)
        {
            if (M is not { } m || s.Width < 80) return;
            var g = Slant(new Rect(s), 0, 9);
            Gloss(dc, g, m.You ? Color.FromRgb(0xFF, 0xF6, 0xF7) : PaperTop, m.You ? Color.FromRgb(0xF0, 0xDD, 0xE0) : PaperBot, 0, 0);
            dc.PushClip(g);
            dc.DrawRectangle(Frozen(Colors.White), null, new Rect(0, 0, s.Width, 1));
            // the band: a thick glossy bar along the card's foot, in the card's own groove
            double x0 = 35, bw = s.Width - x0 - ShareW - 16, by = s.Height - Band - BandFoot;
            if (bw > Band)
            {
                dc.DrawRoundedRectangle(Tac.Grad(true, (PaperGroove, 0), (PaperGroove2, 1)), null, new Rect(x0, by, bw, Band), Band / 2, Band / 2);
                if (Bar > 0)
                {
                    var c = m.ClassColor;
                    var fr = new Rect(x0, by, Math.Max(Band, bw * Math.Clamp(Bar, 0, 1)), Band);
                    dc.DrawRoundedRectangle(Tac.Grad(true, (Tac.Mix(c, Colors.White, .25), 0), (Tac.Mix(c, Colors.Black, .12), .55), (Tac.Mix(c, Colors.Black, .35), 1)), null, fr, Band / 2, Band / 2);
                    dc.DrawRoundedRectangle(Frozen(Tac.A(Colors.White, .55)), null, new Rect(fr.X + 3, fr.Y + 1.2, Math.Max(0, fr.Width - 6), 2), 1, 1);
                    dc.DrawRoundedRectangle(null, Tac.P(Frozen(Tac.A(Tac.Mix(c, Colors.Black, .45), .6)), .8), fr, Band / 2, Band / 2);
                }
            }
            dc.Pop();
        }
    }
}
