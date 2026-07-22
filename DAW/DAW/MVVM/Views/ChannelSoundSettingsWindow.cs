using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DAW.MVVM.Models.Sequencer;
using DAW.MVVM.Views.Controls;
using DAW.Plugins;

namespace DAW.MVVM.Views;

/// <summary>
/// Floating "Sound Settings" popup for a single Channel-Rack channel — the
/// FL-Studio style voice-cutting Group (Cut / By / Cut self) and the
/// amplitude envelope, hosted in the same minimal window chrome as
/// <see cref="Plugins.PluginWindow"/> so it reads as part of the same family
/// of popups even though a rack channel isn't an AudioEffect.
/// </summary>
public sealed class ChannelSoundSettingsWindow : Window
{
    // One window per channel, same convention as PluginWindow's per-effect registry.
    private static readonly Dictionary<ChannelModel, ChannelSoundSettingsWindow> _open = new();

    public static void ShowFor(ChannelModel channel, Window? owner = null)
    {
        if (_open.TryGetValue(channel, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var win = new ChannelSoundSettingsWindow(channel)
        {
            Owner = owner,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        _open[channel] = win;
        win.Closed += (_, _) => _open.Remove(channel);
        win.Show();
    }

    private ChannelSoundSettingsWindow(ChannelModel channel)
    {
        Title = $"Sound Settings — {channel.Name}";
        Width = 400; Height = 660;
        MinWidth = 380; MinHeight = 600;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;

        var mainBorder = new Border
        {
            Background = new SolidColorBrush(PluginTheme.WindowBg),
            BorderBrush = new SolidColorBrush(PluginTheme.WindowBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 20, ShadowDepth = 5,
                Opacity = PluginTheme.ShadowOpacity, Color = PluginTheme.ShadowColor
            }
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = BuildTitleBar(channel.Name);
        Grid.SetRow(titleBar, 0);
        grid.Children.Add(titleBar);

        var control = new ChannelSoundControl { Channel = channel };
        var contentBorder = new Border
        {
            Margin = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = control
        };
        Grid.SetRow(contentBorder, 1);
        grid.Children.Add(contentBorder);

        mainBorder.Child = grid;
        Content = mainBorder;
    }

    private Border BuildTitleBar(string channelName)
    {
        var titleBar = new Border
        {
            Background = new SolidColorBrush(PluginTheme.TitleBarBg),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Padding = new Thickness(12, 8, 8, 8)
        };
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ClickCount < 2) DragMove(); };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        titleStack.Children.Add(new TextBlock { Text = "🎚️", FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        titleStack.Children.Add(new TextBlock
        {
            Text = $"Sound Settings — {channelName}",
            FontWeight = FontWeights.SemiBold, FontSize = 12,
            Foreground = new SolidColorBrush(PluginTheme.TitleText),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(titleStack, 0); Grid.SetColumnSpan(titleStack, 2);
        g.Children.Add(titleStack);

        var closeBtn = new Button
        {
            Content = "✕", Width = 26, Height = 22, FontSize = 10,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(PluginTheme.TitleText), Cursor = Cursors.Hand
        };
        closeBtn.MouseEnter += (_, _) => closeBtn.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23));
        closeBtn.MouseLeave += (_, _) => closeBtn.Background = Brushes.Transparent;
        closeBtn.Click += (_, _) => Close();
        Grid.SetColumn(closeBtn, 2);
        g.Children.Add(closeBtn);

        titleBar.Child = g;
        return titleBar;
    }
}
