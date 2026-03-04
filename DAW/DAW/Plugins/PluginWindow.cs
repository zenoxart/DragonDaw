using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DAW.Audio.Effects;
using DAW.Models;

namespace DAW.Plugins;

/// <summary>
/// Floating window for a single plugin instance.
/// </summary>
public class PluginWindow : Window
{
    public new AudioEffect Effect { get; }
    public PluginDefinition Definition { get; }
    public Track? TargetTrack { get; private set; }
    private bool _effectAddedToTrack;

    public PluginWindow(AudioEffect effect, PluginDefinition definition, Track? targetTrack)
    {
        Effect = effect;
        Definition = definition;
        TargetTrack = targetTrack;

        InitializeWindow();
        BuildUI();
        
        // Add effect to track after window is fully initialized
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Add the effect to the track's chain when window is loaded
        if (TargetTrack != null && !_effectAddedToTrack)
        {
            try
            {
                TargetTrack.EffectChain.AddEffect(Effect);
                _effectAddedToTrack = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to add effect: {ex.Message}");
            }
        }
    }

    private void InitializeWindow()
    {
        Title = $"{Definition.Icon} {Definition.Name}";
        Width = 320;
        Height = 400;
        MinWidth = 280;
        MinHeight = 200;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Topmost = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private void BuildUI()
    {
        var mainBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x61, 0x9C)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 5,
                Opacity = 0.5,
                Color = Colors.Black
            }
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Title Bar
        var titleBar = CreateTitleBar();
        Grid.SetRow(titleBar, 0);
        mainGrid.Children.Add(titleBar);

        // Content
        var content = CreateContent();
        Grid.SetRow(content, 1);
        mainGrid.Children.Add(content);

        mainBorder.Child = mainGrid;
        Content = mainBorder;
    }

    private Border CreateTitleBar()
    {
        var titleBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x45, 0x70)),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Padding = new Thickness(12, 8, 8, 8)
        };

        titleBar.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                // Double-click to toggle maximize
                WindowState = WindowState == WindowState.Maximized 
                    ? WindowState.Normal 
                    : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon and Title
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        titleStack.Children.Add(new TextBlock
        {
            Text = Definition.Icon,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = Definition.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        
        // Track indicator
        if (TargetTrack != null)
        {
            titleStack.Children.Add(new TextBlock
            {
                Text = $" → {TargetTrack.Title}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0xA4, 0xE6)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
        }

        Grid.SetColumn(titleStack, 0);
        Grid.SetColumnSpan(titleStack, 2);
        grid.Children.Add(titleStack);

        // Window Controls
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        
        var minimizeBtn = CreateWindowButton("─", () => WindowState = WindowState.Minimized);
        var closeBtn = CreateWindowButton("✕", Close, isClose: true);
        
        controls.Children.Add(minimizeBtn);
        controls.Children.Add(closeBtn);

        Grid.SetColumn(controls, 2);
        grid.Children.Add(controls);

        titleBar.Child = grid;
        return titleBar;
    }

    private Button CreateWindowButton(string content, Action action, bool isClose = false)
    {
        var btn = new Button
        {
            Content = content,
            Width = 28,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };

        btn.MouseEnter += (s, e) =>
        {
            btn.Background = isClose 
                ? new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23))
                : new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D));
            btn.Foreground = Brushes.White;
        };

        btn.MouseLeave += (s, e) =>
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
        };

        btn.Click += (s, e) => action();
        return btn;
    }

    private ScrollViewer CreateContent()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12)
        };

        var stack = new StackPanel();

        // Enable/Bypass toggle
        var enablePanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x21, 0x26, 0x2D)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var enableGrid = new Grid();
        enableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        enableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var enableLabel = new TextBlock
        {
            Text = "Effect Enabled",
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            VerticalAlignment = VerticalAlignment.Center
        };

        var enableCheck = new CheckBox
        {
            IsChecked = Effect.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center
        };
        enableCheck.Checked += (s, e) => Effect.IsEnabled = true;
        enableCheck.Unchecked += (s, e) => Effect.IsEnabled = false;

        Grid.SetColumn(enableLabel, 0);
        Grid.SetColumn(enableCheck, 1);
        enableGrid.Children.Add(enableLabel);
        enableGrid.Children.Add(enableCheck);
        enablePanel.Child = enableGrid;
        stack.Children.Add(enablePanel);

        // Effect-specific parameters
        AddEffectParameters(stack);

        scroll.Content = stack;
        return scroll;
    }

    private void AddEffectParameters(StackPanel container)
    {
        switch (Effect)
        {
            case EqualizerEffect eq:
                AddSlider(container, "Low", -12, 12, eq.LowGain, v => eq.LowGain = v, "dB");
                AddSlider(container, "Mid", -12, 12, eq.MidGain, v => eq.MidGain = v, "dB");
                AddSlider(container, "High", -12, 12, eq.HighGain, v => eq.HighGain = v, "dB");
                break;

            case CompressorEffect comp:
                AddSlider(container, "Threshold", -60, 0, comp.Threshold, v => comp.Threshold = v, "dB");
                AddSlider(container, "Ratio", 1, 20, comp.Ratio, v => comp.Ratio = v, ":1");
                AddSlider(container, "Attack", 0.1, 100, comp.Attack, v => comp.Attack = v, "ms");
                AddSlider(container, "Release", 10, 1000, comp.Release, v => comp.Release = v, "ms");
                AddSlider(container, "Makeup", 0, 24, comp.MakeupGain, v => comp.MakeupGain = v, "dB");
                break;

            case ReverbEffect reverb:
                AddSlider(container, "Room Size", 0, 1, reverb.RoomSize, v => reverb.RoomSize = v, "%", 100);
                AddSlider(container, "Damping", 0, 1, reverb.Damping, v => reverb.Damping = v, "%", 100);
                AddSlider(container, "Wet", 0, 1, reverb.WetLevel, v => reverb.WetLevel = v, "%", 100);
                AddSlider(container, "Dry", 0, 1, reverb.DryLevel, v => reverb.DryLevel = v, "%", 100);
                break;

            case DelayEffect delay:
                AddSlider(container, "Time", 1, 2000, delay.DelayTime, v => delay.DelayTime = v, "ms");
                AddSlider(container, "Feedback", 0, 0.95, delay.Feedback, v => delay.Feedback = v, "%", 100);
                AddSlider(container, "Wet", 0, 1, delay.WetLevel, v => delay.WetLevel = v, "%", 100);
                AddCheckbox(container, "Ping-Pong", delay.PingPong, v => delay.PingPong = v);
                break;

            case GainEffect gain:
                AddSlider(container, "Gain", -24, 24, gain.Gain, v => gain.Gain = v, "dB");
                AddCheckbox(container, "Soft Clip", gain.SoftClip, v => gain.SoftClip = v);
                break;
        }
    }

    private void AddSlider(StackPanel container, string label, double min, double max, 
        double value, Action<double> setter, string unit, double displayMultiplier = 1)
    {
        var panel = new Border
        {
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

        var labelText = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center
        };

        var valueText = new TextBlock
        {
            Text = $"{value * displayMultiplier:F1} {unit}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0xA4, 0xE6)),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        slider.ValueChanged += (s, e) =>
        {
            setter(e.NewValue);
            valueText.Text = $"{e.NewValue * displayMultiplier:F1} {unit}";
        };

        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(valueText, 2);

        grid.Children.Add(labelText);
        grid.Children.Add(slider);
        grid.Children.Add(valueText);

        panel.Child = grid;
        container.Children.Add(panel);
    }

    private void AddCheckbox(StackPanel container, string label, bool value, Action<bool> setter)
    {
        var check = new CheckBox
        {
            Content = label,
            IsChecked = value,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            Margin = new Thickness(0, 4, 0, 8)
        };

        check.Checked += (s, e) => setter(true);
        check.Unchecked += (s, e) => setter(false);

        container.Children.Add(check);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Remove effect from track when window closes
        if (TargetTrack != null && _effectAddedToTrack)
        {
            try
            {
                TargetTrack.EffectChain.RemoveEffect(Effect);
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
        base.OnClosed(e);
    }
}
