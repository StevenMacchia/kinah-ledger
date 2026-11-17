using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The Parser panel's three looks (Steven, 2026-11-17: "Forge, Podium and Reactor are fantastic too, build them"), each the
/// panel's head, the boss's HP and its foot in the look's own parts, over the rows in its skin (Ui/ParserRows.Skins.cs):
/// <list type="bullet">
/// <item>Forge (key "standard", the default): the boss's art lit gold, the boss, the clock large (gold while it runs); the HP a
/// lit rail notched at its thirds, its result once over; a gold hairline over the foot.</item>
/// <item>Podium: the boss over the place, the clock in a lit gold capsule; the HP a ribbon with its words inside.</item>
/// <item>Reactor: an instrument panel; the clock in an inset readout, the HP a track lit cell by cell; the foot two gauges.</item>
/// </list>
/// </summary>
public sealed partial class ParserPanel
{
    /// <summary>The panel's look for a look key: Podium, Reactor, else Forge (the standard panel).</summary>
    public static ParserRows.Skin SkinOf(string? look) => look switch { ParserPrefs.PodiumLook => ParserRows.Skin.Podium, ParserPrefs.ReactorLook => ParserRows.Skin.Reactor, _ => ParserRows.Skin.Forge };

    ParserRows.Skin _skin = ParserRows.Skin.Forge;
    /// <summary>The panel's look (its rows are built again in it).</summary>
    public ParserRows.Skin Skin
    {
        get => _skin;
        set
        {
            if (_skin == value) return;
            _skin = value;
            ParserRows.Detach(_prows);
            _prows = new ParserRows(ParserRows.Kind.Panel, value);
            _sig = "";
        }
    }

    static readonly Color SInk = Color.FromRgb(0xF4, 0xF5, 0xF9), SGold = Color.FromRgb(0xF5, 0xB9, 0x42), SGoldHi = Color.FromRgb(0xFF, 0xD9, 0x7A),
        SMint = Color.FromRgb(0x3D, 0xD6, 0x8C), SCoral = Color.FromRgb(0xF2, 0x6D, 0x6D), SQuiet = Color.FromRgb(0x8A, 0x90, 0xA4), SSoft = Color.FromRgb(0xB1, 0xB7, 0xC8);
    static SolidColorBrush SB(Color c, double a = 1) { var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B)); b.Freeze(); return b; }
    static Color SA(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);
    static Color SMix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static Color SLit(Color c) => SMix(c, Colors.White, 0.28);
    static Color SDeep(Color c) => SMix(c, Color.FromRgb(0x0B, 0x0C, 0x10), 0.45);
    static LinearGradientBrush SAcross(params (Color C, double At)[] s) => SGrad(new Point(0, 0), new Point(1, 0), s);
    static LinearGradientBrush SDown(params (Color C, double At)[] s) => SGrad(new Point(0, 0), new Point(0, 1), s);
    static LinearGradientBrush SGrad(Point a, Point b, (Color C, double At)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = a, EndPoint = b };
        foreach (var (c, at) in stops) g.GradientStops.Add(new GradientStop(c, at));
        g.Freeze();
        return g;
    }
    static DropShadowEffect SGlow(Color c, double blur, double opacity) => new() { Color = c, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity };
    static DropShadowEffect SLift(double blur = 3, double opacity = 0.7) => new() { Color = Colors.Black, BlurRadius = blur, ShadowDepth = 1, Direction = 270, Opacity = opacity };
    static TextBlock ST(string s, double size, FontWeight w, Color c, double a = 1) { var t = T(s, size, w, SB(c, a)); Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); return t; }
    static T2 At<T2>(T2 el, int col) where T2 : UIElement { Grid.SetColumn(el, col); return el; }
    static Grid Cols3(bool lead)
    {
        var g = new Grid();
        if (lead) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return g;
    }

    /// <summary>The fight's tone once over: mint for a kill (or a run cleared), coral for a wipe or a death, grey otherwise.</summary>
    static Color ToneOf(Encounter e) => e.Tone switch { "good" => SMint, "bad" => SCoral, _ => SSoft };
    string Clock(Encounter e, DateTime now) => e.Waiting ? L.T("Ready") : BossParser.Clock(e.Duration(now));
    /// <summary>The mode and where (Forge says it only for a partial record; Reactor's sub line).</summary>
    static string ModeWhere(Encounter e)
    {
        var where = PK.WhereOf(e);
        var mode = e.Always ? L.T("Always on") : L.T(BossParser.Name(e.ModeKind));
        return where.Length > 0 ? $"{mode} · {where}" : mode;
    }
    /// <summary>A partial record, said in amber under the head with what and where (its tooltip says why).</summary>
    static FrameworkElement Partial(Encounter e)
    {
        var t = ST(ModeWhere(e), 10.5, FontWeights.Medium, SQuiet); t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.Inlines.Add(new Run(L.T("  ·  partial")) { Foreground = HudKit.Warn, FontWeight = FontWeights.SemiBold });
        t.ToolTip = ParserCommand.WhyText(e);
        t.Margin = new Thickness(0, 2, 0, 0);
        return t;
    }

    /// <summary>The head in the panel's look.</summary>
    FrameworkElement SkinHead(Encounter e, DateTime now)
    {
        string artKey = e.ModeKind == ParserMode.Run ? "dungeon" : e.ModeKind == ParserMode.Fights && e.Boss.Length == 0 ? (e.Kind == "Abyss" ? "rift" : "medal") : "boss";
        var st = new StackPanel();
        if (_skin == ParserRows.Skin.Podium)
        {
            var g = Cols3(false);
            var title = ST(e.Title, 15, FontWeights.Bold, SInk); title.TextTrimming = TextTrimming.CharacterEllipsis; title.Effect = SLift();
            var place = ST(e.Place.Length > 0 ? e.Place : PK.WhereOf(e), 10, FontWeights.Medium, SQuiet); place.TextTrimming = TextTrimming.CharacterEllipsis;
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title } };
            if (place.Text.Length > 0) words.Children.Add(place);
            g.Children.Add(words);
            bool run = e.Live && !e.Waiting;
            var clock = ST(Clock(e, now), e.Waiting ? 13 : 17, FontWeights.Bold, run ? Color.FromRgb(0x1C, 0x13, 0x02) : e.Waiting ? SSoft : SInk);
            g.Children.Add(At(new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 1, 10, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = clock,
                Background = run ? SDown((SGoldHi, 0), (SGold, 0.6), (Color.FromRgb(0xC8, 0x8A, 0x1E), 1)) : SB(Colors.White, 0.07),
                BorderBrush = SB(Colors.White, run ? 0.35 : 0.12), BorderThickness = new Thickness(0, 1, 0, 0), Effect = run ? SGlow(SGold, 14, 0.5) : null,
            }, 1));
            st.Children.Add(g);
        }
        else if (_skin == ParserRows.Skin.Reactor)
        {
            var g = Cols3(true);
            var art = GameArt.Element(artKey, 22); art.Margin = new Thickness(0, 0, 8, 0);
            g.Children.Add(art);
            var title = ST(e.Title, 14, FontWeights.Bold, SInk); title.TextTrimming = TextTrimming.CharacterEllipsis;
            bool held = !e.Live && !e.Waiting;
            var sub = ST(held ? OutcomeOf(e, now).Text : ModeWhere(e), 10, FontWeights.SemiBold, held ? ToneOf(e) : SQuiet); sub.TextTrimming = TextTrimming.CharacterEllipsis;
            if (e.Partial) { sub.Inlines.Add(new Run(L.T("  ·  partial")) { Foreground = HudKit.Warn }); sub.ToolTip = ParserCommand.WhyText(e); }
            g.Children.Add(At(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, sub } }, 1));
            bool run = e.Live && !e.Waiting;
            var clock = ST(Clock(e, now), e.Waiting ? 13 : 17, FontWeights.Bold, run ? SGold : e.Waiting ? SSoft : SInk);
            clock.HorizontalAlignment = HorizontalAlignment.Center;
            if (run) clock.Effect = SGlow(SGold, 10, 0.6);
            var ro = Readout(clock, run ? SGold : Colors.White, run ? 0.45 : 0.15); ro.Margin = new Thickness(8, 0, 0, 0); ro.MinWidth = 76;
            g.Children.Add(At(ro, 2));
            st.Children.Add(g);
        }
        else
        {
            var g = Cols3(true);
            var art = GameArt.Element(artKey, 24); art.Margin = new Thickness(0, 0, 9, 0); art.Effect = SGlow(SGold, 10, 0.35);
            g.Children.Add(art);
            var title = ST(e.Title, 14.5, FontWeights.Bold, SInk); title.TextTrimming = TextTrimming.CharacterEllipsis; title.Effect = SLift();
            g.Children.Add(At(title, 1));
            bool run = e.Live && !e.Waiting;
            var clock = ST(Clock(e, now), e.Waiting ? 13 : 20, FontWeights.Bold, run ? SGold : e.Waiting ? SSoft : SInk);
            clock.Margin = new Thickness(10, -2, 0, 0);
            if (run) clock.Effect = SGlow(SGold, 12, 0.55);
            g.Children.Add(At(clock, 2));
            st.Children.Add(g);
            if (e.Partial) st.Children.Add(Partial(e));
        }
        if (_skin == ParserRows.Skin.Podium && e.Partial) st.Children.Add(Partial(e));
        return st;
    }

    /// <summary>The boss's HP in the panel's look (live), or the fight's result on it once over; null with neither.</summary>
    FrameworkElement? SkinHp(Encounter e, DateTime now)
    {
        if (e.Waiting) return null;
        bool live = e.Live && e.BossHpLeft != null, held = !e.Live;
        if (!live && !(held && _skin != ParserRows.Skin.Reactor)) return null;
        double left = live ? e.BossHpLeft!.Value : e.BossHpLeft is { } l && e.Outcome != "kill" && e.Outcome != "cleared" ? l : 1;
        var c = held ? ToneOf(e) : left < 0.34 ? SCoral : SGold;
        if (_skin == ParserRows.Skin.Podium)
        {
            // the ribbon: its fill, its words inside it
            var rib = new Grid { Margin = new Thickness(0, 9, 0, 0), Height = 16 };
            rib.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = SB(Colors.Black, 0.45), BorderBrush = SB(Colors.White, 0.06), BorderThickness = new Thickness(1) });
            var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = SDown((SA(SLit(c), 0.85), 0), (SA(c, 0.55), 0.5), (SA(SDeep(c), 0.6), 1)) };
            rib.SizeChanged += (_, _) => fill.Width = Math.Max(4, rib.ActualWidth * left);
            rib.Children.Add(fill);
            if (live) { Motion.HpGhost(rib, _hpPlace, e.Id, left, now, 3); rib.Children.Add(HudKit.HpTicks()); }
            FrameworkElement words = live ? HpText(e, 10.5, SB(SInk), SB(SInk, 0.85)) : ST(OutcomeOf(e, now).Text, 10, FontWeights.Bold, SInk);
            words.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.9 };
            words.Margin = new Thickness(7, 0, 7, 0); words.HorizontalAlignment = HorizontalAlignment.Left;
            rib.Children.Add(words);
            return rib;
        }
        var g = Cols3(false);
        g.Margin = new Thickness(0, _skin == ParserRows.Skin.Reactor ? 8 : 9, 0, 0);
        Grid track;
        if (_skin == ParserRows.Skin.Reactor)
        {
            // a recessed track lit cell by cell
            track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = SDown((Color.FromArgb(0xD0, 0x01, 0x01, 0x03), 0), (Color.FromArgb(0xB0, 0x0A, 0x0C, 0x10), 1)),
                BorderBrush = SDown((Color.FromArgb(0x80, 0, 0, 0), 0), (Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 1)), BorderThickness = new Thickness(1) });
            var cells = Cells(4);
            track.Children.Add(new Border { Margin = new Thickness(2), Background = SB(c, 0.10), OpacityMask = cells });
            var lit = new Border { Margin = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Background = SAcross((SDeep(c), 0), (c, 0.65), (SMix(c, Colors.White, 0.5), 1)), OpacityMask = cells, Effect = SGlow(c, 9, 0.75) };
            track.SizeChanged += (_, _) => lit.Width = Math.Max(4, (track.ActualWidth - 4) * left);
            track.Children.Add(lit);
        }
        else
        {
            // a lit rail
            track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = SDown((Color.FromArgb(0x60, 0, 0, 0), 0), (Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), 1)) });
            var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = SDown((SLit(c), 0), (c, 0.55), (SDeep(c), 1)), Effect = SGlow(c, 9, 0.6) };
            track.SizeChanged += (_, _) => fill.Width = Math.Max(4, track.ActualWidth * left);
            track.Children.Add(fill);
        }
        if (live) Motion.HpGhost(track, _hpPlace, e.Id, left, now, 3);
        track.Children.Add(HudKit.HpTicks());   // a third and two thirds
        g.Children.Add(track);
        FrameworkElement text = live ? HpText(e, _skin == ParserRows.Skin.Reactor ? 11 : 11.5, SB(left < 0.34 ? SCoral : SInk), SB(SSoft)) : ST(OutcomeOf(e, now).Text, 11.5, FontWeights.Bold, c);
        text.Margin = new Thickness(_skin == ParserRows.Skin.Reactor ? 9 : 10, 0, 0, 0);
        g.Children.Add(At(text, 1));
        return g;
    }

    static DrawingBrush Cells(double cell)
    {
        var d = new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, cell, 1)));
        var b = new DrawingBrush(d) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, cell + 1, 1), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, cell + 1, 1), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    /// <summary>An inset readout: a dark window sunk into the panel, a hairline of light along its lower lip.</summary>
    static Border Readout(UIElement child, Color edge, double a = 0.35) => new()
    {
        Child = child, CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 2),
        Background = SDown((Color.FromArgb(0xF0, 0x03, 0x04, 0x06), 0), (Color.FromArgb(0xE0, 0x0D, 0x0F, 0x14), 1)),
        BorderBrush = SDown((Color.FromArgb(0x90, 0, 0, 0), 0), (SA(edge, a), 1)), BorderThickness = new Thickness(1),
    };

    /// <summary>The foot in the panel's look: the fight's total, the group's figure (gold), the held average badged.</summary>
    FrameworkElement SkinFoot(Encounter e, DateTime now)
    {
        if (_skin == ParserRows.Skin.Reactor)
        {
            // two gauges, each an inset readout: Total, and the group's figure (or your best / peak alone)
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0), Tag = "parser-foot" };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            FrameworkElement Gauge(string label, string fig, Color fc, bool badge, bool right)
            {
                var row = Cols3(false);
                var lt = ST(label, 9.5, FontWeights.Bold, SQuiet); lt.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Children.Add(lt);
                var f = ST(fig, 13.5, FontWeights.Bold, fc); f.HorizontalAlignment = HorizontalAlignment.Right; f.Margin = new Thickness(8, 0, 0, 0);
                if (right) f.Tag = "foot-right";
                var figs = new StackPanel { Orientation = Orientation.Horizontal, Children = { f } };
                if (badge) figs.Children.Add(ParserFoot.AvgBadge());
                row.Children.Add(At(figs, 1));
                var ro = Readout(row, Colors.White, 0.12); ro.Padding = new Thickness(8, 3, 8, 3);
                return ro;
            }
            g.Children.Add(Gauge(L.T("Total"), Amounts.Meter(e.Credited), SInk, false, false));
            var (word, value, held) = ParserFoot.Right(e, now);
            if (word.Length > 0 || held) g.Children.Add(At(Gauge(word, value, SGold, held, word.Length > 0), 2));
            System.Windows.Automation.AutomationProperties.SetName(g, e.FootText(now));
            return g;
        }
        var st = new StackPanel();
        st.Children.Add(new Border
        {
            Height = 1, Margin = new Thickness(0, _skin == ParserRows.Skin.Podium ? 4 : 6, 0, 0),
            Background = _skin == ParserRows.Skin.Podium ? SB(Colors.White, 0.08) : SAcross((SA(SGold, 0), 0), (SA(SGold, 0.28), 0.5), (SA(SGold, 0), 1)),
        });
        var line = ParserFoot.Line(e, now, 12, null, SB(SGold));
        line.Margin = new Thickness(2, 8, 2, 0);
        st.Children.Add(line);
        return st;
    }
}
