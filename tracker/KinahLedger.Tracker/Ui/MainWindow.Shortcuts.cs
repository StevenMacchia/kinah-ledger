using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Keys in this window (0.9.10), beside Ctrl+Z for Undo and Esc on the sheets: Ctrl+, opens Options, Ctrl+N is New
/// session (the button's own way: at once, with its Undo bar), Ctrl+Shift+C copies the share picture without opening the
/// sheet. None of them means anything in a text box (Ctrl+C there still copies the text). Each button's tooltip says its
/// key, as Start run's says the in-game one; Start run and Share say why while they are off.
/// </summary>
public partial class MainWindow
{
    public const string OptionsKey = "Ctrl+,", NewSessionKey = "Ctrl+N", CopyPictureKey = "Ctrl+Shift+C";

    string _shareTip = "";

    void InitShortcuts()
    {
        InputBindings.Add(new KeyBinding(new KeyAct(() => OpenOptions()), Key.OemComma, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new KeyAct(() => _ = NewSessionFromKey()), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new KeyAct(() => CopySharePictureNow()), Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        OptionsBtn.ToolTip = $"{OptionsBtn.ToolTip}\n{OptionsKey} opens it.";
        NewSessionBtn.ToolTip = $"{NewSessionBtn.ToolTip}\n{NewSessionKey} does the same.";
        _shareTip = $"{ShareCardBtn.ToolTip}\n{CopyPictureKey} copies the picture without opening it.";
        // a button that is off says why: Start until a server is chosen, Share until the game is being read
        foreach (var b in new[] { StartBtn, ShareCardBtn }) { ToolTipService.SetShowOnDisabled(b, true); b.IsEnabledChanged += (_, _) => ButtonTips(); }
        ButtonTips();
    }

    /// <summary>Start run's and Share's tooltips: what they do and their keys, or why they are off.</summary>
    void ButtonTips()
    {
        var key = HotkeyText;
        StartBtn.ToolTip = !StartBtn.IsEnabled
            ? (!HasServer && !Farming ? "Pick your server first, in Options › Server: prices and drops are kept per server." : "A moment: the run is starting or stopping.")
            : key.Length > 0 ? $"{StartTip}\n{key} in the game starts or stops it." : StartTip;
        if (_shareTip.Length > 0) ShareCardBtn.ToolTip = ShareCardBtn.IsEnabled ? _shareTip : "Nothing to share yet: Share opens once the game is being read.";
    }

    /// <summary>Ctrl+N and the tray's New session: the Farming page, then the button's own clear with its Undo bar.</summary>
    async Task NewSessionFromKey()
    {
        if (_welcome) { Say("Choose your server first.", "Warn"); return; }
        if (_part != "Farming") ShowPart("Farming");
        await ClearFarmTables();
    }

    /// <summary>
    /// Ctrl+Shift+C and the tray's Copy share picture: the side the Share sheet shows while it is open, else This session,
    /// as the sheet opens on. True when a picture went to the clipboard.
    /// </summary>
    bool CopySharePictureNow()
    {
        if (_shareOn == null) _shareScope = SharePicture.Session;
        if (CurrentSharePic() == null)
        {
            Say(_shareScope == SharePicture.Session ? "Nothing to share yet. Kill or gather something and the picture fills in." : "Nothing on this side of Share yet.", "Warn");
            return false;
        }
        return CopySharePicture();
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
