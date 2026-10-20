using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace KinahLedger.Capture;

public sealed record GameConnection(int Pid, string Process, IPEndPoint Local, IPEndPoint Remote);

/// <summary>
/// Finds the game's live server connections from Windows' own TCP table (the same list `netstat -o` prints).
/// Nothing here touches the game process; it only reads which remote addresses it is talking to,
/// so the capture filter can be limited to exactly those flows.
/// </summary>
public static class GameConnectionFinder
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    const int AF_INET = 2, TCP_TABLE_OWNER_PID_ALL = 5, MIB_TCP_STATE_ESTAB = 5;

    [StructLayout(LayoutKind.Sequential)]
    struct MIB_TCPROW_OWNER_PID { public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort, OwningPid; }

    public static List<GameConnection> Find(IEnumerable<string> processHints)
    {
        var hints = processHints.Select(h => h.ToLowerInvariant()).ToArray();
        var names = new Dictionary<int, string>();
        foreach (var p in Process.GetProcesses()) { try { names[p.Id] = p.ProcessName; } catch { /* exited */ } }

        var result = new List<GameConnection>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0) return result;
            int count = Marshal.ReadInt32(buf);
            var row = buf + 4;
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (int i = 0; i < count; i++, row += rowSize)
            {
                var r = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(row);
                if (r.State != MIB_TCP_STATE_ESTAB) continue;
                if (!names.TryGetValue((int)r.OwningPid, out var name)) continue;
                var lower = name.ToLowerInvariant();
                if (!hints.Any(h => lower.Contains(h))) continue;
                var remote = new IPEndPoint(new IPAddress(r.RemoteAddr), Port(r.RemotePort));
                if (IPAddress.IsLoopback(remote.Address)) continue;
                result.Add(new GameConnection((int)r.OwningPid, name, new IPEndPoint(new IPAddress(r.LocalAddr), Port(r.LocalPort)), remote));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return result;
    }

    // ports in the table are stored in network byte order inside the low 16 bits
    static int Port(uint p) => (int)(((p & 0xFF) << 8) | ((p >> 8) & 0xFF));
}
