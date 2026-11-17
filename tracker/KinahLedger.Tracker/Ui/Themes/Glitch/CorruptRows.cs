using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;
using static KinahLedger.Ui.GlitchKit;

namespace KinahLedger.Ui;

/// <summary>
/// Corrupted's meter: torn data rows. Each member is a dark slab knocked a few px sideways (the slab, never the words),
/// an edge in their class's colour on its left, a thin level along its foot in their class's colour with a magenta ghost
/// and a cyan ghost of it a hair off, the name in white, the DPS and the share right-aligned. You: the rank in a magenta
/// block, a cyan pointer, a white edge round your slab. Deaths a red ✕ block, TOP / HEAL stamped as blocks at a kill (short, so the name and its CP keep their room).
/// The foot: total and group_dps in the stream's words, AVG stamped once the fight is held.
/// </summary>
static class CorruptRows
{
    static readonly FontFamily Face = CodeFace;
    const uint MagC = 0xFF2B6A, CyanC = 0x2BE6FF, LilacC = 0xC9B8FF, GreenC = 0x3DFF8C, RedC = 0xFF3B3B;
    static readonly Brush White = Br(0xFFFFFF), Lilac = Br(LilacC), LilacDim = Br(LilacC, .7), Black = Br(0x000000), Mag = Br(MagC), Cyan = Br(CyanC), Green = Br(GreenC);

    public const double Pitch = 24, Height = 21, GapHeight = 16;
    /// <summary>How far each rank's slab sits sideways: torn, but never past the panel's own tear room.</summary>
    static readonly double[] Torn = { 0, 5, -3, 4, -2, 5, -4, 3, -2, 4 };
    /// <summary>How far each rank's slab foot sits from its top: the slab torn along a line through it.</summary>
    static readonly double[] Split = { -4, 3, 5, -3, 4, -5, 2, -3, 5, -2 };

    public static FrameworkElement Gap(int more)
    {
        var t = Txt(more > 0 ? "/* " + L.F("{0} more", more) + " */" : "/* … */", 9.5, LilacDim, Face);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    public sealed class Row : IThemeRow
    {
        readonly Grid _root = new() { Height = CorruptRows.Height };
        readonly Grid _slab = new() { Margin = new Thickness(-4, 0, -4, 0) };
        readonly Border _ground = new() { BorderThickness = new Thickness(1, 1, 1, 0), Height = 9, VerticalAlignment = VerticalAlignment.Top };
        readonly Border _foot = new() { BorderThickness = new Thickness(1, 0, 1, 1), Margin = new Thickness(0, 10, 0, 0) };
        readonly TranslateTransform _footShift = new();
        readonly Border _edge = new() { Width = 4, HorizontalAlignment = HorizontalAlignment.Left };
        readonly TextBlock _code = Txt("", 10, Lilac, Face);
        readonly TranslateTransform _tear = new();
        readonly Border _glint = new() { Opacity = 0, IsHitTestVisible = false, Margin = new Thickness(-4, 0, -4, 0) };
        readonly Border _rankBox = new() { Width = 20, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _rank = Txt("", 10.5, Lilac, Face);
        readonly TextBlock _you = Txt("▶", 9, Cyan, Face);
        readonly TextBlock _name = Txt("", 12, White, Face, trim: true);
        /// <summary>The Combat Power ("4.7K") beside the name, smaller and in dim lavender; it gives way before the name is cut short.</summary>
        readonly TextBlock _cp = Txt("", 9.5, LilacDim, Face);
        readonly StackPanel _tags = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _heal = Txt("", 10, Green, Face);
        readonly TextBlock _extra = Txt("", 10, LilacDim, Face), _fig = FigCell(13, White, Face, 42), _share = FigCell(10.5, Lilac, Face, 46);
        readonly GlitchLevel _bar = new(4, Br(LilacC, .08));

        public FrameworkElement Root => _root;

        public Row()
        {
            // the slab and its edge, knocked sideways together
            _slab.RenderTransform = _tear;
            _foot.RenderTransform = _footShift;
            _slab.Children.Add(_ground); _slab.Children.Add(_foot); _slab.Children.Add(_edge);
            _root.Children.Add(_slab);
            // the level along the foot, its ghosts a hair off either side
            _bar.Echo(-2, 0).Background = Br(MagC, .6);
            _bar.Echo(2, 0).Background = Br(CyanC, .6);
            _bar.Root.VerticalAlignment = VerticalAlignment.Bottom; _bar.Root.Margin = new Thickness(0, 0, 0, 1);
            _root.Children.Add(_bar.Root);
            var g = new Grid { Margin = new Thickness(4, 0, 10, 3) };
            foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            _rank.HorizontalAlignment = HorizontalAlignment.Center; _rankBox.Child = _rank;
            g.Children.Add(_rankBox);
            _code.Margin = new Thickness(5, 1, 0, 0);
            _you.Margin = new Thickness(0, 0, 4, 0);
            DockPanel.SetDock(_you, Dock.Left); DockPanel.SetDock(_tags, Dock.Right);
            _cp.Margin = new Thickness(6, 2, 0, 0);
            // the code, the pointer, the name, cp= and the tags packed from the left: the cp gives way before the name is cut short
            var name = new GlitchNameLine { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 0, 0), Text = _name, Yields = _cp, Children = { _code, _you, _name, _cp, _tags } };
            _code.Margin = new Thickness(5, 1, 6, 0);
            Grid.SetColumn(name, 1); g.Children.Add(name);
            _heal.Margin = new Thickness(6, 1, 0, 0); Grid.SetColumn(_heal, 2); g.Children.Add(_heal);
            _extra.Margin = new Thickness(6, 1, 0, 0); Grid.SetColumn(_extra, 3); g.Children.Add(_extra);
            _fig.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(_fig, 4); g.Children.Add(_fig);
            _share.Margin = new Thickness(4, 1, 0, 0); Grid.SetColumn(_share, 5); g.Children.Add(_share);
            _root.Children.Add(g);
            _root.Children.Add(_glint);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            bool you = m.You;
            var cc = m.ClassColor;
            _tear.X = Torn[(m.Rank - 1 + Torn.Length) % Torn.Length];
            var slab = you ? Br(0x2A2140, .95) : Br(0x16132A, .9);
            _ground.Background = slab; _foot.Background = slab;
            _ground.BorderBrush = _foot.BorderBrush = you ? Br(0xFFFFFF, .75) : null;
            _footShift.X = Split[(m.Rank - 1 + Split.Length) % Split.Length];
            _code.Text = ClassCode(m.Class).ToLower(CultureInfo.InvariantCulture);
            _code.Foreground = Frozen(Color.FromArgb(0xD9, cc.R, cc.G, cc.B));
            _edge.Background = Frozen(cc);
            _rank.Text = m.Rank.ToString("00", CultureInfo.InvariantCulture);
            _rankBox.Background = you ? Mag : null;
            _rank.Foreground = you ? White : Lilac;
            _you.Visibility = you ? Visibility.Visible : Visibility.Collapsed;
            _name.Text = m.Name;
            _name.Foreground = you ? Br(0xFFD24A) : White;
            _cp.Inlines.Clear();
            if (m.Cp.Length > 0) _cp.Inlines.Add(Rn(m.Cp, LilacDim));
            _cp.Visibility = m.Cp.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _tags.Children.Clear();
            if (m.Deaths > 0) _tags.Children.Add(Tag("✕" + m.Deaths.ToString(CultureInfo.InvariantCulture), Br(RedC), White, Face, 9));
            if (m.TopDps) _tags.Children.Add(Tag(L.T("TOP"), White, Black, Face, 9));
            if (m.TopHeals) _tags.Children.Add(Tag(L.T("HEAL"), Green, Black, Face, 9));
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : "";
            _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _extra.Text = c.Expanded && m.Damage.Length > 0 ? m.Damage + (m.Crit.Length > 0 ? " " + m.Crit : "") : "";
            _extra.Visibility = _extra.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _bar.Fill.Background = Frozen(cc);
            _bar.Root.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Slide(c.Key + "|bar", m.Share);
            _fig.Text = m.Figure;
            _fig.Foreground = m.CpOnly ? Lilac : White;
            _share.Text = m.ShareText;
        }

        /// <summary>A kill's glint on this row: the slab tears out and back in jumps under a white flash (the words stay).</summary>
        public void Glint(double ms)
        {
            double home = _tear.X;
            double[] jumps = { 9, -7, 6, -4, 2, 0 };
            Motion.Play(_glint, ms, t =>
            {
                int k = (int)(t / .5 * jumps.Length);
                _tear.X = t < .5 ? home + jumps[Math.Min(k, jumps.Length - 1)] : home;
                _glint.Background = t < .5 && k % 2 == 0 ? Br(0xFFFFFF, .3) : t < .5 ? Br(MagC, .25) : null;
                _glint.Opacity = 1;
            }, () => { _tear.X = home; _glint.Background = null; });
        }
    }

    /// <summary>The foot: a torn magenta rule, "total 2.03M" and "group_dps 12.7K", AVG stamped once the fight is held.</summary>
    public static FrameworkElement Foot(Encounter e, DateTime now)
    {
        var ff = FootOf(e, now);
        var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var rule = new Grid { Height = 2, Margin = new Thickness(0, 0, 0, 5) };
        rule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        rule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var a = new Border { Background = Br(MagC, .7), Height = 1, VerticalAlignment = VerticalAlignment.Top };
        var b = new Border { Background = Br(CyanC, .7), Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 0, 0) };
        var c = new Border { Background = Br(MagC, .7), Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-2, 0, 0, 0) };
        Grid.SetColumn(b, 1); Grid.SetColumn(c, 2);
        rule.Children.Add(a); rule.Children.Add(b); rule.Children.Add(c);
        sp.Children.Add(rule);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = Txt("", 13.5, White, Face);
        left.Inlines.Add(Rn(Words(L.T("Total ").Trim()) + " ", LilacDim, 11));
        left.Inlines.Add(Rn(ff.Total, White));
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var r = Txt("", 13.5, White, Face);
        if (ff.Label.Length > 0)
        {
            r.Inlines.Add(Rn(Words(ff.Label) + " ", LilacDim, 11));
            r.Inlines.Add(Rn(ff.Dps, White));
        }
        right.Children.Add(r);
        if (ff.Held) right.Children.Add(Tag(L.T("AVG"), Mag, White, Face, 9.5, new Thickness(6, 0, 0, 0)));
        Grid.SetColumn(right, 1); g.Children.Add(right);
        sp.Children.Add(g);
        TagFoot(sp, e, now);
        return sp;
    }

    /// <summary>"Group DPS" as the stream writes it: "group_dps".</summary>
    static string Words(string s)
    {
        s = s.ToLower(CultureInfo.CurrentCulture);
        int i = s.IndexOf(" (", StringComparison.Ordinal);
        return i < 0 ? s.Replace(' ', '_') : s[..i].Replace(' ', '_') + s[i..];
    }
}
