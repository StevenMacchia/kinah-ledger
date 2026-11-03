using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KinahLedger.Ui;

/// <summary>
/// The toast (0.9.10): a "done" (picture copied, goal set, server set) used to go to the faint 12 px status line at the
/// foot for six seconds, where it was missed. Said in the good tone, it now shows in a small card at the bottom centre of
/// the left column, in the Undo bar's look: it fades in, holds a moment and fades out. It sits over the page in the same
/// cell as the Undo bar (never a row of its own), so the window never changes size, and it takes no click and no focus.
/// Warnings and problems stay on the status line, where they wait to be read.
/// </summary>
public partial class MainWindow
{
    /// <summary>How long a short toast holds before it fades; a longer one holds a little more, up to <see cref="ToastMaxSeconds"/>.</summary>
    const double ToastSeconds = 2.5, ToastMaxSeconds = 6;
    readonly DispatcherTimer _toastTimer = new();

    /// <summary>The words in the toast, faded in; a second toast while one shows takes its place and starts its time again.</summary>
    void ShowToast(string text)
    {
        ToastText.Text = text;
        // over the Undo bar when that shows, not on top of it
        Toast.Margin = new Thickness(0, 0, 0, UndoBar.Visibility == Visibility.Visible ? 66 : 14);
        bool showing = Toast.Visibility == Visibility.Visible;
        Toast.Visibility = Visibility.Visible;
        if (!showing) Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        else { Toast.BeginAnimation(OpacityProperty, null); Toast.Opacity = 1; }
        _toastTimer.Stop();
        // about a second more for each 80 characters past the first 60, so a long line can be read before it goes
        _toastTimer.Interval = TimeSpan.FromSeconds(Math.Min(ToastMaxSeconds, ToastSeconds + Math.Max(0, text.Length - 60) / 80.0));
        _toastTimer.Tick -= ToastTimeUp; _toastTimer.Tick += ToastTimeUp;
        _toastTimer.Start();
    }

    void ToastTimeUp(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => { if (!_toastTimer.IsEnabled) { Toast.Visibility = Visibility.Collapsed; Toast.BeginAnimation(OpacityProperty, null); Toast.Opacity = 1; } };
        Toast.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>For the options self-test: the toast's words while it shows, else null.</summary>
    internal string? ToastShowing => Toast.Visibility == Visibility.Visible ? ToastText.Text : null;
}
