using System.Text;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>Plain console readout for the `watch` command, redrawn once a second. The window app shows the same numbers with a panel over the game.</summary>
public sealed class ConsoleView
{
    readonly SessionEngine _s;
    readonly Func<string> _activity;
    readonly Func<string> _status;

    public ConsoleView(SessionEngine s, Func<string> activity, Func<string> status)
    {
        _s = s; _activity = activity; _status = status;
    }

    public void Render()
    {
        double h = _s.Hours;
        var sb = new StringBuilder();
        sb.AppendLine($"Kinah Ledger   {_activity(),-30} {_s.Elapsed:hh\\:mm\\:ss}   {_status()}");
        sb.AppendLine(new string('-', 76));
        sb.AppendLine($"Kinah/hr    {Fk(_s.Kinah / h),12}   bound/hr {Fk(_s.KinahBound / h),12}   kills/hr {_s.Kills / h,8:0}");
        sb.AppendLine($"Kinah total {Fk(_s.Kinah),12}   bound    {Fk(_s.KinahBound),12}   kills    {_s.Kills,8}");
        sb.AppendLine();
        sb.AppendLine($"{"Item",-42}{"Count",8}{"Bound",8}{"Per hr",10}");
        foreach (var r in _s.ItemRows().Take(18))
            sb.AppendLine($"{Trunc(r.Name, 41),-42}{r.Count,8}{r.Bound,8}{(r.Count + r.Bound) / h,10:0.0}");
        if (_s.Events == 0) sb.AppendLine("  no decoded events yet");
        sb.AppendLine();
        sb.AppendLine("Ctrl+C to stop");
        try { Console.Clear(); } catch { /* redirected output */ }
        Console.Write(sb.ToString());
    }

    static string Fk(double v) => Math.Abs(v) >= 1e6 ? $"{v / 1e6:0.00}M" : Math.Abs(v) >= 1e3 ? $"{v / 1e3:0}K" : $"{v:0}";
    static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
