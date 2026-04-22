using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DAW.Plugins;
using DAW.Services;
using DAW.ViewModels;
using Microsoft.Win32;
using NAudio.Wave;

namespace DAW.Views;

/// <summary>
/// Options / Settings window with functional tabs for General, Audio, MIDI,
/// Themes (with live theme switching), Localization (DE/EN), UI-Scaling,
/// Project Metadata, Plugins and Debug logs.
/// </summary>
public partial class OptionsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly StringBuilder _logBuffer = new();

    // Snapshot of original values so we can cancel
    private readonly string _originalTheme;
    private readonly string _originalLanguage;
    private readonly double _originalScale;

    public OptionsWindow(MainViewModel viewModel)
    {
        _vm = viewModel;
        InitializeComponent();

        // Snapshot current state
        _originalTheme = ThemeService.Instance.CurrentTheme;
        _originalLanguage = LocalizationService.Instance.CurrentLanguage;
        _originalScale = GetCurrentScale();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulateAudioDevices();
        PopulatePlugins();
        PopulateProjectMetadata();
        LoadDebugLogs();
        SubscribeToLogs();

        // Select current language
        LanguageCombo.SelectedIndex = _originalLanguage == "en" ? 1 : 0;

        // Select current theme
        var themeIdx = Array.FindIndex(ThemeService.AvailableThemes, t => t.Id == _originalTheme);
        ThemeCombo.SelectedIndex = themeIdx >= 0 ? themeIdx : 0;
        UpdateAccentPreview();

        // Select current UI scale
        var scaleMap = new[] { 0.9, 1.0, 1.1, 1.25, 1.5 };
        var scaleIdx = Array.IndexOf(scaleMap, _originalScale);
        UiScaleCombo.SelectedIndex = scaleIdx >= 0 ? scaleIdx : 1;

        // Hook live-preview events
        ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;
        UiScaleCombo.SelectionChanged += UiScaleCombo_SelectionChanged;
        LanguageCombo.SelectionChanged += LanguageCombo_SelectionChanged;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        UnsubscribeFromLogs();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Language / Localization
    // ──────────────────────────────────────────────────────────────────

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var lang = LanguageCombo.SelectedIndex == 1 ? "en" : "de";
        LocalizationService.Instance.CurrentLanguage = lang;
        AppLogger.Instance.Info($"Language changed to {lang}");
    }

    // ──────────────────────────────────────────────────────────────────
    //  Themes
    // ──────────────────────────────────────────────────────────────────

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedIndex < 0) return;
        var themeId = ThemeService.AvailableThemes[ThemeCombo.SelectedIndex].Id;
        ThemeService.Instance.ApplyTheme(themeId);
        UpdateAccentPreview();
        AppLogger.Instance.Info($"Theme changed to {themeId}");
    }

    private void UpdateAccentPreview()
    {
        var themeIdx = ThemeCombo.SelectedIndex;
        if (themeIdx < 0) themeIdx = 0;
        var themeId = ThemeService.AvailableThemes[themeIdx].Id;
        var accent = ThemeService.Instance.GetAccentColor(themeId);
        AccentPreview.Background = new SolidColorBrush(accent);
        AccentHexText.Text = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
    }

    // ──────────────────────────────────────────────────────────────────
    //  UI Scaling
    // ──────────────────────────────────────────────────────────────────

    private static readonly double[] ScaleValues = [0.9, 1.0, 1.1, 1.25, 1.5];

    private void UiScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UiScaleCombo.SelectedIndex < 0) return;
        var scale = ScaleValues[UiScaleCombo.SelectedIndex];
        ApplyScale(scale);
        AppLogger.Instance.Info($"UI scale changed to {scale * 100}%");
    }

    private static void ApplyScale(double scale)
    {
        if (Application.Current?.MainWindow is Window main)
        {
            main.LayoutTransform = new ScaleTransform(scale, scale);
        }
    }

    private static double GetCurrentScale()
    {
        if (Application.Current?.MainWindow?.LayoutTransform is ScaleTransform st)
            return st.ScaleX;
        return 1.0;
    }

    // ──────────────────────────────────────────────────────────────────
    //  General
    // ──────────────────────────────────────────────────────────────────

    private void BrowseProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizationService.Instance["options.general.projectfolder"],
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            DefaultProjectFolder.Text = dlg.SelectedPath;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Audio Driver
    // ──────────────────────────────────────────────────────────────────

    private void PopulateAudioDevices()
    {
        OutputDeviceCombo.Items.Clear();
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            OutputDeviceCombo.Items.Add(new ComboBoxItem { Content = caps.ProductName });
        }
        if (OutputDeviceCombo.Items.Count > 0)
            OutputDeviceCombo.SelectedIndex = 0;
        else
            OutputDeviceCombo.Items.Add(new ComboBoxItem { Content = "(No output device)" });
    }

    private void TestAudioDriver_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sineProvider = new NAudio.Wave.SampleProviders.SignalGenerator(44100, 1)
            {
                Gain = 0.3,
                Frequency = 440,
                Type = NAudio.Wave.SampleProviders.SignalGeneratorType.Sin
            }.Take(TimeSpan.FromMilliseconds(300));

            using var wo = new WaveOutEvent();
            wo.Init(sineProvider);
            wo.Play();
            System.Threading.Thread.Sleep(400);
            wo.Stop();

            AppLogger.Instance.Info("Audio driver test OK (440 Hz sine)");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Audio driver test failed: {ex.Message}");
            MessageBox.Show($"Audio test failed:\n{ex.Message}", "Audio Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  MIDI
    // ──────────────────────────────────────────────────────────────────

    private void RefreshMidiDevices_Click(object sender, RoutedEventArgs e)
    {
        MidiInputCombo.Items.Clear();
        MidiOutputCombo.Items.Clear();

        for (int i = 0; i < NAudio.Midi.MidiIn.NumberOfDevices; i++)
            MidiInputCombo.Items.Add(new ComboBoxItem { Content = NAudio.Midi.MidiIn.DeviceInfo(i).ProductName });
        if (MidiInputCombo.Items.Count == 0)
            MidiInputCombo.Items.Add(new ComboBoxItem { Content = LocalizationService.Instance["options.midi.nodevice"] });
        MidiInputCombo.SelectedIndex = 0;

        for (int i = 0; i < NAudio.Midi.MidiOut.NumberOfDevices; i++)
            MidiOutputCombo.Items.Add(new ComboBoxItem { Content = NAudio.Midi.MidiOut.DeviceInfo(i).ProductName });
        if (MidiOutputCombo.Items.Count == 0)
            MidiOutputCombo.Items.Add(new ComboBoxItem { Content = LocalizationService.Instance["options.midi.nodevice"] });
        MidiOutputCombo.SelectedIndex = 0;

        MidiMonitorText.Text = $"Scan done – {MidiInputCombo.Items.Count} in, {MidiOutputCombo.Items.Count} out";
        AppLogger.Instance.Info($"MIDI scan: {MidiInputCombo.Items.Count} in, {MidiOutputCombo.Items.Count} out");
    }

    // ──────────────────────────────────────────────────────────────────
    //  Project Metadata
    // ──────────────────────────────────────────────────────────────────

    private void PopulateProjectMetadata()
    {
        ProjectBpmBox.Text = _vm.BPM.ToString("F0");
    }

    // ──────────────────────────────────────────────────────────────────
    //  Plugins
    // ──────────────────────────────────────────────────────────────────

    private void PopulatePlugins()
    {
        var plugins = PluginManager.Instance.Plugins;
        PluginListView.ItemsSource = plugins;
        PluginCountText.Text = $"{plugins.Count} plugin(s)";
    }

    private void RescanPlugins_Click(object sender, RoutedEventArgs e)
    {
        PopulatePlugins();
        AppLogger.Instance.Info($"Plugin rescan – {PluginManager.Instance.Plugins.Count} plugins");
    }

    // ──────────────────────────────────────────────────────────────────
    //  Debug / Logs
    // ──────────────────────────────────────────────────────────────────

    private void LoadDebugLogs()
    {
        var existing = AppLogger.Instance.GetLines();
        foreach (var line in existing)
            _logBuffer.AppendLine(line);
        LogTextBox.Text = _logBuffer.ToString();
        LogTextBox.ScrollToEnd();
        UpdateLogLineCount();
    }

    private void SubscribeToLogs()
    {
        AppLogger.Instance.LineAdded += OnLogLineAdded;
    }

    private void UnsubscribeFromLogs()
    {
        AppLogger.Instance.LineAdded -= OnLogLineAdded;
    }

    private void OnLogLineAdded(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnLogLineAdded(line));
            return;
        }
        _logBuffer.AppendLine(line);
        LogTextBox.AppendText(line + Environment.NewLine);
        LogTextBox.ScrollToEnd();
        UpdateLogLineCount();
    }

    private void UpdateLogLineCount()
    {
        LogLineCount.Text = $"{LogTextBox.LineCount} line(s)";
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Instance.Clear();
        _logBuffer.Clear();
        LogTextBox.Clear();
        UpdateLogLineCount();
    }

    private void CopyLogs_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(LogTextBox.Text))
            Clipboard.SetText(LogTextBox.Text);
    }

    private void SaveLogs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save Logs",
            Filter = "Text Files|*.txt|Log Files|*.log|All|*.*",
            DefaultExt = ".txt",
            FileName = $"dragondaw-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dlg.ShowDialog(this) == true)
        {
            System.IO.File.WriteAllText(dlg.FileName, LogTextBox.Text);
            AppLogger.Instance.Info($"Logs saved to {dlg.FileName}");
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Bottom bar
    // ──────────────────────────────────────────────────────────────────

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        // Revert theme, language, scale
        ThemeService.Instance.ApplyTheme(_originalTheme);
        LocalizationService.Instance.CurrentLanguage = _originalLanguage;
        ApplyScale(_originalScale);
        Close();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        // Apply BPM
        if (double.TryParse(ProjectBpmBox.Text, out var bpm) && bpm > 0)
            _vm.BPM = bpm;

        // Persist settings asynchronously
        _ = PersistSettingsAsync();

        AppLogger.Instance.Info("Settings applied");
        Close();
    }

    private async Task PersistSettingsAsync()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = System.IO.Path.Combine(appData, "Lapis DAW");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "ui_settings.json");

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                Theme = ThemeService.Instance.CurrentTheme,
                Language = LocalizationService.Instance.CurrentLanguage,
                UiScale = UiScaleCombo.SelectedIndex >= 0 ? ScaleValues[UiScaleCombo.SelectedIndex] : 1.0,
            });
            await System.IO.File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Failed to save UI settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads persisted UI settings and applies them at app startup.
    /// Call from App.xaml.cs OnStartup or MainWindow constructor.
    /// </summary>
    public static void LoadAndApplyPersistedSettings()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var path = System.IO.Path.Combine(appData, "Lapis DAW", "ui_settings.json");
            if (!System.IO.File.Exists(path)) return;

            var json = System.IO.File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Theme", out var themeProp))
                ThemeService.Instance.ApplyTheme(themeProp.GetString() ?? "DragonDark");

            if (root.TryGetProperty("Language", out var langProp))
                LocalizationService.Instance.CurrentLanguage = langProp.GetString() ?? "de";

            if (root.TryGetProperty("UiScale", out var scaleProp) && scaleProp.TryGetDouble(out var s) && s > 0.5)
            {
                // Deferred until MainWindow is loaded
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (Application.Current?.MainWindow is Window main)
                        main.LayoutTransform = new ScaleTransform(s, s);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        catch
        {
            // Silently ignore corrupted settings
        }
    }
}
