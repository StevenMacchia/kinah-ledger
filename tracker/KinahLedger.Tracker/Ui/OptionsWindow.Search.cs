using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WPath = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The settings revamp's two helpers (Steven, 2026-11-17: "a complete setting revamp. That makes it a lot easier for users
/// to get through"). Folds: the rarely used rows of a page under one bar (Advanced on the settings pages, More on the
/// overlays'), shut when Options opens, so every page fits the window without scrolling. Search: a box at the top of the
/// left bar that lists every row whose title or hint (its hover too) holds what is typed, and jumps to the one picked,
/// opening its fold and flashing it.
/// </summary>
public partial class OptionsWindow
{
    /* ---------------- folds ---------------- */

    /// <summary>Every fold's body and its head's chevron and count.</summary>
    readonly Dictionary<Panel, (Button Head, FrameworkElement Chevron, TextBlock Count, Func<string>? CountText)> _folds = new();
    /// <summary>The folds left open, by page and title: a page built again (Timers, DPS meter) keeps them open.</summary>
    readonly HashSet<string> _openFolds = new();

    /// <summary>
    /// A fold over <paramref name="body"/> (its Tag is the title): a bar put just before it in its panel, the body shut. The
    /// bar says how many rows are inside, or <paramref name="countText"/>'s words.
    /// </summary>
    Button Fold(Panel body, Func<string>? countText = null)
    {
        var parent = (Panel)body.Parent;
        string title = body.Tag as string ?? "Advanced";
        var chevron = new WPath
        {
            Data = Geometry.Parse("M0,0 L4.5,4.5 L0,9"), StrokeThickness = 1.7, Width = 6, Height = 10, Margin = new Thickness(1, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(0),
        };
        chevron.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        var name = new TextBlock { Text = L.T(title), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var count = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(chevron);
        Grid.SetColumn(name, 1); grid.Children.Add(name);
        Grid.SetColumn(count, 2); grid.Children.Add(count);
        var head = new Button { Content = grid, Tag = "fold:" + title };
        head.SetResourceReference(StyleProperty, "FoldHead");
        AutomationProperties.SetName(head, title);
        head.Click += (_, _) => SetFold(body, body.Visibility != Visibility.Visible);
        parent.Children.Insert(parent.Children.IndexOf(body), head);
        _folds[body] = (head, chevron, count, countText);
        SetFold(body, _openFolds.Contains(FoldKey(body)));
        RefreshFoldCount(body);
        return head;
    }

    string FoldKey(Panel body) => $"{PageOf(body)}:{body.Tag}";

    /// <summary>Opens or shuts a fold.</summary>
    void SetFold(Panel body, bool open)
    {
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (!_folds.TryGetValue(body, out var f)) return;
        ((RotateTransform)f.Chevron.RenderTransform).Angle = open ? 90 : 0;
        AutomationProperties.SetHelpText(f.Head, open ? "open" : "shut");
        if (open) _openFolds.Add(FoldKey(body)); else _openFolds.Remove(FoldKey(body));
    }

    void RefreshFoldCount(Panel body)
    {
        if (!_folds.TryGetValue(body, out var f)) return;
        if (f.CountText != null) { f.Count.Text = f.CountText(); return; }
        int n = Rows(body).Count();
        f.Count.Text = n > 0 ? string.Format(L.T("{0} more", "rows"), n) : "";   // "{0} more" alone is people (the meter)
    }

    void RefreshFoldCounts()
    {
        foreach (var body in _folds.Keys.ToList())
            if (body.Parent == null) _folds.Remove(body);   // a page built again dropped it
            else RefreshFoldCount(body);
    }

    /// <summary>For the tests and renders: every fold on a page, opened or shut.</summary>
    internal void OpenFoldsForTest(bool open = true)
    {
        foreach (var body in _folds.Keys.Where(b => b.Parent != null).ToList()) SetFold(body, open);
    }

    /// <summary>For the tests: the folds' titles on a page, and whether each is shut.</summary>
    internal List<(string Title, bool Shut)> FoldsForTest(Page page) => _folds.Keys.Where(b => b.Parent != null && PageOf(b) == Shown(page))
        .Select(b => (b.Tag as string ?? "", b.Visibility != Visibility.Visible)).ToList();

    /* ---------------- finding rows ---------------- */

    static IEnumerable<DependencyObject> Tree(DependencyObject d)
    {
        yield return d;
        foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            foreach (var x in Tree(c)) yield return x;
    }

    bool IsStyled(FrameworkElement e, string key) => TryFindResource(key) is Style s && ReferenceEquals(e.Style, s);

    /// <summary>The row titles under an element (a page, a fold).</summary>
    IEnumerable<TextBlock> Rows(DependencyObject under) => Tree(under).OfType<TextBlock>().Where(t => IsStyled(t, "RowTitle") && !string.IsNullOrWhiteSpace(Said(t)));

    /// <summary>A text block's words, its runs (inside links too) or its text.</summary>
    static string Said(TextBlock t) => t.Inlines.Count == 0 ? t.Text : string.Concat(Words(t.Inlines));
    static IEnumerable<string> Words(System.Windows.Documents.InlineCollection inlines)
    {
        foreach (var i in inlines)
            if (i is System.Windows.Documents.Run r) yield return r.Text;
            else if (i is System.Windows.Documents.Span s) foreach (var w in Words(s.Inlines)) yield return w;
    }

    /// <summary>The page an element is on, by its panel.</summary>
    Page? PageOf(DependencyObject e)
    {
        var pages = PagePanels.ToList();
        for (DependencyObject? d = e; d != null; d = LogicalTreeHelper.GetParent(d) ?? VisualTreeHelper.GetParent(d))
            foreach (var (p, panel) in pages) if (d == panel) return p;
        return null;
    }

    /// <summary>The whole row a title sits in: the part of its group (a card's panel) that holds it, else its row grid or panel.</summary>
    FrameworkElement RowOf(TextBlock title)
    {
        for (FrameworkElement? d = title; d?.Parent is FrameworkElement up; d = up)
            if (up is Panel && up.Parent is Border card && IsStyled(card, "Group")) return d;
        var parent = title.Parent as FrameworkElement;
        if (parent is Grid g0 && IsStyled(g0, "Row")) return g0;
        if (parent is StackPanel sp && sp.Parent is Grid g && IsStyled(g, "Row")) return g;
        return parent ?? title;
    }

    /// <summary>A row found: its page, the row, its title, what a search is matched against, and the fold it is in.</summary>
    internal sealed record Found(Page Page, FrameworkElement Row, string Title, string Hay, Panel? Fold);

    /// <summary>Every row on every page (and each page's title), in the left bar's order.</summary>
    List<Found> AllRows()
    {
        var all = new List<Found>();
        foreach (var (page, panel) in PagePanels)
        {
            if (Tree(panel).OfType<TextBlock>().FirstOrDefault(t => IsStyled(t, "PageTitle")) is { } pt)
                all.Add(new Found(page, pt, Said(pt), Said(pt).ToLowerInvariant(), null));
            foreach (var t in Rows(panel))
            {
                if (Hidden(t, panel)) continue;   // a row kept hidden (Send report pictures, held for the site) is not offered
                var row = RowOf(t);
                var words = new List<string> { Said(t), t.ToolTip as string ?? "", row.ToolTip as string ?? "" };
                if (t.Parent is Panel p) foreach (var h in p.Children.OfType<TextBlock>().Where(x => x != t && IsStyled(x, "RowHint"))) { words.Add(h.Text); words.Add(h.ToolTip as string ?? ""); }
                var fold = _folds.Keys.FirstOrDefault(b => Tree(b).Contains(t));
                all.Add(new Found(page, row, Said(t), string.Join(" ", words).ToLowerInvariant(), fold));
            }
        }
        return all;
    }

    /// <summary>Collapsed somewhere under its page other than by a fold (a fold opens on a jump).</summary>
    bool Hidden(FrameworkElement e, Panel page)
    {
        for (DependencyObject? d = e; d != null && d != page; d = LogicalTreeHelper.GetParent(d))
            if (d is UIElement { Visibility: Visibility.Collapsed } u && !(u is Panel p && _folds.ContainsKey(p))) return true;
        return false;
    }

    /// <summary>The rows whose title or hint holds every word typed.</summary>
    internal List<Found> Search(string text)
    {
        var terms = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return new();
        return AllRows().Where(f => terms.All(f.Hay.Contains))
            .OrderBy(f => terms.All(f.Title.ToLowerInvariant().Contains) ? 0 : 1)   // a title that says it, before a hint that does
            .ThenBy(f => NavPages.Select(n => n.Page).ToList().IndexOf(f.Page)).ToList();
    }

    /* ---------------- the search box ---------------- */

    void WireSearch()
    {
        SearchBox.TextChanged += (_, _) => { SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; ShowSearch(); };
        SearchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && SearchBox.Text.Length > 0) { SearchBox.Text = ""; e.Handled = true; }   // Esc clears the search before it closes the window
            else if (e.Key == Key.Enter) { if (_hits.Count > 0) Jump(_hits[0]); e.Handled = true; }   // never Save
            else if (e.Key == Key.Down && SearchResults.Children.OfType<Button>().FirstOrDefault() is { } first) { first.Focus(); e.Handled = true; }
        };
        SearchClear.MouseLeftButtonUp += (_, _) => { SearchBox.Text = ""; SearchBox.Focus(); };
        // Ctrl+F: to the box from anywhere in the window
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; } };
    }

    List<Found> _hits = new();

    void ShowSearch()
    {
        string text = SearchBox.Text.Trim();
        bool on = text.Length > 0;
        SearchClear.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Nav.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        SearchScroll.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SearchResults.Children.Clear();
        _hits = on ? Search(text) : new();
        if (!on) return;
        if (_hits.Count == 0)
        {
            var none = new TextBlock { Text = L.T("Nothing matches."), FontSize = 12.5, Margin = new Thickness(12, 6, 8, 0), TextWrapping = TextWrapping.Wrap };
            none.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            SearchResults.Children.Add(none);
            return;
        }
        var names = NavPages.ToDictionary(n => n.Page, n => L.T(n.Name));
        foreach (var f in _hits.Take(40))
        {
            var title = new TextBlock { Text = f.Title, FontSize = 12.5, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            var where = new TextBlock { Text = names[f.Page] + (f.Fold != null ? " › " + L.T(f.Fold.Tag as string ?? "") : ""), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
            where.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            var b = new Button { Content = new StackPanel { Children = { title, where } }, ToolTip = f.Title };
            b.SetResourceReference(StyleProperty, "SearchHit");
            AutomationProperties.SetName(b, f.Title);
            var hit = f;
            b.Click += (_, _) => Jump(hit);
            b.PreviewKeyDown += (_, e) =>
            {
                var list = SearchResults.Children.OfType<Button>().ToList();
                int at = list.IndexOf(b);
                if (e.Key == Key.Down && at + 1 < list.Count) { list[at + 1].Focus(); e.Handled = true; }
                else if (e.Key == Key.Up) { (at > 0 ? (UIElement)list[at - 1] : SearchBox).Focus(); e.Handled = true; }
                else if (e.Key == Key.Escape) { SearchBox.Text = ""; SearchBox.Focus(); e.Handled = true; }
            };
            SearchResults.Children.Add(b);
        }
    }

    /// <summary>To a row found: its page, its fold opened, scrolled into view and flashed. The search stays, to try the next.</summary>
    FrameworkElement Jump(Found f)
    {
        if (_page != f.Page) Go(f.Page);
        // a page built again on showing (Timers) has new rows: the one with the same title
        var row = f.Row;
        if (PresentationSource.FromVisual(row) == null || PageOf(row) != f.Page)
            row = AllRows().FirstOrDefault(x => x.Page == f.Page && x.Title == f.Title)?.Row ?? row;
        GoTo(row);
        return row;
    }

    /// <summary>To an element on any page: its page shown, the folds around it opened, scrolled to and flashed.</summary>
    internal void GoTo(FrameworkElement e)
    {
        if (PageOf(e) is { } page && page != _page) Go(page);
        foreach (var body in _folds.Keys.Where(b => b.Parent != null && Tree(b).Contains(e)).ToList()) SetFold(body, true);
        UpdateLayout();
        var row = e is TextBlock t && IsStyled(t, "RowTitle") ? RowOf(t) : e;
        try
        {
            var at = row.TranslatePoint(new Point(0, 0), (UIElement)Scroller.Content);
            Scroller.ScrollToVerticalOffset(Math.Max(0, at.Y - 60));
        }
        catch (InvalidOperationException) { }
        Flash(row);
    }

    /// <summary>The row lit for a moment in the accent's dim tone, fading out.</summary>
    static void Flash(FrameworkElement row)
    {
        if (row.TryFindResource("AccentDim") is not SolidColorBrush dim) return;
        var brush = new SolidColorBrush(dim.Color) { Opacity = 1 };
        switch (row)
        {
            case Panel p: var was = p.Background; p.Background = brush; Fade(() => p.Background = was); break;
            case Border b: var wasB = b.Background; b.Background = brush; Fade(() => b.Background = wasB); break;
            case Control c: var wasC = c.Background; c.Background = brush; Fade(() => c.Background = wasC); break;
            default: return;
        }
        void Fade(Action back)
        {
            if (!Motion.On) { var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) }; t.Tick += (_, _) => { t.Stop(); back(); }; t.Start(); return; }
            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(700) };
            a.Completed += (_, _) => back();
            brush.BeginAnimation(Brush.OpacityProperty, a);
        }
    }

    internal static IEnumerable<DependencyObject> TreeOf(DependencyObject d) => Tree(d);
    /// <summary>For the options test: every row (and page title) the search knows, a jump to one, and whether a row is on screen.</summary>
    internal List<Found> SearchForTestAll() => AllRows();
    internal FrameworkElement JumpForTest(Found f) => Jump(f);
    internal bool IsShownForTest(FrameworkElement row)
    {
        if (!row.IsVisible) return false;
        var at = row.TranslatePoint(new Point(0, 0), Scroller);
        return at.Y >= -1 && at.Y < Scroller.ViewportHeight - 10;
    }
    internal Panel PageShownPanelForTest => PagePanels.First(p => p.Page == _page).Panel;
    /// <summary>For the options test: a control in its page's header (an overlay's Show over the game).</summary>
    internal bool InHeaderForTest(DependencyObject c) => PagePanels.Any(p => p.Panel.Children.Count > 0 && Tree(p.Panel.Children[0]).Contains(c));

    /// <summary>For the options test: the search box typed into, the titles listed, and the first one picked.</summary>
    internal List<string> SearchForTest(string text) { SearchBox.Text = text; return _hits.Select(h => h.Title).ToList(); }
    internal List<Found> SearchHitsForTest => _hits;
    internal void JumpFirstForTest() { if (_hits.Count > 0) Jump(_hits[0]); }
    internal bool NavShownForTest => Nav.Visibility == Visibility.Visible;
}
