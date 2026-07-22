using System.ComponentModel;
using System.Reflection;
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
    private string _currentPresetName = string.Empty;

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
    /// Name of the currently loaded preset, or empty string if no preset is active.
    /// Updated automatically by <see cref="Services.EffectPresetService"/> on load/save.
    /// </summary>
    public string CurrentPresetName
    {
        get => _currentPresetName;
        set
        {
            if (_currentPresetName != value)
            {
                _currentPresetName = value;
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
    /// Algorithmic latency introduced by this effect, in samples.
    /// Most effects are sample-accurate (0). Override for look-ahead or block-based
    /// effects so the routing engine can insert compensating delays on parallel paths.
    /// </summary>
    public virtual int LatencySamples => 0;

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
    /// Creates a deep copy of this effect with all parameter values copied.
    /// Uses <see cref="EffectFactory"/> to create a new instance, then copies all public read/write properties.
    /// </summary>
    public AudioEffect? Clone()
    {
        var clone = EffectFactory.Create(EffectType);
        if (clone is null) return null;

        clone.IsEnabled = IsEnabled;
        clone.IsExpanded = IsExpanded;

        // Copy all public read/write properties declared on the concrete type
        foreach (var prop in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.DeclaringType == typeof(AudioEffect)) continue; // skip base props already handled
            if (prop.GetIndexParameters().Length > 0) continue;

            try
            {
                prop.SetValue(clone, prop.GetValue(this));
            }
            catch
            {
                // Skip properties that can't be copied (events, etc.)
            }
        }

        return clone;
    }

    /// <summary>
    /// Resets the effect state (e.g., clear delay lines, reset filters).
    /// </summary>
    public virtual void Reset() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises PropertyChanged.  When called from the audio thread the notification
    /// is dispatched asynchronously to the UI thread so the audio callback never
    /// blocks waiting for UI work.
    /// </summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var handler = PropertyChanged;
        if (handler == null) return;

        var args = new PropertyChangedEventArgs(propertyName);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            // Audio thread: post asynchronously, never block
            dispatcher.BeginInvoke(() => handler(this, args),
                System.Windows.Threading.DispatcherPriority.Background);
        }
        else
        {
            handler(this, args);
        }
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
