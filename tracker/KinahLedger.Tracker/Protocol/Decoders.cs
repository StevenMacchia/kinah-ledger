using System.Text.Json;
using System.Text.Json.Serialization;
using KinahLedger.Capture;

namespace KinahLedger.Protocol;

public interface IDecoder
{
    bool TryDecode(Frame f, out GameEvent? ev);
}

/// <summary>Where a field sits inside a message body. Offsets count from the first byte after the length and opcode.</summary>
public sealed class FieldSpec
{
    public int Offset { get; set; }
    public string Type { get; set; } = "u32";   // u8 bool u16 i16 u32 i32 u64 i64
    public bool LittleEndian { get; set; } = true;
}

public sealed class OpcodeSpec
{
    public string Kind { get; set; } = "";                   // ItemAcquired | KinahChanged | MobKilled | ZoneChanged
    public string Opcode { get; set; } = "";                 // "0x1A2B" or decimal
    public string Direction { get; set; } = "ServerToClient";
    public string Note { get; set; } = "";
    public Dictionary<string, FieldSpec> Fields { get; set; } = new();
    /// <summary>Values implied by the opcode itself, e.g. {"bound": 1} for a bound-kinah message.</summary>
    public Dictionary<string, long> Constants { get; set; } = new();

    [JsonIgnore]
    public int OpcodeValue => Convert.ToInt32(Opcode, Opcode.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
}

/// <summary>opcodes.json: the message layouts, kept as data so a game patch means editing a file, not rebuilding.</summary>
public sealed class OpcodeTable
{
    public string? Doc { get; set; }
    public string GameVersion { get; set; } = "";
    public List<OpcodeSpec> Opcodes { get; set; } = new();

    [JsonIgnore] public bool IsEmpty => Opcodes.Count == 0;

    public static OpcodeTable Load(string path)
    {
        var p = Settings.Resolve(path);
        if (!File.Exists(p)) return new OpcodeTable();
        return JsonSerializer.Deserialize<OpcodeTable>(File.ReadAllText(p), Settings.JsonOpts) ?? new OpcodeTable();
    }
}

/// <summary>Data-driven decoder: looks the frame's opcode up in the table and reads the fields it lists.</summary>
public sealed class TableDecoder : IDecoder
{
    readonly Dictionary<(Direction, int), OpcodeSpec> _map = new();

    public TableDecoder(OpcodeTable t)
    {
        foreach (var o in t.Opcodes)
            _map[(Enum.Parse<Direction>(o.Direction, true), o.OpcodeValue)] = o;
    }

    public bool TryDecode(Frame f, out GameEvent? ev)
    {
        ev = null;
        if (!_map.TryGetValue((f.Dir, f.Opcode), out var spec)) return false;

        long G(string name, long dflt = 0) =>
            spec.Constants.TryGetValue(name, out var c) ? c
            : spec.Fields.TryGetValue(name, out var fs) ? Read(f.Body.Span, fs)
            : dflt;

        ev = spec.Kind switch
        {
            "ItemAcquired" => new ItemAcquired(f.Time, G("itemId"), (int)G("count", 1), G("bound") != 0),
            "KinahChanged" => new KinahChanged(f.Time, G("delta"), G("bound") != 0),
            "MobKilled"    => new MobKilled(f.Time, G("mobId")),
            "ZoneChanged"  => new ZoneChanged(f.Time, G("zoneId"), spec.Fields.ContainsKey("instanceId") ? G("instanceId") : (long?)null),
            _ => null
        };
        return ev != null;
    }

    static long Read(ReadOnlySpan<byte> b, FieldSpec s)
    {
        int n = s.Type switch
        {
            "u8" or "bool" => 1,
            "u16" or "i16" => 2,
            "u64" or "i64" => 8,
            _ => 4
        };
        if (s.Offset < 0 || s.Offset + n > b.Length) return 0;
        ulong v = 0;
        for (int i = 0; i < n; i++) v |= (ulong)b[s.Offset + (s.LittleEndian ? i : n - 1 - i)] << (8 * i);
        return s.Type switch
        {
            "i16" => (short)v,
            "i32" => (int)v,
            "i64" => (long)v,
            _ => (long)v
        };
    }
}
