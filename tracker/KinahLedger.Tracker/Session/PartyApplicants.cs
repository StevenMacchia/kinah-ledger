using System.Text.Json;
using KinahLedger.Packets;

namespace KinahLedger.Session;

/// <summary>
/// The people asking to join the player's group (Steven, 2026-11-18: "the game only shows one at a time but sometimes u have
/// like 10 requests"): every application to the group the player leads, oldest first, newest at the bottom, as the decoder
/// tells them (<see cref="PartyApplication"/>). Only a group's leader is sent applications, so a list with anyone on it means
/// the player leads. An applicant leaves the list when they join, when the game takes them off, when every application goes
/// at once (the recruit closed, or the player joined another group), or, since no recording has yet shown the game's own
/// word for a decline, a withdrawal or an expiry, once <see cref="StaleAfter"/> has passed without them joining.
/// Touched from the packet thread (Take) and the window's (Pending): every call holds the lock.
/// </summary>
public sealed class PartyApplicants
{
    /// <summary>One pending application: who, what the game showed of them, and when it came (this PC's clock).</summary>
    public sealed record Applicant(string Key, string Name, string Class, int Level, int ItemLevel, long CombatPower, string Server, DateTime At);

    /// <summary>How long an applicant stays without the game saying anything more (the fallback for declines and expiries).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    readonly object _gate = new();
    readonly List<Applicant> _list = new();
    int _version;

    /// <summary>Changes each time the list changes: the window draws again only then (and each second for the "ago").</summary>
    public int Version { get { lock (_gate) return _version; } }

    /// <summary>The decoder's event: into the list, out of it, or the list emptied.</summary>
    public void Take(PartyApplication a)
    {
        lock (_gate)
        {
            switch (a.What)
            {
                case PartyApplication.Kind.Applied:
                    _list.RemoveAll(x => x.Key == a.Key);   // applying again: the newest at the bottom
                    _list.Add(new Applicant(a.Key, a.Name, a.Class, a.Level, a.ItemLevel, a.CombatPower, a.Server, a.At));
                    if (_list.Count > 64) _list.RemoveAt(0);
                    break;
                case PartyApplication.Kind.Accepted or PartyApplication.Kind.Removed:
                    if (_list.RemoveAll(x => x.Key == a.Key) == 0) return;
                    break;
                case PartyApplication.Kind.Cleared:
                    if (_list.Count == 0) return;
                    _list.Clear();
                    break;
                default: return;
            }
            _version++;
        }
    }

    /// <summary>The game is gone (or the reading stopped for good): nobody is waiting on this PC's answer.</summary>
    public void Clear() { lock (_gate) { if (_list.Count == 0) return; _list.Clear(); _version++; } }

    /// <summary>The applicants pending at <paramref name="now"/>, oldest first; the stale ones go.</summary>
    public IReadOnlyList<Applicant> Pending(DateTime now)
    {
        lock (_gate)
        {
            if (_list.RemoveAll(x => now - x.At >= StaleAfter) > 0) _version++;
            return _list.ToList();
        }
    }

    /// <summary>"12s", "3m", "1h": how long ago an application came, short (the window's right column).</summary>
    public static string Ago(DateTime at, DateTime now)
    {
        var s = Math.Max(0, (now - at).TotalSeconds);
        return s < 60 ? Ui.L.F("{0}s", (int)s) : s < 3600 ? Ui.L.F("{0}m", (int)(s / 60)) : Ui.L.F("{0}h", (int)(s / 3600));
    }
}

/// <summary>
/// The Applicants window's settings (applicants.json): shown or not (on by default), where it sits, and click-through (off by
/// default, as the timers' strip: it drags like the Kinah overlay).
/// </summary>
public sealed class ApplicantPrefs
{
    public bool On { get; set; } = true;
    public bool ClickThrough { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    [System.Text.Json.Serialization.JsonIgnore] public string? Path { get; private set; }

    public static string DefaultPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "applicants.json");

    public static ApplicantPrefs Load(string? path = null)
    {
        path ??= DefaultPath;
        ApplicantPrefs p = new();
        try { if (File.Exists(path)) p = JsonSerializer.Deserialize<ApplicantPrefs>(File.ReadAllText(path), Json) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { CrashLog.Note("applicants.json not read: " + ex.Message); }
        p.Path = path;
        return p;
    }

    /// <summary>Settings that are never saved (renders, self-tests).</summary>
    public static ApplicantPrefs Defaults() => new();

    public void Save()
    {
        if (Path == null) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, Path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Note("applicants.json not saved: " + ex.Message); }
    }
}
