using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DRect = System.Drawing.Rectangle;

namespace KinahLedger.Ui;

/// <summary>Full-screen dim overlay; drag a box around the chat's loot lines. Esc cancels. Returns screen pixels.</summary>
public sealed class RegionPickerWindow : Window
{
    readonly Canvas _canvas = new();
    readonly Rectangle _box = new() { Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0xF5)), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x4C, 0xC3, 0xF5)), Visibility = Visibility.Collapsed };
    readonly TextBlock _size = new() { Foreground = Brushes.White, FontSize = 13, Visibility = Visibility.Collapsed };
    Point _start; bool _drag;

    public DRect? Result { get; private set; }

    public RegionPickerWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0x5C, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        Cursor = Cursors.Cross;

        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0A, 0x0F, 0x18)), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 10, 16, 10),
            Child = new TextBlock { Text = "Drag a box around the chat lines that show loot and Kinah.   Esc to cancel", Foreground = Brushes.White, FontSize = 15, FontFamily = new FontFamily("Segoe UI") }
        };
        Canvas.SetLeft(hint, 28); Canvas.SetTop(hint, 28);
        _canvas.Children.Add(hint);
        _canvas.Children.Add(_box);
        _canvas.Children.Add(_size);
        Content = _canvas;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseLeftButtonDown += (_, e) => { _drag = true; _start = e.GetPosition(_canvas); _box.Visibility = Visibility.Visible; CaptureMouse(); };
        MouseMove += (_, e) => { if (_drag) Draw(e.GetPosition(_canvas)); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_drag) return;
            _drag = false; ReleaseMouseCapture();
            var p = e.GetPosition(_canvas);
            var r = new Rect(_start, p);
            if (r.Width < 20 || r.Height < 10) { _box.Visibility = Visibility.Collapsed; return; }
            var src = PresentationSource.FromVisual(this);
            var m = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            var tl = m.Transform(new Point(Left + r.X, Top + r.Y));
            var br = m.Transform(new Point(Left + r.Right, Top + r.Bottom));
            Result = DRect.FromLTRB((int)Math.Round(tl.X), (int)Math.Round(tl.Y), (int)Math.Round(br.X), (int)Math.Round(br.Y));
            Close();
        };
    }

    void Draw(Point p)
    {
        var r = new Rect(_start, p);
        Canvas.SetLeft(_box, r.X); Canvas.SetTop(_box, r.Y); _box.Width = r.Width; _box.Height = r.Height;
        _size.Text = $"{(int)r.Width} × {(int)r.Height}";
        _size.Visibility = Visibility.Visible;
        Canvas.SetLeft(_size, r.X); Canvas.SetTop(_size, r.Bottom + 6);
    }

    public static DRect? Pick()
    {
        var w = new RegionPickerWindow();
        w.ShowDialog();
        return w.Result;
    }
}
