using System.Diagnostics;
using System.Windows.Threading;

namespace KinahLedger.Ui;

/// <summary>
/// Types a line out a character at a time, a terminal's way (Terminal look, 2026-11-18: "when u gather something the item pops
/// up instead of typing it out"). A line is a few segments (each writes its own text), shown as a growing prefix of their
/// whole: about 18 to 30 ms a character with a little jitter, a slight pause after the prompt and after the amount, and the
/// whole line no longer than <see cref="CapMs"/> however long the name. One line types at a time: a line that arrives while
/// another types waits its turn, unless it is <c>flush</c> (a new pickup), which finishes what was typing and what waited at
/// once. One <see cref="DispatcherTimer"/>, running only while a line types; a render holds a line at a point instead
/// (<see cref="Hold"/>) and starts nothing.
/// </summary>
sealed class Typewriter
{
    /// <summary>The longest a line takes to type, however many characters.</summary>
    public const double CapMs = 600;
    const double CharMinMs = 18, CharJitterMs = 12, PauseMs = 70;

    /// <summary>A piece of the line: its words and where it writes the part typed so far.</summary>
    public readonly record struct Seg(string Full, Action<string> Write);

    public sealed class Job
    {
        public string Key { get; }
        internal Seg[] Segs;
        /// <summary>Called with the characters shown so far, at every change (the cursor, a tile or a badge that waits for the end).</summary>
        internal readonly Action<int>? Progress;
        internal readonly Action? Done;
        /// <summary>The time at which each character is shown, ms from the line's start.</summary>
        internal readonly double[] At;
        public int Total { get; }
        internal int Shown = -1;

        /// <param name="pauses">Character positions the line waits before (after the prompt, after the amount).</param>
        public Job(string key, Seg[] segs, IReadOnlyCollection<int>? pauses = null, Action<int>? progress = null, Action? done = null)
        {
            Key = key; Segs = segs; Progress = progress; Done = done;
            Total = segs.Sum(s => s.Full.Length);
            var d = new double[Total];
            double t = 0;
            for (int i = 0; i < Total; i++)
            {
                t += CharMinMs + Random.Shared.NextDouble() * CharJitterMs + (pauses != null && pauses.Contains(i) ? PauseMs : 0);
                d[i] = t;
            }
            // the cap: a long line types faster, all of it
            if (Total > 0 && t > CapMs) { double k = CapMs / t; for (int i = 0; i < Total; i++) d[i] *= k; }
            At = d;
        }

        internal string Text => string.Concat(Segs.Select(s => s.Full));
    }

    readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
    readonly Queue<Job> _queue = new();
    readonly Stopwatch _clock = new();
    Job? _cur;

    public Typewriter() => _timer.Tick += (_, _) => Tick();

    /// <summary>Whether a line (of this key, when given) is typing or waiting to.</summary>
    public bool Active(string? key = null) => (_cur != null && (key == null || _cur.Key == key)) || _queue.Any(j => key == null || j.Key == key);

    /// <summary>
    /// Types <paramref name="job"/>: now if nothing types, after the lines before it otherwise; <paramref name="flush"/>: after
    /// those are finished at once. Its text is empty from this call (reserve its width first, so nothing shifts).
    /// </summary>
    public void Start(Job job, bool flush)
    {
        if (flush) FinishAll();
        Show(job, 0);
        if (_cur == null) Begin(job); else _queue.Enqueue(job);
    }

    /// <summary>A render's frame: the line at this share of its characters, nothing running; done at 1.</summary>
    public void Hold(Job job, double at)
    {
        int n = (int)Math.Round(Math.Clamp(at, 0, 1) * job.Total);
        Show(job, n);
        if (n >= job.Total) job.Done?.Invoke();
    }

    /// <summary>Every line finished where it stands, shown whole.</summary>
    public void FinishAll()
    {
        while (_cur != null)
        {
            var j = _cur;
            _cur = null;
            Show(j, j.Total);
            j.Done?.Invoke();
            if (_queue.Count > 0) { _cur = _queue.Dequeue(); Show(_cur, 0); }
        }
        _timer.Stop();
    }

    /// <summary>A line given up (what it wrote was drawn over): its done does not run.</summary>
    public void Cancel(string key)
    {
        if (_queue.Count > 0)
        {
            var keep = _queue.Where(j => j.Key != key).ToList();
            _queue.Clear();
            foreach (var j in keep) _queue.Enqueue(j);
        }
        if (_cur?.Key == key) { _cur = null; Next(); }
    }

    /// <summary>The line of this key drawn anew (its text the same): it goes on typing in the new pieces from where it was.</summary>
    public void Rebind(string key, Seg[] segs)
    {
        foreach (var j in _queue.Prepend(_cur).Where(j => j != null && j.Key == key))
        {
            if (string.Concat(segs.Select(s => s.Full)) != j!.Text) continue;
            j.Segs = segs;
            Show(j, Math.Max(0, j.Shown), force: true);
        }
    }

    void Begin(Job job)
    {
        _cur = job;
        if (job.Total == 0) { FinishAll(); return; }
        _clock.Restart();
        _timer.Start();
    }

    void Next()
    {
        if (_cur != null) return;
        if (_queue.Count > 0) Begin(_queue.Dequeue()); else _timer.Stop();
    }

    void Tick()
    {
        var j = _cur;
        if (j == null) { _timer.Stop(); return; }
        double ms = _clock.Elapsed.TotalMilliseconds;
        int n = j.Shown < 0 ? 0 : j.Shown;
        while (n < j.Total && j.At[n] <= ms) n++;
        if (n != j.Shown) Show(j, n);
        if (n < j.Total) return;
        _cur = null;
        j.Done?.Invoke();
        Next();
    }

    static void Show(Job j, int n, bool force = false)
    {
        if (!force && n == j.Shown) return;
        j.Shown = n;
        int left = n;
        foreach (var s in j.Segs)
        {
            int take = Math.Min(left, s.Full.Length);
            s.Write(s.Full[..take]);
            left -= take;
        }
        j.Progress?.Invoke(n);
    }
}
