using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The gathering attempt tally (Steven, 2026-10-24: "you're not logging the mining attempts"). Until then an attempt was an
/// item line that counted (or a failure line), so a gather whose line the chat lost, or whose item was set aside, was no
/// attempt, and every "Essence Extraction failed" went to Odyle. Now:
/// <list type="bullet">
/// <item>Every race the gathering bar finishes is one attempt the moment it ends (Success or Fail), whatever its name. An
/// upgrade roll is the same gather's second race, so it is an attempt only when no attempt was counted in the 10 s before
/// it (its base race went between passes).</item>
/// <item>The attempt is named, in order, by the gathering panel's item line read during the race; the gather's result (the
/// chat's "X acquired." or the loot list row its race released) within 6 s after a Success; the node on the target bar
/// (GatheringCatalog.MatchNode, fragments allowed); the last node named in the last minute; else "unknown node".</item>
/// <item>A missed race: a gather result from the chat with the gathering panel up (or a race or node in the last 30 s), the
/// panel naming that item or nothing, and no Success race within 6 s before it or 2.5 s after: one successful attempt.</item>
/// <item>A failure: the bar's Fail race or a "… failed" chat line, one attempt for both within 3 s, under the current node.</item>
/// </list>
/// Attempts go to the farming engines (SessionEngine.GatherAttempt), which then count item lines as yields only.
/// </summary>
public sealed partial class TrackerSession
{
    public const double ResultSeconds = 6, FailSameSeconds = 3, UpgradeSameSeconds = 10, LastNodeSeconds = 60, PanelRaceSeconds = 8, TargetNodeSeconds = 15, ResultBeforeRaceSeconds = 2.5, GatheringUpSeconds = 30, LateResultSeconds = 15;
    public const string UnknownNode = "unknown node";

    /// <summary>One counted attempt: when, how it ended, where it was filed and what named it.</summary>
    sealed class Attempt
    {
        public DateTime At; public bool Ok; public GatherFamily Family; public string Node = UnknownNode;
        /// <summary>"panel", "result", "target", "recent", "unknown": the witness that named it (the first two are sure).</summary>
        public string By = "unknown";
        /// <summary>A bar race (true) or a result whose race wasn't seen, or a failure line (false).</summary>
        public bool Race;
        /// <summary>Its result has come: a later result is another gather's.</summary>
        public bool Claimed;
    }
    readonly List<Attempt> _attempts = new();
    /// <summary>Chat results waiting a moment for the race they belong to (the chat can show a line before the bar's race is read).</summary>
    readonly List<(DateTime At, GatheringCatalog.GatherNode Node, string Raw)> _results = new();
    (GatheringCatalog.GatherNode? Node, DateTime At) _lastNamed = (null, DateTime.MinValue);
    readonly object _tallyLock = new();
    /// <summary>Every Success race the bar finished, upgrade rolls too (an upgrade roll is no attempt of its own, but its result is its own).</summary>
    readonly List<DateTime> _okRaces = new();
    bool OkRaceNear(DateTime t, double before, double after) => _okRaces.Any(r => (t - r).TotalSeconds >= -after && (t - r).TotalSeconds <= before);

    IEnumerable<SessionEngine> FarmEngines() => new[] { Engine, Secondary }.Concat(Views()).Where(e => e != null)!;

    /// <summary>What names an attempt at <paramref name="t"/>, short of its result: the panel, the target bar's node, the last named node, else unknown.</summary>
    (GatherFamily Family, string Node, string By) NameAt(DateTime t)
    {
        if (_panelItem.Item.Length > 0 && (t - _panelItem.At).TotalSeconds is >= -1 and <= PanelRaceSeconds && GatheringCatalog.NodeOf(_panelItem.Item) is { } pn)
            return (pn.Family, pn.Name, "panel");
        if (Target.Node is { } tn && (t - Target.NodeAt).TotalSeconds is >= -1 and <= TargetNodeSeconds) return (tn.Family, tn.Name, "target");
        if (_lastNamed.Node is { } ln && (t - _lastNamed.At).TotalSeconds is >= -1 and <= LastNodeSeconds) return (ln.Family, ln.Name, "recent");
        // the panel's own title, when it names the skill ("Essence Extraction"); else no skill
        var fam = GatherSkills.FromSkillText(string.Join(" ", CraftPanel.LastLines));
        return (fam ?? GatherFamily.Unknown, UnknownNode, "unknown");
    }

    void Named(GatherFamily family, string node, string by, DateTime t)
    {
        if (by is "panel" or "result" or "target" && node != UnknownNode) _lastNamed = (new GatheringCatalog.GatherNode(node, null, family), t);
    }

    void Tally(Attempt a, string why)
    {
        _attempts.Add(a);
        _attempts.RemoveAll(x => (a.At - x.At).TotalSeconds > 120);
        Named(a.Family, a.Node, a.By, a.At);
        foreach (var e in FarmEngines()) { e.AttemptsFromTally = true; e.GatherAttempt(a.At, a.Family, a.Node, a.Ok); }
        CrashLog.Note($"gather attempt at {a.At.ToLocalTime():HH:mm:ss.f}: {a.Node} ({GatherSkills.Label(a.Family)}), {(a.Ok ? "success" : "failed")}, {why}; named by {a.By switch { "panel" => "the gathering panel", "result" => "its result", "target" => "the target bar", "recent" => "the last node named", _ => "nothing: no node read" }}");
    }

    /// <summary>A race the gathering bar finished (GatherOutcome): an attempt now, named as well as it can be.</summary>
    void GatherRace(DateTime at, bool ok, bool upgradeRoll)
    {
        lock (_tallyLock)
        {
            GatherTickLocked(at);
            if (ok || upgradeRoll) { _okRaces.Add(at); _okRaces.RemoveAll(r => (at - r).TotalSeconds > 120); }
            if (upgradeRoll)
            {
                // the same gather's second race: its attempt was counted with its base race (or its result)
                if (_attempts.LastOrDefault(x => x.Ok && (at - x.At).TotalSeconds is >= 0 and <= UpgradeSameSeconds) is { } b)
                { CrashLog.Note($"gathering bar: upgrade roll on the {b.Node} gather of {b.At.ToLocalTime():HH:mm:ss}, the same attempt"); return; }
                ok = true;   // an upgrade rolls only on a gather that worked: its base race went between passes
            }
            if (!ok && _attempts.LastOrDefault(x => !x.Ok && Math.Abs((at - x.At).TotalSeconds) <= FailSameSeconds) is { } f)
            { f.Race = true; CrashLog.Note($"gathering bar: fail, the same failed attempt as the failure line at {f.At.ToLocalTime():HH:mm:ss}"); return; }
            var (family, node, by) = NameAt(at);
            var a = new Attempt { At = at, Ok = ok, Family = family, Node = node, By = by, Race = true };
            // a chat result read just before the race was registered (the chat can be a pass ahead of the bar): this race's
            if (ok && _results.FindIndex(r => (at - r.At).TotalSeconds is >= 0 and <= ResultBeforeRaceSeconds) is int ri and >= 0)
            {
                var r = _results[ri]; _results.RemoveAt(ri);
                a.Claimed = true;
                // the chat claimed no token then (there was none yet): its line claims this race's now, so the loot list's row
                // for the same gather can't count it a second time (15:21:17.8 chat, 17.9 race, the row counted again at 20.0)
                lock (_gatherTokens) { int ti = _gatherTokens.FindLastIndex(x => x.At == at && !x.Used); if (ti >= 0) _gatherTokens[ti] = _gatherTokens[ti] with { Used = true }; }
                if (by != "panel") { a.Family = r.Node.Family; a.Node = r.Node.Name; a.By = "result"; }
            }
            Tally(a, upgradeRoll ? "the gathering bar's upgrade roll (its base race wasn't seen)" : $"the gathering bar's {(ok ? "Success" : "Fail")} race");
            // a gather that worked: a witness of a second pickup of its node's material (ItemRepeatGuard)
            if (a.Ok) _repeats.Race(at, a.Node == UnknownNode ? "" : a.Node, a.Family);
        }
    }

    /// <summary>
    /// What a counted event says about gathering (Count): a gathering material's result, or a failure line. True when it is
    /// a gather the panel or its bar just produced (then it is gathering's, never a run's loot).
    /// </summary>
    bool GatherSeen(GameEvent ev)
    {
        foreach (var e in FarmEngines()) e.AttemptsFromTally = true;
        if (ev is GatherFailed gf) { GatherFailLine(gf.Time, gf.Skill); return false; }
        if (ev is not ItemAcquired ia || !IsGatherMaterial(ia.ItemId)) return false;
        bool produced = GatherJustProduced(ia.ItemId, ia.Time);
        if (GatheringCatalog.NodeOf(_items.NameOf(ia.ItemId)) is { } node && node.Family != GatherFamily.Unknown)
            GatherResult(ia.Time, node, _items.NameOf(ia.ItemId), ev.Via.Contains("loot list"));
        return produced;
    }

    /// <summary>
    /// A gathering material picked up: names the Success race just before it when nothing surer did; a chat result with no
    /// race around it waits a moment for one (GatherTick), then counts as a gather whose race went between passes.
    /// </summary>
    void GatherResult(DateTime t, GatheringCatalog.GatherNode node, string raw, bool fromList)
    {
        lock (_tallyLock)
        {
            Named(node.Family, node.Name, "result", t);
            var race = _attempts.LastOrDefault(x => x.Ok && x.Race && (t - x.At).TotalSeconds is >= -ResultBeforeRaceSeconds and <= ResultSeconds);
            // an upgrade roll's yield ("Acquired Fine Odyle x2." after the roll): the attempt was its base race's
            if (race == null && OkRaceNear(t, ResultSeconds, ResultBeforeRaceSeconds)) return;
            // a line late for its own race (while gathering the chat can come 6 to 11 s after it): the race no result has claimed
            if (race == null && LateRace(t) is { } late) { late.Claimed = true; return; }
            if (race != null)
            {
                // a gemstone rides along with ore: it names only a race nothing else named
                if (!race.Claimed && race.By is not ("panel" or "result") && (node.Name != GatheringCatalog.GemNode || race.By == "unknown") && !string.Equals(race.Node, node.Name, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var e in FarmEngines()) e.RefileAttempt(race.Family, race.Node, node.Family, node.Name, true);
                    CrashLog.Note($"gather attempt of {race.At.ToLocalTime():HH:mm:ss} named {node.Name} by its result (was {race.Node}, named by {race.By})");
                    race.Family = node.Family; race.Node = node.Name; race.By = "result";
                }
                race.Claimed = true;
                return;
            }
            // a loot list row counts only on a race (its token), so it never stands for a missed one; the chat's line can
            if (!fromList) _results.Add((t, node, raw));
        }
    }

    /// <summary>A Success race in the 15 s before <paramref name="t"/> that no result has claimed yet (the chat ran up to 12 s behind the row while gathering, 2026-10-24 09:58:38).</summary>
    Attempt? LateRace(DateTime t) => _attempts.FirstOrDefault(x => x.Ok && x.Race && !x.Claimed && (t - x.At).TotalSeconds is >= 0 and <= LateResultSeconds);

    /// <summary>A "… failed" chat line: one failed attempt, the same as the bar's Fail race within 3 s.</summary>
    void GatherFailLine(DateTime t, string skill)
    {
        lock (_tallyLock)
        {
            if (_attempts.LastOrDefault(x => !x.Ok && Math.Abs((t - x.At).TotalSeconds) <= FailSameSeconds) is { } f)
            { CrashLog.Note($"\"{skill} failed\": the same failed attempt as the {(f.Race ? "gathering bar's Fail race" : "failure line")} at {f.At.ToLocalTime():HH:mm:ss}"); return; }
            var (family, node, by) = NameAt(t);
            Tally(new Attempt { At = t, Ok = false, Family = family, Node = node, By = by }, $"the chat's \"{skill} failed\"");
        }
    }

    /// <summary>Settles chat results that waited long enough for a race: those still without one are gathers whose race wasn't seen.</summary>
    internal void GatherTick(DateTime now) { lock (_tallyLock) GatherTickLocked(now); }

    void GatherTickLocked(DateTime now)
    {
        for (int i = 0; i < _results.Count; i++)
        {
            var (at, node, raw) = _results[i];
            if ((now - at).TotalSeconds < ResultBeforeRaceSeconds) continue;
            _results.RemoveAt(i--);
            // any Success race just before it: that gather's line, read late or read again
            if (OkRaceNear(at, ResultSeconds, 0)) continue;
            if (LateRace(at) is { } late) { late.Claimed = true; continue; }
            // the same item counted from a missed race a moment ago: the same line
            if (_attempts.Any(x => x.Ok && !x.Race && x.Node == node.Name && Math.Abs((at - x.At).TotalSeconds) <= FailSameSeconds)) continue;
            // a gemstone beside the ore of a gather just counted: that gather's bonus, not another
            if (node.Name == GatheringCatalog.GemNode && _attempts.Any(x => x.Ok && Math.Abs((at - x.At).TotalSeconds) <= ResultSeconds)) continue;
            bool panelUp = (_panelItem.Item.Length > 0 && (at - _panelItem.At).TotalSeconds is >= -2 and <= PanelItemSeconds)
                || (at - _gatherPanelAt).TotalSeconds is >= -2 and <= PanelItemSeconds;
            bool gathering = panelUp || _attempts.Any(x => (at - x.At).TotalSeconds is >= 0 and <= GatheringUpSeconds)
                || (Target.Node != null && (at - Target.NodeAt).TotalSeconds is >= -2 and <= TargetNodeSeconds);
            if (!gathering) { CrashLog.Note($"gather: {node.Name} from the chat with no gathering panel, race or node around it: no attempt"); continue; }
            if (!PanelAgrees(node.Name, at)) { CrashLog.Note($"gather: {node.Name} from the chat while the gathering panel names {_panelItem.Item}: not this gather's, no attempt"); continue; }
            var name = NameAt(at);
            var a = new Attempt { At = at, Ok = true, Family = node.Family, Node = node.Name, By = "result", Claimed = true };
            if (name.By == "panel") { a.Family = name.Family; a.Node = name.Node; a.By = "panel"; }
            CrashLog.Note($"gather: race not seen, counted from the result ({raw})");
            Tally(a, "its result (the race went between passes)");
        }
    }

    /// <summary>
    /// Whether a gathering material at <paramref name="t"/> is what the gathering panel or its bar just produced: the panel
    /// named it lately, or a Success race no line has claimed is waiting. Then no achievement's or box's window takes it
    /// (Steven, 2026-10-24 15:21: an Odyle gathered 8 s after an achievement was filed as its reward).
    /// </summary>
    bool GatherJustProduced(long itemId, DateTime t)
    {
        if (!IsGatherMaterial(itemId)) return false;
        var name = _items.NameOf(itemId);
        if (!PanelAgrees(name, t)) return false;
        var b = GatherBase(name);
        bool panelNamed = _panelItem.Item.Length > 0 && (t - _panelItem.At).TotalSeconds is >= -2 and <= PanelItemSeconds && (b == _panelItem.Item || b.StartsWith(_panelItem.Item + " ") || _panelItem.Item.StartsWith(b + " "));
        return panelNamed || GatheringNow(itemId, t) || HasGatherToken(t);
    }

    /// <summary>
    /// The achievement's, box's or inventory's window doesn't take a gather the panel or its bar just produced: null then,
    /// with a note; else <paramref name="why"/>. The inventory's window runs 10 s past every read that shows one of its
    /// lines first seen 20 s ago or less, so it outlasts the visit: on 2026-10-25 Steven unlocked skins and stored items at
    /// 11:33:41 to :51, walked to a Kukuru node, and his first gather's "Acquired Kukuru x3." at 11:34:09, with the gathering
    /// panel naming Kukuru 2 s before, was filed as "the inventory at work" and never counted.
    /// </summary>
    string? NotForGather(string? why, long itemId, int count, DateTime t)
    {
        if (why == null || !(why == "an achievement's reward" || why.StartsWith("a box's", StringComparison.Ordinal) || why.StartsWith("the inventory at work", StringComparison.Ordinal)) || !GatherJustProduced(itemId, t)) return why;
        CrashLog.Note($"{_items.NameOf(itemId)} x{count}: the gathering panel or its bar just produced it, so a gather, not {why}");
        return null;
    }
}
