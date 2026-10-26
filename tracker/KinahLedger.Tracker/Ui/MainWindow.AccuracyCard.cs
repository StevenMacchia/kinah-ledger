using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Accuracy on the Farming page (2026-10-25): one chip under the session line, in the look's own chip colours ("✓ Nothing
/// looked wrong", "2 to look at"). It used to be a fixed-dark slab inside the themed Session card, with a share that read
/// 20% while nothing was wrong. A click opens the Accuracy card over the window, a dark game tooltip like the item card
/// (MainWindow.CardKit.cs): the line, the four counts (checked, matched, to look at, not checked) and the things to look
/// at, each with Report (the miscount report, prefilled with that line and the pictures kept around it) and Fine (off the
/// list for this session). The words come from <see cref="AccuracyCardView"/>; the figures from the session's
/// <see cref="AccuracyScore"/>.
/// </summary>
public partial class MainWindow
{
    string _accChipSig = "", _accSig = "";
    AccuracyCardView? _accView;
    /// <summary>A layout render's sample score (never the player's own).</summary>
    AccuracyScore? _demoScore;
    /// <summary>The Accuracy card while it is open over the window (it is the window's one open card, _card, then).</summary>
    Popup? _accPopup;
    /// <summary>Most things to look at the card lists; the rest are counted under it.</summary>
    const int AccRowsShown = 8;

    /// <summary>Redraws the chip, and the card when it is open, from the score now; does nothing when the words haven't changed (so a hover or a click isn't lost).</summary>
    void FillAccuracyCard()
    {
        AccuracyScore.Summary? s = null;
        try { s = _demo ? _demoScore?.Snapshot() : _watcher.Chat?.AccuracySummary(); }
        catch (Exception ex) { CrashLog.Write("accuracy card", ex); }
        if (s == null || _part != "Farming" || _welcome)
        {
            AccuracyChip.Visibility = Visibility.Collapsed; _accView = null; _accChipSig = _accSig = "";
            if (_accPopup is { IsOpen: true }) _accPopup.IsOpen = false;
            return;
        }
        var v = AccuracyCardView.From(s, _prefs.ShowAccuracy);
        _accView = v;
        // nothing to say while nothing needs a look (the chip shows with Show accuracy details on, or things to look at)
        AccuracyChip.Visibility = v.Chip.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var chipSig = $"{v.Chip}|{v.Tone}";
        if (chipSig != _accChipSig)
        {
            _accChipSig = chipSig;
            var t = new TextBlock { Text = v.Chip, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, v.Tone == "warn" ? "Warn" : "Good");
            AccuracyChip.Child = t;
        }
        // the card open over the window follows the count (a Fine, a new thing to look at)
        var sig = $"{v.Line}|{string.Join(",", v.Counts.Select(c => c.Count))}|{string.Join(",", v.Rows.Select(r => r.Id))}|{v.Detail}";
        if (sig == _accSig) return;
        _accSig = sig;
        if (_accPopup is { IsOpen: true } p && ReferenceEquals(p, _card)) p.Child = BuildAccuracyCard(v);
    }

    /// <summary>A click on the chip: the Accuracy card over the window, under the chip.</summary>
    void AccuracyChipClick(object sender, MouseButtonEventArgs e)
    {
        if (_accView == null) return;
        if (_card != null) _card.IsOpen = false;
        _accPopup = _card = new Popup { Child = BuildAccuracyCard(_accView), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Bottom, PlacementTarget = AccuracyChip };
        _card.IsOpen = true;
    }

    /// <summary>The Accuracy card: the name, the line, the four counts (the figures behind them on hover), then the things to look at, newest first.</summary>
    Border BuildAccuracyCard(AccuracyCardView v)
    {
        var (card, body) = CkShell(460);
        var title = CkText(AccuracyCardView.Title, CkGold, 16, FontWeights.SemiBold, display: true); title.Margin = new Thickness(20, 14, 20, 0);
        body.Children.Add(title);
        var line = CkText(v.Line, CkInk, 14); line.Margin = new Thickness(20, 6, 20, 0); line.TextWrapping = TextWrapping.Wrap; line.TextTrimming = TextTrimming.None;
        if (v.Line.Length > 0) body.Children.Add(line);
        // the four counts as the game's tooltips list stats: the name on the left, the figure on the right
        var counts = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Background = Brushes.Transparent };
        foreach (var (label, count, sub) in v.Counts)
            counts.Children.Add(CkKv(label, count.ToString("N0"), label == "To look at" && count > 0 ? CkWarn : CkInk, sub));
        // the quest kill counter against the count, from the old "Accuracy · Kinah counter agrees …" line under the tables,
        // which this card replaces (2026-10-25: two things called Accuracy on one page)
        var detail = v.Detail;
        if (!_demo && _watcher.Chat is { QuestKillsGame: > 0 } qc) detail += (detail.Length > 0 ? "\n" : "") + $"Quest kills: the game's counter {qc.QuestKillsGame:N0}, counted {qc.QuestKillsCounted:N0}.";
        if (detail.Length > 0 && v.Counts.Count > 0) { counts.ToolTip = detail; ToolTipService.SetInitialShowDelay(counts, 250); }
        if (v.Counts.Count > 0) body.Children.Add(counts);
        if (!v.HasRows) { body.Children.Add(new Border { Height = 14 }); return card; }

        // the things to look at, newest first: the time, what, why in one sentence, then Report and Fine
        body.Children.Add(CkSection("TO LOOK AT"));
        foreach (var r in v.Rows.Take(AccRowsShown))
        {
            var g = new Grid { Margin = new Thickness(20, 4, 20, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var time = CkText(r.Time, CkFaint, 12, mono: true); time.VerticalAlignment = VerticalAlignment.Top; time.Margin = new Thickness(0, 1, 0, 0);
            g.Children.Add(time);
            var what = new StackPanel();
            what.Children.Add(CkText(r.Text, CkInk, 13));
            var why = CkText(r.Why, CkFaint, 12); why.TextWrapping = TextWrapping.Wrap; why.TextTrimming = TextTrimming.None;
            what.Children.Add(why);
            Grid.SetColumn(what, 1); g.Children.Add(what);
            var acts = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 1, 0, 0) };
            var check = r.Check;
            var report = new Hyperlink(new Run("Report")) { Foreground = Ck(CkGold), TextDecorations = null, ToolTip = "Save what was read around this moment to a folder on this PC, for a bug report." };
            report.Click += (_, _) => { if (_card != null) _card.IsOpen = false; ReportCheck(check); };
            var fine = new Hyperlink(new Run("Fine")) { Foreground = Ck(CkMuted), TextDecorations = null, ToolTip = "It's right: take it off this list for this session." };
            fine.Click += (_, _) => { if (_demo) _demoScore?.Dismiss(check.Id); else _watcher.Chat?.Score.Dismiss(check.Id); FillAccuracyCard(); UpdateHud(); };
            acts.Inlines.Add(report); acts.Inlines.Add(new Run("   ") { Foreground = Ck(CkFaint) }); acts.Inlines.Add(fine);
            Grid.SetColumn(acts, 2); g.Children.Add(acts);
            body.Children.Add(g);
        }
        if (v.Rows.Count > AccRowsShown) body.Children.Add(CkNote($"and {v.Rows.Count - AccRowsShown} more"));
        body.Children.Add(new Border { Height = 12 });
        return card;
    }

    /// <summary>"Report" on a thing to look at: the miscount report, headed by that line, with what was kept around its moment.</summary>
    void ReportCheck(AccuracyScore.Check c)
    {
        var p = AccuracyCardView.Prefill(c, _prefs.SendReportFrames && _prefs.Share);
        var chat = _watcher.Chat;
        if (_demo || chat == null) { Say("Nothing to report yet: reports are saved once the tracker is reading the game.", "Warn"); return; }
        try
        {
            var dir = chat.WriteReport(p.Why, p.Focus);
            Open(dir);
            if (p.SendPictures) _ = SendReportPictures(dir, "Accuracy: " + c.Reason);
            Say($"Saved what was read around {c.At.ToLocalTime():HH:mm:ss} to a folder on this PC. Zip it to attach to a bug report.", "Good");
            Status.ToolTip = dir;
        }
        catch (Exception ex) { CrashLog.Write("miscount report", ex); Say("Couldn't save the report: " + ex.Message, "Bad"); }
    }

    /// <summary>A sample score for layout renders: a farming hour's pickups, one Kina line the counter refused.</summary>
    void LoadAccuracyDemo()
    {
        var a = new AccuracyScore(); var t = DateTime.UtcNow.AddMinutes(-40);
        for (int i = 0; i < 230; i++) a.Counted(t.AddSeconds(i * 9), $"{3690 + (i % 7) * 40:N0} Kinah", true, "k:" + (3690 + (i % 7) * 40), "counter", true);
        for (int i = 0; i < 60; i++) a.Counted(t.AddSeconds(i * 30 + 4), "Thick hide ×1", false, "i:thick hidex1", "row", false);
        for (int i = 0; i < 4; i++) a.Counted(t.AddSeconds(i * 400 + 7), "Spiritstone ×1", false, "i:spiritstonex1", null, false);
        a.Refused(DateTime.UtcNow.AddMinutes(-6), "Acquired 2367 Kina.", 2367);
        _demoScore = a;
    }

    /// <summary>The card on its own, with its things to look at, for a layout render.</summary>
    public void ShotAccuracyCard(string path)
    {
        FillAccuracyCard();
        if (_accView != null) SaveCard(BuildAccuracyCard(_accView), path);
    }

    /// <summary>
    /// The panel's accuracy line, as the chip worked it out last, and whether it is things to look at (its colour). Only with
    /// Options › Show accuracy details on: Steven (2026-10-26, Ticker) read "23 to look at" on the panel as an error, so with
    /// the switch off (the default) the panel shows none of it, in every look; the window's live and Accuracy cards still do.
    /// </summary>
    public string? HudAccuracy => _prefs.ShowAccuracy ? _accView?.Hud : null;
    public bool HudAccuracyToLook => _accView?.HasRows == true;
}
