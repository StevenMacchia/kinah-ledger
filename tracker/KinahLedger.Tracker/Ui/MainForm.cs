using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>The window that opens on double-click: pick the chat area once, choose what you're farming, press Start.</summary>
public sealed class MainForm : Form
{
    static readonly string[] Activities =
    {
        "Fire Temple", "Expedition: Conquest", "Dying Dramata's Nest", "Cradle of Nihility", "Shattered Arkanis",
        "Submerged Life Temple", "Corroded Decontamination Facility", "Field boss rotation", "Spacetime Rift",
        "Abyss elites", "Open-world elites", "Gathering: ore", "Gathering: hides and herbs", "Daily duty missions", "Other"
    };

    static readonly Color Bg = Color.FromArgb(10, 15, 24), Panel = Color.FromArgb(17, 26, 40), Line = Color.FromArgb(30, 42, 61),
        Ink = Color.FromArgb(233, 239, 247), Muted = Color.FromArgb(135, 151, 174), Faint = Color.FromArgb(98, 113, 138),
        Gold = Color.FromArgb(241, 208, 140), Violet = Color.FromArgb(197, 187, 255), Accent = Color.FromArgb(76, 195, 245),
        Good = Color.FromArgb(78, 208, 140), Bad = Color.FromArgb(255, 123, 107), BtnTop = Color.FromArgb(58, 141, 230), BtnBot = Color.FromArgb(39, 102, 184);

    readonly Settings _settings;
    readonly UserPrefs _prefs = UserPrefs.Load();
    readonly ItemDictionary _items;
    TrackerSession? _session;
    HudForm? _hud;
    Rectangle? _region = RegionStore.Load();

    readonly Label _regionLbl = new(), _kinahHr = new(), _lootHr = new(), _totals = new(), _status = new();
    readonly Button _pick = new(), _test = new(), _start = new();
    readonly ComboBox _activity = new();
    readonly CheckBox _hudChk = new(), _shareChk = new();
    readonly ListBox _lines = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public MainForm(Settings settings)
    {
        _settings = settings;
        _items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);

        Text = "Kinah Ledger";
        Icon = MakeIcon();
        BackColor = Bg; ForeColor = Ink;
        Font = new Font("Segoe UI", 9.75f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(560, 520);
        StartPosition = _prefs.WinX is int x && _prefs.WinY is int y && Screen.AllScreens.Any(s => s.WorkingArea.Contains(x, y)) ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
        if (StartPosition == FormStartPosition.Manual) Location = new Point(_prefs.WinX!.Value, _prefs.WinY!.Value);

        Build();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        UpdateRegionLabel();
        FormClosing += OnClosing;
    }

    void Build()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(18, 14, 18, 10), BackColor = Bg };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // title
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // setup
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // stats
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // lines
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // status
        Controls.Add(root);

        // title
        var title = new Label { Text = "KINAH LEDGER", Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Ink, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        var sub = new Label { Text = "Unofficial Aion 2 farming tracker. Reads loot lines from the chat on your screen; nothing touches the game.", ForeColor = Muted, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 0, 0, 12) };
        var head = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        head.Controls.Add(title); head.Controls.Add(sub);
        root.Controls.Add(head, 0, 0);

        // setup card
        var setup = Card();
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, BackColor = Panel, Padding = new Padding(12, 10, 12, 10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(Lbl("Chat area"), 0, 0);
        _regionLbl.AutoSize = true; _regionLbl.ForeColor = Ink; _regionLbl.Margin = new Padding(0, 7, 0, 0);
        grid.Controls.Add(_regionLbl, 1, 0);
        var pickRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, BackColor = Panel };
        Style(_pick, "Pick chat area", primary: false); _pick.Click += (_, _) => Pick();
        Style(_test, "Test read", primary: false); _test.Click += async (_, _) => await TestRead();
        pickRow.Controls.Add(_pick); pickRow.Controls.Add(_test);
        grid.Controls.Add(pickRow, 2, 0);

        grid.Controls.Add(Lbl("Farming"), 0, 1);
        _activity.DropDownStyle = ComboBoxStyle.DropDown; _activity.Items.AddRange(Activities); _activity.Text = _prefs.Activity;
        _activity.BackColor = Bg; _activity.ForeColor = Ink; _activity.FlatStyle = FlatStyle.Flat; _activity.Width = 280; _activity.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(_activity, 1, 1);

        var opts = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 0), BackColor = Panel };
        Check(_hudChk, "Show panel over the game", _prefs.Hud);
        Check(_shareChk, "Share runs anonymously", _prefs.Share);
        new ToolTip().SetToolTip(_shareChk, "When a run ends, its totals go to the public tables: region, activity, gear bracket, minutes, kills, Kinah, item counts and a random install ID.\nNever your character name, server or account. Nothing is sent until the site's endpoint is configured.");
        opts.Controls.Add(_hudChk); opts.Controls.Add(_shareChk);
        grid.Controls.Add(opts, 1, 2);

        Style(_start, "Start tracking", primary: true); _start.Width = 150; _start.Height = 38; _start.Margin = new Padding(0, 6, 0, 0);
        _start.Click += async (_, _) => { if (_session == null) await StartTracking(); else await StopTracking(); };
        grid.Controls.Add(_start, 2, 2);
        setup.Controls.Add(grid);
        root.Controls.Add(setup, 0, 1);

        // stats
        var stats = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8), BackColor = Bg };
        for (int i = 0; i < 3; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        stats.Controls.Add(Stat("KINAH / HR", _kinahHr, Gold), 0, 0);
        stats.Controls.Add(Stat("LOOT ITEMS / HR", _lootHr, Violet), 1, 0);
        stats.Controls.Add(Stat("THIS SESSION", _totals, Ink), 2, 0);
        root.Controls.Add(stats, 0, 2);

        // lines
        _lines.Dock = DockStyle.Fill; _lines.BackColor = Panel; _lines.ForeColor = Muted; _lines.BorderStyle = BorderStyle.None;
        _lines.Font = new Font("Consolas", 9.5f); _lines.IntegralHeight = false; _lines.SelectionMode = SelectionMode.None;
        var linesCard = Card(); linesCard.Padding = new Padding(10, 8, 10, 8); linesCard.Controls.Add(_lines);
        root.Controls.Add(linesCard, 0, 3);

        // status
        _status.AutoSize = true; _status.ForeColor = Faint; _status.Margin = new Padding(0, 8, 0, 0);
        _status.Text = "Not tracking.";
        root.Controls.Add(_status, 0, 4);

        _kinahHr.Text = "0"; _lootHr.Text = "0"; _totals.Text = "00:00:00";
    }

    /* ---------- actions ---------- */

    void Pick()
    {
        var wasTopMost = TopMost;
        Hide();
        try { var r = WinUi.PickRegion(); if (r != null) { _region = r; RegionStore.Save(r.Value); } }
        finally { Show(); TopMost = wasTopMost; UpdateRegionLabel(); }
    }

    async Task TestRead()
    {
        if (_region == null) { Pick(); if (_region == null) return; }
        _test.Enabled = false; _status.Text = "Reading…";
        try
        {
            var reader = new ScreenReader(_region.Value, _settings.Ocr.Scale);
            var parser = new ChatParser(_items);
            var lines = await Task.Run(() => reader.ReadAsync());
            _lines.Items.Clear();
            int hits = 0;
            foreach (var l in lines)
            {
                var evs = parser.Parse(l, DateTime.UtcNow).ToList();
                hits += evs.Count;
                _lines.Items.Add((evs.Count > 0 ? "✓ " : "  ") + l);
            }
            _status.Text = lines.Count == 0
                ? "Nothing recognized. Use borderless or windowed mode and make sure the box covers the chat text."
                : $"{lines.Count} line(s) read, {hits} look like loot. If loot lines aren't ticked, try a bigger chat font or a tighter box.";
        }
        catch (Exception ex) { _status.Text = ex.Message; _status.ForeColor = Bad; }
        finally { _test.Enabled = true; }
    }

    async Task StartTracking()
    {
        if (_region == null) { Pick(); if (_region == null) return; }
        SavePrefs();
        try
        {
            _settings.Upload.Enabled = _shareChk.Checked;
            _session = new TrackerSession(_region.Value, _settings, _items, _activity.Text.Trim().Length > 0 ? _activity.Text.Trim() : "unknown");
            _session.LootLine += l => BeginInvoke(() => { _lines.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {l}"); while (_lines.Items.Count > 200) _lines.Items.RemoveAt(_lines.Items.Count - 1); });
            _lines.Items.Clear();
            _session.Start();
            if (_hudChk.Checked) _hud = HudForm.Launch();
            _start.Text = "Stop"; _pick.Enabled = false; _test.Enabled = false; _activity.Enabled = false;
            _status.ForeColor = Faint; _status.Text = "Tracking. Loot lines appear below as they're read.";
        }
        catch (Exception ex) { _session = null; _status.Text = ex.Message; _status.ForeColor = Bad; }
        await Task.CompletedTask;
    }

    async Task StopTracking()
    {
        var s = _session; if (s == null) return;
        _start.Enabled = false; _status.Text = "Stopping…";
        await s.StopAsync();
        CloseHud();
        string msg;
        if (!s.Eligible) msg = "Run too short to share (under 5 minutes or no loot). Kept on this PC.";
        else if (!_shareChk.Checked) msg = "Run kept on this PC (sharing is off).";
        else if (string.IsNullOrWhiteSpace(_settings.Upload.Endpoint)) msg = "Run kept on this PC (sharing isn't live yet).";
        else msg = await s.UploadAsync(_settings) ? "Run shared anonymously." : "Couldn't reach the site; run kept on this PC.";
        _session = null;
        Refresh();
        _start.Text = "Start tracking"; _start.Enabled = true; _pick.Enabled = true; _test.Enabled = true; _activity.Enabled = true;
        _status.ForeColor = Faint; _status.Text = msg;
    }

    void CloseHud() { var h = _hud; _hud = null; if (h != null && !h.IsDisposed) { try { h.BeginInvoke(h.Close); } catch (InvalidOperationException) { } } }

    new void Refresh()
    {
        var s = _session; if (s == null) return;
        var e = s.Engine; double h = e.Hours;
        long items = e.Items.Values.Sum() + e.ItemsBound.Values.Sum();
        _kinahHr.Text = Fk((e.Kinah + e.KinahBound) / h);
        _lootHr.Text = Fk(items / h);
        _totals.Text = $"{e.Elapsed:hh\\:mm\\:ss}\n{Fk(e.Kinah)} Kinah · {Fk(e.KinahBound)} bound\n{items} items · {e.Kills} kills";
        _status.Text = s.LastError.Length > 0 ? "Read problem: " + s.LastError : $"Tracking {s.Activity} · {s.Reads} reads · every {_settings.Ocr.IntervalMs} ms" + (s.Unaligned > 0 ? $" · {s.Unaligned} skipped" : "");
        _hud?.Update($"{Fk((e.Kinah + e.KinahBound) / h)} Kinah/hr",
            $"{Fk(e.Kinah)} tradeable · {Fk(e.KinahBound)} bound\n{items} items · {e.Kills} kills · {e.Elapsed:hh\\:mm\\:ss}",
            s.LastLine.Length > 44 ? s.LastLine[..43] + "…" : s.LastLine);
    }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        SavePrefs();
        var s = _session;
        if (s != null)
        {
            e.Cancel = true;
            _ = StopTracking().ContinueWith(_ => BeginInvoke(Close));
        }
        else CloseHud();
    }

    void SavePrefs()
    {
        _prefs.Activity = _activity.Text; _prefs.Hud = _hudChk.Checked; _prefs.Share = _shareChk.Checked;
        _prefs.WinX = Location.X; _prefs.WinY = Location.Y;
        _prefs.Save();
    }

    void UpdateRegionLabel()
    {
        _regionLbl.Text = _region is Rectangle r ? $"{r.Width} × {r.Height} px at {r.X}, {r.Y}" : "Not set. Pick the chat lines that show loot and Kinah.";
        _regionLbl.ForeColor = _region == null ? Gold : Ink;
    }

    /* ---------- small helpers ---------- */

    static Panel Card() => new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Panel, Padding = Padding.Empty, Margin = new Padding(0, 0, 0, 0) };
    static Label Lbl(string t) => new() { Text = t, ForeColor = Muted, AutoSize = true, Margin = new Padding(0, 7, 0, 0) };
    static void Check(CheckBox c, string text, bool on) { c.Text = text; c.Checked = on; c.AutoSize = true; c.ForeColor = Ink; c.Margin = new Padding(0, 0, 18, 0); c.FlatStyle = FlatStyle.Flat; }
    static Control Stat(string label, Label value, Color color)
    {
        var p = new Panel { BackColor = Panel, Height = 86, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), Padding = new Padding(12, 8, 12, 8) };
        var l = new Label { Text = label, ForeColor = Faint, Font = new Font("Segoe UI", 8f, FontStyle.Bold), AutoSize = true, Location = new Point(12, 8) };
        value.ForeColor = color; value.Font = new Font("Segoe UI", color == Ink ? 9.5f : 20f, color == Ink ? FontStyle.Regular : FontStyle.Bold);
        value.AutoSize = false; value.Location = new Point(12, 28); value.Size = new Size(200, 52); value.TextAlign = ContentAlignment.TopLeft;
        p.Controls.Add(l); p.Controls.Add(value);
        return p;
    }
    static void Style(Button b, string text, bool primary)
    {
        b.Text = text; b.AutoSize = true; b.FlatStyle = FlatStyle.Flat; b.Height = 32; b.Padding = new Padding(10, 0, 10, 0); b.Margin = new Padding(0, 2, 8, 2);
        b.FlatAppearance.BorderColor = primary ? BtnTop : Line; b.FlatAppearance.BorderSize = 1;
        b.BackColor = primary ? BtnBot : Panel; b.ForeColor = Ink; b.Cursor = Cursors.Hand;
        if (primary) b.Paint += (s, e) =>
        {
            using var br = new LinearGradientBrush(b.ClientRectangle, BtnTop, BtnBot, LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(br, b.ClientRectangle);
            TextRenderer.DrawText(e.Graphics, b.Text, new Font(b.Font, FontStyle.Bold), b.ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using var pen = new Pen(BtnTop); e.Graphics.DrawRectangle(pen, 0, 0, b.Width - 1, b.Height - 1);
        };
    }
    static string Fk(double v) => Math.Abs(v) >= 1e6 ? $"{v / 1e6:0.00}M" : Math.Abs(v) >= 1e3 ? $"{v / 1e3:0}K" : $"{v:0}";

    /// <summary>A stacked-coins mark drawn in code, so the window and taskbar have an icon without shipping an .ico yet.</summary>
    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var dark = new SolidBrush(Color.FromArgb(26, 39, 64));
            using var gold = new SolidBrush(Color.FromArgb(227, 181, 91));
            using var edge = new Pen(Color.FromArgb(241, 208, 140), 1.5f);
            var hex = new[] { new PointF(16, 2), new PointF(29, 9), new PointF(29, 23), new PointF(16, 30), new PointF(3, 23), new PointF(3, 9) };
            g.FillPolygon(dark, hex); g.DrawPolygon(edge, hex);
            var inner = new[] { new PointF(16, 9), new PointF(22, 12.5f), new PointF(22, 19.5f), new PointF(16, 23), new PointF(10, 19.5f), new PointF(10, 12.5f) };
            g.FillPolygon(gold, inner);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
