using System.ComponentModel;
using System.Runtime.CompilerServices;
using NAudio.Wave;

namespace DAW.Audio.Effects;

/// <summary>
/// Base class for all audio effects in the DAW.
/// Thread-safe for real-time audio processing.
/// </summary>
public abstract class AudioEffect : INotifyPropertyChanged
{
    private volatile string _name = "Effect";
    private volatile bool _isEnabled = true;
    private volatile bool _isExpanded = true;

    /// <summary>
    /// Display name of the effect.
    /// </summary>
    public string Name
    {
        get => _name;
        protected set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Whether the effect is active in the signal chain.
    /// Thread-safe for real-time access.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
    }
    
    /// <summary>
    /// Whether the effect panel is expanded in UI.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Effect type identifier for serialization/UI.
    /// </summary>
    public abstract string EffectType { get; }
    
    /// <summary>
    /// Icon for the effect (emoji or icon character).
    /// </summary>
    public abstract string Icon { get; }

    /// <summary>
    /// Processes a sample buffer through this effect.
    /// Called from audio thread - must be thread-safe!
    /// </summary>
    /// <param name="buffer">Audio sample buffer (interleaved if stereo)</param>
    /// <param name="offset">Offset into the buffer to start processing</param>
    /// <param name="count">Number of samples to process</param>
    /// <param name="sampleRate">Sample rate in Hz</param>
    /// <param name="channels">Number of channels (1=mono, 2=stereo)</param>
    public abstract void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels);

    /// <summary>
    /// Resets the effect state (e.g., clear delay lines, reset filters).
    /// </summary>
    public virtual void Reset() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        // Fire on UI thread if needed
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Thread-safe property setter for audio parameters.
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
