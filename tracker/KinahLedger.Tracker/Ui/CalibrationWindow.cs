using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KinahLedger.Ocr;
using DRect = System.Drawing.Rectangle;

namespace KinahLedger.Ui;

/// <summary>
/// First-run calibration, option A (Steven picked it from three mocks on 2026-10-22): one step at a time. The game frame
/// on the left spotlights the area being checked; a game-tooltip card on the right says what was read there, whether the
/// text is big enough, and what to change when it isn't. Steps: the chat, the loot list beside the character (it only
/// shows after a kill), the HP bar of a targeted enemy, then a summary. Everything is read live, every 1.5 seconds.
/// </summary>
public sealed class CalibrationWindow : Window
{
    enum State { Good, Waiting, Problem }
    sealed record Step(string Key, string Name, string Where, string Icon);
    static readonly Step[] Steps =
    {
        new("chat", "Chat", "bottom left", "💬"),
        new("loot", "Loot list", "beside your character", "💰"),
        new("hp", "HP bar", "top centre", "❤"),
        new("done", "All set", "", "✓"),
    };

    readonly Settings _settings;
    readonly Func<string, bool> _knows;
    readonly Func<DRect?> _manualChat;
    readonly Action _pickChat;
    int _step;
    readonly Image _frame = new() { Stretch = Stretch.Uniform };
    readonly StackPanel _pips = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 12, 0, 0) };
    readonly Border _cardHost = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly TargetReader _target = new();
    bool _busy;
    readonly Dictionary<string, (State State, string Sub, List<string> Read, string Size)> _results = new();

    public CalibrationWindow(Settings settings, Func<string, bool> knows, Func<DRect?> manualChat, Action pickChat)
    {
        _settings = settings; _knows = knows; _manualChat = manualChat; _pickChat = pickChat;
        Title = "Kinah Ledger · Check the setup";
        Width = 1340; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Bg");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        var left = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16), BorderThickness = new Thickness(1), Width = 860, Child = new StackPanel { Children = { new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = _frame, MinHeight = 380 }, _pips } } };
        left.SetResourceReference(Border.BorderBrushProperty, "Line");
        left.SetResourceReference(Border.BackgroundProperty, "Card");
        Content = new Grid
        {
            Margin = new Thickness(22),
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
            Children = { left, _cardHost }
        };
        Grid.SetColumn(_cardHost, 1); _cardHost.Margin = new Thickness(18, 0, 0, 0); _cardHost.VerticalAlignment = VerticalAlignment.Top;
        _timer.Tick += async (_, _) => await Refresh();
        Loaded += async (_, _) => { Show(0); await Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Goes to a step (for the off-screen renders).</summary>
    public void GoTo(int step) { Show(step); _ = Refresh(); }

    void Show(int step)
    {
        _step = Math.Clamp(step, 0, Steps.Length - 1);
        _pips.Children.Clear();
        for (int i = 0; i < Steps.Length; i++)
            _pips.Children.Add(new Border { Width = 28, Height = 5, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush(i <= _step ? Color.FromRgb(0xF8, 0xD5, 0x98) : Color.FromRgb(0x2A, 0x30, 0x58)) });
        Draw();
    }

    // ---- reading ----

    async Task Refresh()
    {
        if (_busy || _step >= 3) return;
        _busy = true;
        try
        {
            var game = GameWindow.Find(_settings.ProcessHints);
            var key = Steps[_step].Key;
            if (game == null) { _results[key] = (State.Waiting, "waiting for the game", new(), ""); _frame.Source = null; Draw(); return; }
            var g = game.Value;
            DRect area;
            List<string> read = new(); string size = ""; State st; string sub;
            switch (key)
            {
                case "chat":
                {
                    var r = _manualChat() ?? (await ChatLocator.DetectAsync(g)).region;
                    area = r ?? new DRect(g.Left + (int)(g.Width * 0.02), g.Top + (int)(g.Height * 0.6), (int)(g.Width * 0.17), (int)(g.Height * 0.3));
                    if (r == null) { st = State.Problem; sub = "no chat text found"; break; }
                    if (GameWindow.Covered(r.Value, _settings.ProcessHints) is { } over) { st = State.Problem; sub = $"another window covers it ({over})"; break; }
                    // the lines as the tracker reads them, and the text's height from single words (a line box can span two lines)
                    var lines = await new ScreenReader(r.Value, _settings.Ocr.Scale).ReadAsync();
                    read = lines.TakeLast(4).ToList();
                    var words = (await ScreenReader.ReadBoxesAsync(r.Value, _settings.Ocr.Scale)).SelectMany(l => l.Words ?? Array.Empty<OcrWordBox>()).Where(w => w.Text.Length >= 3 && w.Text.Any(char.IsLetter)).Select(w => w.Box.Height).OrderBy(h => h).ToList();
                    int px = words.Count == 0 ? 0 : words[words.Count / 2];
                    size = px == 0 ? "" : TextSize(px, g);
                    st = lines.Count > 0 && (px == 0 || px >= 13) ? State.Good : lines.Count > 0 ? State.Problem : State.Waiting;
                    sub = $"{lines.Count} line{(lines.Count == 1 ? "" : "s")}" + (px > 0 ? $" · text {px} px" : "");
                    break;
                }
                case "loot":
                {
                    area = LootPopup.Region(g);
                    if (GameWindow.Covered(area, _settings.ProcessHints) is { } overL) { st = State.Problem; sub = $"another window covers it ({overL})"; break; }
                    var lines = await ScreenReader.ReadBoxesAsync(area, 2, boost: true);
                    // as the tracker counts them: Kinah, and items on the item list; anything else in the area isn't loot
                    var rows = LootPopup.Rows(lines.Select(l => (l.Box.Top, l.Text))).Where(x => (x.IsKinah && x.Amount >= 10) || (!x.IsKinah && _knows(x.Name))).ToList();
                    var prev = _results.TryGetValue(key, out var p) ? p : default;
                    if (rows.Count > 0) { read = rows.Select(x => x.IsKinah ? $"{x.Amount:N0} Kina" : $"{x.Name} ×{x.Count}").ToList(); st = State.Good; sub = $"read {rows.Count} row{(rows.Count == 1 ? "" : "s")}"; }
                    else if (prev.State == State.Good) { read = prev.Read; st = State.Good; sub = prev.Sub; }   // it reads once; the list then fades
                    else { st = State.Waiting; sub = "defeat one enemy to check"; }
                    break;
                }
                default:
                {
                    area = TargetReader.Strip(g);
                    if (GameWindow.Covered(area, _settings.ProcessHints) is { } overH) { st = State.Problem; sub = $"another window covers it ({overH})"; break; }
                    await _target.TickAsync(g);
                    bool fresh = _target.Name != null && (DateTime.UtcNow - _target.NameAt).TotalSeconds < 5;
                    if (fresh) { read = new() { _target.Name! }; st = State.Good; sub = _target.Name!; }
                    else if (_target.Unlisted.Length > 0 && (DateTime.UtcNow - _target.UnlistedAt).TotalSeconds < 5) { read = new() { _target.Unlisted }; st = State.Waiting; sub = "a name not on the enemy list"; }
                    else { st = State.Waiting; sub = "target an enemy"; }
                    break;
                }
            }
            _results[key] = (st, sub, read, size);
            var bmp = await Task.Run(() => Spotlight(g, area, st));
            _frame.Source = bmp;
            Draw();
        }
        catch (Exception ex) { CrashLog.Write("calibration", ex); }
        finally { _busy = false; }
    }

    /// <summary>Whether text of this height reads reliably, in words, with what to change when it doesn't.</summary>
    static string TextSize(int px, DRect game) =>
        px >= 18 ? $"{px} px tall at {game.Width} × {game.Height}: comfortably readable."
        : px >= 13 ? $"{px} px tall: readable. A larger UI scale in the game's settings reads even better."
        : $"{px} px tall: too small to read reliably. Raise the game's UI scale (or the chat text size) in its settings.";

    /// <summary>The game frame, dimmed except the area, outlined in the state's colour.</summary>
    static BitmapSource Spotlight(DRect game, DRect area, State st)
    {
        using var full = ScreenReader.Capture(game);
        double k = 826.0 / game.Width;
        int w = 826, h = Math.Max(1, (int)(game.Height * k));
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var gr = System.Drawing.Graphics.FromImage(bmp))
        {
            gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            gr.DrawImage(full, 0, 0, w, h);
            var a = new System.Drawing.Rectangle((int)((area.Left - game.Left) * k), (int)((area.Top - game.Top) * k), (int)(area.Width * k), (int)(area.Height * k));
            using var dim = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(150, 4, 6, 16));
            var region = new System.Drawing.Region(new System.Drawing.Rectangle(0, 0, w, h)); region.Exclude(a);
            gr.FillRegion(dim, region);
            var c = st == State.Good ? System.Drawing.Color.FromArgb(0x62, 0xD9, 0x8B) : st == State.Waiting ? System.Drawing.Color.FromArgb(0xF3, 0xB4, 0x5E) : System.Drawing.Color.FromArgb(0xFF, 0x6B, 0x5E);
            using var pen = new System.Drawing.Pen(c, 2);
            gr.DrawRectangle(pen, a);
        }
        return Native.ToSource(bmp);
    }

    // ---- the card (the game-tooltip look) ----

    void Draw()
    {
        var s = Steps[_step];
        if (s.Key == "done") { _cardHost.Child = DoneCard(); return; }
        var r = _results.TryGetValue(s.Key, out var x) ? x : (State.Waiting, "reading…", new List<string>(), "");
        var card = Card(r.Item1, s.Icon, s.Name, $"Step {_step + 1} of 3 · {s.Where}", r.Item1 == State.Good ? (s.Key == "loot" ? "Reads" : "Reading") : r.Item1 == State.Waiting ? Hint(s.Key) : "Needs a fix");
        var body = (StackPanel)((Border)card.Child).Child;
        body.Children.Add(Section("WHAT IT READS"));
        var readBox = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xD9, 0xA6)), TextWrapping = TextWrapping.Wrap };
        if (r.Item3.Count == 0) readBox.Text = r.Item2;
        foreach (var l in r.Item3)
        {
            bool loot = System.Text.RegularExpressions.Regex.IsMatch(l, @"acqui|obtain|kina|×", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            readBox.Inlines.Add(new System.Windows.Documents.Run(l + "\n") { Foreground = loot ? new SolidColorBrush(Color.FromRgb(0x62, 0xD9, 0x8B)) : readBox.Foreground });
        }
        body.Children.Add(Pad(new Border { Background = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Child = readBox }));
        if (r.Item4.Length > 0) { body.Children.Add(Section("TEXT SIZE")); body.Children.Add(Pad(Text(r.Item4))); }
        if (r.Item2.StartsWith("another window")) { body.Children.Add(Section("FIX")); body.Children.Add(Pad(Text("Bring Aion 2 to the front, or move the other window off this area. The tracker reads what is on screen."))); }
        else if (s.Key == "loot" && r.Item1 != State.Good) { body.Children.Add(Section("HOW")); body.Children.Add(Pad(Text("The list shows for about three seconds after a kill. Defeat anything nearby and this turns green."))); }
        if (s.Key == "hp" && r.Item1 != State.Good) { body.Children.Add(Section("HOW")); body.Children.Add(Pad(Text("Click an enemy so its name and health bar show at the top of the screen."))); }
        var foot = new WrapPanel { Margin = new Thickness(14, 10, 14, 12) };
        foot.Children.Add(Btn(r.Item1 == State.Good ? "Looks right · Next" : "Next", true, () => Show(_step + 1)));
        if (s.Key == "chat") foot.Children.Add(Btn("Pick the area myself", false, () => { _pickChat(); _results.Remove("chat"); }));
        else if (r.Item1 != State.Good) foot.Children.Add(Btn("Skip", false, () => Show(_step + 1)));
        if (_step > 0) foot.Children.Add(Btn("Back", false, () => Show(_step - 1)));
        body.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x10, 0x5A, 0xA6, 0xFF)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2B, 0x4A)), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 6, 0, 0), Child = foot });
        _cardHost.Child = card;
    }

    static string Hint(string key) => key == "loot" ? "Kill one enemy" : key == "hp" ? "Target an enemy" : "Waiting";

    UIElement DoneCard()
    {
        int good = new[] { "chat", "loot", "hp" }.Count(k => _results.TryGetValue(k, out var r) && r.State == State.Good);
        var card = Card(good == 3 ? State.Good : State.Waiting, "✓", good == 3 ? "Everything reads" : $"{good} of 3 read",
            good == 3 ? "the tracker is ready" : "what didn't read yet checks itself while you play", good == 3 ? "Ready" : "Almost");
        var body = (StackPanel)((Border)card.Child).Child;
        body.Children.Add(Section("CHECKED"));
        foreach (var s in Steps.Take(3))
        {
            var r = _results.TryGetValue(s.Key, out var x) ? x : (State.Waiting, "not checked", new List<string>(), "");
            body.Children.Add(Pad(Text($"{(r.Item1 == State.Good ? "✓" : "•")}  {s.Name}: {r.Item2}", r.Item1 == State.Good ? "Good" : "Muted")));
        }
        var foot = new WrapPanel { Margin = new Thickness(14, 10, 14, 12) };
        foot.Children.Add(Btn("Done", true, Close));
        foot.Children.Add(Btn("Check again", false, () => { _results.Clear(); Show(0); }));
        body.Children.Add(foot);
        return card;
    }

    static Border Card(State st, string icon, string name, string sub, string chip)
    {
        var col = st == State.Good ? Color.FromRgb(0x62, 0xD9, 0x8B) : st == State.Waiting ? Color.FromRgb(0xF3, 0xB4, 0x5E) : Color.FromRgb(0xFF, 0x6B, 0x5E);
        var band = new Border
        {
            Padding = new Thickness(14, 12, 14, 12), BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x5A, col.R, col.G, col.B)),
            Background = new LinearGradientBrush(Color.FromArgb(0x48, col.R, col.G, col.B), Color.FromArgb(0x0A, col.R, col.G, col.B), 0),
        };
        var g = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        var ic = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), Child = new TextBlock { Text = icon, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White } };
        var names = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0xEC, 0xF7)) }, new TextBlock { Text = sub, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA1, 0xC2)), TextTrimming = TextTrimming.CharacterEllipsis } } };
        var chipB = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(9, 3, 9, 3), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(0x2E, col.R, col.G, col.B)), Child = new TextBlock { Text = chip, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(col) } };
        Grid.SetColumn(names, 1); Grid.SetColumn(chipB, 2);
        g.Children.Add(ic); g.Children.Add(names); g.Children.Add(chipB);
        band.Child = g;
        var body = new StackPanel { Children = { band } };
        return new Border
        {
            Width = 350, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x36, 0x60)),
            Background = new LinearGradientBrush(Color.FromRgb(0x14, 0x18, 0x33), Color.FromRgb(0x0F, 0x12, 0x26), 90),
            Child = new Border { Child = body },
        };
    }

    static UIElement Section(string t)
    {
        var g = new Grid { Margin = new Thickness(14, 12, 14, 6), ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() } };
        var tb = new TextBlock { Text = t, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xD5, 0x98)) };
        var rule = new Border { Height = 1, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = new LinearGradientBrush(Color.FromArgb(0x70, 0xF8, 0xD5, 0x98), Color.FromArgb(0, 0xF8, 0xD5, 0x98), 0) };
        Grid.SetColumn(rule, 1); g.Children.Add(tb); g.Children.Add(rule);
        return g;
    }

    static FrameworkElement Pad(FrameworkElement e) { e.Margin = new Thickness(14, 0, 14, 4); return e; }

    static TextBlock Text(string t, string tone = "")
        => new() { Text = t, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(tone == "Good" ? Color.FromRgb(0x62, 0xD9, 0x8B) : tone == "Muted" ? Color.FromRgb(0x9A, 0xA1, 0xC2) : Color.FromRgb(0xCD, 0xD2, 0xEA)) };

    static Button Btn(string text, bool primary, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0), FontSize = 13, FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand };
        if (primary) b.SetResourceReference(StyleProperty, "PrimaryButton");
        b.Click += (_, _) => click();
        return b;
    }
}
