using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace KinahLedger.Ui;

/// <summary>
/// A table row the window updates in place. Until 0.8.36 every half-second tick handed the tables a new list of rows, and
/// WPF threw away and rebuilt every row control each time (the icon and its gradient tile, the tooltips, the grade
/// triggers); with a farming session on screen that was most of the UI thread, and clicks and drags waited behind it
/// (Steven, 2026-10-25: "EXTREMELY slow ... once things start being added"). Now a row control stays for as long as its
/// table has a row at that position, and a tick only hands it new words.
/// </summary>
public sealed class RowItem : INotifyPropertyChanged
{
    DropRow _row;
    public RowItem(DropRow row) { _row = row; }
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The row as the last redraw worked it out.</summary>
    public DropRow Row => _row;

    /// <summary>Takes the row's new words; true when anything changed (record equality: the same words and the same icon slot are the same row).</summary>
    public bool Set(DropRow row)
    {
        if (row == _row) return false;
        _row = row;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));   // an empty name: every binding re-reads
        return true;
    }

    // the names the row templates bind (Ui/MainWindow.xaml), forwarded
    public string Name => _row.Name;
    public string Drops => _row.Drops;
    public string DropPct => _row.DropPct;
    public string PerHour => _row.PerHour;
    public bool Header => _row.Header;
    public string Grade => _row.Grade;
    public IconSlot? Icon => _row.Icon;
    public string Initial => _row.Initial;
    public string Price => _row.Price;
    public string Value => _row.Value;
    public string Sub => _row.Sub;
    public string SubShown => _row.SubShown;
    public string Tone => _row.Tone;
    public string Key => _row.Key;
    public bool Child => _row.Child;
    public string Report => _row.Report;
    public string ReportText => _row.ReportText;
    public bool Reported => _row.Reported;
    public string Tip => _row.Tip;
    public long Worth => _row.Worth;
    public string PriceTone => _row.PriceTone;
    public string PriceTip => _row.PriceTip;
    public bool Mat => _row.Mat;
}

public partial class MainWindow
{
    internal static readonly IReadOnlyList<DropRow> NoRows = Array.Empty<DropRow>();

    /// <summary>
    /// Puts <paramref name="rows"/> in a table, keeping its row controls: a row that is already there gets the new words, a
    /// new one is added at the end, extra ones are taken off the end. Rows match by position, so a row that moves up a place
    /// changes its words rather than its control (the same as a rebuild looked, at a fraction of the work).
    /// </summary>
    internal static void SyncRows(ItemsControl table, IReadOnlyList<DropRow> rows)
    {
        if (table.ItemsSource is not ObservableCollection<RowItem> items) { items = new(); table.ItemsSource = items; }
        for (int i = 0; i < rows.Count; i++)
        {
            if (i < items.Count) items[i].Set(rows[i]);
            else items.Add(new RowItem(rows[i]));
        }
        while (items.Count > rows.Count) items.RemoveAt(items.Count - 1);
    }

    /// <summary>The row behind a tapped row control.</summary>
    internal static DropRow? RowOf(object? dataContext) => dataContext as DropRow ?? (dataContext as RowItem)?.Row;

    /// <summary>What each hand-built part of the window was last drawn from, so a tick that changes nothing redraws nothing.</summary>
    readonly Dictionary<string, string> _drawnSig = new();

    /// <summary>True when <paramref name="part"/> has to be drawn again: its words differ from the last draw's.</summary>
    bool Changed(string part, string sig)
    {
        if (_drawnSig.TryGetValue(part, out var was) && was == sig) return false;
        _drawnSig[part] = sig;
        return true;
    }

    /// <summary>
    /// True while the window is being dragged. Windows runs the drag in its own loop, and a tick that re-laid the window out
    /// mid-drag (the window sizes to its content) made the drag stall; the tick waits for the drop.
    /// </summary>
    bool _moving;

    internal const int WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;

    /// <summary>The start and end of a drag: the size is pinned for the drag, and content sizing comes back on the drop.</summary>
    internal bool OnSizeMove(int msg)
    {
        if (msg == WM_ENTERSIZEMOVE) { _moving = true; SizeToContent = SizeToContent.Manual; return true; }
        if (msg == WM_EXITSIZEMOVE) { _moving = false; SizeToContent = SizeToContent.WidthAndHeight; return true; }
        return false;
    }
}
