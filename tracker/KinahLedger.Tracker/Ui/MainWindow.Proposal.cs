using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The redesign proposal's prototype pages (2026-11-14, docs/ui/redesign-proposal/): the real window, filled by the demo,
/// rearranged into the proposed layout: the header's page pills with Timers (and Parser), one Overlays button, the Farm page
/// as 0.10.80 drew it, and a Timers page built from the window's own pieces and theme. Only
/// `ui-shot --proposal` calls it. Nothing here is wired to a live session.
/// </summary>
public partial class MainWindow
{
    const double PW = 662;   // the ledger column's width

    /// <summary>Rearranges the window into the proposal's <paramref name="page"/>.</summary>
    internal void ProposalPage(string page)
    {
        ProposalShell(page);
        switch (page)
        {
            case "Farm": ProposalFarm(); break;
            case "Timers": ProposalHost(ProposalTimers()); break;
        }
    }

    // ---------------------------------------------------------------- the shell: the header's pills and the Overlays button

    /// <summary>
    /// The header as 0.10.80 drew it, with the page pills Farm · Craft · Runs · Timers · Parser · Quests (Parser and Quests
    /// only while they're on), the Timers pill carrying the next watched countdown, and one round Overlays button left of
    /// the site's.
    /// </summary>
    void ProposalShell(string page)
    {
        // the site's round button moves to the footer's links (kinahledger.com beside Discord), so five pills fit
        SiteBtn.Visibility = Visibility.Collapsed;
        var first = DiscordText.Inlines.FirstInline;
        DiscordText.Inlines.InsertBefore(first, new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("kinahledger.com")) { Style = (Style)FindResource("LinkStyle") });
        DiscordText.Inlines.InsertBefore(first, new System.Windows.Documents.Run("   "));
        if (PartTabs.Child is StackPanel pills)
        {
            pills.Children.Clear();
            foreach (var name in new[] { "Farm", "Craft", "Runs", "Timers", "Parser" })
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                if (name == "Timers") { var tb = TabBadge("4:52", name == page ? "GoldInk" : "Gold"); if (name == page) tb.SetResourceReference(Border.BackgroundProperty, "Gold"); content.Children.Add(tb); }
                var btn = new Button { Content = content, Style = (Style)FindResource(name == page ? "SegOn" : "SegOff"), Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(pills.Children.Count > 0 ? 2 : 0, 0, 0, 0) };
                pills.Children.Add(btn);
            }
        }
        var ov = new Button { Style = (Style)FindResource("RoundBtn"), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Overlays over the game: Kinah, Timers, Parser" };
        var face = new Grid();
        var icon = new System.Windows.Shapes.Path { Style = (Style)FindResource("QIconStyle"), Data = (Geometry)FindResource("IcPanels") };
        face.Children.Add(icon);
        var lit = new Ellipse { Width = 7, Height = 7, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, -4, 0) };
        lit.SetResourceReference(Shape.FillProperty, "Good");
        face.Children.Add(lit);
        ov.Content = face;
        HeadButtons.Children.Insert(0, ov);
    }

    /// <summary>The Overlays button's menu: the three overlays, each a switch, and where each one is set up.</summary>
    internal Border ProposalOverlaysMenu()
    {
        var body = new StackPanel { Width = 300 };
        var head = QT("Overlays over the game", 12.5, FontWeights.SemiBold, "Ink"); head.Margin = new Thickness(14, 12, 14, 2);
        body.Children.Add(head);
        var sub = QT("Each one is separate. Drag it to move it.", 11.5, FontWeights.Medium, "Faint"); sub.Margin = new Thickness(14, 0, 14, 8);
        body.Children.Add(sub);
        foreach (var (name, what, on, where) in new[]
        {
            ("Kinah", "your hour, pickups, best", true, "Look: Options › In game"),
            ("Timers", "events and world bosses, chime 5 min before", true, "Set up on the Timers page"),
            ("Parser", "boss fights, DPS by member", false, "Set up on the Parser page"),
        })
        {
            var g = new Grid { Margin = new Thickness(14, 0, 14, 0), Height = 50 };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(QT(name, 12.5, FontWeights.SemiBold, on ? "Ink" : "Muted"));
            t.Children.Add(QT(what, 11, FontWeights.Medium, "Faint", trim: true));
            t.Children.Add(QT(where, 11, FontWeights.Medium, "Link", trim: true));
            g.Children.Add(t);
            var sw = Switch("", on); sw.Margin = new Thickness(0); Grid.SetColumn(sw, 1); g.Children.Add(sw);
            var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(14, 0, 14, 0) }; rule.SetResourceReference(Border.BorderBrushProperty, "CardHair");
            var host = new Grid(); host.Children.Add(rule); host.Children.Add(g);
            body.Children.Add(host);
        }
        var foot = new Border { Height = 8 };
        body.Children.Add(foot);
        var card = new Border { Child = body, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Card2");
        card.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        return card;
    }

    static Border TabBadge(string text, string tone)
    {
        var t = QT(text, 10.5, FontWeights.Bold, tone);
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        var b = new Border { Child = t, CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, "GoldIcTint");
        return b;
    }

    // ---------------------------------------------------------------- Farm: as 0.10.80 drew it

    void ProposalFarm()
    {
        // Steven, 2026-11-14: "I loved everything until you added inventory and gear worn": the page stays as it was
        InvCard.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- a page of its own in the ledger column

    void ProposalHost(FrameworkElement body)
    {
        foreach (UIElement c in LeftCol.Children) c.Visibility = Visibility.Collapsed;
        TodayStrip.Visibility = Visibility.Collapsed;
        LeftCol.Children.Add(body);
    }

    Border ProposalCard(UIElement child, Thickness? pad = null, Thickness? margin = null)
    {
        var b = new Border { Child = child, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = pad ?? new Thickness(14, 12, 14, 12), Margin = margin ?? new Thickness(0) };
        b.SetResourceReference(Border.BackgroundProperty, "Card");
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        return b;
    }

    /// <summary>A card's head line: its art, its name, a muted note, and on the right a figure or a control.</summary>
    Grid CardHead(string glyph, string title, string note, UIElement? right = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ic = QGlyph(glyph, 14, "Muted"); ic.Margin = new Thickness(0, 0, 8, 0);
        sp.Children.Add(ic);
        sp.Children.Add(QT(title, 12.5, FontWeights.SemiBold, "Ink"));
        if (note.Length > 0) { var n = QT(note, 12, FontWeights.Medium, "Faint"); n.Margin = new Thickness(8, 0, 0, 0); sp.Children.Add(n); }
        g.Children.Add(sp);
        if (right != null) { Grid.SetColumn(right, 2); g.Children.Add(right); }
        return g;
    }

    /// <summary>A page's title band: the name, one line under it, and the page's own controls on the right.</summary>
    Grid PageHead(string title, string sub, params UIElement[] right)
    {
        var g = new Grid { Margin = new Thickness(2, 4, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = QT(title, 20, FontWeights.SemiBold, "Ink"); t.FontFamily = (FontFamily)FindResource("DisplayFont");
        left.Children.Add(t);
        left.Children.Add(QT(sub, 12, FontWeights.Medium, "Faint"));
        g.Children.Add(left);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var r in right) sp.Children.Add(r);
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        return g;
    }

    static FrameworkElement Switch(string label, bool on)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var l = QT(label, 12, FontWeights.SemiBold, on ? "Ink" : "Muted"); l.Margin = new Thickness(0, 0, 8, 0);
        sp.Children.Add(l);
        var track = new Border { Width = 32, Height = 18, CornerRadius = new CornerRadius(9), Padding = new Thickness(2) };
        track.SetResourceReference(Border.BackgroundProperty, on ? "Gold" : "LineStrong");
        var knob = new Ellipse { Width = 14, Height = 14, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        knob.SetResourceReference(Shape.FillProperty, on ? "GoldInk" : "Muted");
        track.Child = knob;
        sp.Children.Add(track);
        return sp;
    }

    static Border SmallBtn(string text, bool primary = false)
    {
        var t = QT(text, 12, FontWeights.SemiBold, primary ? "GoldInk" : "Ink");
        var b = new Border { Child = t, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(10, 0, 0, 0), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, primary ? "Gold" : "Card2");
        b.SetResourceReference(Border.BorderBrushProperty, primary ? "Gold" : "LineStrong");
        return b;
    }

    /// <summary>A segmented choice: the options side by side, the chosen one lit.</summary>
    static Border Seg(string chosen, params string[] options)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var o in options)
        {
            bool on = o == chosen;
            var b = new Border { Child = QT(o, 11.5, FontWeights.SemiBold, on ? "Ink" : "Muted"), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3, 9, 4) };
            b.SetResourceReference(Border.BackgroundProperty, on ? "ItemSel" : "Card");
            sp.Children.Add(b);
        }
        var g = new Border { Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(2), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        g.SetResourceReference(Border.BackgroundProperty, "Card");
        g.SetResourceReference(Border.BorderBrushProperty, "Line");
        return g;
    }

    // ---------------------------------------------------------------- Timers: events and world bosses, bells, the overlay

    FrameworkElement ProposalTimers()
    {
        var page = new StackPanel { Width = PW };
        page.Children.Add(PageHead("Timers", "On the game's clock · the world map read 4 s ago",
            Switch("Overlay", true), SmallBtn("⇱  Pop out")));

        // the alert line: how far ahead, the chime, what is watched
        var alert = new Grid();
        alert.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        alert.ColumnDefinitions.Add(new ColumnDefinition());
        alert.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bell = Bell(true, 16); bell.Margin = new Thickness(0, 0, 10, 0); alert.Children.Add(bell);
        var at = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        at.Children.Add(QT("The overlay pops up 5 min before a belled boss or event", 12.5, FontWeights.SemiBold, "Ink"));
        at.Children.Add(QT("Kashapa, Gartua, Lagta and the Abyss · bell any row to add it", 11.5, FontWeights.Medium, "Faint"));
        Grid.SetColumn(at, 1); alert.Children.Add(at);
        var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ctl.Children.Add(Seg("5 min", "1", "3", "5 min", "10"));
        ctl.Children.Add(Switch("Chime", true));
        Grid.SetColumn(ctl, 2); alert.Children.Add(ctl);
        page.Children.Add(ProposalCard(alert, new Thickness(14, 10, 14, 10), new Thickness(0, 0, 0, 10)));

        // you're in
        var live = new Grid();
        live.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        live.ColumnDefinitions.Add(new ColumnDefinition());
        live.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(2, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }; dot.SetResourceReference(Shape.FillProperty, "Good");
        live.Children.Add(dot);
        var lt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lt.Children.Add(QT("You're in Defend Shugo Merchants", 13, FontWeights.SemiBold, "Ink"));
        lt.Children.Add(QT("Shugo Festival · under way, began 1 min ago", 11.5, FontWeights.Medium, "Faint"));
        Grid.SetColumn(lt, 1); live.Children.Add(lt);
        var lv = QT("Under way", 13, FontWeights.SemiBold, "Good"); Grid.SetColumn(lv, 2); live.Children.Add(lv);
        page.Children.Add(ProposalCard(live, new Thickness(14, 10, 14, 10), new Thickness(0, 0, 0, 10)));

        // belled first, then events, then the rest of the map
        var belled = new (string Name, string Place, string When, string Small, string Tone)[]
        {
            ("Soul Ruler Kashapa", "World boss", "4m 52s", "at 07:03", "Gold"),
            ("Eternal Gartua", "Eternal Isle · Verteron", "5h 06m", "at 11:44", "Ink"),
            ("High Commander Lagta", "Lagta Fortress", "1h 12m", "at 07:50", "Ink"),
        };
        page.Children.Add(TimerSection("Belled", "alerts on", belled.Select(b => TimerRow(b.Name, b.Place, b.When, b.Small, b.Tone, true)).Append(AbyssGroupRow()).ToList()));

        var events = new[]
        {
            TimerRow("Dimensional Invasion", "Every hour at half past", "On now", "started 8 min ago", "Good", false),
            TimerRow("Shugo Festival", "Every hour · Verteron and Altgard", "21m 41s", "at 07:00", "Ink", true),
            TimerRow("Spacetime Rift", "Every 3 hours · one of ten zones", "21m 41s", "at 07:00", "Ink", false),
            TimerRow("Artifact Siege", "Mon, Thu, Sat · Lower Reshanta", "21m 41s", "at 07:00", "Ink", false),
        };
        page.Children.Add(TimerSection("Events", "the site's schedule on the game's clock", events.ToList()));

        var bosses = SampleBosses(DateTime.UtcNow.AddSeconds(-4)).Where(b => b.Name != "Eternal Gartua").Take(10)
            .Select(b => b.State == BossTimers.Kind.Up
                ? TimerRow(b.Name, b.Place, "Up now", $"up {(int)(DateTime.UtcNow - b.SpawnPc).TotalMinutes} min", "Good", false)
                : TimerRow(b.Name, b.Place, BossTimers.Span(b.SpawnPc - DateTime.UtcNow), $"at {b.SpawnPc.ToLocalTime():HH:mm}", "Ink", false)).ToList();
        page.Children.Add(TimerSection("Verteron world bosses", "24 on the map · 14 more below", bosses, Seg("Verteron", "Verteron", "Altgard", "Abyss")));
        return page;
    }

    FrameworkElement TimerSection(string title, string note, List<FrameworkElement> rows, UIElement? right = null)
    {
        var body = new StackPanel();
        body.Children.Add(CardHead(title == "Events" ? "Star" : "Flag", title, note, right));
        var grid = new UniformGrid { Columns = 2 };
        foreach (var r in rows) grid.Children.Add(r);
        body.Children.Add(grid);
        return ProposalCard(body, new Thickness(14, 12, 14, 6), new Thickness(0, 0, 0, 10));
    }

    FrameworkElement TimerRow(string name, string place, string when, string small, string tone, bool belled)
    {
        var g = new Grid { Height = 42, Margin = new Thickness(0, 0, 14, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(QT(name, 12.5, FontWeights.SemiBold, "Ink", trim: true));
        left.Children.Add(QT(place, 11, FontWeights.Medium, "Faint", trim: true));
        g.Children.Add(left);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        var w = QT(when, 13, FontWeights.SemiBold, tone); w.HorizontalAlignment = HorizontalAlignment.Right;
        System.Windows.Documents.Typography.SetNumeralAlignment(w, FontNumeralAlignment.Tabular);
        right.Children.Add(w);
        var s = QT(small, 11, FontWeights.Medium, "Faint"); s.HorizontalAlignment = HorizontalAlignment.Right; right.Children.Add(s);
        Grid.SetColumn(right, 1); g.Children.Add(right);
        var bell = Bell(belled, 14); bell.HorizontalAlignment = HorizontalAlignment.Right; bell.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(bell, 2); g.Children.Add(bell);
        var rule = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 14, 0) }; rule.SetResourceReference(Border.BorderBrushProperty, "CardHair");
        var host = new Grid(); host.Children.Add(rule); host.Children.Add(g);
        return host;
    }

    FrameworkElement AbyssGroupRow() =>
        TimerRow("Abyss world bosses (8)", "Lower Reshanta · open the Abyss map to time them", "No timer", "all 8 belled", "Muted", true);

    /// <summary>The bell: filled gold when the row alerts, an outline when it doesn't.</summary>
    static FrameworkElement Bell(bool on, double size)
    {
        var geo = Geometry.Parse("M6,1 C3.6,1 2,2.9 2,5.2 L2,8.2 L0.6,10.2 L11.4,10.2 L10,8.2 L10,5.2 C10,2.9 8.4,1 6,1 Z M4.4,11.4 C4.7,12.4 5.3,12.9 6,12.9 C6.7,12.9 7.3,12.4 7.6,11.4 Z");
        var p = new System.Windows.Shapes.Path { Data = geo, Stretch = Stretch.Uniform, Width = size, Height = size, StrokeThickness = 1.2 };
        if (on) p.SetResourceReference(Shape.FillProperty, "Gold");
        else { p.SetResourceReference(Shape.StrokeProperty, "Faint"); p.Opacity = 0.8; }
        return p;
    }

    // ---------------------------------------------------------------- the timers overlay over the game

    /// <summary>The in-game timers overlay: hidden until a belled boss or event is near, then this card with a soft chime.</summary>
    internal Border ProposalTimerOverlay()
    {
        var body = new StackPanel { Width = 300 };
        var head = new Grid { Margin = new Thickness(12, 10, 12, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = GameArt.Element("boss", 30); art.Margin = new Thickness(0, 0, 10, 0); head.Children.Add(art);
        var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nm.Children.Add(HudText("Soul Ruler Kashapa", 13.5, FontWeights.SemiBold, Color.FromRgb(0xED, 0xEF, 0xF5)));
        nm.Children.Add(HudText("World boss · spawns 07:03", 11.5, FontWeights.Medium, Color.FromRgb(0xAF, 0xB5, 0xC6)));
        Grid.SetColumn(nm, 1); head.Children.Add(nm);
        var t = HudText("4:52", 24, FontWeights.SemiBold, Color.FromRgb(0xF5, 0xB9, 0x42)); t.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        Grid.SetColumn(t, 2); head.Children.Add(t);
        body.Children.Add(head);
        var track = new Grid { Height = 3, Margin = new Thickness(12, 9, 12, 0) };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromRgb(0x23, 0x27, 0x36)) });
        track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Width = 276 * (292.0 / 300), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xB9, 0x42)) });
        body.Children.Add(track);
        var next = new Grid { Margin = new Thickness(12, 8, 12, 9) };
        next.ColumnDefinitions.Add(new ColumnDefinition());
        next.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        next.Children.Add(HudText("Then: Dimensional Invasion at 07:30", 11.5, FontWeights.Medium, Color.FromRgb(0x86, 0x8D, 0xA1)));
        var b = Bell(true, 11); b.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(b, 1); next.Children.Add(b);
        body.Children.Add(next);
        return new Border
        {
            Child = body, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xF5, 0xB9, 0x42)),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x0B, 0x0C, 0x10)),
        };
    }

    static TextBlock HudText(string s, double size, FontWeight w, Color c) =>
        new() { Text = s, FontSize = size, FontWeight = w, Foreground = new SolidColorBrush(c), FontFamily = HudKit.Text, TextTrimming = TextTrimming.CharacterEllipsis };
}
