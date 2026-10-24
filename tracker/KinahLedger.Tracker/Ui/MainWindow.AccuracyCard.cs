using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Accuracy card under the session line (Steven, 2026-10-24): "Verified by the game's own numbers: 98%" and one line
/// under it ("1 pickup unsure · tap to see"). Tapped, the things to check open inline, each with Report (the miscount
/// report, prefilled with that line and the pictures kept around it) and Fine (off the list for this session). Drawn in
/// the game-tooltip look (MainWindow.CardKit.cs). The words come from <see cref="AccuracyCardView"/>; the figures from
/// the session's <see cref="AccuracyScore"/>.
/// </summary>
public partial class MainWindow
{
    bool _accOpen;
    string _accSig = "";
    AccuracyCardView? _accView;
    /// <summary>A layout render's sample score (never the player's own).</summary>
    AccuracyScore? _demoScore;
    /// <summary>Most things to check the open list shows; the rest are counted under it.</summary>
    const int AccRowsShown = 8;

    /// <summary>Redraws the card from the score now; does nothing when the words haven't changed (so a hover or a click isn't lost).</summary>
    void FillAccuracyCard()
    {
        AccuracyScore.Summary? s = null;
        try { s = _demo ? _demoScore?.Snapshot() : _watcher.Chat?.AccuracySummary(); }
        catch (Exception ex) { CrashLog.Write("accuracy card", ex); }
        if (s == null || _part != "Farming" || _welcome)
        {
            AccuracyCard.Visibility = Visibility.Collapsed; _accView = null; _accSig = "";
            return;
        }
        var v = AccuracyCardView.From(s);
        _accView = v;
        if (!v.HasRows) _accOpen = false;
        var sig = $"{v.Line}|{v.Secondary}|{v.Status}|{_accOpen}|{string.Join(",", v.Rows.Select(r => r.Id))}|{v.Detail}";
        AccuracyCard.Visibility = Visibility.Visible;
        if (sig == _accSig) return;
        _accSig = sig;
        BuildAccuracyCard(v);
    }

    void BuildAccuracyCard(AccuracyCardView v)
    {
        var card = AccuracyCard;
        card.CornerRadius = new CornerRadius(6); card.BorderThickness = new Thickness(1); card.BorderBrush = Ck(Color.FromRgb(0x3C, 0x44, 0x66));
        card.Background = new LinearGradientBrush(Color.FromRgb(0x15, 0x1A, 0x2C), Color.FromRgb(0x0C, 0x0F, 0x1C), 90);
        var body = new StackPanel();
        card.Child = body;

        // the face: the name, the share and the one line under it; the whole of it opens the list when there is one
        var face = new StackPanel { Margin = new Thickness(16, 12, 16, 12), Background = Brushes.Transparent };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(CkText(AccuracyCardView.Title, CkGold, 15, FontWeights.SemiBold, display: true));
        var tone = v.Tone switch { "good" => CkGood, "gold" => CkGold, "warn" => CkWarn, _ => CkFaint };
        if (v.Status.Length > 0)
        {
            var chip = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 2, 9, 3), Background = Ck(tone, 0x22), BorderBrush = Ck(tone, 0x66), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = CkText(v.Status, tone, 11.5, FontWeights.SemiBold) };
            Grid.SetColumn(chip, 1); head.Children.Add(chip);
        }
        face.Children.Add(head);
        var line = CkText(v.Line, CkInk, 14); line.Margin = new Thickness(0, 6, 0, 0);
        face.Children.Add(line);
        var sec = CkText(v.Secondary, v.HasRows ? CkGold : CkMuted, 12.5); sec.Margin = new Thickness(0, 3, 0, 0);
        face.Children.Add(sec);
        if (v.Detail.Length > 0) { face.ToolTip = v.Detail; ToolTipService.SetInitialShowDelay(face, 250); }
        if (v.HasRows)
        {
            face.Cursor = Cursors.Hand;
            face.MouseLeftButtonUp += (_, _) => { _accOpen = !_accOpen; FillAccuracyCard(); };
        }
        body.Children.Add(face);
        if (!_accOpen || !v.HasRows) return;

        // the things to check, newest first: the time, what, why in one sentence, then Report and Fine
        var list = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        list.Children.Add(new Border { Height = 1, Margin = new Thickness(16, 0, 16, 6), Background = new LinearGradientBrush(Color.FromArgb(0x66, CkGoldRule.R, CkGoldRule.G, CkGoldRule.B), Colors.Transparent, 0) });
        foreach (var r in v.Rows.Take(AccRowsShown))
        {
            var g = new Grid { Margin = new Thickness(16, 5, 16, 5) };
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
            report.Click += (_, _) => ReportCheck(check);
            var fine = new Hyperlink(new Run("Fine")) { Foreground = Ck(CkMuted), TextDecorations = null, ToolTip = "It's right: take it off this list for this session." };
            fine.Click += (_, _) => { if (_demo) _demoScore?.Dismiss(check.Id); else _watcher.Chat?.Score.Dismiss(check.Id); FillAccuracyCard(); UpdateHud(); };
            acts.Inlines.Add(report); acts.Inlines.Add(new Run("   ") { Foreground = Ck(CkFaint) }); acts.Inlines.Add(fine);
            Grid.SetColumn(acts, 2); g.Children.Add(acts);
            list.Children.Add(g);
        }
        if (v.Rows.Count > AccRowsShown) list.Children.Add(CkNote($"and {v.Rows.Count - AccRowsShown} more"));
        body.Children.Add(list);
    }

    /// <summary>"Report" on a thing to check: the miscount report, headed by that line, with what was kept around its moment.</summary>
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

    /// <summary>The card on its own, open, for a layout render.</summary>
    public void ShotAccuracyCard(string path)
    {
        if (_demoScore == null) return;
        _accOpen = true; _accSig = "";
        FillAccuracyCard();
        UpdateLayout();
        double w = AccuracyCard.ActualWidth, h = AccuracyCard.ActualHeight;
        if (w < 1 || h < 1) return;
        // through a brush: rendering a nested element straight would carry its place in the window with it
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(AccuracyCard), null, new Rect(0, 0, w, h));
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        SavePng(rtb, path);
        _accOpen = false; _accSig = "";
        FillAccuracyCard();
    }

    /// <summary>The panel's accuracy line, as the card worked it out last.</summary>
    public string? HudAccuracy => _accView?.Hud;
}
