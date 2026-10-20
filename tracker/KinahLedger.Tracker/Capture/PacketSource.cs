using System.Net;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace KinahLedger.Capture;

public enum Direction : byte { ClientToServer = 0, ServerToClient = 1 }

public sealed record TcpSegment(DateTime Time, Direction Dir, uint Seq, byte[] Payload, string FlowKey);

/// <summary>
/// Passive capture of the game's TCP flows through Npcap. The BPF filter is built from the exact
/// server addresses the client is connected to, so nothing else on the machine is read.
/// </summary>
public sealed class PacketSource : IDisposable
{
    readonly LibPcapLiveDevice _dev;
    readonly HashSet<(IPAddress, int)> _remotes;

    public event Action<TcpSegment>? Segment;
    public string DeviceName => _dev.Description ?? _dev.Name;
    public long Packets { get; private set; }

    public static IReadOnlyList<string> Devices() =>
        LibPcapLiveDeviceList.Instance
            .Select(d => $"{d.Name}  {d.Description}  [{string.Join(", ", d.Addresses.Select(a => a.Addr?.ipAddress?.ToString()).Where(x => x != null))}]")
            .ToList();

    public PacketSource(IReadOnlyList<GameConnection> conns, string? deviceName)
    {
        if (conns.Count == 0) throw new ArgumentException("no connections to follow");
        _remotes = conns.Select(c => (c.Remote.Address, c.Remote.Port)).ToHashSet();

        var devs = LibPcapLiveDeviceList.Instance;
        if (devs.Count == 0) throw new InvalidOperationException("No capture adapters found. Install Npcap (https://npcap.com) and start the tracker again.");
        var locals = conns.Select(c => c.Local.Address).ToHashSet();

        _dev = deviceName != null
            ? devs.FirstOrDefault(d => d.Name.Contains(deviceName, StringComparison.OrdinalIgnoreCase) || (d.Description ?? "").Contains(deviceName, StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException($"no adapter matches '{deviceName}'; see `devices`")
            : devs.FirstOrDefault(d => d.Addresses.Any(a => a.Addr?.ipAddress != null && locals.Contains(a.Addr.ipAddress)))
              ?? devs.First(d => !d.Loopback);

        _dev.Open(new DeviceConfiguration { Mode = DeviceModes.None, ReadTimeout = 250, BufferSize = 16 * 1024 * 1024, Snaplen = 65535 });
        _dev.Filter = "tcp and (" + string.Join(" or ", _remotes.Select(r => $"(host {r.Item1} and port {r.Item2})")) + ")";
        _dev.OnPacketArrival += OnPacket;
    }

    public void Start() => _dev.StartCapture();
    public void Stop() { try { _dev.StopCapture(); } catch { /* already stopped */ } }

    void OnPacket(object sender, PacketCapture e)
    {
        var raw = e.GetPacket();
        var pkt = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
        var tcp = pkt.Extract<TcpPacket>();
        if (tcp?.PayloadData is not { Length: > 0 } data) return;
        if (tcp.ParentPacket is not IPPacket ip) return;
        Packets++;

        bool fromServer = _remotes.Contains((ip.SourceAddress, tcp.SourcePort));
        var dir = fromServer ? Direction.ServerToClient : Direction.ClientToServer;
        // one flow key per direction: TCP sequence numbers are independent each way
        var key = fromServer
            ? $"{ip.DestinationAddress}:{tcp.DestinationPort}<{ip.SourceAddress}:{tcp.SourcePort}"
            : $"{ip.SourceAddress}:{tcp.SourcePort}>{ip.DestinationAddress}:{tcp.DestinationPort}";
        Segment?.Invoke(new TcpSegment(raw.Timeval.Date, dir, tcp.SequenceNumber, data, key));
    }

    public void Dispose() { Stop(); try { _dev.Close(); } catch { /* ignore */ } }
}
