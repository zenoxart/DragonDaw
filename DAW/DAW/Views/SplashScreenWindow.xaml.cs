using System.Windows;
using System.Windows.Media.Animation;

namespace DAW.Views;

/// <summary>
/// Splash screen shown while the main window is loading.
/// Call SetStatus() to update the label, SetProgress() to animate the bar,
/// then FadeOut() once the main window is ready.
/// </summary>
public partial class SplashScreenWindow : Window
{
    public SplashScreenWindow()
    {
        InitializeComponent();
    }

    /// <summary>Updates the status label text.</summary>
    public void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    /// <summary>
    /// Animates the progress bar to <paramref name="fraction"/> (0.0 – 1.0).
    /// The track width is (Window.Width − 64) = 356 px.
    /// </summary>
    public void SetProgress(double fraction)
    {
        double trackWidth = ActualWidth > 0 ? ActualWidth - 64 : 356;
        double target = Math.Clamp(fraction, 0.0, 1.0) * trackWidth;

        var anim = new DoubleAnimation(target,
            new Duration(TimeSpan.FromMilliseconds(200)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ProgressBar.BeginAnimation(WidthProperty, anim);
    }

    /// <summary>
    /// Fades the splash out and closes it.
    /// The returned Task completes once the window is closed.
    /// </summary>
    public Task FadeOutAsync()
    {
        var tcs = new TaskCompletionSource();

        var anim = new DoubleAnimation(0.0,
            new Duration(TimeSpan.FromMilliseconds(350)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        anim.Completed += (_, _) =>
        {
            Close();
            tcs.TrySetResult();
        };

        BeginAnimation(OpacityProperty, anim);
        return tcs.Task;
    }
}
