using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// Share this pull (1.0.36): one click on a finished boss pull in the Parser page's list makes the meter's share picture of it
/// (Ui/MeterShare.cs: the boss, the instance and mode, the pull's clock and the time it started, the group's rows in the
/// player's own meter look with class icons, bars, DPS, share and heal lines, the kinahledger.com mark), with a gold "NEW
/// PERSONAL BEST" badge when the pull beat your best on that fight (FightRecords). Copied to the clipboard and saved in
/// Pictures\Kinah Ledger, as every other share picture; nothing is uploaded. The page gets no busier: one small picture mark
/// on each row, faint until the row is pointed at, with no words of its own (the hover says them). The pull doesn't open.
/// </summary>
public partial class MainWindow
{
    /// <summary>The share mark's column in a pull's row, and how bright it sits while the row isn't pointed at.</summary>
    const double PullShareCol = 30, PullShareRest = 0.55;

    /// <summary>A pull the mark is on: a finished boss pull with members on its meter.</summary>
    static bool PullShareable(Encounter e) => !e.Live && e.ModeKind == ParserMode.Bosses && Shareable(e);

    /// <summary>
    /// The row's share mark (tagged "parser-row-share"), or null for a pull with nothing to share. A click copies the pull's
    /// picture without opening it; for a moment after, a tick in its place says it worked (the toast says where to paste it).
    /// </summary>
    Button? PullShareButton(Encounter e)
    {
        if (!PullShareable(e)) return null;
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(PictureMark), StrokeThickness = 1.4, Width = 12, Height = 12, Stretch = Stretch.None,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var b = new Button
        {
            Content = icon, Width = 24, Height = 24, Padding = new Thickness(0), Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Opacity = PullShareRest, Tag = "parser-row-share", Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = L.T("Share this pull: copies a picture of its meter to paste in Discord or anywhere with Ctrl+V. Nothing is uploaded."),
        };
        b.SetResourceReference(StyleProperty, "RoundBtn");
        if (!_parserPrefs.Hovers) b.ToolTip = null;   // Explanations on hover off: no hover here either (its name stays for screen readers)
        AutomationProperties.SetName(b, $"Share a picture of {e.Title}");
        string key = $"{e.Id}|pull-share";
        void Look(bool done)
        {
            icon.Data = Geometry.Parse(done ? TickMark : PictureMark);
            icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, done ? "Good" : b.IsMouseOver ? "Gold" : "Muted");
        }
        bool showing = _copiedKey == key && DateTime.UtcNow < _copiedUntil;
        Look(showing);
        b.MouseEnter += (_, _) => Look(_copiedKey == key && DateTime.UtcNow < _copiedUntil);
        b.MouseLeave += (_, _) => Look(_copiedKey == key && DateTime.UtcNow < _copiedUntil);
        void Settle()
        {
            var left = _copiedUntil - DateTime.UtcNow;
            var t = new System.Windows.Threading.DispatcherTimer { Interval = left > TimeSpan.Zero ? left : TimeSpan.FromMilliseconds(1) };
            t.Tick += (_, _) => { t.Stop(); if (_copiedKey != key || DateTime.UtcNow >= _copiedUntil) Look(false); };
            t.Start();
        }
        if (showing) Settle();
        b.Click += (_, ev) =>
        {
            ev.Handled = true;   // the row under it opens the pull on a click; this one only shares it
            if (!SharePull(e)) return;
            _copiedKey = key; _copiedUntil = DateTime.UtcNow + CopiedFor;
            Look(true);
            Settle();
        };
        return b;
    }

    /// <summary>The pull's picture copied and saved (the meter's share picture, its PB badge when it set one); true when it went to the clipboard.</summary>
    bool SharePull(Encounter e) => ShareMeter(e);

    // ---- for the options test and renders ----

    internal bool SharePullForTest(Encounter e) => SharePull(e);
    internal static bool PullShareableForTest(Encounter e) => PullShareable(e);
}
