using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace KinahLedger.Ui;

/// <summary>
/// Settings › DPS meter and Settings › Applicants: one line under the page's header when something keeps the meter off
/// ("Needs Npcap · Get it", Ui/MainWindow.MeterBlock.cs). Nothing when the meter can read. Game reading keeps its own status line.
/// </summary>
public partial class OptionsWindow
{
    readonly Dictionary<Page, Border> _meterBlockHosts = new();

    /// <summary>A place for the line on <paramref name="page"/>, filled now and again whenever the case changes.</summary>
    Border MeterBlockHost(Page page)
    {
        var host = new Border { Tag = MainWindow.MeterBlockTag };
        _meterBlockHosts[page] = host;
        FillMeterBlock(host);
        return host;
    }

    internal void MeterBlockChanged()
    {
        foreach (var h in _meterBlockHosts.Values) FillMeterBlock(h);
        if (_parserLookStage != null) { _parserLookStage.Tag = null; DrawParserStage(_parserLookShown); }   // its height follows the line
    }

    /// <summary>The DPS meter page's stage: a little shorter while the line shows, so the page still fits.</summary>
    double MeterStageHeight => _host.MeterBlockShown == MeterBlock.None ? 176 : 150;   // 196/170 until the eight themes' chips took two rows (2026-11-20)

    void FillMeterBlock(Border host)
    {
        var b = _host.MeterBlockShown;
        if (b == MeterBlock.None) { host.Child = null; host.Visibility = Visibility.Collapsed; return; }
        var (text, link) = MainWindow.MeterBlockShort(b);
        var line = MainWindow.MeterBlockWords(b).Line;
        var t = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, ToolTip = line };
        var dot = new Run("● ") { FontSize = 10 };
        dot.SetResourceReference(TextElement.ForegroundProperty, "Warn");
        var words = new Run(text) { FontWeight = FontWeights.SemiBold };
        words.SetResourceReference(TextElement.ForegroundProperty, "Warn");
        t.Inlines.Add(dot); t.Inlines.Add(words);
        // the link that acts here: the download, Game reading, or the restart; the booster case has only its line
        Action? act = b switch
        {
            MeterBlock.Missing or MeterBlock.AdminsOnly => () => MainWindow.Open(MeterBlocks.NpcapUrl),
            MeterBlock.Off => () => Go(Page.Reading),
            MeterBlock.Restart => () => _host.RestartFromOptions(),
            _ => null,
        };
        string linkText = b switch { MeterBlock.AdminsOnly => L.T("Get Npcap"), MeterBlock.NoServer => "", _ => link };
        if (act != null && linkText.Length > 0)
        {
            var sep = new Run("  ·  ");
            sep.SetResourceReference(TextElement.ForegroundProperty, "Faint");
            var h = new Hyperlink(new Run(linkText)) { Tag = b == MeterBlock.Missing || b == MeterBlock.AdminsOnly ? MeterBlocks.NpcapUrl : null };
            if (TryFindResource("LinkStyle") is Style ls) h.Style = ls;
            h.Click += (_, _) => act();
            t.Inlines.Add(sep); t.Inlines.Add(h);
        }
        // one line, so the page still fits without scrolling: how to fix it is on hover, and on the Parser page's card
        AutomationProperties.SetName(t, text + (linkText.Length > 0 ? " · " + linkText : ""));
        host.Child = t;
        host.Margin = new Thickness(0, 6, 0, 0);
        host.Visibility = Visibility.Visible;
    }

    /// <summary>For the self-test: the line on <paramref name="page"/> (its words, and the link's address when it has one), or null.</summary>
    internal (string Text, string? Url)? MeterBlockLineForTest(Page page)
    {
        if (!_meterBlockHosts.TryGetValue(page, out var host) || host.Visibility != Visibility.Visible || host.Child is not TextBlock t) return null;
        return (AutomationProperties.GetName(t), t.Inlines.OfType<Hyperlink>().FirstOrDefault()?.Tag as string);
    }
}
