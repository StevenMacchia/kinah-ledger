using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Save as CSV (0.10.11): today's loot from the Farming page's ⋯ menu, the runs shown from the ALL RUNS box on the Events
/// page, each as one file for a spreadsheet (Session/CsvExport.cs). A save dialog asks where; the status line says the
/// file's name. Nothing leaves the PC.
/// </summary>
public partial class MainWindow
{
    public const string SaveLootText = "Save today's loot as CSV…", SaveRunsText = "Save as CSV";

    /// <summary>Today's loot (every zone, the day the Today strip shows) as CSV text, or null with nothing picked up yet.</summary>
    string? LootCsv()
    {
        if (TodayEngine is not { } day) return null;
        var farm = TodayLoot.Farm(day, _items);
        return farm.Any ? CsvExport.Loot(farm) : null;
    }

    /// <summary>The ⋯ menu's Save today's loot as CSV…</summary>
    void SaveLootCsv()
    {
        if (LootCsv() is not { } text) { Say("Nothing picked up today yet: the file would be empty.", "Warn"); return; }
        SaveCsv(CsvExport.FileName("loot", DateTime.Now), text, "today's loot");
    }

    /// <summary>The ALL RUNS box's Save as CSV: the runs it shows, in the order it shows them.</summary>
    void SaveRunsCsv(IReadOnlyList<InstanceRun> runs)
    {
        if (runs.Count == 0) { Say("No runs to save yet.", "Warn"); return; }
        SaveCsv(CsvExport.FileName("runs", DateTime.Now), CsvExport.Runs(RunsOrder.Sorted(runs, RunsSortMode, RunValue), _items, RunValue), $"{runs.Count} run{(runs.Count == 1 ? "" : "s")}");
    }

    /// <summary>Asks where to put the file (Documents by default), writes it with a UTF-8 mark for Excel, and says so.</summary>
    void SaveCsv(string fileName, string text, string what)
    {
        string? path;
        if (SaveCsvForTest != null) path = SaveCsvForTest(fileName);
        else
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = fileName, DefaultExt = ".csv", Filter = "CSV, comma separated (*.csv)|*.csv", AddExtension = true, OverwritePrompt = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Title = "Save as CSV",
            };
            path = dlg.ShowDialog(this) == true ? dlg.FileName : null;
        }
        if (path == null) return;
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Say($"Saved {what} as {Path.GetFileName(path)}.", "Good");
        }
        catch (Exception ex)
        {
            CrashLog.Write("save csv", ex);
            Say($"Couldn't save the file: {ex.Message}", "Warn");
        }
    }

    /// <summary>A small pill with a plain action, the look of the run list's order dropdown without its chevron.</summary>
    Border PillButton(string text, Action click, string tip)
    {
        var b = new Border { Child = QT(text, 11.5, FontWeights.ExtraBold, "Ink"), Height = 26, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, "Card2"); b.SetResourceReference(Border.BorderBrushProperty, "CardLineHover");
        ToolTipService.SetInitialShowDelay(b, 400);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    /// <summary>Set by the options self-test: takes the file's name and gives the path to write, or null for Cancel, instead of a dialog.</summary>
    internal static Func<string, string?>? SaveCsvForTest;
    /// <summary>For the options self-test: today's loot as the file would hold it.</summary>
    internal string? LootCsvForTest() => LootCsv();
    /// <summary>For the options self-test: the ALL RUNS box's Save as CSV, on the runs the Events page shows now.</summary>
    internal void SaveRunsCsvForTest() => SaveRunsCsv(ShownRunsForTest());
}
