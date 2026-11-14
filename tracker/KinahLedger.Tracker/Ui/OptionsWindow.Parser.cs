using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// Options › In game › Parser (Steven, 2026-11-14: minimal by default; the extras are switches in Settings): the Parser
/// page and its overlay show the boss, the clock and each member's DPS; these add the rest. On the page's preview-then-Save
/// model: read from parser.json when Options opens, written on Save (ParserPrefs.Saved tells the window), dropped on Cancel.
/// </summary>
public partial class OptionsWindow
{
    TextBlock? _parserLabel;
    CheckBox _parserDetailsChk = null!, _parserKillsChk = null!, _parserFiltersChk = null!, _parserHoversChk = null!, _parserSeparateChk = null!;

    void LoadParser()
    {
        if (_parserLabel == null) BuildParser();
        var p = ParserPrefs.Load();
        _parserDetailsChk.IsChecked = p.Details;
        _parserKillsChk.IsChecked = p.KillsNear;
        _parserFiltersChk.IsChecked = p.Filters;
        _parserHoversChk.IsChecked = p.Hovers;
        _parserSeparateChk.IsChecked = p.Separate;
    }

    void SaveParser()
    {
        if (_parserLabel == null) return;
        var p = ParserPrefs.Load();
        p.Details = _parserDetailsChk.IsChecked == true;
        p.KillsNear = _parserKillsChk.IsChecked == true;
        p.Filters = _parserFiltersChk.IsChecked == true;
        p.Hovers = _parserHoversChk.IsChecked == true;
        p.Separate = _parserSeparateChk.IsChecked == true;
        p.Save();
        ParserPrefs.RaiseSaved();
    }

    void BuildParser()
    {
        _parserLabel = Styled(new TextBlock { Text = "PARSER" }, "GroupLabel");
        var stack = new StackPanel();
        CheckBox Row(string title, string hint, string name)
        {
            var g = Styled(new Grid(), "Row");
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel();
            sp.Children.Add(Styled(new TextBlock { Text = title }, "RowTitle"));
            sp.Children.Add(Styled(new TextBlock { Text = hint }, "RowHint"));
            g.Children.Add(sp);
            var cb = Styled(new CheckBox(), "Switch");
            AutomationProperties.SetName(cb, title);
            Grid.SetColumn(cb, 1);
            g.Children.Add(cb);
            if (stack.Children.Count > 0) stack.Children.Add(Styled(new Border(), "Divider"));
            stack.Children.Add(g);
            return cb;
        }
        _parserDetailsChk = Row("Group details", "Each member's class, Combat Power, item level and damage beside their DPS, on the Parser page and its overlay.", "Parser group details");
        _parserKillsChk = Row("Kills and deaths", "Kills near you and the group's deaths on boss pulls and runs. Every fight always shows your kills and deaths.", "Parser kills and deaths");
        _parserFiltersChk = Row("List filters", "Pick a kind and a boss or an instance on the Parser's list of past records.", "Parser list filters");
        _parserHoversChk = Row("Explanations on hover", "What a figure, a row or a switch means, when the pointer rests on it.", "Parser explanations on hover");
        _parserSeparateChk = Row("Separate parser overlay", "The Parser in its own overlay you can drag anywhere. Off, it opens above the Kinah overlay when a fight starts and folds away after.", "Separate parser overlay");
        var group = Styled(new Border { Child = stack }, "Group");
        // before Rare drops, which stays the page's last group
        int at = _rareLabel != null ? PageInGame.Children.IndexOf(_rareLabel) : -1;
        if (at < 0) { PageInGame.Children.Add(_parserLabel); PageInGame.Children.Add(group); }
        else { PageInGame.Children.Insert(at, group); PageInGame.Children.Insert(at, _parserLabel); }
    }

    /// <summary>For the options test: the Parser group's four switches.</summary>
    internal (CheckBox Details, CheckBox Kills, CheckBox Filters, CheckBox Hovers, CheckBox Separate) ParserSwitchesForTest => (_parserDetailsChk, _parserKillsChk, _parserFiltersChk, _parserHoversChk, _parserSeparateChk);
}
