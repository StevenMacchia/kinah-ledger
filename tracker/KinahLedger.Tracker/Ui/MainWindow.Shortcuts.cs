using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Keys in this window (0.9.10), beside Ctrl+Z for Undo and Esc on the sheets: Ctrl+, opens Options, Ctrl+N is Clear
/// everything (New session until 0.9.13; the ⋯ menu's own way: at once, with its Undo bar), Ctrl+Shift+C copies the share picture without opening the
/// sheet. None of them means anything in a text box (Ctrl+C there still copies the text). Each button's tooltip says its
/// key, as Start run's says the in-game one; Start run and Share say why while they are off.
/// </summary>
public partial class MainWindow
{
    public const string OptionsKey = "Ctrl+,", NewSessionKey = "Ctrl+N", CopyPictureKey = "Ctrl+Shift+C";
    /// <summary>Window size (0.10.11, Ui/MainWindow.Fit.cs): a step bigger, a step smaller, back to 100%. The numeric keypad's + - 0 work too.</summary>
    public const string WindowBiggerKey = "Ctrl+=", WindowSmallerKey = "Ctrl+-", WindowResetKey = "Ctrl+0";
    /// <summary>The pages (0.10.26): Ctrl+1 Farming, Ctrl+2 Crafting, Ctrl+3 Events; the keypad's 1 2 3 too. Nothing before a server is chosen.</summary>
    public const string PageKeys = "Ctrl+1 · 2 · 3";
    /// <summary>New session's name since it left the card (0.9.13), in the ⋯ menu and the tray.</summary>
    public const string ClearAllText = "Clear everything";

    /// <summary>The Farming count has been drawn once, so Share has something to open on (it was the header's Share button's IsEnabled).</summary>
    bool _shareReady;
    /// <summary>The Farming ⋯ menu's rows for the two things that left the header (UX pass 2026-11-13).</summary>
    public const string ShareText = "Share a picture…", FullViewText = "Full view";

    void InitShortcuts()
    {
        InputBindings.Add(new KeyBinding(new KeyAct(() => OpenOptions()), Key.OemComma, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new KeyAct(() => _ = ClearAllFromKey()), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new KeyAct(() => CopySharePictureNow()), Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        // window size: Ctrl with = (the + key), - and 0, on the main keys and the keypad; Shift+= (a real +) too
        foreach (var (key, steps) in new[] { (Key.OemPlus, 1), (Key.Add, 1), (Key.OemMinus, -1), (Key.Subtract, -1), (Key.D0, 0), (Key.NumPad0, 0) })
        {
            int s = steps;
            InputBindings.Add(new KeyBinding(new KeyAct(() => StepWindowScale(s)), key, ModifierKeys.Control));
            if (key == Key.OemPlus) InputBindings.Add(new KeyBinding(new KeyAct(() => StepWindowScale(1)), key, ModifierKeys.Control | ModifierKeys.Shift));
        }
        // the pages: Ctrl+1, 2, 3 as the tabs sit, a click by hand (the page then stays until something else starts)
        foreach (var (key, part) in new[] { (Key.D1, "Farming"), (Key.NumPad1, "Farming"), (Key.D2, "Crafting"), (Key.NumPad2, "Crafting"), (Key.D3, "Events"), (Key.NumPad3, "Events") })
        {
            var p = part;
            InputBindings.Add(new KeyBinding(new KeyAct(() => { if (!_welcome && _part != p) ShowPart(p); }), key, ModifierKeys.Control));
        }
        OptionsBtn.ToolTip = $"{OptionsBtn.ToolTip}\n{OptionsKey} opens them.";
        // a button that is off says why: Start until a server is chosen (Share, in the Farming ⋯ menu, says so on its row)
        ToolTipService.SetShowOnDisabled(StartBtn, true); StartBtn.IsEnabledChanged += (_, _) => ButtonTips();
        // whether a run would clear counts changes with every pickup: said as the tooltip opens (0.9.13)
        HoverTip.Opening += el => { if (ReferenceEquals(el, StartBtn)) ButtonTips(); };
        ButtonTips();
    }

    /// <summary>Start run's tooltip: what it does and its key, or why it is off.</summary>
    void ButtonTips()
    {
        var key = HotkeyText;
        StartBtn.ToolTip = !StartBtn.IsEnabled
            ? (!HasServer && !Farming ? "Pick your server first, in Options › Server: prices and drops are kept per server." : "A moment: the run is starting or stopping.")
            : (key.Length > 0 ? $"{StartTip}\n{key} in the game starts or stops it." : StartTip)
              + (!Farming && HasCounts() ? "\nStarting a run clears the counts on the tables first (Undo brings them back). The Today strip keeps the day." : "");
    }

    /// <summary>Ctrl+N and the tray's Clear everything: the Farming page, then the ⋯ menu's own clear with its Undo bar.</summary>
    async Task ClearAllFromKey()
    {
        if (_welcome) { Say("Choose your server first.", "Warn"); return; }
        if (_part != "Farming") ShowPart("Farming");
        await ClearFarmTables();
    }

    /// <summary>
    /// Ctrl+Shift+C and the tray's Copy share picture: the side the Share sheet shows while it is open, else Today,
    /// as the sheet opens on (Today). True when a picture went to the clipboard.
    /// </summary>
    bool CopySharePictureNow()
    {
        if (_shareOn == null) _shareScope = SharePicture.Today;
        if (CurrentSharePic() == null)
        {
            Say(_shareScope is SharePicture.Today or SharePicture.Session ? "Nothing to share yet. Kill or gather something and the picture fills in." : "Nothing on this side of Share yet.", "Warn");
            return false;
        }
        return CopySharePicture();
    }

    /// <summary>For the options self-test: a key with its modifiers, as the window's bindings see it.</summary>
    internal void PressForTest(Key key, ModifierKeys mods)
    {
        foreach (var kb in InputBindings.OfType<KeyBinding>())
            if (kb.Key == key && kb.Modifiers == mods) { kb.Command.Execute(null); return; }
    }

    /// <summary>A key binding's action.</summary>
    sealed class KeyAct : ICommand
    {
        readonly Action _run;
        public KeyAct(Action run) { _run = run; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _run();
    }
}
