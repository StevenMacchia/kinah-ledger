using System.Drawing;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// "Help improve counting" (Session/LabelWriter.cs), the session's side: each count a second witness confirmed becomes a
/// training pair. The witnesses are the ones the counting already uses:
///   kina-line  a chat Kina line the Kinah counter's rise confirmed (KinaWitness, SettleKina);
///   chat-item  a chat pickup line the loot list showed too (same item, same count, within 12 s);
///   loot-row   that loot list row (its text, icon hash, name and icon crops); a Kina row the counter confirmed; a rare
///              row a second read of its picture confirmed;
///   gather     a gathering material's row the gathering bar's Success race confirmed;
///   card-slot  an event card slot named by a sure icon whose grade the slot's backing agrees with.
/// The crops are cut while the readers' pictures are here, kept for a few seconds by their text, and only the ones a
/// written pair names are saved. Nothing here counts anything, and with the setting off nothing is kept.
/// </summary>
public sealed partial class TrackerSession
{
    LabelWriter Labels => LabelWriter.Shared;
    const double LabelMatchSeconds = 12, LabelKeepSeconds = 15;
    const int LabelKeepMax = 32;

    readonly object _lblLock = new();
    // the loot list's rows lately: the line (text, icon hash), the row as parsed, the item it settled to (0: Kina)
    readonly List<(DateTime At, LootLine Line, LootPopup.Row Row, long Id)> _lblRows = new();
    // chat pickups counted lately that no loot list row has confirmed yet
    readonly List<(DateTime At, string Raw, ItemAcquired Ev)> _lblChat = new();
    // crops lately, by their text: a loot row's name words and icon (with the raw OCR text before an icon renamed it), a
    // chat pickup line's item words
    readonly Dictionary<string, (DateTime At, string Raw, Bitmap? Name, Size NameSpan, Bitmap? Icon, Size IconSpan)> _lblListCrops = new();
    readonly Dictionary<string, (DateTime At, Bitmap Crop, Size Span)> _lblChatCrops = new();

    static string LabelKey(string text) => new string((text ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    Size LabelScreen => GameRect is { } g ? g.Size : Size.Empty;
    int LabelTextPx => (int)Math.Round(_textScale?.TextPx ?? 0);

    /// <summary>Hook in Commit: a counted pickup that a second witness stood behind becomes a pair.</summary>
    void LabelCommitted(GameEvent ev)
    {
        if (!Labels.Enabled) return;
        try
        {
            var now = ev.Time;
            switch (ev)
            {
                case ItemAcquired a when a.Via == "chat" || (a.Via.StartsWith("chat") && a.Via.Contains("confirmed by a second read")):
                {
                    var raw = ChatLineFor(a);
                    if (raw == null) return;
                    if (a.Via.Contains("confirmed by a second read")) { LabelChat(raw, a, null, "second read", now); return; }
                    lock (_lblLock)
                    {
                        PruneLabels(now);
                        var row = _lblRows.LastOrDefault(r => SameItem(r.Id, a.ItemId) && r.Row.Count == a.Count && !r.Row.IsKinah && Math.Abs((now - r.At).TotalSeconds) <= LabelMatchSeconds);
                        if (row.Row != null) { LabelChat(raw, a, row, "loot list", now); _lblRows.Remove(row); }
                        else _lblChat.Add((now, raw, a));
                    }
                    return;
                }
                case ItemAcquired a when a.Via == "loot list" && !_settings.Ocr.LootListAdds && IsGatherMaterial(a.ItemId):
                    LabelRow("gather", a.ItemId, a.Count, false, "gathering bar", now);
                    return;
                case ItemAcquired a when a.Via.StartsWith("loot list") && a.Via.Contains("confirmed by a second read"):
                    LabelRow("loot-row", a.ItemId, a.Count, false, "second read", now);
                    return;
                case KinahChanged k when !k.Bound && k.Delta > 0 && k.Via is "loot list · balance" or "loot list · counter":
                    LabelRow("loot-row", 0, k.Delta, true, "kinah counter", now);
                    return;
            }
        }
        catch (Exception ex) { CrashLog.Write("help improve counting", ex); }
    }

    /// <summary>Hook in SettleKina: a chat Kina line the counter's rise confirmed.</summary>
    void LabelKina(KinaWitness.Candidate c)
    {
        if (!Labels.Enabled || c.Line.Length == 0) return;
        try
        {
            lock (_lblLock)
            {
                var crops = new List<(string, Bitmap, CropKind, Size)>();
                if (_lblChatCrops.TryGetValue(LabelKey(c.Line), out var cc)) crops.Add(("name", cc.Crop, CropKind.ChatItem, cc.Span));
                Labels.Write("kina-line", c.Line, null, 0, "Kina", c.Amount, ["chat", "kinah counter"], LabelScreen, LabelTextPx, c.At, crops);
            }
        }
        catch (Exception ex) { CrashLog.Write("help improve counting", ex); }
    }

    /// <summary>Hook in ProcessList: this read's rows, kept a few seconds; a chat pickup waiting for its row gets it here.</summary>
    void LabelListRead(List<LootLine> lines, DateTime now)
    {
        if (!Labels.Enabled) { lock (_lblLock) { _lblRows.Clear(); _lblChat.Clear(); } return; }
        try
        {
            lock (_lblLock)
            {
                PruneLabels(now);
                foreach (var l in lines)
                {
                    if (LootPopup.Rows(new[] { (l.Top, l.Text) }).FirstOrDefault() is not { } row) continue;
                    long id = 0;
                    if (!row.IsKinah)
                    {
                        var m = ResolveName(row.Name);
                        if (m.Source == NameMatch.Refused || m.Id is not long rid) continue;
                        id = rid;
                    }
                    _lblRows.Add((now, l, row, id));
                }
                if (_lblRows.Count > LabelKeepMax * 4) _lblRows.RemoveRange(0, _lblRows.Count - LabelKeepMax * 4);
                foreach (var w in _lblChat.ToList())
                {
                    var row = _lblRows.LastOrDefault(r => SameItem(r.Id, w.Ev.ItemId) && r.Row.Count == w.Ev.Count && !r.Row.IsKinah && Math.Abs((r.At - w.At).TotalSeconds) <= LabelMatchSeconds);
                    if (row.Row == null) continue;
                    LabelChat(w.Raw, w.Ev, row, "loot list", now);
                    _lblChat.Remove(w); _lblRows.Remove(row);
                }
            }
        }
        catch (Exception ex) { CrashLog.Write("help improve counting", ex); }
    }

    /// <summary>Hook in LootMeasure: each item row's name words and icon square cut while the picture is here.</summary>
    void LabelListPicture(Bitmap raw, Point origin, List<OcrLineBox> boxes, List<(int Top, string Text)> lines, DateTime now)
    {
        if (!Labels.Enabled) return;
        try
        {
            for (int i = 0; i < boxes.Count && i < lines.Count; i++)
            {
                if (LootPopup.Rows(new[] { lines[i] }).Count == 0) continue;
                var span = boxes[i].Box; span.Offset(-origin.X, -origin.Y);
                int h = span.Height, side = (int)(h * 1.6), gap = (int)(h * 0.3);
                var icon = new Rectangle(span.Left - gap - side, span.Top + h / 2 - side / 2, side, side);
                var name = Crops.Cut(raw, CropKind.LootName, span);
                var ic = Crops.Cut(raw, CropKind.LootIcon, icon);
                lock (_lblLock)
                {
                    var key = LabelKey(lines[i].Text);
                    if (_lblListCrops.Remove(key, out var old)) { old.Name?.Dispose(); old.Icon?.Dispose(); }
                    _lblListCrops[key] = (now, boxes[i].Text, name, span.Size, ic, icon.Size);
                    PruneLabels(now);
                }
            }
        }
        catch (Exception ex) { CrashLog.Write("help improve counting", ex); }
    }

    /// <summary>Hook beside the decoder's chat shadow: each own pickup line's item words cut while the capture is here (never the line, never a sender).</summary>
    void LabelChatSpans(Bitmap raw, IReadOnlyList<(string Line, string Name, Rectangle Box)> spans)
    {
        if (!Labels.Enabled) return;
        try
        {
            var now = DateTime.UtcNow;
            foreach (var s in spans)
            {
                if (LabelWriter.SafeText("chat-item", s.Line) == null) continue;
                if (Crops.Cut(raw, CropKind.ChatItem, s.Box) is not { } crop) continue;
                lock (_lblLock)
                {
                    var key = LabelKey(s.Line);
                    if (_lblChatCrops.Remove(key, out var old)) old.Crop.Dispose();
                    _lblChatCrops[key] = (now, crop, s.Box.Size);
                    PruneLabels(now);
                }
            }
        }
        catch (Exception ex) { CrashLog.Write("help improve counting", ex); }
    }

    /// <summary>Hook before event cards are filed: each slot named by a sure icon its backing grade agrees with.</summary>
    void LabelCards(EventCards.Merger m, List<(int Card, bool Received, List<RewardGrid.Slot> Slots, bool More)> report, DateTime now)
    {
        if (!Labels.Enabled) return;
        try
        {
            foreach (var (card, received, slots, _) in report)
            {
                if (!received) continue;
                for (int at = 0; at < slots.Count; at++)
                {
                    var sl = slots[at];
                    if (sl.Count <= 0 || sl.Icons.Count == 0 || sl.Icons[0].Distance > GridSureDistance) continue;
                    if (SlotName(_items, sl) is not { } name || name.Equals("Kina", StringComparison.OrdinalIgnoreCase)) continue;
                    long id = _items.IdOf(name);
                    if (id <= 0) continue;
                    var crops = new List<(string, Bitmap, CropKind, Size)>();
                    if (!sl.Box.IsEmpty && m.CropAt(card, at) is { } c) crops.Add(("slot", c, CropKind.CardSlot, sl.Box.Size));
                    var text = ((sl.Grade ?? "") + " " + (string.IsNullOrEmpty(sl.CountText) ? "" : "x" + sl.CountText)).Trim();
                    Labels.Write("card-slot", text, sl.Icons[0].Key, id, name, sl.Count, ["slot icon", "backing grade"], LabelScreen, LabelTextPx, now, crops);
                }
            }
        }
        catch (Exception ex) { CrashLog.Write("help improve counting", ex); }
    }

    /// <summary>The same item by name: the list holds a few names twice under different ids (LootCrossCheck.ItemKey).</summary>
    bool SameItem(long a, long b) => a == b || (a > 0 && b > 0 && string.Equals(_items.NameOf(a), _items.NameOf(b), StringComparison.OrdinalIgnoreCase));

    /// <summary>The chat line this pickup came from, in the read it was counted from.</summary>
    string? ChatLineFor(ItemAcquired a)
    {
        var key = "i:" + _items.NameOf(a.ItemId).ToLowerInvariant() + "x" + a.Count;
        return _lastLines.LastOrDefault(l => _parser.Meaning(l).Key == key);
    }

    /// <summary>A chat pickup and, when the loot list was its witness, that row: two pairs. Called under _lblLock.</summary>
    void LabelChat(string raw, ItemAcquired a, (DateTime At, LootLine Line, LootPopup.Row Row, long Id)? row, string witness, DateTime now)
    {
        var name = _items.NameOf(a.ItemId);
        var chatCrops = new List<(string, Bitmap, CropKind, Size)>();
        if (_lblChatCrops.TryGetValue(LabelKey(raw), out var cc)) chatCrops.Add(("name", cc.Crop, CropKind.ChatItem, cc.Span));
        Labels.Write("chat-item", raw, null, a.ItemId, name, a.Count, ["chat", witness], LabelScreen, LabelTextPx, now, chatCrops);
        if (row is { } r) WriteRow("loot-row", r, a.ItemId, name, a.Count, "chat", now);
    }

    /// <summary>The latest row of this item (or Kina amount) in the last few seconds, as a pair.</summary>
    void LabelRow(string kind, long id, long qty, bool kina, string witness, DateTime now)
    {
        lock (_lblLock)
        {
            PruneLabels(now);
            var row = _lblRows.LastOrDefault(r => kina ? r.Row.IsKinah && r.Row.Amount == qty : !r.Row.IsKinah && SameItem(r.Id, id) && (kind == "gather" || r.Row.Count == qty));
            if (row.Row == null) return;
            WriteRow(kind, row, id, kina ? "Kina" : _items.NameOf(id), qty, witness, now);
            _lblRows.Remove(row);
        }
    }

    /// <summary>One loot list row as a pair, with its name and icon crops when they were cut. Called under _lblLock.</summary>
    void WriteRow(string kind, (DateTime At, LootLine Line, LootPopup.Row Row, long Id) row, long id, string name, long qty, string witness, DateTime now)
    {
        var crops = new List<(string, Bitmap, CropKind, Size)>();
        string text = row.Line.Text;
        if (_lblListCrops.TryGetValue(LabelKey(row.Line.Text), out var lc))
        {
            if (lc.Raw.Length > 0) text = lc.Raw;   // what OCR read, before an icon renamed the row
            if (lc.Name != null) crops.Add(("name", lc.Name, CropKind.LootName, lc.NameSpan));
            if (lc.Icon != null) crops.Add(("icon", lc.Icon, CropKind.LootIcon, lc.IconSpan));
        }
        Labels.Write(kind, text, row.Line.Icon != 0 ? row.Line.Icon.ToString("x16") : null, id, name, qty, ["loot list", witness], LabelScreen, LabelTextPx, now, crops);
    }

    /// <summary>Lets go of rows, waiting chat lines and crops older than a few seconds (and the oldest past the cap). Called under _lblLock.</summary>
    void PruneLabels(DateTime now)
    {
        _lblRows.RemoveAll(r => Math.Abs((now - r.At).TotalSeconds) > LabelKeepSeconds);
        _lblChat.RemoveAll(c => Math.Abs((now - c.At).TotalSeconds) > LabelKeepSeconds);
        foreach (var k in _lblListCrops.Where(kv => Math.Abs((now - kv.Value.At).TotalSeconds) > LabelKeepSeconds).Select(kv => kv.Key).ToList())
            if (_lblListCrops.Remove(k, out var v)) { v.Name?.Dispose(); v.Icon?.Dispose(); }
        foreach (var k in _lblChatCrops.Where(kv => Math.Abs((now - kv.Value.At).TotalSeconds) > LabelKeepSeconds).Select(kv => kv.Key).ToList())
            if (_lblChatCrops.Remove(k, out var v)) v.Crop.Dispose();
        while (_lblListCrops.Count > LabelKeepMax) { var k = _lblListCrops.MinBy(kv => kv.Value.At).Key; if (_lblListCrops.Remove(k, out var v)) { v.Name?.Dispose(); v.Icon?.Dispose(); } }
        while (_lblChatCrops.Count > LabelKeepMax) { var k = _lblChatCrops.MinBy(kv => kv.Value.At).Key; if (_lblChatCrops.Remove(k, out var v)) v.Crop.Dispose(); }
    }
}
