using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Undo instead of "Are you sure?" (2026-10-25 audit): Clear the tables, Clear on Events, Start over on Crafting, Remove
/// on a run and Stop run from the window each happen on screen at once, with an Undo bar for <see cref="UndoSeconds"/>
/// seconds. Nothing under Session/ can put a count back once it is gone, so the window holds the action instead: the
/// tables, crafts or runs only look cleared, and the real clear (which shares what was counted, as before) happens when
/// the bar goes. A stop works the same way: the run keeps counting underneath until the bar goes, so Undo carries on
/// with the same run. One action waits at a time; a second makes the first final. Closing the window makes it final too.
/// </summary>
public partial class MainWindow
{
    const int UndoSeconds = 8;

    /// <summary>The action waiting behind the bar: what it is ("tables", "crafts", "runs", "run", "stop"), its words, how to put it back, and how to make it final.</summary>
    sealed record PendingUndo(string Kind, string Text, Action Undo, Func<Task> Commit);
    PendingUndo? _pendingUndo;
    readonly DispatcherTimer _undoTimer = new() { Interval = TimeSpan.FromSeconds(UndoSeconds) };

    /// <summary>The Farming tables while a clear can still be undone: an empty count in the same zone, shown in place of the real one.</summary>
    SessionEngine? _clearedView;
    /// <summary>The Crafting part while Start over can still be undone: an empty count shown in place of the real one.</summary>
    SessionEngine? _craftCleared;
    /// <summary>Runs hidden while a Clear or a Remove can still be undone, by <see cref="RunKey"/>.</summary>
    readonly HashSet<string> _hiddenRuns = new();
    /// <summary>True while Stop run from the window can still be undone: the run is still counting underneath.</summary>
    bool _stopPending;

    /// <summary>A run on as the window shows it: false the moment Stop is pressed, though the run only ends when its Undo bar goes.</summary>
    bool RunOn => Farming && !_stopPending;
    /// <summary>The run's count as the window shows it (null while its stop can still be undone).</summary>
    SessionEngine? RunEngine => _stopPending ? null : _watcher.Farm;
    /// <summary>The crafting count the Crafting part shows.</summary>
    SessionEngine? CraftShown => _craftCleared ?? _watcher.Chat?.CraftView;

    static string RunKey(DateTime start, string zone) => $"{start.Ticks}|{zone.ToLowerInvariant()}";

    /// <summary>The bar's button, its timer and Ctrl+Z, wired once from the constructor.</summary>
    void WireUndo()
    {
        _undoTimer.Tick += async (_, _) => await CommitPending();
        UndoBtn.Click += (_, _) => UndoPending();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Z || Keyboard.Modifiers != ModifierKeys.Control || _pendingUndo == null || Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
            e.Handled = true;
            UndoPending();
        };
    }

    /// <summary>
    /// Does <paramref name="apply"/> now (what the player sees) and shows the bar; <paramref name="commit"/> runs when the bar
    /// goes, <paramref name="undo"/> on Undo. An action already waiting is made final first.
    /// </summary>
    async Task Offer(string kind, string text, Action apply, Action undo, Func<Task> commit)
    {
        await CommitPending();
        apply();
        _pendingUndo = new PendingUndo(kind, text, undo, commit);
        UndoText.Text = text;
        UndoBar.Visibility = Visibility.Visible;
        UndoTimeScale.BeginAnimation(ScaleTransform.ScaleXProperty, _demo ? null : new DoubleAnimation(1, 0, TimeSpan.FromSeconds(UndoSeconds)));
        _undoTimer.Stop();
        if (!_demo) _undoTimer.Start();   // a layout render keeps the bar up
        CrashLog.Note($"undo bar: {kind} ({text})");
    }

    /// <summary>The waiting action made final (the bar ran out, another action came, or the window is closing).</summary>
    async Task CommitPending()
    {
        var p = _pendingUndo; if (p == null) return;
        EndUndo();
        try { await p.Commit(); }
        catch (Exception ex) { CrashLog.Write("undo bar: " + p.Kind, ex); }
    }

    /// <summary>Undo: the waiting action put back as if it never happened.</summary>
    void UndoPending()
    {
        var p = _pendingUndo; if (p == null) return;
        EndUndo();
        try { p.Undo(); CrashLog.Note($"undone: {p.Kind}"); }
        catch (Exception ex) { CrashLog.Write("undo: " + p.Kind, ex); }
    }

    void EndUndo()
    {
        _pendingUndo = null;
        _undoTimer.Stop();
        UndoTimeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        UndoTimeScale.ScaleX = 1;
        UndoBar.Visibility = Visibility.Collapsed;
    }

    /* ---------------- the actions ---------------- */

    /// <summary>Start over on the Crafting part: the recipes, tries and profit go, with Undo.</summary>
    Task StartCraftOver() => Offer("crafts", "Crafting started over",
        apply: () => { _craftCleared = new SessionEngine(_items); FillCraft(CraftShown); },
        undo: () => { _craftCleared = null; FillCraft(CraftShown); Say("Crafting is back as it was.", "Good"); },
        commit: () => { _watcher.Chat?.ResetCraftView(); _craftCleared = null; FillCraft(CraftShown); return Task.CompletedTask; });

    /// <summary>Clear on the Events part: the runs shown go, with Undo; runs not sent to the site yet are still sent, and the one under way stays.</summary>
    async Task ClearRuns()
    {
        if (_watcher.Chat?.Instances is not { } il) return;
        var open = il.Current is { } cur ? RunKey(cur.Start, cur.Zone) : "";
        var keys = il.Snapshot().Select(r => RunKey(r.Start, r.Zone)).Where(k => k != open).ToList();
        if (keys.Count == 0) return;
        await Offer("runs", "Expedition and event runs cleared",
            apply: () => { _hiddenRuns.UnionWith(keys); ShowInstances(); },
            undo: () => { _hiddenRuns.Clear(); ShowInstances(); },
            commit: () => { _hiddenRuns.Clear(); il.ClearShown(); ShowInstances(); return Task.CompletedTask; });
    }

    /// <summary>Stop run from the window's button: the window shows the run stopped at once; it ends for real (saved and shared) when the bar goes.</summary>
    Task StopFromWindow() => Offer("stop", "Run stopped",
        apply: () => { _stopPending = true; SetTrackingUi(false); Say("Run stopped. It's saved in a few seconds; Undo carries on with the same run.", "Faint"); Tick(); },
        undo: () => { _stopPending = false; SetTrackingUi(true); FlashHud("Run resumed"); Say("Run resumed: the same run carries on.", "Good"); Tick(); },
        commit: async () => { _stopPending = false; await StopTracking(); });

    /// <summary>For layout renders (KL_SHOT_UNDO=1): the bar as it shows two seconds after Clear the tables.</summary>
    void ShowUndoSample()
    {
        _ = Offer("tables", "Farming tables cleared", () => { }, () => { }, () => Task.CompletedTask);
        UndoTimeScale.ScaleX = 0.75;
    }

    /// <summary>For the options self-test: the bar's words while it shows, else null.</summary>
    internal string? UndoShowing => UndoBar.Visibility == Visibility.Visible ? UndoText.Text : null;
    /// <summary>For the options self-test: Undo pressed.</summary>
    internal void PressUndo() => UndoPending();
    /// <summary>For the options self-test: the "…" menu's Clear the tables.</summary>
    internal Task ClearTablesForTest() => ClearFarmTables();
    /// <summary>For the options self-test: true while the tables only look cleared (the clear can still be undone).</summary>
    internal bool TablesClearedForUndo => _clearedView != null;
}
