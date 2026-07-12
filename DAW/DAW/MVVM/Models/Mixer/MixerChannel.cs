using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DAW.Audio;
using DAW.Audio.Effects;
using DAW.MVVM.Models;

namespace DAW.MVVM.Models.Mixer;

/// <summary>
/// Represents a mixer channel in the routing system.
/// Inspired by FL Studio's mixer tracks.
/// Each channel can receive audio from a source track or other channels,
/// process it through an effect chain, and route to the master or other channels.
/// </summary>
public class MixerChannel : INotifyPropertyChanged
{
    private int _channelNumber;
    private string _name = string.Empty;
    private double _volume = 0.8;
    private double _pan = 0.0;
    private bool _isMuted;
    private bool _isSolo;
    private bool _isSelected;
    private Color _color = Colors.Gray;
    private Track? _sourceTrack;
    private double _meterLeft;
    private double _meterRight;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MixerChannel(int channelNumber)
    {
        ChannelNumber = channelNumber;
        Name = $"Ch {channelNumber}";
        EffectChain = new EffectChain();
        SendTargets = new ObservableCollection<int>();
        EffectSlots = new ObservableCollection<EffectSlot>();
        
        // Initialize 8 effect slots
        for (int i = 1; i <= 8; i++)
        {
            EffectSlots.Add(new EffectSlot(i));
        }
    }

    /// <summary>
    /// Unique channel number (1-based, like FL Studio).
    /// Channel 0 is reserved for Master.
    /// </summary>
    public int ChannelNumber
    {
        get => _channelNumber;
        set => SetField(ref _channelNumber, value);
    }

    /// <summary>
    /// Display name of this channel.
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>
    /// Volume level (0.0 to 1.0+).
    /// </summary>
    public double Volume
    {
        get => _volume;
        set => SetField(ref _volume, value);
    }

    /// <summary>
    /// Pan position (-1.0 = left, 0.0 = center, +1.0 = right).
    /// </summary>
    public double Pan
    {
        get => _pan;
        set => SetField(ref _pan, value);
    }

    /// <summary>
    /// Whether this channel is muted.
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    /// <summary>
    /// Whether this channel is soloed.
    /// </summary>
    public bool IsSolo
    {
        get => _isSolo;
        set => SetField(ref _isSolo, value);
    }

    /// <summary>
    /// Whether this channel is currently selected (for routing UI).
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>
    /// Color for visual identification.
    /// </summary>
    public Color Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    /// <summary>
    /// Optional reference to a source track (for track-based channels).
    /// If null, this is an empty/FX channel.
    /// </summary>
    public Track? SourceTrack
    {
        get => _sourceTrack;
        set
        {
            if (SetField(ref _sourceTrack, value) && value != null)
            {
                // Sync name and color from track
                Name = value.Title;
                Color = value.ChannelColor;
            }
        }
    }

    /// <summary>
    /// Effect chain for this channel.
    /// </summary>
    public EffectChain EffectChain { get; }

    /// <summary>
    /// Effect slots for mixer UI.
    /// </summary>
    public ObservableCollection<EffectSlot> EffectSlots { get; }

    /// <summary>
    /// List of channel numbers this channel sends to (routing targets).
    /// Empty = routes only to master.
    /// </summary>
    public ObservableCollection<int> SendTargets { get; }

    /// <summary>
    /// Left channel meter level (0.0 to 1.0+).
    /// </summary>
    public double MeterLeft
    {
        get => _meterLeft;
        set => SetField(ref _meterLeft, value);
    }

    /// <summary>
    /// Right channel meter level (0.0 to 1.0+).
    /// </summary>
    public double MeterRight
    {
        get => _meterRight;
        set => SetField(ref _meterRight, value);
    }

    /// <summary>
    /// Whether this is an empty channel (no source track).
    /// </summary>
    public bool IsEmpty => SourceTrack == null;

    /// <summary>
    /// Display text for volume in dB.
    /// </summary>
    public string VolumeDisplay
    {
        get
        {
            if (Volume < 0.001) return "-∞ dB";
            double db = 20 * Math.Log10(Volume);
            return $"{db:+0.0;-0.0} dB";
        }
    }

    /// <summary>
    /// Display text for pan.
    /// </summary>
    public string PanDisplay
    {
        get
        {
            if (Math.Abs(Pan) < 0.01) return "C";
            int percent = (int)(Pan * 100);
            return percent < 0 ? $"{-percent}% L" : $"{percent}% R";
        }
    }

    /// <summary>
    /// Adds a send to another channel.
    /// </summary>
    public void AddSend(int targetChannelNumber)
    {
        if (!SendTargets.Contains(targetChannelNumber))
        {
            SendTargets.Add(targetChannelNumber);
            OnPropertyChanged(nameof(SendTargets));
        }
    }

    /// <summary>
    /// Removes a send to another channel.
    /// </summary>
    public void RemoveSend(int targetChannelNumber)
    {
        if (SendTargets.Remove(targetChannelNumber))
        {
            OnPropertyChanged(nameof(SendTargets));
        }
    }

    /// <summary>
    /// Toggles a send to another channel.
    /// </summary>
    public void ToggleSend(int targetChannelNumber)
    {
        if (SendTargets.Contains(targetChannelNumber))
            RemoveSend(targetChannelNumber);
        else
            AddSend(targetChannelNumber);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        
        // Notify dependent properties
        if (propertyName == nameof(Volume))
            OnPropertyChanged(nameof(VolumeDisplay));
        if (propertyName == nameof(Pan))
            OnPropertyChanged(nameof(PanDisplay));
        if (propertyName == nameof(SourceTrack))
            OnPropertyChanged(nameof(IsEmpty));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
