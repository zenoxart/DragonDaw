using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.MVVM.Models.Sequencer;

/// <summary>
/// A pattern contains a set of channels, each with a step grid.
/// Patterns are the fundamental unit of the Channel Rack / Step Sequencer.
/// Multiple patterns form a song in the Arrangement view.
/// </summary>
public class PatternModel : INotifyPropertyChanged
{
    private string _name        = "Pattern 1";
    private int    _stepCount   = 16;   // global step count for all channels
    private double _swing       = 0.0;  // 0–1; 0 = straight

    // ── Identity ─────────────────────────────────────────────────────────────

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    // ── Timing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Global step count. Changing this resizes every channel's Steps collection.
    /// Valid values: 1–128.
    /// </summary>
    public int StepCount
    {
        get => _stepCount;
        set
        {
            if (!SetField(ref _stepCount, Math.Clamp(value, 1, 256))) return;
            foreach (var ch in Channels)
                ch.StepCount = _stepCount;
        }
    }

    /// <summary>
    /// Swing amount 0–1.  0 = perfectly straight.  0.5 = moderate swing (every second
    /// 16th note delayed by half a subdivision).
    /// </summary>
    public double Swing
    {
        get => _swing;
        set => SetField(ref _swing, Math.Clamp(value, 0.0, 1.0));
    }

    // ── Channels ──────────────────────────────────────────────────────────────

    public ObservableCollection<ChannelModel> Channels { get; } = [];

    // ── Factory helpers ───────────────────────────────────────────────────────

    public void AddChannel(string name, string icon = "🎹")
    {
        var ch = new ChannelModel(name, _stepCount);
        ch.PluginIcon = icon;
        Channels.Add(ch);
    }

    public void RemoveChannel(ChannelModel channel) => Channels.Remove(channel);

    // ── INotifyPropertyChanged ─────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    // ── Static factory: default "Kit" pattern ─────────────────────────────────

    /// <summary>
    /// Creates the initial pattern shown on application start / new project:
    /// completely EMPTY — no preset channels, no preset sounds, no pre-filled
    /// steps. The user adds their own channels via the Channel Rack.
    /// </summary>
    public static PatternModel CreateDefault()
    {
        return new PatternModel { Name = "Pattern 1" };
    }
}
