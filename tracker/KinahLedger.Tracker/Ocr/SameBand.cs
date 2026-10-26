using System.Drawing;

namespace KinahLedger.Ocr;

/// <summary>
/// The loot list's unchanged check (ocr.list_skip_same, 2026-10-26, branch tracker/pass-cost): the chat's way
/// (ScreenReader.ReadCaptureAsync, ocr.chat_skip_same) for the list. A capture whose boosted pixels (the picture the
/// recognizer is handed), place and size are exactly the last recognized read's stands on that read's lines: the
/// recognizer would say the same again. It is recognized anyway once that read is <see cref="ForceSeconds"/> old, and
/// after another reader saw the screen change since it (the chat's lines, the target, the Kinah counter): a row lives
/// about 2.5 s (LootRows.RowSeconds), so a row that fades in slowly or a count that changes in place is still read in
/// its life even if a fingerprint ever matched wrongly. Only the recognition is skipped: the caller measures every row
/// (its place, contrast, icon hash) on the new capture, and every read still goes through LootRows as before.
/// <typeparamref name="T"/> is a read's line: the live list keeps OcrLineBox, the replay bank's text cases LootLine.
/// </summary>
public sealed class SameBand<T>
{
    /// <summary>A recognized read stands for at most this long; the next capture after it is recognized again.</summary>
    public const double ForceSeconds = 2.0;

    ulong _hash; Point _origin; Size _size; double _scale; DateTime _at = DateTime.MinValue; List<T>? _lines;

    /// <summary>Captures asked about, and those of them that stood on the last read, since the start (the audit line).</summary>
    public int Reads { get; private set; }
    public int Skips { get; private set; }

    /// <summary>
    /// A copy of the last recognized read's lines when this capture (<paramref name="hash"/> of its boosted pixels, at
    /// <paramref name="origin"/>, <paramref name="size"/>, read at <paramref name="scale"/>) can stand on them; null when it
    /// must be recognized. <paramref name="changedAt"/>: when another reader last saw the screen change.
    /// </summary>
    public List<T>? Same(ulong hash, Point origin, Size size, double scale, DateTime now, DateTime changedAt)
    {
        Reads++;
        if (_lines == null || hash != _hash || origin != _origin || size != _size || scale != _scale) return null;
        double age = (now - _at).TotalSeconds;
        if (age < 0 || age >= ForceSeconds || changedAt > _at) return null;
        Skips++;
        return new List<T>(_lines);
    }

    /// <summary>A capture was recognized: its fingerprint and lines are what the next ones are checked against.</summary>
    public void Recognized(ulong hash, Point origin, Size size, double scale, DateTime now, IEnumerable<T> lines)
    {
        _hash = hash; _origin = origin; _size = size; _scale = scale; _at = now; _lines = new List<T>(lines);
    }

    /// <summary>Nothing to stand on: the next capture is recognized (the switch turned off, the list not read).</summary>
    public void Forget() => _lines = null;
}
