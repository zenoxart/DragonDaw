using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.MVVM.Models.Mixer;

/// <summary>
/// Root model for the entire mixer state.
/// Contains all channels and the currently selected channel.
/// </summary>
public sealed class MixerModel : INotifyPropertyChanged
{
    private int _selectedChannelIndex = -1;
    private MixerChannelModel? _masterChannel;

    public MixerModel()
    {
        // Create Master channel
        MasterChannel = new MixerChannelModel
        {
            Name = "Master",
            ChannelType = MixerChannelType.Master
        };
    }

    /// <summary>
    /// The master channel (always exists)
    /// </summary>
    public MixerChannelModel MasterChannel
    {
        get => _masterChannel!;
        private set => SetField(ref _masterChannel, value);
    }

    /// <summary>
    /// All track channels
    /// </summary>
    public ObservableCollection<MixerChannelModel> Channels { get; } = [];

    /// <summary>
    /// Index of the currently selected channel (-1 = Master, 0+ = Track index)
    /// </summary>
    public int SelectedChannelIndex
    {
        get => _selectedChannelIndex;
        set
        {
            if (SetField(ref _selectedChannelIndex, value))
            {
                OnPropertyChanged(nameof(SelectedChannel));
                SelectedChannelChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// The currently selected channel (Master if index is -1)
    /// </summary>
    public MixerChannelModel? SelectedChannel
    {
        get
        {
            if (_selectedChannelIndex < 0) return MasterChannel;
            if (_selectedChannelIndex >= Channels.Count) return null;
            return Channels[_selectedChannelIndex];
        }
    }

    /// <summary>
    /// Event fired when the selected channel changes
    /// </summary>
    public event EventHandler<int>? SelectedChannelChanged;

    /// <summary>
    /// Adds a new track channel
    /// </summary>
    public MixerChannelModel AddChannel(string name)
    {
        var channel = new MixerChannelModel
        {
            Name = name,
            ChannelType = MixerChannelType.Track
        };
        Channels.Add(channel);
        return channel;
    }

    /// <summary>
    /// Removes a track channel by index
    /// </summary>
    public bool RemoveChannel(int index)
    {
        if (index < 0 || index >= Channels.Count) return false;
        
        Channels.RemoveAt(index);
        
        // Adjust selection if needed
        if (SelectedChannelIndex >= Channels.Count)
        {
            SelectedChannelIndex = Channels.Count - 1;
        }
        
        return true;
    }

    /// <summary>
    /// Selects the master channel
    /// </summary>
    public void SelectMaster()
    {
        SelectedChannelIndex = -1;
    }

    /// <summary>
    /// Selects a track channel by index
    /// </summary>
    public void SelectChannel(int index)
    {
        if (index >= 0 && index < Channels.Count)
        {
            SelectedChannelIndex = index;
        }
    }

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
