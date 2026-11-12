using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The character in the header and its menu (Session/Characters.cs): which character is counted for, read off the class
/// icon between the health and mana bars, switched by itself when another one is in the world; the menu names it,
/// switches by hand (two characters of one class can't be told apart by the icon) or splits one off as a new character.
/// </summary>
public partial class MainWindow
{
    /// <summary>The demo's character for renders (ui-shot); null otherwise.</summary>
    string? _demoCharacter;

    /// <summary>The character counted for now, as the header says it; empty before the first one was seen.</summary>
    string CharacterLabel => _demo ? _demoCharacter ?? "" : Characters.Current is { } c ? Characters.Label(c) : "";

    /// <summary>The header's line: the character (when known) in ink, then the server and region, muted.</summary>
    void FillServerChip(string serverText)
    {
        ServerChip.Inlines.Clear();
        var who = CharacterLabel;   // the chip is hidden on the first-run screen anyway
        if (who.Length > 0) { var name = new Run(who); name.SetResourceReference(TextElement.ForegroundProperty, "Ink"); ServerChip.Inlines.Add(name); ServerChip.Inlines.Add(new Run(" · ")); }   // follows the look when it changes
        ServerChip.Inlines.Add(new Run(serverText));
        ServerChip.ToolTip = _welcome ? "Pick your server below."
            : who.Length > 0 ? $"Counting for {who}, read from the class icon between the health and mana bars. Click to name it or switch characters."
            : "Your server. Click for the character and server: the character is read from the class icon between the health and mana bars once you're in the world.";
    }

    /// <summary>Hooks the watcher's character events (from the window's start, when it watches the game).</summary>
    void InitCharacters()
    {
        // a name read off the nameplate: the header follows
        _watcher.CharacterNamed += () => Dispatcher.BeginInvoke(() => ServerRowState());
        // a 0.10.51 or 0.10.52 install, whose first character seen took all the history kept before: put it right, once
        if (!_demo && Characters.NeedsPlacing) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => _ = PlaceHistoryAsync());
        _watcher.CharacterSeen += v => Dispatcher.BeginInvoke(async () =>
        {
            try { await OnCharacterSeen(v); }
            catch (Exception ex) { CrashLog.Write("character switch", ex); }
            finally { _watcher.CharacterHandled(); }
        });
    }

    /// <summary>A steady class icon that isn't the character counted for now.</summary>
    async Task OnCharacterSeen(CharacterSpotter.Verdict v)
    {
        if (_demo) return;
        var server = _prefs.Server;
        if (Characters.Current == null)
        {
            // the first character seen after the update takes over everything kept so far, then the old runs are sorted
            var first = v.Character ?? Characters.Add(v.Print, server);
            SaveCharacterState();
            if (_watcher.Chat is { } ch) ch.Instances.Reload(() => Characters.Adopt(first)); else Characters.Adopt(first);
            BestRuns.Reset();
            ServerRowState(); Redraw();
            Say($"Counting for {Characters.Label(first)} from now on. Each character keeps its own numbers; click the name at the top to name it.", "Good");
            _statusHoldUntil = DateTime.UtcNow.AddSeconds(15);
            if (Characters.NeedsPlacing) _ = PlaceHistoryAsync();
            return;
        }
        var c = v.Character ?? Characters.Add(v.Print, server);
        await SwitchCharacter(c, v.Kind == CharacterSpotter.Kind.New ? "A new character" : Characters.Label(c));
    }

    bool _placing;

    /// <summary>
    /// The history kept before characters put with the characters it belongs to (Session/CharacterSort.cs Place), once:
    /// the pictures are read off the window's thread (every kept picture is opened), then the files change between a save
    /// and a load of the character counted for, so nothing it holds in memory is written back over them.
    /// </summary>
    async Task PlaceHistoryAsync()
    {
        if (_demo || _placing || Characters.Adopter is not { } adopter) return;
        _placing = true;
        try
        {
            var server = _prefs.Server;
            var shots = await Task.Run(() => CharacterSort.ReadShots(EventShotRecovery.ShotsDir));
            SaveCharacterState();
            var outcome = CharacterSort.Place(adopter, server, shots, act => { if (_watcher.Chat is { } ch) ch.Instances.Reload(act); else act(); });
            ReloadCharacterState();
            var said = new List<string>();
            if (outcome.Named.Count > 0) said.Add("named " + string.Join(", ", outcome.Named.Select(x => x.Name)) + " from their pictures");
            if (outcome.Moved > 0) said.Add("runs sorted by class icon: " + string.Join(", ", outcome.To.Select(x => $"{x.Runs} to {Characters.Label(x.Who)}")));
            if (outcome.HistoryMoved && outcome.Main != null) said.Add($"the days, bests, goals and quest points from before go to {Characters.Label(outcome.Main)}, your most played");
            if (said.Count > 0) { Say("Characters: " + string.Join("; ", said) + ".", "Faint"); _statusHoldUntil = DateTime.UtcNow.AddSeconds(20); }
        }
        catch (Exception ex) { CrashLog.Write("characters place", ex); Characters.MarkSorted(); }
        finally { _placing = false; }
    }

    /// <summary>The character counted for: its today, runs, goals, quest points and bests read again from its folder.</summary>
    void ReloadCharacterState()
    {
        _watcher.LoadDay();
        _watcher.Chat?.Instances.Reload();
        _goals = Goals.Load(); _questLog = Quests.Load(); _questLoaded = true;
        BestRuns.Reset();
        ServerRowState(); RedrawGoals(); Redraw();
    }

    /// <summary>The current character's numbers written to its folder: today, goals, quests.</summary>
    void SaveCharacterState()
    {
        _watcher.SaveDay();
        SaveGoals();
        SaveQuests();
    }

    bool _switchingCharacter;

    /// <summary>
    /// Counts for another character from here on: the run and the always-on count close as for any character switch
    /// (StartFresh), the old character's numbers are saved, and the new one's today, days, runs, goals and quest points load.
    /// </summary>
    async Task SwitchCharacter(Character c, string why)
    {
        if (_demo || _switchingCharacter || Characters.Current?.Id == c.Id) return;
        _switchingCharacter = true;
        ForgetRare();   // the other character's drop never leads this one's moment (Ui/MainWindow.RareMoment.cs)
        try
        {
            CrashLog.Note($"character: counting for {Characters.Label(c)} now (was {(Characters.Current is { } was ? Characters.Label(was) : "nobody")})");
            await StartFresh(why == "A new character" ? "Another character" : $"{why} is in the world");
            SaveCharacterState();
            Characters.SetCurrent(c);
            ReloadCharacterState();
            Say(why == "A new character"
                ? $"A character not seen before: counting for {Characters.Label(c)}, with numbers of its own. Click its name at the top to name it."
                : $"Counting for {Characters.Label(c)} now: its own today, days, runs, goals and quest points.", "Good");
            _statusHoldUntil = DateTime.UtcNow.AddSeconds(15);
        }
        finally { _switchingCharacter = false; }
    }

    /// <summary>The menu under the character and server line.</summary>
    void ShowCharacterMenu()
    {
        if (_welcome) return;
        var cur = _demo ? DemoCharacters()[0] : Characters.Current;
        var here = (_demo ? DemoCharacters() : Characters.All).Where(c => c.Server.Length == 0 || string.Equals(c.Server, _prefs.Server, StringComparison.OrdinalIgnoreCase)).ToList();
        ShowMenu(ServerChip, CharacterMenu(cur, here));
        // the menu hangs under the chip, from its left edge
        if (_card != null) _card.HorizontalOffset = 0;
    }

    /// <summary>The demo's characters, for the menu's render: the one counted for and two others.</summary>
    List<Character> DemoCharacters() => new()
    {
        new() { Id = "c1", Name = _demoCharacter == "Character 1" ? "" : _demoCharacter ?? "", Server = "Siel (Elyos)", LastSeen = DateTime.UtcNow },
        new() { Id = "c2", Name = "", Server = "Siel (Elyos)", LastSeen = DateTime.UtcNow.AddHours(-3) },
        new() { Id = "c3", Name = "", Server = "Siel (Elyos)", LastSeen = DateTime.UtcNow.AddDays(-1) },
    };

    /// <summary>
    /// The menu's entries: name the character counted for, count for another one of this server, split one off, and the
    /// server. Before any character was seen, a line on how it is found.
    /// </summary>
    List<MenuEntry> CharacterMenu(Character? cur, List<Character> here)
    {
        var entries = new List<MenuEntry>();
        if (cur == null)
            entries.Add(new MenuEntry("The character is read from the class icon between the health and mana bars, once you're in the world. Each one keeps its own numbers.", Info: true));
        else
        {
            entries.Add(new MenuEntry("Name this character…", () => { if (!_demo) OpenNameSheet(cur); }, Sub: $"Counting for {Characters.Label(cur)}"));
            var others = here.Where(c => c.Id != cur.Id).ToList();
            if (others.Count > 0)
            {
                entries.Add(MenuEntry.Line);
                foreach (var o in others)
                {
                    var who = o;
                    entries.Add(new MenuEntry($"Count for {Characters.Label(who)}", () => { if (!_demo) _ = SwitchByHand(who); },
                        Sub: $"Last seen {who.LastSeen.ToLocalTime():MMM d, HH:mm}"));
                }
            }
            entries.Add(MenuEntry.Line);
            entries.Add(new MenuEntry("Count as a new character", () => { if (!_demo) _ = SplitNewCharacter(); },
                Sub: "For a second character of the same class, which the icon can't tell apart."));
        }
        entries.Add(MenuEntry.Line);
        entries.Add(new MenuEntry("Server and region…", () => OpenOptions(OptionsWindow.Page.Server), Sub: Server is { } s ? $"{s.Label} · {s.Region.Name}" : "None chosen"));
        return entries;
    }

    /// <summary>Layout render: the character menu as the demo's header opens it (ui-character-menu.png).</summary>
    internal void ShotCharacterMenu(string dir)
    {
        var all = DemoCharacters();
        SaveCard(BuildMenu(CharacterMenu(all[0], all)), System.IO.Path.Combine(dir, "ui-character-menu.png"));
    }

    /// <summary>
    /// The player picks a character. Nothing is taught: when another class's icon is on screen the reader switches back
    /// once it is steady; two characters of one class stay put, as the one counted for keeps an icon it matches.
    /// </summary>
    async Task SwitchByHand(Character c)
    {
        _watcher.CharacterHandled();
        await SwitchCharacter(c, Characters.Label(c));
    }

    /// <summary>A second character of a class already counted: a new character with the icon on screen, counted from now on.</summary>
    async Task SplitNewCharacter()
    {
        var c = Characters.Add(_watcher.Spotter.Print ?? "", _prefs.Server);
        _watcher.CharacterHandled();
        await SwitchCharacter(c, "A new character");
        OpenNameSheet(c);
    }

    /// <summary>A small card to name a character: a box, Save and Cancel.</summary>
    void OpenNameSheet(Character c)
    {
        if (_card != null) _card.IsOpen = false;
        var (card, body) = CkShell(320);
        var title = CkText("Name this character", CkInk, 14); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(20, 14, 20, 4);
        body.Children.Add(title);
        var hint = CkText("As it is in the game, or anything that tells it apart. Only on this PC.", CkFaint, 12);
        hint.TextWrapping = TextWrapping.Wrap; hint.TextTrimming = TextTrimming.None; hint.Margin = new Thickness(20, 0, 20, 10);
        body.Children.Add(hint);
        var box = new TextBox
        {
            Height = 34, FontSize = 13, Padding = new Thickness(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center, Text = c.Name, MaxLength = 24,
            Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), Foreground = Ck(CkInk), CaretBrush = Ck(CkGold), Margin = new Thickness(20, 0, 20, 12),
        };
        AutomationProperties.SetName(box, "Character name");
        body.Children.Add(box);
        var acts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 20, 16) };
        var save = new Button { Content = "Save", Height = 30, Padding = new Thickness(16, 0, 16, 0), FontSize = 12.5 };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        var cancel = new Button { Content = "Cancel", Height = 30, Padding = new Thickness(14, 0, 14, 0), FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0) };
        void Save()
        {
            Characters.Rename(c, box.Text);
            if (_card != null) _card.IsOpen = false;
            ServerRowState(); Redraw();
        }
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => { if (_card != null) _card.IsOpen = false; };
        box.KeyDown += (_, k) => { if (k.Key == Key.Enter) { k.Handled = true; Save(); } };
        acts.Children.Add(save); acts.Children.Add(cancel);
        body.Children.Add(acts);
        _card = new InCard { Child = card, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Bottom, PlacementTarget = ServerChip };
        _card.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        _card.IsOpen = true;
    }
}
