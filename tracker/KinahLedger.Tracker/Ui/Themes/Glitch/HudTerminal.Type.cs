using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using static KinahLedger.Ui.GlitchKit;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Terminal types what is new (Steven, 2026-11-18: "when u gather something the item pops up instead of typing it out. it
/// should make it look like its typing it out since its fallout style lol"): the pickup's log line "> +7.5K Spiritstone" a
/// character at a time with the block cursor at its end, the "!! RARE" line, a new best's "> record --new" and a run's clear
/// verdict the same, one line at a time (Ui/Typewriter.cs). The figures that tick (the hour, the clock) never type. A line's
/// width is reserved for its whole text while it types, so nothing shifts. In a render (Still) the whole line shows; a held
/// frame (KL_MOTION_AT) shows it typed that far.
/// </summary>
sealed partial class HudTerminal
{
    readonly Typewriter _type = new();
    readonly FreshPicks _fresh = new();
    /// <summary>The pickup just made at this update (Ui/HudFreshPicks.cs), the one the log line types; null for a refresh, a loaded session or the first draw.</summary>
    Pick? _freshPick;
    TextBlock? _verdictTb;
    bool _clearSeen, _wasClear, _clearFresh;
    int _rareLabel;

    const string KeyPick = "pick", KeyRare = "rare", KeyPrompt = "prompt", KeyVerdict = "verdict";

    /// <summary>Once per draw: whether a pickup was just made, and a run's clear just came.</summary>
    void FreshDraw(RunBuddy.State? rs)
    {
        _freshPick = _fresh.Next(Values, out _);
        bool clear = rs?.Clear == true;
        _clearFresh = _clearSeen && clear && !_wasClear;
        _wasClear = clear;
        _clearSeen = true;
    }

    /// <summary>The element's width set to what it will be with its whole text: nothing around it moves while it types.</summary>
    static void Reserve(FrameworkElement e)
    {
        e.ClearValue(FrameworkElement.MinWidthProperty);
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        e.MinWidth = Math.Max(0, e.DesiredSize.Width - e.Margin.Left - e.Margin.Right);
    }

    static Typewriter.Seg[] SegsOf(IEnumerable<Run> runs) => runs.Select(r => new Typewriter.Seg(r.Text, s => r.Text = s)).ToArray();

    // ---- the pickup's log line

    Typewriter.Seg[] PickSegs() => SegsOf(_pick.Inlines.OfType<Run>());

    void TypePick(Pick p, bool held)
    {
        Reserve(_pickRow);
        var pauses = new List<int> { 2 };   // after the prompt "> "
        if (p.Worth.Length > 0) pauses.Add(2 + p.Worth.Length + 1);   // and after the amount
        var job = new Typewriter.Job(KeyPick, PickSegs(), pauses, _ => _cursor.Opacity = 1, () => _pickRow.ClearValue(FrameworkElement.MinWidthProperty));
        if (held) _type.Hold(job, Motion.FrozenAt ?? 1); else _type.Start(job, flush: true);
    }

    // ---- the rare drop's line

    void TypeRare(bool held)
    {
        if (_rareLine.Child is not StackPanel sp || sp.Children.Count < 3 || sp.Children[0] is not TextBlock label) return;
        _rareLabel = label.Text.Length;
        Reserve(_rareLine);
        var job = new Typewriter.Job(KeyRare, RareSegs(sp), null, RareProgress, () => _rareLine.ClearValue(FrameworkElement.MinWidthProperty));
        if (held) _type.Hold(job, Motion.FrozenAt ?? 1); else _type.Start(job, flush: false);
    }

    /// <summary>The line's two texts, "!! RARE: " then the item's name; its tile stays hidden (its place kept) until the label is typed.</summary>
    static Typewriter.Seg[] RareSegs(StackPanel sp)
    {
        var parts = new List<Typewriter.Seg>();
        foreach (var t in sp.Children.OfType<TextBlock>()) parts.Add(new Typewriter.Seg(t.Text, s => t.Text = s));
        return parts.ToArray();
    }

    void RareProgress(int n)
    {
        if (_rareTile is { } tile) tile.Visibility = n >= _rareLabel ? Visibility.Visible : Visibility.Hidden;
    }

    // ---- a new best: the prompt writes the record

    void TypePrompt(bool held)
    {
        string full = _prompt.Text;
        Reserve(_prompt);
        var job = new Typewriter.Job(KeyPrompt, new[] { new Typewriter.Seg(full, s => _prompt.Text = s) }, new[] { 2 },
            n => _ok.Visibility = n >= full.Length ? Visibility.Visible : Visibility.Hidden,
            () => _prompt.ClearValue(FrameworkElement.MinWidthProperty));
        if (held) _type.Hold(job, Motion.FrozenAt ?? 1); else _type.Start(job, flush: false);
    }

    /// <summary>The prompt drawn again: the new best's line goes on typing in it; any other prompt is written whole.</summary>
    void PromptDrawn(bool beat)
    {
        if (beat && _type.Active(KeyPrompt)) _type.Rebind(KeyPrompt, new[] { new Typewriter.Seg(_prompt.Text, s => _prompt.Text = s) });
        else _type.Cancel(KeyPrompt);
    }

    // ---- the run's clear verdict

    /// <summary>The verdict drawn under the clock ("[2nd fastest]", "[★ NEW BEST]"), or none: a clear just come types it.</summary>
    void VerdictDrawn(TextBlock? vb)
    {
        _verdictTb = vb;
        if (vb == null) { _type.Cancel(KeyVerdict); return; }
        var segs = new[] { new Typewriter.Seg(vb.Text, s => vb.Text = s) };
        if (_type.Active(KeyVerdict)) { Reserve(vb); _type.Rebind(KeyVerdict, segs); return; }
        if (_clearFresh && Moves)
        {
            Reserve(vb);
            _type.Start(new Typewriter.Job(KeyVerdict, segs, null, null, () => vb.ClearValue(FrameworkElement.MinWidthProperty)), flush: false);
        }
    }

    /// <summary>A render's held frame: also the clear's verdict typed that far.</summary>
    public override void MotionFrame(IReadOnlyDictionary<string, string> values)
    {
        base.MotionFrame(values);
        if (_runOn && _verdictTb is { } vb)
        {
            Reserve(vb);
            _type.Hold(new Typewriter.Job(KeyVerdict, new[] { new Typewriter.Seg(vb.Text, s => vb.Text = s) }), Motion.FrozenAt ?? 1);
        }
    }
}
