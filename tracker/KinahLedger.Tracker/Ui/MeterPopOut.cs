using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The DPS meter in a window of its own (Ui/PopOutWindow.cs), for a second screen beside a game in exclusive fullscreen. It
/// draws the meter as the Parser's own overlay does (Ui/ParserOverlay.cs): the look picked (the standard panel, Podium,
/// Reactor, or a theme's fight look), live, scaled to the window's width. It follows the fight the overlay would show (under
/// way, the held result, a dungeon's ready card, Always on), and with nothing to show, the last boss pull kept; it is drawn
/// whether the overlay is on or not (MainWindow.PopOuts.cs). Share in its head copies the picture of the fight it shows.
/// </summary>
sealed class MeterPopOut : PopOutWindow
{
    public const double DefaultWidth = 440;
    readonly ParserPanel _panel = new(glass: HudGlass.On);
    readonly FrameworkElement _panelFace;
    readonly Border _face = new();
    readonly Border _host;
    readonly Button _share;
    HudStackLook? _lookFace;
    string _lookKey = ParserPrefs.StandardLook;
    bool _lookSet;

    /// <summary>Share in the head was pressed: the window copies the picture of <see cref="Shown"/>.</summary>
    public event Action? ShareAsked;
    public ParserMode Mode { get; set; }
    public bool ShowDetails { get; set; }
    /// <summary>The fight drawn now (null: nothing yet, the quiet line instead).</summary>
    public Encounter? Shown { get; private set; }

    public MeterPopOut(Window main, PopOutPrefs.Spot spot, Action save)
        : base(main, spot, save, L.T("DPS meter"), "DPS meter window", DefaultWidth, 300)
    {
        foreach (var art in new[] { "boss", "dungeon", "medal", "rift" }) GameArt.Image(art);
        _panelFace = HudGlass.On ? HudGlass.Ring(_panel) : _panel;
        _panel.SetOpacity(1);   // solid on the desktop: nothing of a game behind it
        _face.Child = _panelFace;
        _host = new Border { Child = _face };
        TextElement.SetFontFamily(_host, HudKit.Text);
        TextElement.SetFontWeight(_host, FontWeights.Medium);
        TextOptions.SetTextFormattingMode(_host, TextFormattingMode.Display);
        Typography.SetNumeralAlignment(_host, FontNumeralAlignment.Tabular);
        _share = new Button { Content = L.T("Share", "button"), Height = 26, FontSize = 12, Padding = new Thickness(12, 0, 12, 0), ToolTip = L.T("A picture of this fight to paste in Discord or anywhere. Nothing is uploaded.") };
        _share.SetResourceReference(StyleProperty, "PillBtn");
        _share.Height = 26;
        System.Windows.Automation.AutomationProperties.SetName(_share, "Share a picture of the meter");
        _share.Click += (_, _) => ShareAsked?.Invoke();
        HeadButtons.Children.Add(_share);
    }

    /// <summary>The meter's look (the Parser window's own pick): set again each tick, drawn anew only when it changes.</summary>
    public string Look
    {
        get => _lookKey;
        set
        {
            var k = ParserPrefs.LookOf(value);
            if (k == _lookKey && _lookSet) return;
            _lookKey = k; _lookSet = true;
            _panel.Skin = ParserPanel.SkinOf(k);
            _lookFace = ParserOverlay.MakeLook(k, still: false);
            _lookFace?.SetOpacity(1);
            // a theme's look keeps room round itself for its glow: given back as a negative margin (as the share picture does,
            // ParserOverlay.MeterFace), so the meter's own box is what fills the window's width, with no empty strip beside it
            _face.Child = _lookFace == null ? _panelFace : _lookFace is IHudFightAbove ? null
                : _lookFace is HudThemeLook && _lookFace.Inset is var i && i.Left + i.Top + i.Right + i.Bottom > 0
                    ? new Border { Child = _lookFace.Root, Margin = new Thickness(-i.Left, -i.Top, -i.Right, -i.Bottom) } : _lookFace.Root;
        }
    }

    /// <summary>The Kinah overlay's corner and padding, as the overlay's panel takes them.</summary>
    public void SetFrame(double radius, Thickness padding) => _panel.SetFrame(radius, padding);

    /// <summary>The meter's width moved (Settings, an edge of the overlay): drawn again at it; the window scales it to its own.</summary>
    public void ApplyFightWidth() { _lookFace?.ApplyWidth(); _panel.Width = HudStackLook.FightWidth + 10; }

    /// <summary>The fight, or null for the quiet line; Share only for a fight with members on it.</summary>
    public void Draw(Encounter? e, DateTime now, bool shareable)
    {
        Shown = e;
        _share.IsEnabled = shareable;
        if (e == null) { SetFace(null, L.T("No boss pull yet. The meter shows here when one starts.")); return; }
        SetFace(_host);
        if (_lookFace is IHudFightAbove fa)
        {
            var el = fa.FightAbove(e, now);
            if (!ReferenceEquals(_face.Child, el)) _face.Child = el;
            return;
        }
        if (_lookFace != null) { _lookFace.ShowFight(e, now); return; }
        _panel.Mode = Mode; _panel.ShowDetails = ShowDetails;
        _panel.Draw(e, now);
    }

    internal bool ShareEnabledForTest => _share.IsEnabled;
    internal void ShareForTest() => _share.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
}
