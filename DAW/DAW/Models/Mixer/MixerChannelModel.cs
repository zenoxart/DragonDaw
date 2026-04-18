using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.Models.Mixer;

/// <summary>
/// Model for a mixer channel (Track or Master).
/// Contains the channel configuration and effect chain.
/// </summary>
public sealed class MixerChannelModel : INotifyPropertyChanged
{
    public const int MaxEffectSlots = 10;
    
    private string _name = "Channel";
    private string _channelId = Guid.NewGuid().ToString();
    private double _volume = 0.8;
    private double _pan = 0.0;
    private bool _isMuted;
    private bool _isSolo;

    public MixerChannelModel()
    {
        // Initialize 10 effect slots
        for (int i = 1; i <= MaxEffectSlots; i++)
        {
            EffectSlots.Add(new EffectSlotModel { SlotIndex = i });
        }
    }

    /// <summary>
    /// Unique channel identifier
    /// </summary>
    public string ChannelId
    {
        get => _channelId;
        set => SetField(ref _channelId, value);
    }

    /// <summary>
    /// Display name of the channel
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>
    /// Channel type (Master, Track, Send, etc.)
    /// </summary>
    public MixerChannelType ChannelType { get; set; } = MixerChannelType.Track;

    /// <summary>
    /// Volume level as linear amplitude (0.0 to ~1.9953 = +6 dB)
    /// </summary>
    public double Volume
    {
        get => _volume;
        set => SetField(ref _volume, Math.Clamp(value, 0.0, 1.99526231496888));
    }

    /// <summary>
    /// Pan position (-1.0 = Left, 0.0 = Center, 1.0 = Right)
    /// </summary>
    public double Pan
    {
        get => _pan;
        set => SetField(ref _pan, Math.Clamp(value, -1.0, 1.0));
    }

    /// <summary>
    /// Whether the channel is muted
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    /// <summary>
    /// Whether the channel is solo'd
    /// </summary>
    public bool IsSolo
    {
        get => _isSolo;
        set => SetField(ref _isSolo, value);
    }

    /// <summary>
    /// The 10 effect slots for this channel
    /// </summary>
    public ObservableCollection<EffectSlotModel> EffectSlots { get; } = [];

    /// <summary>
    /// Gets an effect slot by index (1-based)
    /// </summary>
    public EffectSlotModel? GetSlot(int slotIndex)
    {
        if (slotIndex < 1 || slotIndex > MaxEffectSlots) return null;
        return EffectSlots[slotIndex - 1];
    }

    /// <summary>
    /// Gets the count of active (non-empty) effect slots
    /// </summary>
    public int ActiveEffectCount => EffectSlots.Count(s => s.HasPlugin);

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}

/// <summary>
/// Types of mixer channels
/// </summary>
public enum MixerChannelType
{
    Master,
    Track,
    Send,
    Return,
    Group
}
