using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace KinahLedger.Packets;

/// <summary>
/// Where the copies of the game's packets come from (Steven, 2026-11-13: "all of the other apps do packet reading like the
/// parsers"), in the order the meters use: Npcap when it is installed (no admin needed), Windows' raw sockets when the
/// tracker runs as administrator, otherwise none and the screen reading carries on alone. Both only listen: a copy of what
/// passes the network card, filtered to the game's server connection. Nothing is sent, and the game is never touched.
/// </summary>
public interface IPacketSource : IDisposable
{
    /// <summary>"Npcap" or "raw socket", for the status line.</summary>
    string Kind { get; }
    /// <summary>Blocks for the next IPv4 packet (header included) or a short timeout; null on a timeout, throws when the source failed.</summary>
    (DateTime At, byte[] Ip)? Next();
}

public static class PacketSources
{
    public static bool IsAdmin()
    {
        try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        catch (Exception) { return false; }
    }

    /// <summary>Npcap's folder: its wpcap.dll is there when it is installed (WinPcap-compatible mode puts another copy in System32).</summary>
    public static string NpcapDir => Path.Combine(Environment.SystemDirectory, "Npcap");
    public static bool NpcapInstalled => File.Exists(Path.Combine(NpcapDir, "wpcap.dll"));

    /// <summary>
    /// A source for the game's server connection, or null with the reason ("Npcap isn't installed..."): Npcap first, then a
    /// raw socket when running as administrator.
    /// </summary>
    public static IPacketSource? Open(TcpTable.Link game, out string why)
    {
        var tried = new List<string>();
        if (NpcapInstalled)
        {
            try { why = ""; return new NpcapSource(game); }
            catch (Exception ex) { tried.Add("Npcap: " + ex.Message); }
        }
        else tried.Add("Npcap isn't installed");
        if (IsAdmin())
        {
            try { why = ""; return new RawSocketSource(game); }
            catch (Exception ex) { tried.Add("raw socket: " + ex.Message); }
        }
        else tried.Add("the tracker isn't running as administrator");
        why = string.Join("; ", tried);
        return null;
    }
}

/// <summary>
/// Npcap through its wpcap.dll (the libpcap API), loaded from Npcap's own folder at run time: nothing of Npcap ships with
/// the tracker, so a PC without it simply has no packet reading. Opens the network card the game's connection uses, with a
/// capture filter for that connection only.
/// </summary>
public sealed unsafe class NpcapSource : IPacketSource
{
    public string Kind => "Npcap";

    static IntPtr _lib;
    static delegate* unmanaged[Cdecl]<IntPtr*, byte*, int> _findAllDevs;
    static delegate* unmanaged[Cdecl]<IntPtr, void> _freeAllDevs;
    static delegate* unmanaged[Cdecl]<byte*, int, int, int, byte*, IntPtr> _openLive;
    static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int, uint, int> _compile;
    static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int> _setFilter;
    static delegate* unmanaged[Cdecl]<IntPtr, void> _freeCode;
    static delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, IntPtr*, int> _nextEx;
    static delegate* unmanaged[Cdecl]<IntPtr, int> _dataLink;
    static delegate* unmanaged[Cdecl]<IntPtr, void> _close;
    static delegate* unmanaged[Cdecl]<IntPtr, IntPtr> _getErr;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetDllDirectory(string? path);

    static void Load()
    {
        if (_lib != IntPtr.Zero) return;
        // wpcap.dll loads Packet.dll beside it: Npcap's folder joins the search while it loads, then the search is as before
        SetDllDirectory(PacketSources.NpcapDir);
        try { _lib = NativeLibrary.Load(Path.Combine(PacketSources.NpcapDir, "wpcap.dll")); }
        finally { SetDllDirectory(null); }
        IntPtr F(string name) => NativeLibrary.GetExport(_lib, name);
        _findAllDevs = (delegate* unmanaged[Cdecl]<IntPtr*, byte*, int>)F("pcap_findalldevs");
        _freeAllDevs = (delegate* unmanaged[Cdecl]<IntPtr, void>)F("pcap_freealldevs");
        _openLive = (delegate* unmanaged[Cdecl]<byte*, int, int, int, byte*, IntPtr>)F("pcap_open_live");
        _compile = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int, uint, int>)F("pcap_compile");
        _setFilter = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)F("pcap_setfilter");
        _freeCode = (delegate* unmanaged[Cdecl]<IntPtr, void>)F("pcap_freecode");
        _nextEx = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, IntPtr*, int>)F("pcap_next_ex");
        _dataLink = (delegate* unmanaged[Cdecl]<IntPtr, int>)F("pcap_datalink");
        _close = (delegate* unmanaged[Cdecl]<IntPtr, void>)F("pcap_close");
        _getErr = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)F("pcap_geterr");
    }

    IntPtr _pcap;
    readonly int _link;

    /// <summary>The capture's link type (1 = Ethernet), as a capture file's interface block keeps it.</summary>
    public int LinkType => _link;
    /// <summary>The most of each frame kept (all of it, for the game's packets).</summary>
    public const int SnapLen = 65535;

    public NpcapSource(TcpTable.Link game)
        // only the game's connection: the rest of this PC's traffic is never handed to the tracker
        : this(game.Local, $"tcp and host {game.Remote} and port {game.RemotePort}") { }

    /// <summary>
    /// The network card with <paramref name="local"/> among its addresses, with a capture filter (pcap's filter language):
    /// packet-record opens it on the game's server ports, any server, so a move to an event or Abyss server is recorded too.
    /// </summary>
    public NpcapSource(IPAddress local, string filter)
    {
        Load();
        var device = DeviceFor(local) ?? throw new InvalidOperationException($"no network card has the address {local}");
        var err = stackalloc byte[512];
        var dev = Encoding.ASCII.GetBytes(device + "\0");
        fixed (byte* d = dev) _pcap = _openLive(d, SnapLen, 0, 250, err);
        if (_pcap == IntPtr.Zero) throw new InvalidOperationException(Marshal.PtrToStringAnsi((IntPtr)err) ?? "couldn't open the network card");
        _link = _dataLink(_pcap);
        try { Filter(filter); }
        catch { Dispose(); throw; }
    }

    /// <summary>Replaces the capture filter on the open card (packet-record widens it when the game moves to another port).</summary>
    public void Filter(string filter)
    {
        var f8 = Encoding.ASCII.GetBytes(filter + "\0");
        var prog = Marshal.AllocHGlobal(16);   // struct bpf_program { u_int bf_len; struct bpf_insn *bf_insns; }
        try
        {
            int rc;
            fixed (byte* f = f8) rc = _compile(_pcap, prog, f, 1, 0xFFFFFFFF);
            if (rc != 0) throw new InvalidOperationException("capture filter: " + Error());
            rc = _setFilter(_pcap, prog);
            _freeCode(prog);
            if (rc != 0) throw new InvalidOperationException("capture filter: " + Error());
        }
        finally { Marshal.FreeHGlobal(prog); }
    }

    string Error() => _pcap == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(_getErr(_pcap)) ?? "";

    /// <summary>The Npcap device ("\Device\NPF_{...}") that has <paramref name="local"/> among its addresses.</summary>
    static string? DeviceFor(IPAddress local)
    {
        IntPtr all;
        var err = stackalloc byte[512];
        if (_findAllDevs(&all, err) != 0 || all == IntPtr.Zero) return null;
        try
        {
            var want = local.GetAddressBytes();
            // pcap_if { next, name, description, addresses, flags }; pcap_addr { next, addr, netmask, broadaddr, dstaddr }
            for (var d = all; d != IntPtr.Zero; d = Marshal.ReadIntPtr(d))
            {
                for (var a = Marshal.ReadIntPtr(d, 24); a != IntPtr.Zero; a = Marshal.ReadIntPtr(a))
                {
                    var sa = Marshal.ReadIntPtr(a, 8);
                    if (sa == IntPtr.Zero || Marshal.ReadInt16(sa) != 2) continue;   // AF_INET
                    var ip = new byte[4];
                    Marshal.Copy(sa + 4, ip, 0, 4);
                    if (ip.AsSpan().SequenceEqual(want)) return Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(d, 8));
                }
            }
            return null;
        }
        finally { _freeAllDevs(all); }
    }

    public (DateTime At, byte[] Ip)? Next() =>
        NextFrame() is { } f && IpOf(f.Frame, _link) is { } ip ? (f.At, ip) : null;

    /// <summary>
    /// Blocks for the next whole frame as the card handed it (link header included, see <see cref="LinkType"/>) or a short
    /// timeout: null on a timeout. <c>Length</c> is the frame's length on the wire, which is more than the bytes kept only
    /// past <see cref="SnapLen"/>.
    /// </summary>
    public (DateTime At, byte[] Frame, int Length)? NextFrame()
    {
        if (_pcap == IntPtr.Zero) throw new ObjectDisposedException(nameof(NpcapSource));
        IntPtr hdr, data;
        int rc = _nextEx(_pcap, &hdr, &data);
        if (rc == 0) return null;
        if (rc < 0) throw new InvalidOperationException("Npcap: " + Error());
        // struct pcap_pkthdr on Windows: timeval { long sec; long usec } (4 bytes each), caplen, len
        int sec = Marshal.ReadInt32(hdr), usec = Marshal.ReadInt32(hdr, 4), cap = Marshal.ReadInt32(hdr, 8), len = Marshal.ReadInt32(hdr, 12);
        var at = DateTime.UnixEpoch.AddSeconds((uint)sec).AddTicks(usec * 10L);
        return (at, new ReadOnlySpan<byte>((void*)data, cap).ToArray(), Math.Max(len, cap));
    }

    /// <summary>The IPv4 packet inside a captured frame: Ethernet (with VLAN tags), loopback or raw IP.</summary>
    internal static byte[]? IpOf(ReadOnlySpan<byte> f, int link)
    {
        int o;
        switch (link)
        {
            case 1:   // Ethernet
                if (f.Length < 14) return null;
                o = 12;
                int et = (f[o] << 8) | f[o + 1];
                o += 2;
                while (et is 0x8100 or 0x88A8 && f.Length >= o + 4) { et = (f[o + 2] << 8) | f[o + 3]; o += 4; }
                if (et != 0x0800) return null;
                break;
            case 0: case 108: o = 4; break;   // loopback: a 4-byte family header
            case 12: case 14: case 101: case 228: o = 0; break;   // raw IP
            default: return null;
        }
        return f.Length > o && f[o] >> 4 == 4 ? f[o..].ToArray() : null;
    }

    public void Dispose()
    {
        if (_pcap != IntPtr.Zero) { _close(_pcap); _pcap = IntPtr.Zero; }
    }
}

/// <summary>
/// Windows' raw sockets with receive-all (SIO_RCVALL): every IPv4 packet through the card with the game's local address,
/// kept here only when it belongs to the game's connection. Needs administrator, as every meter that offers it says.
/// </summary>
public sealed class RawSocketSource : IPacketSource
{
    public string Kind => "raw socket";
    readonly Socket _s;
    readonly byte[] _buf = new byte[65536];

    public RawSocketSource(TcpTable.Link game)
    {
        _s = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP) { ReceiveTimeout = 250 };
        try
        {
            _s.Bind(new IPEndPoint(game.Local, 0));
            _s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
            _s.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), new byte[4]);
        }
        catch { _s.Dispose(); throw; }
    }

    public (DateTime At, byte[] Ip)? Next()
    {
        try
        {
            int n = _s.Receive(_buf);
            return n > 20 ? (DateTime.UtcNow, _buf.AsSpan(0, n).ToArray()) : null;
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut) { return null; }
    }

    public void Dispose() => _s.Dispose();
}
