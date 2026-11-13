using System.Net;
using System.Runtime.InteropServices;

namespace KinahLedger.Packets;

/// <summary>
/// Windows' table of TCP connections with the process that owns each (iphlpapi GetExtendedTcpTable): how the packet reader
/// finds the game's server connection from the game's process id, with nothing sent anywhere. IPv4 only, as the game uses.
/// </summary>
public static class TcpTable
{
    public sealed record Link(IPAddress Local, int LocalPort, IPAddress Remote, int RemotePort, uint Pid);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int ipVersion, int tableClass, uint reserved);

    const int AfInet = 2, TcpTableOwnerPidAll = 5, Established = 5;

    /// <summary>Established IPv4 connections owned by one of <paramref name="pids"/>.</summary>
    public static List<Link> Of(IReadOnlyCollection<uint> pids)
    {
        var list = new List<Link>();
        if (pids.Count == 0) return list;
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
        for (int attempt = 0; attempt < 3 && size > 0; attempt++)
        {
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                uint err = GetExtendedTcpTable(buf, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
                if (err == 122) continue;   // the table grew between the two calls: ask again with the new size
                if (err != 0) return list;
                int n = Marshal.ReadInt32(buf);
                for (int i = 0; i < n; i++)
                {
                    var row = buf + 4 + i * 24;   // MIB_TCPROW_OWNER_PID: state, local addr, local port, remote addr, remote port, pid
                    if (Marshal.ReadInt32(row) != Established) continue;
                    uint pid = (uint)Marshal.ReadInt32(row + 20);
                    if (!pids.Contains(pid)) continue;
                    list.Add(new Link(new IPAddress((uint)Marshal.ReadInt32(row + 4)), Port(Marshal.ReadInt32(row + 8)),
                                      new IPAddress((uint)Marshal.ReadInt32(row + 12)), Port(Marshal.ReadInt32(row + 16)), pid));
                }
                return list;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return list;
    }

    /// <summary>A port as the table holds it: network byte order in the low two bytes.</summary>
    static int Port(int raw) => ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);

    /// <summary>
    /// The game's own server connection among its links: not the web (443, 80: the launcher, telemetry and the like, all
    /// TLS) and not this PC. On 2026-11-13 AION 2 had one, to port 13328.
    /// </summary>
    public static Link? GameServer(IEnumerable<Link> links) =>
        links.Where(l => l.RemotePort is not (443 or 80) && !IPAddress.IsLoopback(l.Remote) && !l.Remote.Equals(IPAddress.Any))
             .OrderBy(l => l.RemotePort).FirstOrDefault();
}
