using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// The window Week wrapped and Day wrapped open in (2026-11-11): the recap sheet on its own, a cross over its corner, and
/// under it a bar with its buttons (Copy picture, Save picture) and a line saying what they did. Like the recipe window
/// (Ui/CraftWindow.cs) it is owned by the main window, drags from anywhere that isn't a control, closes on Esc or the cross,
/// and remembers where it was put until the app closes. It opens only on the player's click, beside the window they just
/// clicked in, and never brings itself forward on its own: opening the other sheet while it is up redraws it in place.
/// </summary>
sealed class WrappedWindow : Window
{
    /// <summary>Where the player last put it, this run of the app.</summary>
    static Point? _at;

    readonly Grid _sheetHost = new();
    readonly StackPanel _acts = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _note = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly Border _bar;
    /// <summary>The sheet on show, so its sequence can be stopped when another replaces it or the window closes.</summary>
    FrameworkElement? _sheet;

    /// <summary>The scope on show ("week" or "day").</summary>
    public string Scope { get; private set; } = "";

    public WrappedWindow(Window owner)
    {
        Owner = owner;
        Title = "Wrapped";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = owner.Topmost;
        _note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var barRow = new Grid();
        barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barRow.ColumnDefinitions.Add(new ColumnDefinition());
        barRow.Children.Add(_acts);
        Grid.SetColumn(_note, 1); barRow.Children.Add(_note);
        _bar = new Border { Child = barRow, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        _bar.SetResourceReference(Border.BackgroundProperty, "Card");
        _bar.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        var root = new StackPanel { Cursor = Cursors.SizeAll };   // it drags from anywhere that isn't a control; controls keep their own cursor
        root.Children.Add(_sheetHost);
        root.Children.Add(_bar);
        Content = root;
        MouseLeftButtonDown += (_, e) =>
        {
            if (CraftWindow.Interactive(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch (InvalidOperationException) { /* the button was already up: nothing to drag */ }
            _at = new Point(Left, Top);
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closed += (_, _) => { if (_sheet != null) Motion.Halt(_sheet); };
    }

    /// <summary>
    /// Shows <paramref name="sheet"/> and plays its sequence (<paramref name="cues"/>): in place when the window is up, else
    /// over the main window's top (or where it was last put). <paramref name="actions"/> are the bar's buttons, the first
    /// the primary one; with none (a sheet with too little to share) the bar holds the note alone.
    /// </summary>
    /// <param name="offscreen">For a render (ui-shot): shown far off the screens, never where the player would see it.</param>
    public void Present(string scope, FrameworkElement sheet, Motion.Cue[] cues, IReadOnlyList<(string Text, string Tip, Action Go)> actions, string note, bool offscreen = false)
    {
        Scope = scope;
        if (_sheet != null) Motion.Halt(_sheet);
        _sheet = sheet;
        _sheetHost.Children.Clear();
        _sheetHost.Children.Add(sheet);
        _bar.Width = sheet.Width;   // as wide as the sheet: a long line under the buttons wraps, the window never widens
        var close = new Button { Content = "✕", FontSize = 13, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 0), ToolTip = "Close (Esc)" };
        close.SetResourceReference(StyleProperty, "RoundBtn");
        AutomationProperties.SetName(close, "Close the recap");
        close.Click += (_, _) => Close();
        _sheetHost.Children.Add(close);
        _acts.Children.Clear();
        foreach (var (text, tip, go) in actions)
        {
            var b = new Button { Content = text, Height = 32, FontSize = 12.5, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(_acts.Children.Count > 0 ? 8 : 0, 0, 0, 0), ToolTip = tip };
            if (_acts.Children.Count == 0) b.SetResourceReference(StyleProperty, "PrimaryButton");
            AutomationProperties.SetName(b, text);
            b.Click += (_, _) => go();
            _acts.Children.Add(b);
        }
        SetNote(note);
        if (!IsVisible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (offscreen) { Left = -12000; Top = -12000; ShowActivated = false; }
            else Place(sheet);
            Show();   // on the player's click in the main window, so the game had already let go of the focus
        }
        Motion.Sequence(sheet, cues);
    }

    /// <summary>The line in the bar: what Copy or Save just did, or what the buttons are for. A bar with no button and no line isn't shown.</summary>
    public void SetNote(string text)
    {
        _note.Text = text;
        _note.Margin = new Thickness(_acts.Children.Count > 0 ? 14 : 2, 0, 0, 0);
        _bar.Visibility = _acts.Children.Count > 0 || text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The first spot opens this far under the main window's top edge, clear of its header.</summary>
    const double UnderHeader = 48;
    /// <summary>At least this much of the window stays on a screen, so it can always be dragged back.</summary>
    const double OnScreen = 160;

    /// <summary>The first spot: where it was last put, else centred over the main window, under its header; always on a screen.</summary>
    void Place(FrameworkElement sheet)
    {
        sheet.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = sheet.DesiredSize.Width;
        var o = Owner;
        double left = _at?.X ?? o.Left + Math.Max(0, o.ActualWidth - w) / 2, top = _at?.Y ?? o.Top + UnderHeader;
        left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Math.Min(w, OnScreen));
        top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - OnScreen);
        Left = left; Top = top;
    }
}
