using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The crafting line's shared parts (the mock's feature 2, kinah-bench/mock-hud-next/f2-crafting.png): the crafts as the
/// looks get them ("craft0" newest to "craft3", from MainWindow.HudCraft.cs), the success and fail bar in the window's
/// craft card colours (CkOddsBar: blue for worked, orange for failed), and a craft's tile, the recipe's icon framed in blue
/// with a ✓ when it worked, in orange with a ✕ when it failed. Every brush is frozen, as the rest of the panel's.
/// </summary>
static class CraftKit
{
    public const uint OkC = 0x3D8BFF, Ok2C = 0x69A8FF, FailC = 0xE8892F, LabelC = 0x8DB8FF;
    public static readonly SolidColorBrush Ok2 = B(Ok2C), Fail = B(FailC), Label = B(LabelC), FailBar = B(FailC, .85);
    public static readonly Brush OkBar = MakeOkBar();
    static Brush MakeOkBar()
    {
        var g = new LinearGradientBrush(Rgb(OkC), Rgb(Ok2C), 0);
        g.Freeze();
        return g;
    }

    /// <summary>The crafts in the values, newest first; a craft's Note is "worked" or "failed".</summary>
    public static List<Pick> Crafts(IReadOnlyDictionary<string, string> v)
    {
        string V(string k) => v.TryGetValue(k, out var s) ? s : "";
        var list = new List<Pick>();
        for (int i = 0; i < HudLooks.Picks; i++)
        {
            var c = "craft" + i;
            if (V(c).Length == 0) continue;
            var at = long.TryParse(V(c + "At"), out var ticks) && ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
            list.Add(new Pick("craft|" + V(c + "Id") + "|" + i, V(c), "", V(c + "Worth"), "", V(c + "Icon"), V(c + "Ok") == "1" ? "worked" : "failed", false, at));
        }
        return list;
    }

    public static bool Worked(Pick p) => p.Note == "worked";

    /// <summary>The success and fail bar: blue for the crafts that worked, orange for those that failed, rounded ends.</summary>
    public static Grid Bar(double width, double height)
    {
        var g = new Grid { Width = width, Height = height, VerticalAlignment = VerticalAlignment.Center, Clip = new RectangleGeometry(new Rect(0, 0, width, height), height / 2, height / 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition());
        var fail = new Border { Background = FailBar }; Grid.SetColumn(fail, 1);
        g.Children.Add(new Border { Background = OkBar }); g.Children.Add(fail);
        return g;
    }

    public static void SetBar(Grid bar, int ok, int fail)
    {
        bar.Visibility = ok + fail > 0 ? Visibility.Visible : Visibility.Collapsed;
        bar.ColumnDefinitions[0].Width = new GridLength(Math.Max(ok, 0), GridUnitType.Star);
        bar.ColumnDefinitions[1].Width = new GridLength(Math.Max(fail, 0), GridUnitType.Star);
    }

    /// <summary>A craft's tile: the recipe's icon (the item's tile, as the pickups'), framed and glowing in blue or orange, with the ✓ or ✕ on its corner.</summary>
    public static FrameworkElement Tile(Pick p, double size)
    {
        bool ok = Worked(p);
        var c = Rgb(ok ? Ok2C : FailC);
        var tile = HudKit.Tile(p, size);
        if (tile is not Grid g) return tile;
        g.Children.Insert(0, Glow(Rgb(ok ? OkC : FailC), 5, 7, .55));
        g.Children.Add(new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = Frozen(c), IsHitTestVisible = false });
        var mark = new Grid
        {
            Width = 12, Height = 12,
            Children =
            {
                new Ellipse { Fill = ok ? B(OkC) : Fail, Stroke = Slot, StrokeThickness = 1 },
                new TextBlock
                {
                    Text = ok ? "✓" : "✕", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = White, FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1),
                },
            },
        };
        Canvas.SetRight(mark, -4); Canvas.SetBottom(mark, -4);
        g.Children.Add(new Canvas { Width = size, Height = size, IsHitTestVisible = false, Children = { mark } });
        return g;
    }
}

/// <summary>
/// The Card while crafting: "CRAFT 18:05 Orichalcum Greaves" in the head with the newest craft on the right ("worked
/// +137K" in blue, "failed −39K" in orange), then "12 crafts · 3 failed · +1.12M" over the success bar and "sells for
/// 176K each", and the last three crafts as tiles. The farming head and figures come back when crafting ends.
/// </summary>
public sealed partial class HudCard
{
    FrameworkElement _farmHead = null!, _farmBody = null!;
    Grid _craftHead = null!, _craftBody = null!, _cBar = null!;
    Ellipse _cDot = null!;
    TextBlock _cTime = null!, _cRecipe = null!, _cNewest = null!, _cFigs = null!, _cSells = null!;
    StackPanel _cLine2 = null!, _cTiles = null!;
    string _craftLitId = "";

    /// <summary>The crafting head and body, laid over the farming ones in the card's stack; only one of each shows.</summary>
    void BuildCraft(FrameworkElement head, FrameworkElement body)
    {
        _farmHead = head; _farmBody = body;
        _cDot = Dot(7);
        var label = T("CRAFT", 11, CraftKit.Label, weight: FontWeights.SemiBold); label.Margin = new Thickness(6, 0, 0, 0);
        _cTime = T("", 13, Ink, mono: true); _cTime.Margin = new Thickness(6, 0, 0, 0);
        _cRecipe = T("", 13, Ink); _cRecipe.Margin = new Thickness(10, 0, 0, 0);
        _cNewest = T("", 13, Ink); _cNewest.Margin = new Thickness(8, 0, 0, 0); _cNewest.TextTrimming = TextTrimming.None;
        _craftHead = new Grid { Height = 18, Visibility = Visibility.Collapsed };
        foreach (var w in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) _craftHead.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        int col = 0;
        foreach (var e in new FrameworkElement[] { _cDot, label, _cTime, _cRecipe, _cNewest }) { Grid.SetColumn(e, col++); _craftHead.Children.Add(e); }

        _cFigs = new TextBlock { LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextTrimming = TextTrimming.CharacterEllipsis };
        _cBar = CraftKit.Bar(72, 6);
        _cSells = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        _cLine2 = new StackPanel { Orientation = Orientation.Horizontal, Height = 16, Margin = new Thickness(0, 3, 0, 0), Children = { _cBar, _cSells } };
        var figs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _cFigs, _cLine2 } };
        _cTiles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) };
        _craftBody = new Grid { Visibility = Visibility.Collapsed };
        _craftBody.ColumnDefinitions.Add(new ColumnDefinition());
        _craftBody.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _craftBody.Children.Add(figs);
        Grid.SetColumn(_cTiles, 1); _craftBody.Children.Add(_cTiles);

        var stack = (StackPanel)_card.Child;
        foreach (var (farm, craft) in new (FrameworkElement, FrameworkElement)[] { (head, _craftHead), (body, _craftBody) })
        {
            int at = stack.Children.IndexOf(farm);
            stack.Children.RemoveAt(at);
            stack.Children.Insert(at, new Grid { Children = { farm, craft } });
        }
    }

    /// <summary>The crafting state from the "craft" values: shown while "craft" is on, the farming head and figures otherwise.</summary>
    void CraftState(IReadOnlyDictionary<string, string> values)
    {
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        bool on = V("craft") == "1";
        if (Changed("craftOn", on ? "1" : ""))
        {
            _farmHead.Visibility = _farmBody.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            _craftHead.Visibility = _craftBody.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
        if (!on) return;

        bool live = V("paused").Length == 0 && V("show") != "dim";
        var crafts = CraftKit.Crafts(values);
        var newest = crafts.FirstOrDefault();
        if (Changed("craftHead", $"{live}|{V("craftTime")}|{V("craftRecipe")}|{newest?.Id}|{newest?.Worth}"))
        {
            SetDot(_cDot, live);
            _cTime.Text = V("craftTime"); _cTime.Foreground = live ? Ink : Faint;
            _cRecipe.Text = V("craftRecipe");
            _cNewest.Inlines.Clear();
            if (newest != null)
            {
                bool ok = CraftKit.Worked(newest);
                _cNewest.Inlines.Add(R(ok ? "worked" : "failed", 13, ok ? CraftKit.Ok2 : CraftKit.Fail));
                if (newest.Worth.Length > 0) { _cNewest.Inlines.Add(R("  ", 13, Ink)); _cNewest.Inlines.Add(R(newest.Worth, 13, ok ? Gold : CraftKit.Fail, mono: true)); }
            }
        }

        // "12 crafts · 3 failed · +1.12M" over the bar and "sells for 176K each"; before the first craft, a faint line
        if (Changed("craftFigs", $"{V("craftTries")}|{V("craftOk")}|{V("craftFail")}|{V("craftProfit")}|{V("craftSells")}"))
        {
            _cFigs.Inlines.Clear(); _cSells.Inlines.Clear();
            int.TryParse(V("craftOk").Replace(",", ""), out int ok);
            int.TryParse(V("craftFail").Replace(",", ""), out int fail);
            if (ok + fail == 0)
            {
                _cFigs.Inlines.Add(R("Waiting for the first craft", 13, Faint));
                _cLine2.Visibility = Visibility.Collapsed;
            }
            else
            {
                _cFigs.Inlines.Add(R(V("craftTries"), 13, Ink, mono: true));
                _cFigs.Inlines.Add(R(ok + fail == 1 ? " craft · " : " crafts · ", 13, Muted));
                _cFigs.Inlines.Add(R(V("craftFail"), 13, Ink, mono: true));
                _cFigs.Inlines.Add(R(" failed", 13, Muted));
                var profit = V("craftProfit");
                if (profit.Length > 0)
                {
                    _cFigs.Inlines.Add(R(" ·  ", 13, Muted));
                    _cFigs.Inlines.Add(R(profit, 18, profit.Contains('−') ? CraftKit.Fail : Gold, mono: true));
                }
                CraftKit.SetBar(_cBar, ok, fail);
                if (V("craftSells").Length > 0)
                {
                    _cSells.Inlines.Add(R("sells for ", 12, Faint));
                    _cSells.Inlines.Add(R(V("craftSells"), 12, Gold, mono: true));
                    _cSells.Inlines.Add(R(" each", 12, Faint));
                }
                else _cSells.Inlines.Add(R("no Market price yet", 12, Faint));
                _cLine2.Visibility = Visibility.Visible;
            }
        }

        // the last three crafts as tiles, newest first, the newest lit
        var tiles = crafts.Take(3).ToList();
        if (Changed("craftTiles", string.Join("|", tiles.Select(t => $"{t.Id}:{t.Icon}"))))
        {
            _cTiles.Children.Clear();
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = CraftKit.Tile(tiles[i], 28);
                t.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
                t.Opacity = i switch { 0 => 1, 1 => .9, _ => .75 };
                if (i == 0 && t is Grid g) g.Children.Add(CraftLight(tiles[0]));
                _cTiles.Children.Add(t);
            }
        }
    }

    /// <summary>The newest craft's light: a gold frame and soft glow, fading over 5 s from the craft (held in a still render).</summary>
    FrameworkElement CraftLight(Pick p)
    {
        var c = Rgb(GoldC);
        var light = new Grid { IsHitTestVisible = false };
        light.Children.Add(new Border { Margin = new Thickness(-1), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Frozen(Color.FromArgb(0x40, c.R, c.G, c.B)) });
        var glow = Glow(c, 5, 10, .35);
        glow.BorderBrush = Frozen(Color.FromArgb(0xCC, c.R, c.G, c.B));
        light.Children.Add(glow);
        bool fresh = p.Id != _craftLitId;
        _craftLitId = p.Id;
        if (Still) return light;
        var age = p.At == DateTime.MinValue ? 0 : (DateTime.UtcNow - p.At).TotalSeconds;
        if (age >= 5 || !fresh) { light.Opacity = 0; return light; }
        light.Opacity = 1 - age / 5;
        light.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(5 - age)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        return light;
    }
}

/// <summary>
/// The Ticker while crafting: the pill reads "CRAFT +1.12M | 12 · 3 failed" with the small success bar (no timer, to fit
/// 270 px), and each craft slides in as a toast, "Orichalcum Greaves · worked +137K" with a blue edge or "· failed −39K"
/// with an orange one, and fades like a pickup. The farming pill and pickups come back when crafting ends.
/// </summary>
public sealed partial class HudTicker
{
    UIElement _farmRow = null!;
    StackPanel _craftRow = null!;
    Ellipse _cDot = null!;
    TextBlock _cProfit = null!, _cCount = null!;
    Grid _cBar = null!;

    void BuildCraft()
    {
        _farmRow = _pill.Child;
        _cDot = Dot(8); _cDot.Margin = new Thickness(0, 0, 7, 0);
        var label = T("CRAFT", 11, CraftKit.Label, weight: FontWeights.SemiBold); label.Margin = new Thickness(0, 0, 7, 0);
        _cProfit = T("", 15, Gold, mono: true);
        _cCount = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _cBar = CraftKit.Bar(40, 5); _cBar.Margin = new Thickness(8, 0, 0, 0);
        _craftRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _cDot, label, _cProfit, Rule(14, 9), _cCount, _cBar } };
    }

    /// <summary>The pill in the crafting state while "craft" is on; true then, and the stack shows the crafts instead of the pickups.</summary>
    bool CraftPill(IReadOnlyDictionary<string, string> values)
    {
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        bool on = V("craft") == "1";
        if (Changed("craftOn", on ? "1" : "")) _pill.Child = on ? _craftRow : _farmRow;
        if (!on) return false;
        bool live = V("paused").Length == 0 && V("show") != "dim";
        if (Changed("craftPill", $"{live}|{V("craftProfit")}|{V("craftTries")}|{V("craftOk")}|{V("craftFail")}"))
        {
            SetDot(_cDot, live);
            var profit = V("craftProfit");
            _cProfit.Text = profit; _cProfit.Foreground = profit.Contains('−') ? CraftKit.Fail : Gold;
            _cProfit.Visibility = profit.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            int.TryParse(V("craftOk").Replace(",", ""), out int ok);
            int.TryParse(V("craftFail").Replace(",", ""), out int fail);
            _cCount.Inlines.Clear();
            if (ok + fail == 0) _cCount.Inlines.Add(R("no crafts yet", 12, Muted));
            else
            {
                _cCount.Inlines.Add(R(V("craftTries"), 12, Muted, mono: true));
                _cCount.Inlines.Add(R(" · ", 12, Muted));
                _cCount.Inlines.Add(R(V("craftFail"), 12, Muted, mono: true));
                _cCount.Inlines.Add(R(" failed", 12, Muted));
            }
            CraftKit.SetBar(_cBar, ok, fail);
        }
        return true;
    }

    /// <summary>One craft: the recipe's tile with ✓ or ✕, "Orichalcum Greaves · worked", and what it made in gold or cost in orange.</summary>
    Border CraftToast(Pick p)
    {
        bool ok = CraftKit.Worked(p);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(CraftKit.Tile(p, 24));
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(9, 0, 0, 0) };
        name.Inlines.Add(R(p.Name, 13, Ink));
        name.Inlines.Add(R(" · " + p.Note, 12, ok ? Faint : CraftKit.Fail));
        Grid.SetColumn(name, 1); g.Children.Add(name);
        var worth = T(p.Worth, 13, ok ? Gold : CraftKit.Fail, mono: true); worth.Margin = new Thickness(9, 0, 0, 0);
        Grid.SetColumn(worth, 2); g.Children.Add(worth);
        return new Border
        {
            Width = 270, Height = 34, Padding = new Thickness(6, 0, 10, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = ok ? CraftKit.Ok2 : CraftKit.Fail, Background = Wash("pick", false), Child = g,
        };
    }
}
