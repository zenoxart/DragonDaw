using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DAW.Audio.Effects;
using DAW.Models;
using DAW.Services;
using DAW.Views.Controls;

namespace DAW.Plugins;

/// <summary>
/// Floating window for a single plugin instance.
/// </summary>
public class PluginWindow : Window
{
    public new AudioEffect Effect { get; }
    public PluginDefinition Definition { get; }
    public Track? TargetTrack { get; private set; }

    private ComboBox? _presetCombo;

    // ── Singleton-per-effect registry ────────────────────────────────────────
    // Maps each AudioEffect instance to its open PluginWindow (if any).
    // Ensures only one window per effect instance can be open at a time.
    private static readonly Dictionary<AudioEffect, PluginWindow> _openWindows = new();

    /// <summary>
    /// Opens (or focuses) the editor window for <paramref name="effect"/>.
    /// If a window for this exact effect instance is already open it is
    /// brought to the foreground instead of creating a second one.
    /// </summary>
    public static void Show(AudioEffect effect, PluginDefinition definition,
                            Track? targetTrack, Window? owner = null)
    {
        // Already open → just focus it
        if (_openWindows.TryGetValue(effect, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            existing.Focus();
            return;
        }

        var win = new PluginWindow(effect, definition, targetTrack)
        {
            Owner = owner,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen
        };
        win.Show();
    }

    public PluginWindow(AudioEffect effect, PluginDefinition definition, Track? targetTrack)
    {
        Effect = effect;
        Definition = definition;
        TargetTrack = targetTrack;

        // Register this window in the open-windows map
        _openWindows[effect] = this;

        InitializeWindow();
        BuildUI();
    }

    // Per-plugin content minimum sizes (width, height) — enforced on window AND on the control itself
    private static (double minW, double minH) ContentMinSize(AudioEffect fx) => fx switch
    {
        EqualizerEffect  => (540, 360),
        CompressorEffect => (440, 420),
        SaturationEffect => (460, 320),
        ReverbEffect     => (480, 490),
        GainEffect       => (260, 270),
        DelayEffect      => (360, 420),
        SpectreEffect    => (SpectreControl.MinW, SpectreControl.MinH),
        MasterEffect     => (MasterControl.MinW,  MasterControl.MinH),
        _                => (280, 260)
    };

    private void InitializeWindow()
    {
        Title = $"{Definition.Icon} {Definition.Name}";

        var (cmw, cmh) = ContentMinSize(Effect);

        // Window minimum = content minimum + chrome (title ~38 + preset ~34 + enable ~44 + padding)
        const double chromeH = 120;
        const double chromeW = 24;

        MinWidth  = cmw + chromeW;
        MinHeight = cmh + chromeH;

        // Default opening size = minimum + comfortable extra space
        Width  = MinWidth  + 60;
        Height = MinHeight + 40;

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
            Background = new SolidColorBrush(PluginTheme.WindowBg),
            BorderBrush = new SolidColorBrush(PluginTheme.WindowBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 5,
                Opacity = PluginTheme.ShadowOpacity,
                Color = PluginTheme.ShadowColor
            }
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Preset bar
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

        // Title Bar
        var titleBar = CreateTitleBar();
        Grid.SetRow(titleBar, 0);
        mainGrid.Children.Add(titleBar);

        // Preset Bar
        var presetBar = CreatePresetBar();
        Grid.SetRow(presetBar, 1);
        mainGrid.Children.Add(presetBar);

        // Content
        var content = CreateContent();
        Grid.SetRow(content, 2);
        mainGrid.Children.Add(content);

        mainBorder.Child = mainGrid;
        Content = mainBorder;
    }

    private Border CreatePresetBar()
    {
        var bar = new Border
        {
            Background = new SolidColorBrush(PluginTheme.PresetBarBg),
            BorderBrush = new SolidColorBrush(PluginTheme.PresetBarBorder),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 5, 10, 5)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // Label
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ComboBox
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // Save
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // Delete

        // Label
        var label = new TextBlock
        {
            Text = "PRESET",
            Foreground = new SolidColorBrush(PluginTheme.PresetLabel),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        // Preset combo
        _presetCombo = new ComboBox
        {
            FontSize = 10,
            MinWidth = 100,
            IsEditable = true,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(PluginTheme.ComboBg),
            Foreground = new SolidColorBrush(PluginTheme.ComboFg),
            BorderBrush = new SolidColorBrush(PluginTheme.ComboBorder),
            BorderThickness = new Thickness(1)
        };
        RefreshPresetList();
        _presetCombo.SelectionChanged += OnPresetSelected;
        Grid.SetColumn(_presetCombo, 1);
        grid.Children.Add(_presetCombo);

        // Save button
        var saveBtn = CreatePresetButton("💾", "Preset speichern", OnSavePreset);
        Grid.SetColumn(saveBtn, 2);
        grid.Children.Add(saveBtn);

        // Delete button
        var deleteBtn = CreatePresetButton("🗑", "Preset löschen", OnDeletePreset);
        Grid.SetColumn(deleteBtn, 3);
        grid.Children.Add(deleteBtn);

        bar.Child = grid;
        return bar;
    }

    private static Button CreatePresetButton(string content, string tooltip, RoutedEventHandler handler)
    {
        var btn = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Width = 26,
            Height = 22,
            FontSize = 11,
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(PluginTheme.BtnBg),
            Foreground = new SolidColorBrush(PluginTheme.BtnFg),
            BorderBrush = new SolidColorBrush(PluginTheme.BtnBorder),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        btn.Click += handler;
        return btn;
    }

    private void RefreshPresetList()
    {
        if (_presetCombo == null) return;
        _presetCombo.SelectionChanged -= OnPresetSelected;
        _presetCombo.Items.Clear();

        foreach (var name in EffectPresetService.ListPresets(Effect.EffectType))
            _presetCombo.Items.Add(name);

        // Show whichever preset is currently loaded on the effect
        var active = Effect.CurrentPresetName;
        if (!string.IsNullOrEmpty(active) && _presetCombo.Items.Contains(active))
            _presetCombo.SelectedItem = active;
        else
            _presetCombo.Text = active; // preserves typed-but-unsaved name

        _presetCombo.SelectionChanged += OnPresetSelected;
    }

    private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_presetCombo?.SelectedItem is string presetName && !string.IsNullOrEmpty(presetName))
        {
            EffectPresetService.LoadPreset(Effect, presetName);
        }
    }

    private void OnSavePreset(object sender, RoutedEventArgs e)
    {
        var name = _presetCombo?.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            // Prompt with a simple input dialog
            name = PromptPresetName();
            if (string.IsNullOrEmpty(name)) return;
        }

        EffectPresetService.SavePreset(Effect, name);
        RefreshPresetList();
        _presetCombo!.Text = name;
    }

    private void OnDeletePreset(object sender, RoutedEventArgs e)
    {
        var name = _presetCombo?.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var result = MessageBox.Show(
            $"Preset \"{name}\" löschen?",
            "Preset löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            EffectPresetService.DeletePreset(Effect.EffectType, name);
            Effect.CurrentPresetName = string.Empty;
            RefreshPresetList();
        }
    }

    private string? PromptPresetName()
    {
        var dialog = new Window
        {
            Title = "Preset speichern",
            Width = 300,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(PluginTheme.DialogBg)
        };

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = "Preset-Name:",
            Foreground = new SolidColorBrush(PluginTheme.InputFg),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var input = new TextBox
        {
            FontSize = 12,
            Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(PluginTheme.InputBg),
            Foreground = new SolidColorBrush(PluginTheme.InputFg),
            BorderBrush = new SolidColorBrush(PluginTheme.InputBorder),
            Text = Effect.Name
        };
        stack.Children.Add(input);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        string? result = null;
        var okBtn = new Button
        {
            Content = "Speichern",
            Width = 80,
            Padding = new Thickness(0, 4, 0, 4),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okBtn.Click += (_, _) => { result = input.Text?.Trim(); dialog.Close(); };

        var cancelBtn = new Button
        {
            Content = "Abbrechen",
            Width = 80,
            Padding = new Thickness(0, 4, 0, 4),
            IsCancel = true
        };

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        stack.Children.Add(btnPanel);
        dialog.Content = stack;

        input.Focus();
        input.SelectAll();
        dialog.ShowDialog();

        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private Border CreateTitleBar()
    {
        var titleBar = new Border
        {
            Background = new SolidColorBrush(PluginTheme.TitleBarBg),
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
            Foreground = new SolidColorBrush(PluginTheme.TitleText),
            VerticalAlignment = VerticalAlignment.Center
        });
        
        // Track indicator
        if (TargetTrack != null)
        {
            titleStack.Children.Add(new TextBlock
            {
                Text = $" → {TargetTrack.Title}",
                FontSize = 10,
                Foreground = new SolidColorBrush(PluginTheme.TitleAccent),
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
            Foreground = new SolidColorBrush(PluginTheme.BtnFg),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };

        btn.MouseEnter += (s, e) =>
        {
            btn.Background = isClose 
                ? new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23))
                : new SolidColorBrush(PluginTheme.BtnHover);
            btn.Foreground = Brushes.White;
        };

        btn.MouseLeave += (s, e) =>
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = new SolidColorBrush(PluginTheme.BtnFg);
        };

        btn.Click += (s, e) => action();
        return btn;
    }

    private Grid CreateContent()
    {
        // Root grid: Enable-row (Auto) + control row (Star)
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });             // enable toggle
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // plugin control

        // ── Enable / Bypass toggle strip ──
        var enablePanel = new Border
        {
            Background = new SolidColorBrush(PluginTheme.ControlBg),
            BorderBrush = new SolidColorBrush(PluginTheme.Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 6, 12, 6)
        };
        var enableGrid = new Grid();
        enableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        enableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var enableLabel = new TextBlock
        {
            Text = "Effect Enabled",
            Foreground = new SolidColorBrush(PluginTheme.TextPrimary),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11
        };
        var enableCheck = new CheckBox { IsChecked = Effect.IsEnabled, VerticalAlignment = VerticalAlignment.Center };
        enableCheck.Checked   += (s, e) => Effect.IsEnabled = true;
        enableCheck.Unchecked += (s, e) => Effect.IsEnabled = false;
        Grid.SetColumn(enableLabel, 0);
        Grid.SetColumn(enableCheck, 1);
        enableGrid.Children.Add(enableLabel);
        enableGrid.Children.Add(enableCheck);
        enablePanel.Child = enableGrid;
        Grid.SetRow(enablePanel, 0);
        root.Children.Add(enablePanel);

        // ── Plugin control — fills remaining space, never shrinks below minimum ──
        var (minW, minH) = ContentMinSize(Effect);
        var ctrl = CreatePluginControl();
        ctrl.MinWidth  = minW;
        ctrl.MinHeight = minH;

        // Wrap in a Border for corner rounding; Border also enforces the min size
        var ctrlBorder = new Border
        {
            MinWidth     = minW,
            MinHeight    = minH,
            ClipToBounds = true,
            Child        = ctrl
        };
        Grid.SetRow(ctrlBorder, 1);
        root.Children.Add(ctrlBorder);

        return root;
    }

    /// <summary>Creates the bare plugin FrameworkElement (no chrome).</summary>
    private FrameworkElement CreatePluginControl()
    {
        switch (Effect)
        {
            case EqualizerEffect eq:
            {
                var c = new ParametricEqControl
                {
                    Effect  = eq,
                    Cursor  = Cursors.Hand,
                    ToolTip = "Drag: Freq/Gain · Scroll: Q · Right-click: Band mode"
                };
                return c;
            }
            case CompressorEffect comp:
            {
                var c = new CompressorControl { Effect = comp, Cursor = Cursors.Hand,
                    ToolTip = "Drag knobs · Click ratio · Right-click = reset" };
                return c;
            }
            case ReverbEffect reverb:
            {
                var c = new ReverbControl { Effect = reverb, Cursor = Cursors.Hand,
                    ToolTip = "↕ Drag · Shift = fine · Dbl-click = reset · Click mode to cycle" };
                return c;
            }
            case SaturationEffect sat:
            {
                var c = new SaturationControl { Effect = sat, Cursor = Cursors.Hand,
                    ToolTip = "↕ Drag knobs · Right-click = reset · Click buttons" };
                return c;
            }
            case DelayEffect delay:
            {
                var c = new DelayControl { Effect = delay, Cursor = Cursors.Hand,
                    ToolTip = "↕ Drag knobs · Shift = fine · Right-click = reset" };
                return c;
            }
            case GainEffect gain:
            {
                var c = new GainControl { Effect = gain, Cursor = Cursors.Hand,
                    ToolTip = "↕ Drag knob · Right-click = reset" };
                return c;
            }
            case SpectreEffect spectre:
            {
                var c = new SpectreControl { Effect = spectre, Cursor = Cursors.Hand,
                    ToolTip = "Drag nodes = Freq/Gain · Scroll = Q · Right-click node = reset · ◄► = Algo/Ch" };
                return c;
            }
            case MasterEffect master:
            {
                var vm = new ViewModels.MasterViewModel(master);
                var c  = new MasterControl { ViewModel = vm, Cursor = Cursors.Hand,
                    ToolTip = "↕ Drag knobs · Shift = fine · Scroll = adjust · Right-click = reset" };
                // Stop the meter timer when window closes
                Closed += (_, _) => vm.StopMetering();
                return c;
            }
            default:
            {
                // Fallback: generic sliders in a scroll viewer
                var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) };
                var stack = new StackPanel();
                AddEffectParameters(stack);
                scroll.Content = stack;
                return scroll;
            }
        }
    }

    private void AddEffectParameters(StackPanel container)
    {
        switch (Effect)
        {
            case EqualizerEffect eq:
                AddParametricEqUI(container, eq);
                break;

            case CompressorEffect comp:
                Add1176CompressorUI(container, comp);
                break;

            case ReverbEffect reverb:
                AddReverbUI(container, reverb);
                break;

            case SaturationEffect sat:
                AddSaturationUI(container, sat);
                break;

            case DelayEffect delay:
                var delayCtrl = new Views.Controls.DelayControl
                {
                    Effect    = delay,
                    MinHeight = 440,
                    Margin    = new Thickness(0, 0, 0, 4),
                    Cursor    = Cursors.Hand,
                    ToolTip   = "↕ Drag knobs · Shift = fine · Right-click = reset · Click mode arrows to cycle"
                };
                container.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background   = new SolidColorBrush(PluginTheme.SurfaceBg),
                    ClipToBounds = true,
                    Child        = delayCtrl
                });
                break;

            case GainEffect gain:
                var gainCtrl = new Views.Controls.GainControl
                {
                    Effect = gain,
                    MinHeight = 260,
                    Margin = new Thickness(0, 0, 0, 4),
                    Cursor = Cursors.Hand,
                    ToolTip = "↕ Drag knob · Right-click = reset"
                };
                container.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(PluginTheme.SurfaceBg),
                    ClipToBounds = true,
                    Child = gainCtrl
                });
                break;
        }
    }

    /// <summary>
    /// Builds the full parametric EQ UI: interactive graph on top, band controls below.
    /// </summary>
    private void AddParametricEqUI(StackPanel container, EqualizerEffect eq)
    {
        // ── Interactive frequency response graph ──
        var eqControl = new ParametricEqControl
        {
            Effect = eq,
            MinHeight = 200,
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            ToolTip = "Drag: Freq/Gain · Scroll: Q · Right-click: Band mode"
        };
        // Round corners via clip
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(PluginTheme.SurfaceBg),
            ClipToBounds = true,
            Child = eqControl
        };
        container.Children.Add(border);

        // ── Hint text ──
        container.Children.Add(new TextBlock
        {
            Text = "↔ Drag = Freq/Gain   ⟳ Scroll = Q   Right-click = Mode / Options",
            Foreground = new SolidColorBrush(PluginTheme.TextHint),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
    }

    /// <summary>
    /// Builds the 1176-style compressor UI with VU meter and knobs.
    /// </summary>
    private void Add1176CompressorUI(StackPanel container, CompressorEffect comp)
    {
        var compControl = new CompressorControl
        {
            Effect = comp,
            MinHeight = 260,
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            ToolTip = "Drag knobs up/down · Click ratio buttons · Right-click to reset"
        };
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(PluginTheme.SurfaceBg),
            ClipToBounds = true,
            Child = compControl
        };
        container.Children.Add(border);

        container.Children.Add(new TextBlock
        {
            Text = "↕ Drag knobs · Click ratio · Right-click = Reset",
            Foreground = new SolidColorBrush(PluginTheme.TextHint),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
    }

    /// <summary>
    /// Builds the Valhalla Room–inspired reverb UI with rotary knobs.
    /// </summary>
    private void AddReverbUI(StackPanel container, ReverbEffect reverb)
    {
        var reverbControl = new ReverbControl
        {
            Effect = reverb,
            MinHeight = 420,
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            ToolTip = "↕ Drag knobs · Shift = fine · Double-click / Right-click = reset · Click mode to cycle"
        };
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(PluginTheme.IsLight ? PluginTheme.SurfaceBg : Color.FromRgb(0x08, 0x0A, 0x12)),
            ClipToBounds = true,
            Child = reverbControl
        };
        container.Children.Add(border);

        container.Children.Add(new TextBlock
        {
            Text = "↕ Drag = adjust · Shift+Drag = fine · Dbl-click = reset · Click mode to cycle",
            Foreground = new SolidColorBrush(PluginTheme.TextHint),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
    }

    /// <summary>
    /// Builds the Black Box HG-2–inspired saturation UI.
    /// </summary>
    private void AddSaturationUI(StackPanel container, SaturationEffect sat)
    {
        var satControl = new Views.Controls.SaturationControl
        {
            Effect    = sat,
            MinHeight = 360,
            MinWidth = 700,
            Margin    = new Thickness(0, 0, 0, 4),
            Cursor    = Cursors.Hand,
            ToolTip   = "↕ Drag knobs · Right-click = reset · Click buttons to toggle"
        };
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background   = new SolidColorBrush(PluginTheme.SurfaceBg),
            ClipToBounds = true,
            Child        = satControl
        };
        container.Children.Add(border);

        container.Children.Add(new TextBlock
        {
            Text                = "↕ Drag = adjust · Shift = fine · Right-click = reset · Click buttons to toggle",
            Foreground          = new SolidColorBrush(PluginTheme.TextHint),
            FontSize            = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 0, 0, 4)
        });
    }

    /// <summary>
    /// Builds an FL-style horizontal strip of 7 vertical EQ band columns.
    /// Each column: band number, vertical gain fader, freq knob, Q knob, mode combobox.
    /// </summary>
    private void AddEqBandStrip(StackPanel container, EqualizerEffect eq)
    {
        var strip = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        for (int i = 0; i < EqualizerEffect.BandCount; i++)
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var modeValues = Enum.GetValues<EqBandMode>();

        for (int i = 0; i < EqualizerEffect.BandCount; i++)
        {
            var band = eq.Bands[i];
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };

            // Band number
            col.Children.Add(new TextBlock
            {
                Text = $"{band.Number}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0xA4, 0xE6)),
                FontSize = 10, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2)
            });

            // Gain value display
            var gainDisplay = new TextBlock
            {
                Text = $"{band.Gain:F0}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),
                FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center
            };
            col.Children.Add(gainDisplay);

            // Vertical gain fader
            var gainFader = new Slider
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Minimum = -18, Maximum = 18, Value = band.Gain,
                Height = 120, Width = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                Template = CreateVerticalFaderTemplate()
            };
            var bandCapture = band;
            gainFader.ValueChanged += (s, e) =>
            {
                bandCapture.Gain = e.NewValue;
                gainDisplay.Text = $"{e.NewValue:F0}";
            };
            col.Children.Add(gainFader);

            // Freq label + slider-knob + value
            col.Children.Add(new TextBlock
            {
                Text = "Freq", Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x66, 0x77)),
                FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
            });
            var freqDisplay = new TextBlock
            {
                Text = $"{band.Frequency:F0}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0xA4, 0xE6)),
                FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center
            };
            var freqSlider = new Slider
            {
                Minimum = 20, Maximum = 20000, Value = band.Frequency,
                Width = 32, Height = 18, Template = CreateFaderTemplate()
            };
            freqSlider.ValueChanged += (s, e) =>
            {
                bandCapture.Frequency = e.NewValue;
                freqDisplay.Text = $"{e.NewValue:F0}";
            };
            col.Children.Add(freqSlider);
            col.Children.Add(freqDisplay);

            // Q label + slider-knob + value
            col.Children.Add(new TextBlock
            {
                Text = "Q", Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x66, 0x77)),
                FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0)
            });
            var qDisplay = new TextBlock
            {
                Text = $"{band.Q:F1}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0xA4, 0xE6)),
                FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center
            };
            var qSlider = new Slider
            {
                Minimum = 0.05, Maximum = 30, Value = band.Q,
                Width = 32, Height = 18, Template = CreateFaderTemplate()
            };
            qSlider.ValueChanged += (s, e) =>
            {
                bandCapture.Q = e.NewValue;
                qDisplay.Text = $"{e.NewValue:F1}";
            };
            col.Children.Add(qSlider);
            col.Children.Add(qDisplay);

            // Mode combobox
            var modeCombo = new ComboBox
            {
                ItemsSource = modeValues,
                SelectedItem = band.Mode,
                Width = 36, FontSize = 7, Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(1, 0, 1, 0),
                Background = new SolidColorBrush(PluginTheme.ControlBg),
                Foreground = new SolidColorBrush(PluginTheme.TextPrimary),
                BorderBrush = new SolidColorBrush(PluginTheme.Border)
            };
            modeCombo.SelectionChanged += (s, e) =>
            {
                if (modeCombo.SelectedItem is EqBandMode m)
                    bandCapture.Mode = m;
            };
            col.Children.Add(modeCombo);

            Grid.SetColumn(col, i);
            strip.Children.Add(col);
        }

        container.Children.Add(strip);
    }

    private void AddBandHeader(StackPanel container, EqBand band, string[] modeNames)
    {
        var headerGrid = new Grid { Margin = new Thickness(0, 8, 0, 2) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bandLabel = new TextBlock
        {
            Text = $"Band {band.Number}",
            Foreground = new SolidColorBrush(PluginTheme.TextAccent),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var modeCombo = new ComboBox
        {
            ItemsSource = modeNames,
            SelectedIndex = (int)band.Mode,
            Width = 85,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(PluginTheme.ControlBg),
            Foreground = new SolidColorBrush(PluginTheme.TextPrimary),
            BorderBrush = new SolidColorBrush(PluginTheme.Border)
        };
        modeCombo.SelectionChanged += (s, e) =>
        {
            if (modeCombo.SelectedIndex >= 0)
                band.Mode = (EqBandMode)modeCombo.SelectedIndex;
        };

        var enableCheck = new CheckBox
        {
            IsChecked = band.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        enableCheck.Checked += (s, e) => band.IsEnabled = true;
        enableCheck.Unchecked += (s, e) => band.IsEnabled = false;

        var rightStack = new StackPanel { Orientation = Orientation.Horizontal };
        rightStack.Children.Add(modeCombo);
        rightStack.Children.Add(enableCheck);

        Grid.SetColumn(bandLabel, 0);
        Grid.SetColumn(rightStack, 2);
        headerGrid.Children.Add(bandLabel);
        headerGrid.Children.Add(rightStack);

        container.Children.Add(headerGrid);

        container.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(PluginTheme.Separator),
            Margin = new Thickness(0, 0, 0, 4)
        });
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
            Foreground = new SolidColorBrush(PluginTheme.TextSecondary),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 18,
            Template = CreateFaderTemplate()
        };

        var valueText = new TextBlock
        {
            Text = $"{value * displayMultiplier:F1} {unit}",
            Foreground = new SolidColorBrush(PluginTheme.TextAccent),
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
            Foreground = new SolidColorBrush(PluginTheme.TextPrimary),
            Margin = new Thickness(0, 4, 0, 8)
        };

        check.Checked += (s, e) => setter(true);
        check.Unchecked += (s, e) => setter(false);

        container.Children.Add(check);
    }

    private static ControlTemplate? _cachedFaderTemplate;
    private static string? _cachedFaderTheme;

    /// <summary>
    /// Creates a horizontal fader ControlTemplate matching the FLFaderH style.
    /// Cached per theme.
    /// </summary>
    private static ControlTemplate CreateFaderTemplate()
    {
        var themeId = ThemeService.Instance.CurrentTheme;
        if (_cachedFaderTemplate != null && _cachedFaderTheme == themeId) return _cachedFaderTemplate;

        var t = PluginTheme.FaderTrack;
        var bg = PluginTheme.FaderThumbBg;
        var bd = PluginTheme.FaderThumbBdr;
        var gl = PluginTheme.FaderGradLight;
        var gd = PluginTheme.FaderGradDark;

        string xaml = $"""
            <ControlTemplate TargetType="Slider"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid>
                    <Border Height="4" Background="{Hex(t)}" CornerRadius="2" VerticalAlignment="Center">
                        <Border.Effect>
                            <DropShadowEffect ShadowDepth="1" BlurRadius="2" Opacity="0.2"/>
                        </Border.Effect>
                    </Border>
                    <Track x:Name="PART_Track">
                        <Track.Thumb>
                            <Thumb Width="10" Height="18">
                                <Thumb.Template>
                                    <ControlTemplate>
                                        <Border Background="{Hex(bg)}" CornerRadius="2"
                                                BorderBrush="{Hex(bd)}" BorderThickness="1">
                                            <Border CornerRadius="1" Margin="2,2">
                                                <Border.Background>
                                                    <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                                                        <GradientStop Color="{Hex(gl)}" Offset="0"/>
                                                        <GradientStop Color="{Hex(gd)}" Offset="1"/>
                                                    </LinearGradientBrush>
                                                </Border.Background>
                                            </Border>
                                        </Border>
                                    </ControlTemplate>
                                </Thumb.Template>
                            </Thumb>
                        </Track.Thumb>
                    </Track>
                </Grid>
            </ControlTemplate>
            """;

        _cachedFaderTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        _cachedFaderTheme = themeId;
        return _cachedFaderTemplate;
    }

    private static ControlTemplate? _cachedVerticalFaderTemplate;
    private static string? _cachedVerticalFaderTheme;

    private static ControlTemplate CreateVerticalFaderTemplate()
    {
        var themeId = ThemeService.Instance.CurrentTheme;
        if (_cachedVerticalFaderTemplate != null && _cachedVerticalFaderTheme == themeId) return _cachedVerticalFaderTemplate;

        var t = PluginTheme.FaderTrack;
        var bg = PluginTheme.FaderThumbBg;
        var bd = PluginTheme.FaderThumbBdr;
        var gl = PluginTheme.FaderGradLight;
        var gd = PluginTheme.FaderGradDark;

        string xaml = $"""
            <ControlTemplate TargetType="Slider"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid>
                    <Border Width="3" Background="{Hex(t)}" CornerRadius="1.5" HorizontalAlignment="Center"/>
                    <Rectangle Width="8" Height="1" Fill="{Hex(PluginTheme.Border)}" VerticalAlignment="Center" HorizontalAlignment="Center"/>
                    <Track x:Name="PART_Track">
                        <Track.Thumb>
                            <Thumb Width="18" Height="10">
                                <Thumb.Template>
                                    <ControlTemplate>
                                        <Border Background="{Hex(bg)}" CornerRadius="2"
                                                BorderBrush="{Hex(bd)}" BorderThickness="1">
                                            <Border CornerRadius="1" Margin="2,1">
                                                <Border.Background>
                                                    <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                                                        <GradientStop Color="{Hex(gl)}" Offset="0"/>
                                                        <GradientStop Color="{Hex(gd)}" Offset="1"/>
                                                    </LinearGradientBrush>
                                                </Border.Background>
                                            </Border>
                                        </Border>
                                    </ControlTemplate>
                                </Thumb.Template>
                            </Thumb>
                        </Track.Thumb>
                    </Track>
                </Grid>
            </ControlTemplate>
            """;

        _cachedVerticalFaderTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        _cachedVerticalFaderTheme = themeId;
        return _cachedVerticalFaderTemplate;
    }

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    protected override void OnClosed(EventArgs e)
    {
        // Unregister so the same effect can be reopened later
        _openWindows.Remove(Effect);
        base.OnClosed(e);
    }
}
