using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// A plain stand-in for a theme's look while it is being drawn: every figure as plain text on a dark card, so the registry,
/// the picker, the renders and the tests work before the look itself. A finished look never derives from it.
/// </summary>
abstract class HudThemeDraft : HudThemeLook
{
    readonly StackPanel _root = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _frame;
    readonly TextBlock _line = new() { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Ink };
    readonly TextBlock _note = new() { FontSize = 11.5, Visibility = Visibility.Collapsed };
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel _fight = new() { Visibility = Visibility.Collapsed };

    protected HudThemeDraft()
    {
        _frame = new Border { Background = B(PanelC, .9), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Child = new StackPanel { Children = { _line, _note } } };
        _root.Children.Add(_timers); _root.Children.Add(_frame); _root.Children.Add(_fight);
    }

    public override FrameworkElement Root => _root;
    public override void SetOpacity(double opacity) => _frame.Background = B(PanelC, opacity);
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    protected override void DrawLine(Figures f)
    {
        _line.Text = !f.Counted || f.Rate.Length == 0 ? Waiting(f.Abyss) : $"{f.Rate} {f.Unit}   {f.Total} {f.TotalWord}" + string.Concat(f.Stats.Select(s => $"   {s.Figure} {s.Word}"));
        _note.Text = f.Note; _note.Foreground = f.NoteTone;
        _note.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timers.Children.Clear();
        if (due.FirstOrDefault() is { } i) _timers.Children.Add(new TextBlock { Text = TimerWords(i, now).Text, Foreground = Gold, FontWeight = FontWeights.Bold });
    }

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fight.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; return; }
        _fight.Children.Add(new TextBlock { Text = $"{e.Title}  {(e.Waiting ? L.T("Ready") : Clock(e.Duration(now)))}  {OutcomeText(e)}", Foreground = Ink, FontWeight = FontWeights.Bold });
        if (e.Waiting) _fight.Children.Add(new TextBlock { Text = e.ReadyText, Foreground = Muted });
        else { PutRows(_fight, e, now, FightWidth - 40); if (e.HasDamage) _fight.Children.Add(ParserFoot.Line(e, now, 12)); }
        _fight.Visibility = Visibility.Visible;
    }

    protected override void KinahShown(bool shown) => _frame.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
}
