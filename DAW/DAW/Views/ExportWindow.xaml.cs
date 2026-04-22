using System.Windows;
using System.Windows.Controls;
using DAW.Audio;
using DAW.Models;

namespace DAW.Views;

public partial class ExportWindow : Window
{
    private readonly IReadOnlyList<Track> _tracks;
    private readonly EffectChain _masterEffectChain;
    private readonly double _masterVolume;
    private readonly string _outputPath;
    private CancellationTokenSource? _cts;
    private bool _isExporting;

    public ExportWindow(IReadOnlyList<Track> tracks, EffectChain masterEffectChain, double masterVolume, string outputPath)
    {
        _tracks = tracks;
        _masterEffectChain = masterEffectChain;
        _masterVolume = masterVolume;
        _outputPath = outputPath;
        InitializeComponent();

        // Pre-fill output path and detect format from extension
        OutputPathBox.Text = outputPath;
        var ext = System.IO.Path.GetExtension(outputPath).ToLowerInvariant();
        FormatCombo.SelectedIndex = ext switch
        {
            ".mp3" => 1,
            ".flac" => 2,
            _ => 0
        };

        Loaded += ExportWindow_Loaded;
    }

    private async void ExportWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Auto-start rendering after the window is shown
        await Task.Yield(); // let UI render first
        StartExport();
    }

    private string GetSelectedExtension()
    {
        if (FormatCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return tag switch
            {
                "Mp3" => ".mp3",
                "Flac" => ".flac",
                _ => ".wav"
            };
        return ".wav";
    }

    private ExportFormat GetSelectedFormat()
    {
        if (FormatCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return tag switch
            {
                "Mp3" => ExportFormat.Mp3,
                "Flac" => ExportFormat.Flac,
                _ => ExportFormat.Wav
            };
        return ExportFormat.Wav;
    }

    private int GetSelectedValue(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out var val))
            return val;
        return 0;
    }

    private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputPathBox == null || string.IsNullOrEmpty(OutputPathBox.Text)) return;
        OutputPathBox.Text = System.IO.Path.ChangeExtension(OutputPathBox.Text, GetSelectedExtension());
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        StartExport();
    }

    private async void StartExport()
    {
        if (_isExporting) return;

        var path = OutputPathBox.Text;
        if (string.IsNullOrWhiteSpace(path)) return;

        var settings = new AudioExportSettings
        {
            OutputPath = path,
            Format = GetSelectedFormat(),
            SampleRate = GetSelectedValue(SampleRateCombo),
            BitDepth = GetSelectedValue(BitDepthCombo),
            Channels = GetSelectedValue(ChannelsCombo)
        };

        if (settings.SampleRate == 0) settings.SampleRate = 44100;
        if (settings.BitDepth == 0) settings.BitDepth = 16;
        if (settings.Channels == 0) settings.Channels = 2;

        _isExporting = true;
        ExportBtn.IsEnabled = false;
        FormatCombo.IsEnabled = false;
        SampleRateCombo.IsEnabled = false;
        BitDepthCombo.IsEnabled = false;
        ChannelsCombo.IsEnabled = false;

        _cts = new CancellationTokenSource();

        var progress = new Progress<ExportProgress>(p =>
        {
            ExportProgress.Value = p.Percentage;
            PercentText.Text = $"{p.Percentage:F0}%";
            StatusText.Text = p.Status;
            if (p.Elapsed.TotalSeconds > 0 && p.Estimated.TotalSeconds > 0)
            {
                var remaining = p.Estimated - p.Elapsed;
                TimeText.Text = remaining.TotalSeconds > 0
                    ? $"~{remaining:mm\\:ss} remaining"
                    : "";
            }
        });

        try
        {
            await AudioExportService.ExportAsync(
                _tracks, _masterEffectChain, _masterVolume,
                settings, progress, _cts.Token);

            StatusText.Text = "✓ Export complete!";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
            TimeText.Text = "";
            CancelBtn.Content = "Close";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Export cancelled.";
            ExportProgress.Value = 0;
            PercentText.Text = "0%";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"✗ Error: {ex.Message}";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF8, 0x51, 0x49));
        }
        finally
        {
            _isExporting = false;
            ExportBtn.IsEnabled = true;
            FormatCombo.IsEnabled = true;
            SampleRateCombo.IsEnabled = true;
            BitDepthCombo.IsEnabled = true;
            ChannelsCombo.IsEnabled = true;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_isExporting && _cts != null)
        {
            _cts.Cancel();
        }
        else
        {
            Close();
        }
    }
}
