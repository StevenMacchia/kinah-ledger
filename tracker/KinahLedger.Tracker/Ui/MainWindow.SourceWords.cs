using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// Steven, 2026-11-14: "Stop referring to packet reading ANYWHERE in the app except the settings." The window shows a figure
/// without saying where it came from; only Options names the feature, its switch and what it needs. options-test walks every
/// page with <see cref="SourceWordsShown"/> and fails on any of these words in a text or a hover.
/// </summary>
public partial class MainWindow
{
    internal static readonly Regex SourceWords = new(
        @"\b(packets?|traffic|npcap|decoders?|recordings?)\b|game['’]s\s+(own\s+)?messages?|from the game itself|packet reading|game['’]s clock|PC['’]s clock",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The texts and hovers drawn under <paramref name="root"/> (the window when null) that name where a figure came from.</summary>
    internal List<string> SourceWordsShown(DependencyObject? root = null)
    {
        var hits = new List<string>();
        void Text(string? s, string where) { if (!string.IsNullOrEmpty(s) && SourceWords.IsMatch(s)) hits.Add($"{where}: {s}"); }
        void Walk(DependencyObject d)
        {
            if (d is UIElement { Visibility: not Visibility.Visible }) return;
            if (d is TextBlock tb) Text(InlineText(tb), "text");
            if (d is FrameworkElement fe)
            {
                if (fe.ToolTip is string tip) Text(tip, $"hover on {fe.GetType().Name} {fe.Name}");
                else if (fe.ToolTip is TextBlock ttb) Text(InlineText(ttb), $"hover on {fe.GetType().Name} {fe.Name}");
            }
            if (d is ContentControl { Content: string cs }) Text(cs, "label");
            int n = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(d) : 0;
            for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root ?? this);
        return hits.Distinct().ToList();
    }
}
