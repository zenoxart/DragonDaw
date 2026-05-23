using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.Models.Sequencer;

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

    public static PatternModel CreateDefault()
    {
        var p = new PatternModel { Name = "Beat 1" };
        p.AddChannel("Kick",    "🥁");
        p.AddChannel("Snare",   "🪘");
        p.AddChannel("Hi-Hat",  "🎶");
        p.AddChannel("Open HH", "🎶");
        p.AddChannel("Clap",    "👏");
        p.AddChannel("Bass",    "🎸");
        p.AddChannel("Lead",    "🎹");
        p.AddChannel("Pad",     "🌊");

        // Pre-fill a typical four-on-the-floor kick pattern
        int[] kickSteps  = [0, 4, 8, 12];
        int[] snareSteps = [4, 12];
        int[] hatSteps   = [0, 2, 4, 6, 8, 10, 12, 14];

        foreach (int s in kickSteps)  p.Channels[0].Steps[s].IsActive = true;
        foreach (int s in snareSteps) p.Channels[1].Steps[s].IsActive = true;
        foreach (int s in hatSteps)   p.Channels[2].Steps[s].IsActive = true;

        return p;
    }
}
