using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Platform;
using KinahLedger.Session;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// "That was wrong" (2026-10-26, mock f3-mark-wrong.png and f3-companion.png): a second shortcut, off until the player
/// picks a key in Options › In game. Pressed in the game, it files a line report on the newest counted line, as tapping
/// the line and pressing Report does (the last minute of reads saved with the line marked), without the window taking
/// focus. The panel answers for 3 s ("Saved the last minute · Report in tracker", a gold clock and a gold line running
/// down); the window marks the line with a gold edge and a flag and says under the feed "Marked 23:54:10 Spiritstone ×1
/// as wrong · Undo". Undo (or Ctrl+Z) withdraws the mark and deletes the report; pictures go to the site, when Options ›
/// Sharing allows, only once Undo is no longer possible. Nothing counted in the last 2 minutes: the panel says so and
/// nothing is filed.
/// </summary>
public partial class MainWindow
{
    /// <summary>How far back the newest counted line may be.</summary>
    const double MarkWindowMinutes = 2;
    /// <summary>How long Undo stays possible: longer than the bar's 8 s, since the player is in the game when the key is pressed.</summary>
    const double MarkUndoSeconds = 30;
    const string MarkSaved = "Saved the last minute · Report in tracker", MarkNothing = "Nothing counted in the last 2 minutes",
        MarkAgain = "Already marked · Report in tracker", MarkFailed = "Couldn't save the report · see the tracker";

    /// <summary>The lines marked wrong this session (<see cref="MarkKey"/>): drawn with a gold edge and a flag while in a feed.</summary>
    readonly HashSet<string> _marked = new();
    /// <summary>The line under the feed while Undo is possible, and the feed's hint it stands in for.</summary>
    TextBlock? _markLine, _markHint;

    static string MarkKey(FeedLine l) => $"{l.At.Ticks}|{l.Tab}|{l.Text}";
    bool IsMarked(FeedLine l) => _marked.Count > 0 && _marked.Contains(MarkKey(l));

    /* ---------------- the shortcut ---------------- */

    /// <summary>The That was wrong shortcut as the player sees it ("Ctrl+Shift+J"); empty when it is off.</summary>
    public string MarkWrongText => Hotkey.Parse(_hudPrefs.MarkWrongKey)?.ToString() ?? "";

    /// <summary>The That was wrong shortcut couldn't be set: the status line's sentence and the panel's short line (Ui/MainWindow.Notices.cs).</summary>
    (string Long, string Short)? _markKeyProblem;

    /// <summary>Lets go of the That was wrong key, so the start/stop key can take it in a swap (ApplyOptions).</summary>
    void ReleaseMarkWrongKey()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) Native.UnregisterHotKey(h, Hotkey.MarkWrongId);
    }

    /// <summary>
    /// Registers the That was wrong key from hud.json, by the start/stop key's rules (Platform/Hotkey.cs): a message when it
    /// can't be used (the rules, the start/stop key's own keys, or another app has it), else null. Off is no problem.
    /// </summary>
    string? ApplyMarkWrongKey()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return null;
        Native.UnregisterHotKey(h, Hotkey.MarkWrongId);
        if (_hotkeySuspended) return null;
        var text = _hudPrefs.MarkWrongKey;
        if (Hotkey.Explain(text) is { } why)
        {
            _markKeyProblem = ($"The That was wrong shortcut {text.Trim()} is off: {char.ToLowerInvariant(why[0])}{why[1..]} Pick another in Options › In game.", $"That was wrong key {text.Trim()} is off · pick another in Options");
            return _markKeyProblem.Value.Long;
        }
        if (Hotkey.Parse(text) is not { } key) { _markKeyProblem = null; return null; }
        if (Hotkey.Same(text, _prefs.Hotkey))
        {
            _markKeyProblem = ($"{key} is already the start/stop shortcut, so That was wrong is off. Pick another in Options › In game.", $"{key} is the start/stop key · pick another in Options");
            return _markKeyProblem.Value.Long;
        }
        _markKeyProblem = Native.RegisterHotKey(h, Hotkey.MarkWrongId, key.NativeModifiers, key.VirtualKey) ? null
            : ($"{key} is already used by another app, so That was wrong is off. Pick another in Options › In game.", $"{key} is taken by another app · pick another in Options");
        return _markKeyProblem?.Long;
    }

    /* ---------------- the key pressed ---------------- */

    /// <summary>The newest counted line on any tab from the last 2 minutes: a pickup, Kinah or a craft (not a run step or a flagged run).</summary>
    FeedLine? NewestCounted()
    {
        var since = DateTime.UtcNow.AddMinutes(-MarkWindowMinutes);
        return new[] { FeedTab.Farming, FeedTab.Crafting, FeedTab.Events }.SelectMany(FeedFor)
            .Where(l => l.Kind is not ("flag" or "step") && l.At >= since).OrderByDescending(l => l.At).FirstOrDefault();
    }

    /// <summary>The That was wrong key: the newest counted line marked and its report saved, or the panel saying there was none.</summary>
    void MarkLastLineWrong()
    {
        try
        {
            if (NewestCounted() is { } line) { MarkWrong(line); return; }
            ShowMarkNotice(MarkNothing);
            Say("That was wrong: nothing was counted in the last 2 minutes, so nothing was saved.", "Faint");
            CrashLog.Note("mark wrong: nothing counted in the last 2 minutes");
        }
        catch (Exception ex) { CrashLog.Write("mark wrong", ex); }
    }

    /// <summary>
    /// Marks <paramref name="line"/> wrong: the report saved now (the minute it covers is the one just played), the line
    /// marked in the window with Undo under its feed, the panel's notice. A layout render or test saves nothing.
    /// </summary>
    void MarkWrong(FeedLine line)
    {
        if (IsMarked(line)) { ShowMarkNotice(MarkAgain); return; }
        (string Dir, string Why)? saved = null;
        if (!_demo)
        {
            // the sheet's own default (Wrong amount), as tapping the line and pressing Report files it; the note says how
            saved = SaveLineReport(new MiscountLine("wrong_amount", line), "marked in game with the That was wrong key");
            if (saved == null) { ShowMarkNotice(MarkFailed); return; }
        }
        var key = MarkKey(line);
        var text = $"Marked {line.At.ToLocalTime():HH:mm:ss} {line.Text} as wrong";
        CrashLog.Note("mark wrong: " + text);
        _ = Offer("mark", text,
            apply: () => { _marked.Add(key); ShowMarkLine(line, text); Redraw(); },
            undo: () =>
            {
                _marked.Remove(key); HideMarkLine(); Redraw();
                if (saved is { } s) try { Directory.Delete(s.Dir, recursive: true); } catch (Exception ex) { CrashLog.Write("mark wrong: undo", ex); }
                Say("The mark is taken back and its report deleted.", "Good");
            },
            commit: () =>
            {
                HideMarkLine();
                if (saved is { } s) _ = SendReportPictures(s.Dir, s.Why);   // only once Undo is no longer possible, and only when Options › Sharing allows
                return Task.CompletedTask;
            },
            bar: line.Tab == FeedTab.Events,   // the Events card has no line under its feed: the Undo bar says it there
            seconds: MarkUndoSeconds);
        ShowMarkNotice(MarkSaved);
        Say(text + ": the last minute is saved in your reports folder.", "Good");
        if (saved is { } sv) Status.ToolTip = sv.Dir;
    }

    /* ---------------- the window ---------------- */

    /// <summary>The line under the feed (the mock's "Marked 23:54:10 Spiritstone ×1 as wrong · Undo"), in place of the feed's hint until Undo is no longer possible.</summary>
    void ShowMarkLine(FeedLine line, string text)
    {
        HideMarkLine();
        var hint = line.Tab switch { FeedTab.Farming => FeedHint, FeedTab.Crafting => CraftHint, _ => null };
        if (hint?.Parent is not Panel host) return;
        var t = new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var flag = FlagGlyph(13); flag.Margin = new Thickness(0, 0, 7, 0);
        t.Inlines.Add(new InlineUIContainer(flag) { BaselineAlignment = BaselineAlignment.Center });
        t.Inlines.Add(new Run(text + "  ·  "));
        var undo = new Hyperlink(new Run("Undo")) { ToolTip = "Takes the mark back and deletes the report it saved (Ctrl+Z)." };
        undo.SetResourceReference(TextElement.ForegroundProperty, "Gold");
        undo.Click += (_, _) => UndoPending();
        t.Inlines.Add(undo);
        System.Windows.Automation.AutomationProperties.SetName(t, text);
        host.Children.Insert(host.Children.IndexOf(hint) + 1, t);
        hint.Visibility = Visibility.Collapsed;
        _markLine = t; _markHint = hint;
    }

    void HideMarkLine()
    {
        if (_markLine?.Parent is Panel p) p.Children.Remove(_markLine);
        if (_markHint != null) _markHint.Visibility = Visibility.Visible;
        _markLine = _markHint = null;
    }

    /// <summary>A marked feed row (DrawFeed): a faint gold wash, a 3 px gold edge, its time in gold and a flag after its name.</summary>
    void MarkRow(Border row, FeedLine l)
    {
        if (row.Child is not Grid g) return;
        var wash = new SolidColorBrush(Color.FromArgb(0x14, 0xF8, 0xD5, 0x98)); wash.Freeze();
        row.Background = wash;
        // after FeedRow's own hover handlers, so the row keeps its wash
        row.MouseEnter += (_, _) => row.Background = wash;
        row.MouseLeave += (_, _) => row.Background = wash;
        var edge = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-12, 0, 0, 0), IsHitTestVisible = false };
        edge.SetResourceReference(Border.BackgroundProperty, "Gold");
        g.Children.Add(edge);
        if (g.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 0) is { } time)
        {
            var local = l.At.ToLocalTime();
            time.Text = local.Date == DateTime.Today ? local.ToString("HH:mm:ss") : local.ToString("MMM d");
            time.FontSize = 11.5; time.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"); time.SetResourceReference(TextBlock.ForegroundProperty, "Gold");
        }
        if (g.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 2) is { } what)
        {
            var flag = FlagGlyph(13); flag.Margin = new Thickness(7, 0, 0, 0);
            var box = new InlineUIContainer(flag) { BaselineAlignment = BaselineAlignment.Center };
            // after the name and amount, before the faint word ("gathered", the enemy) when there is one
            if (FeedWords(l).Small.Length > 0 && what.Inlines.LastInline is { } small) what.Inlines.InsertBefore(small, box);
            else what.Inlines.Add(box);
        }
        row.ToolTip = "Marked as wrong with the That was wrong key; the report is saved. Click to report it another way.";
    }

    /// <summary>The mock's flag: a pole and a gold pennant, in the look's gold.</summary>
    static FrameworkElement FlagGlyph(double size)
    {
        var pole = new Path { Data = Geometry.Parse("M3.5,1.5 L3.5,14.5"), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        pole.SetResourceReference(Shape.StrokeProperty, "Gold");
        var pennant = new Path { Data = Geometry.Parse("M3.5,2 L13,2 L10.6,5.75 L13,9.5 L3.5,9.5 Z") };
        pennant.SetResourceReference(Shape.FillProperty, "Gold");
        return new Viewbox { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Child = new Canvas { Width = 16, Height = 16, Children = { pole, pennant } } };
    }

    /* ---------------- layout renders and the options self-test ---------------- */

    /// <summary>For layout renders: the window just after the key, the newest Farming line marked and Undo under the feed.</summary>
    internal void ShowMarkedSample()
    {
        if (FeedFor(FeedTab.Farming).FirstOrDefault() is { } line) MarkWrong(line);
    }

    /// <summary>For layout renders: the Card and the Ticker with the key's notice, its gold line at 58% (ui-hud-card-marked.png, ui-hud-ticker-marked.png).</summary>
    internal List<(string File, string Look, Dictionary<string, string> Values)> MarkedLookStates()
    {
        var v = new Dictionary<string, string>(HudSample()) { ["notice"] = MarkSaved, ["noticeClock"] = "@0.58" };
        return new() { ("ui-hud-card-marked.png", HudLooks.Card, v), ("ui-hud-ticker-marked.png", HudLooks.Ticker, v) };
    }

    /// <summary>For the options self-test: the key pressed (with the sample feed five minutes old when <paramref name="nothingRecent"/>); the panel's notice after it.</summary>
    internal string PressMarkWrongForTest(bool nothingRecent)
    {
        if (nothingRecent) _demoFeed = _demoFeed.Select(l => l with { At = l.At.AddMinutes(-5) }).ToList();
        MarkLastLineWrong();
        return HudNotice();
    }

    /// <summary>For the options self-test: how many lines are marked, whether the line under the feed shows, and what waits behind Undo.</summary>
    internal (int Marked, bool LineShown, string? Pending) MarkStateForTest => (_marked.Count, _markLine != null, _pendingUndo?.Kind);
}
