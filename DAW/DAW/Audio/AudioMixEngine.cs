using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DAW.Audio;

/// <summary>
/// Central audio mix engine that renders all tracks through a single output device.
/// This guarantees sample-accurate synchronization — every track is mixed in the
/// same audio callback so there is zero start-up delay between tracks.
/// </summary>
public sealed class AudioMixEngine : IDisposable
{
    private readonly WaveOutEvent _waveOut;
    private readonly MixingSampleProvider _mixer;
    private readonly MeteringSampleProvider _masterMeter;
    private readonly object _lock = new();

    // Maps the raw track output → the adapted provider actually added to the mixer,
    // so RemoveInput can find and remove the correct instance.
    private readonly Dictionary<ISampleProvider, ISampleProvider> _inputMap = new();

    /// <summary>
    /// Creates the engine with a stereo 44.1 kHz mix bus (matches most audio files).
    /// </summary>
    public AudioMixEngine(int sampleRate = 44100, int channels = 2)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _mixer = new MixingSampleProvider(format)
        {
            ReadFully = true  // output silence when no inputs are connected
        };

        // Master metering — captures peak levels of the final mix
        _masterMeter = new MeteringSampleProvider(_mixer);

        _waveOut = new WaveOutEvent
        {
            DesiredLatency = 100,   // ms — reasonable default
            NumberOfBuffers = 3
        };
        _waveOut.Init(_masterMeter);
    }

    /// <summary>Master output metering provider — read peaks from this.</summary>
    public MeteringSampleProvider MasterMeter => _masterMeter;

    /// <summary>The shared mix format that all track providers must match.</summary>
    public WaveFormat MixFormat => _mixer.WaveFormat;

    /// <summary>
    /// Adds a track's sample provider to the mix bus.
    /// Automatically wraps the input to match the mix format if needed.
    /// Safe to call multiple times with the same input — duplicates are ignored.
    /// </summary>
    public void AddInput(ISampleProvider input)
    {
        lock (_lock)
        {
            if (_inputMap.ContainsKey(input))
                return; // already connected

            var adapted = EnsureMatchingFormat(input);
            _inputMap[input] = adapted;
            _mixer.AddMixerInput(adapted);
        }
    }

    /// <summary>Removes a track's sample provider from the mix bus.</summary>
    public void RemoveInput(ISampleProvider input)
    {
        lock (_lock)
        {
            if (_inputMap.Remove(input, out var adapted))
            {
                _mixer.RemoveMixerInput(adapted);
            }
        }
    }

    /// <summary>Removes all inputs from the mix bus.</summary>
    public void RemoveAllInputs()
    {
        lock (_lock)
        {
            foreach (var adapted in _inputMap.Values)
            {
                _mixer.RemoveMixerInput(adapted);
            }
            _inputMap.Clear();
        }
    }

    /// <summary>
    /// Ensures the provider's WaveFormat exactly matches the mixer's format.
    /// Handles sample rate and channel count mismatches.
    /// </summary>
    private ISampleProvider EnsureMatchingFormat(ISampleProvider input)
    {
        var target = _mixer.WaveFormat;
        var source = input.WaveFormat;

        ISampleProvider result = input;

        // Fix sample rate mismatch
        if (source.SampleRate != target.SampleRate)
        {
            result = new WdlResamplingSampleProvider(result, target.SampleRate);
        }

        // Fix channel count mismatch (mono → stereo)
        if (result.WaveFormat.Channels == 1 && target.Channels == 2)
        {
            result = new MonoToStereoSampleProvider(result);
        }
        // stereo → mono (unlikely but safe)
        else if (result.WaveFormat.Channels == 2 && target.Channels == 1)
        {
            result = new StereoToMonoSampleProvider(result);
        }

        return result;
    }

    /// <summary>Starts the single shared output device — all connected tracks play at once.</summary>
    public void Play()
    {
        if (_waveOut.PlaybackState != PlaybackState.Playing)
            _waveOut.Play();
    }

    /// <summary>Pauses the shared output device.</summary>
    public void Pause()
    {
        if (_waveOut.PlaybackState == PlaybackState.Playing)
            _waveOut.Pause();
    }

    /// <summary>Stops the shared output device.</summary>
    public void Stop()
    {
        if (_waveOut.PlaybackState != PlaybackState.Stopped)
            _waveOut.Stop();
    }

    public PlaybackState PlaybackState => _waveOut.PlaybackState;

    public void Dispose()
    {
        _waveOut.Stop();
        _waveOut.Dispose();
    }
}
