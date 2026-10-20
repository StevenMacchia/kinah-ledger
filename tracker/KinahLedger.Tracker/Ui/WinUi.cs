using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KinahLedger.Ui;

public static class WinUi
{
    static bool _init;
    public static void Init()
    {
        if (_init) return;
        _init = true;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }

    /// <summary>Full-screen dim overlay; drag a box around the chat's loot lines. Esc cancels.</summary>
    public static Rectangle? PickRegion()
    {
        Init();
        using var f = new RegionPickerForm();
        return f.ShowDialog() == DialogResult.OK ? f.Selection : null;
    }
}

sealed class RegionPickerForm : Form
{
    Point _start; Rectangle _sel; bool _dragging;
    public Rectangle Selection => _sel;

    public RegionPickerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.35;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } }
    protected override void OnMouseDown(MouseEventArgs e) { _dragging = true; _start = PointToScreen(e.Location); _sel = new Rectangle(_start, Size.Empty); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = PointToScreen(e.Location);
        _sel = Rectangle.FromLTRB(Math.Min(_start.X, p.X), Math.Min(_start.Y, p.Y), Math.Max(_start.X, p.X), Math.Max(_start.Y, p.Y));
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        if (_sel.Width < 20 || _sel.Height < 10) return;
        DialogResult = DialogResult.OK;
        Close();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var font = new Font("Segoe UI", 14f);
        e.Graphics.DrawString("Drag a box around the chat lines that show loot and Kinah.  Esc to cancel.", font, Brushes.White, 24, 24);
        if (_sel.Width == 0) return;
        var r = RectangleToClient(_sel);
        using var pen = new Pen(Color.DeepSkyBlue, 2);
        using var fill = new SolidBrush(Color.FromArgb(60, 76, 195, 245));
        e.Graphics.FillRectangle(fill, r);
        e.Graphics.DrawRectangle(pen, r);
        e.Graphics.DrawString($"{_sel.Width} × {_sel.Height}", font, Brushes.White, r.Left, r.Bottom + 6);
    }
}

/// <summary>Small always-on-top readout. Drag it anywhere; it sits over a borderless-window game.</summary>
public sealed class HudForm : Form
{
    readonly Label _big = new(), _small = new(), _foot = new();

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2;

    public HudForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Size = new Size(300, 120);
        Location = new Point(wa.Right - Width - 24, wa.Top + 24);
        BackColor = Color.FromArgb(10, 15, 24);
        Opacity = 0.92;
        Padding = new Padding(12, 10, 12, 8);

        _big.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
        _big.ForeColor = Color.FromArgb(241, 208, 140);
        _big.AutoSize = false; _big.Dock = DockStyle.Top; _big.Height = 34;
        _small.Font = new Font("Segoe UI", 9.5f);
        _small.ForeColor = Color.FromArgb(186, 198, 214);
        _small.AutoSize = false; _small.Dock = DockStyle.Top; _small.Height = 40;
        _foot.Font = new Font("Segoe UI", 8.5f);
        _foot.ForeColor = Color.FromArgb(98, 113, 138);
        _foot.AutoSize = false; _foot.Dock = DockStyle.Top; _foot.Height = 18;
        Controls.Add(_foot); Controls.Add(_small); Controls.Add(_big);

        foreach (Control c in new Control[] { this, _big, _small, _foot })
            c.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0); } };
        _big.Text = "Kinah Ledger";
        _small.Text = "waiting for loot lines…";
    }

    public void Update(string big, string small, string foot)
    {
        if (IsDisposed) return;
        try { BeginInvoke(() => { _big.Text = big; _small.Text = small; _foot.Text = foot; }); } catch (InvalidOperationException) { /* closing */ }
    }

    /// <summary>Runs the window on its own thread so the reader loop never waits on the UI.</summary>
    public static HudForm Launch()
    {
        WinUi.Init();
        HudForm? form = null;
        using var ready = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            form = new HudForm();
            form.HandleCreated += (_, _) => ready.Set();
            Application.Run(form);
        }) { IsBackground = true, Name = "hud" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        ready.Wait(5000);
        return form!;
    }
}
