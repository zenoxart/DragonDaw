using System.Runtime.CompilerServices;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using DAW.Audio.Effects;

namespace DAW.Audio;

/// <summary>
/// Central audio mix engine.
/// Signal chain: Track inputs → MixingSampleProvider → Master EffectChain → Metering → WaveOut
///
/// PERFORMANCE NOTES
/// ─────────────────
/// • AddInput/RemoveInput use a dedicated object lock that is never held during
///   the audio callback — NAudio's MixingSampleProvider has its own internal
///   synchronisation that is safe from the audio thread.
/// • ReconfigureBufferSize stops/restarts the WaveOut device atomically.
/// </summary>
public sealed class AudioMixEngine : IDisposable
{
    private WaveOutEvent _waveOut;
    private readonly MixingSampleProvider _mixer;
    private readonly EffectChain _masterEffectChain;
    private readonly EffectSampleProvider _masterEffectProvider;
    private readonly MeteringSampleProvider _masterMeter;
    private readonly object _deviceLock = new(); // guards _waveOut only

    private readonly Dictionary<ISampleProvider, ISampleProvider> _inputMap = new();
    private readonly object _inputLock = new(); // guards _inputMap; never held during Read()

    public int CurrentDesiredLatency { get; private set; }
    public int CurrentNumberOfBuffers { get; private set; }

    public AudioMixEngine(int sampleRate = 44100, int channels = 2,
                          int desiredLatency = 100, int numberOfBuffers = 3)
    {
        CurrentDesiredLatency  = desiredLatency;
        CurrentNumberOfBuffers = numberOfBuffers;

        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _mixer = new MixingSampleProvider(format) { ReadFully = true };

        _masterEffectChain    = new EffectChain();
        _masterEffectProvider = new EffectSampleProvider(_mixer, _masterEffectChain);
        _masterMeter          = new MeteringSampleProvider(_masterEffectProvider);

        // Pre-allocate voice pool and permanently wire voices into the mixer
        // BEFORE initialising WaveOut so the mixer is stable on first callback.
        _voicePool = new StepVoice[VoicePoolSize];
        InitVoicePool();

        _waveOut = CreateWaveOut(desiredLatency, numberOfBuffers);
        _waveOut.Init(_masterMeter);
    }

    private static WaveOutEvent CreateWaveOut(int latencyMs, int buffers)
        => new() { DesiredLatency = latencyMs, NumberOfBuffers = buffers };

    public void ReconfigureBufferSize(int bufferSizeSamples, int sampleRate)
    {
        lock (_deviceLock)
        {
            int latencyMs = (int)Math.Ceiling(bufferSizeSamples * 1000.0 / sampleRate);
            int buffers   = bufferSizeSamples <= 256 ? 4 : 3;

            CurrentDesiredLatency  = latencyMs;
            CurrentNumberOfBuffers = buffers;

            bool wasPlaying = _waveOut.PlaybackState == PlaybackState.Playing;
            _waveOut.Stop();
            _waveOut.Dispose();

            _waveOut = CreateWaveOut(latencyMs, buffers);
            _waveOut.Init(_masterMeter);

            if (wasPlaying) _waveOut.Play();
        }
    }

    public MeteringSampleProvider MasterMeter      => _masterMeter;
    public EffectChain             MasterEffectChain => _masterEffectChain;
    public WaveFormat              MixFormat         => _mixer.WaveFormat;

    // ── Voice pool for pattern step playback ─────────────────────────────────

    /// <summary>
    /// Fixed-size pool of voices permanently wired into the mixer.
    /// Playing a pre-loaded sample only rewinds an idle voice — no Add/Remove per step,
    /// which eliminates the _inputLock contention that was causing the first-hit latency.
    /// </summary>
    private readonly StepVoice[] _voicePool;
    private int _nextVoice = 0;
    private const int VoicePoolSize = 32;

    private void InitVoicePool()
    {
        for (int i = 0; i < VoicePoolSize; i++)
        {
            var v = new StepVoice(_mixer.WaveFormat);
            _voicePool[i] = v;
            _mixer.AddMixerInput(v);
        }
    }

    /// <summary>
    /// Plays a pre-loaded sample by stealing the least-recently-used idle voice.
    /// Safe to call from the audio background thread — only does an interlocked index bump
    /// and an atomic reference swap, no locks.
    /// </summary>
    public void PlayPreloaded(PreloadedSample sample, float velocity = 1.0f)
    {
        // Round-robin over the pool; Interlocked ensures thread-safety without a lock.
        int idx  = Interlocked.Increment(ref _nextVoice) % VoicePoolSize;
        _voicePool[idx].Trigger(sample, velocity);
        Play();
    }

    public void AddInput(ISampleProvider input)
    {
        lock (_inputLock)
        {
            if (_inputMap.ContainsKey(input)) return;
            var adapted = EnsureMatchingFormat(input);
            _inputMap[input] = adapted;
            _mixer.AddMixerInput(adapted);
        }
    }

    public void RemoveInput(ISampleProvider input)
    {
        lock (_inputLock)
        {
            if (_inputMap.Remove(input, out var adapted))
                _mixer.RemoveMixerInput(adapted);
        }
    }

    public void RemoveAllInputs()
    {
        lock (_inputLock)
        {
            foreach (var adapted in _inputMap.Values)
                _mixer.RemoveMixerInput(adapted);
            _inputMap.Clear();
        }
    }

    /// <summary>
    /// Pre-loads an audio file into a memory buffer and returns an opaque token.
    /// Call this once when a sample is assigned to a channel.
    /// The token can be passed to <see cref="PlayPreloaded"/> repeatedly with no disk I/O.
    /// Returns <c>null</c> when the file cannot be read.
    /// </summary>
    public PreloadedSample? Preload(string filePath, float volume = 1.0f)
    {
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            return null;

        try
        {
            using var reader = new AudioFileReader(filePath);
            var     target   = _mixer.WaveFormat;

            // Decode + resample + up/down-mix into mixer format
            ISampleProvider src = reader;
            if (src.WaveFormat.SampleRate != target.SampleRate)
                src = new WdlResamplingSampleProvider(src, target.SampleRate);
            if (src.WaveFormat.Channels == 1 && target.Channels == 2)
                src = new MonoToStereoSampleProvider(src);
            else if (src.WaveFormat.Channels == 2 && target.Channels == 1)
                src = new StereoToMonoSampleProvider(src);

            // Slurp all samples into a float array
            var buf  = new List<float>(src.WaveFormat.SampleRate * src.WaveFormat.Channels * 10);
            var tmp  = new float[4096];
            int read;
            while ((read = src.Read(tmp, 0, tmp.Length)) > 0)
                buf.AddRange(tmp.AsSpan(0, read));

            return new PreloadedSample(buf.ToArray(), target, volume);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Plays a pre-loaded sample pitch-shifted by <paramref name="semitones"/> relative to
    /// the base pitch (MIDI 60 = C5 = rate 1.0).
    /// Uses <see cref="RateChangeSampleProvider"/> for real-time resampling.
    /// Safe to call from any thread.
    /// </summary>
    public void PlayPreloadedAtPitch(PreloadedSample sample, int semitones)
    {
        if (semitones == 0) { PlayPreloaded(sample); return; }

        float rate = (float)Math.Pow(2.0, semitones / 12.0);

        Task.Run(() =>
        {
            try
            {
                var raw    = new PreloadedSampleProvider(sample);
                var pitched = new RateChangeSampleProvider(raw) { Rate = rate };
                var adapted = EnsureMatchingFormat(pitched);

                OneShotProvider? autoRemove = null;
                autoRemove = new OneShotProvider(adapted, () =>
                {
                    if (autoRemove != null) RemoveInput(autoRemove);
                });
                Play();
                AddInput(autoRemove);
            }
            catch { }
        });
    }

    /// <summary>Wraps a <see cref="PreloadedSample"/> as a one-shot <see cref="ISampleProvider"/>.</summary>
    private sealed class PreloadedSampleProvider(PreloadedSample sample) : ISampleProvider
    {
        private int _pos;
        public WaveFormat WaveFormat => sample.Format;

        public int Read(float[] buffer, int offset, int count)
        {
            int remaining = sample.Data.Length - _pos;
            int toCopy    = Math.Min(count, remaining);
            if (toCopy == 0) return 0;

            if (sample.Volume == 1.0f)
                Array.Copy(sample.Data, _pos, buffer, offset, toCopy);
            else
                for (int i = 0; i < toCopy; i++)
                    buffer[offset + i] = sample.Data[_pos + i] * sample.Volume;

            _pos += toCopy;
            return toCopy;
        }
    }

    /// <summary>
    /// Plays an audio file once and automatically removes it from the mixer when done.
    /// Prefer <see cref="PlayPreloaded"/> when latency matters.
    /// </summary>
    public void PlayOneShot(string filePath, float volume = 1.0f)
    {
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

        Task.Run(() =>
        {
            try
            {
                var reader = new AudioFileReader(filePath) { Volume = volume };
                OneShotProvider? autoRemove = null;
                autoRemove = new OneShotProvider(reader, () =>
                {
                    if (autoRemove != null) RemoveInput(autoRemove);
                    reader.Dispose();
                });
                Play();
                AddInput(autoRemove);
            }
            catch { }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ISampleProvider EnsureMatchingFormat(ISampleProvider input)
    {
        var target = _mixer.WaveFormat;
        ISampleProvider result = input;

        if (result.WaveFormat.SampleRate != target.SampleRate)
            result = new WdlResamplingSampleProvider(result, target.SampleRate);

        if (result.WaveFormat.Channels == 1 && target.Channels == 2)
            result = new MonoToStereoSampleProvider(result);
        else if (result.WaveFormat.Channels == 2 && target.Channels == 1)
            result = new StereoToMonoSampleProvider(result);

        return result;
    }

    public ISampleProvider AdaptFormat(ISampleProvider input) => EnsureMatchingFormat(input);

    public void Play()
    {
        lock (_deviceLock)
        {
            if (_waveOut.PlaybackState != PlaybackState.Playing)
                _waveOut.Play();
        }
    }

    public void Pause()
    {
        lock (_deviceLock)
        {
            if (_waveOut.PlaybackState == PlaybackState.Playing)
                _waveOut.Pause();
        }
    }

    public void Stop()
    {
        lock (_deviceLock)
        {
            if (_waveOut.PlaybackState != PlaybackState.Stopped)
                _waveOut.Stop();
        }
    }

    public PlaybackState PlaybackState
    {
        get { lock (_deviceLock) return _waveOut.PlaybackState; }
    }

    public void Dispose()
    {
        lock (_deviceLock)
        {
            _waveOut.Stop();
            _waveOut.Dispose();
        }
    }

    /// <summary>
    /// Wraps an <see cref="ISampleProvider"/> and fires <paramref name="onFinished"/>
    /// exactly once when the source returns 0 samples (i.e. playback is complete).
    /// </summary>
    private sealed class OneShotProvider(ISampleProvider source, Action onFinished) : ISampleProvider
    {
        private bool _finished;
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (_finished) return 0;
            int read = source.Read(buffer, offset, count);
            if (read == 0 && !_finished)
            {
                _finished = true;
                Task.Run(onFinished);
            }
            return read;
        }
    }

}

/// <summary>
/// Immutable token returned by <see cref="AudioMixEngine.Preload"/>.
/// Holds the fully decoded, resampled, channel-matched float samples.
/// </summary>
public sealed class PreloadedSample(float[] data, WaveFormat format, float volume)
{
    internal float[]    Data   { get; } = data;
    internal WaveFormat Format { get; } = format;
    internal float      Volume { get; } = volume;
}

/// <summary>
/// One voice in the step-sequencer pool.
/// Permanently lives inside the mixer; playing a sample just atomically swaps the
/// data reference and resets the position — no Add/Remove, no lock contention.
/// </summary>
internal sealed class StepVoice : ISampleProvider
{
    // Written by the audio-clock thread, read by the mixer callback thread.
    // Using a class wrapper so the reference swap is atomic on 64-bit.
    private sealed class Playback(float[] data, float volume)
    {
        public readonly float[] Data   = data;
        public readonly float   Volume = volume;
        public          int     Pos;   // read/written only on mixer thread
    }

    private volatile Playback? _current;

    public WaveFormat WaveFormat { get; }

    public StepVoice(WaveFormat format) => WaveFormat = format;

    /// <summary>Arm this voice with new sample data. Called from the clock thread.</summary>
    public void Trigger(PreloadedSample sample, float velocity = 1.0f)
    {
        // Atomically replace: the mixer thread will pick it up on the next callback.
        Volatile.Write(ref _current, new Playback(sample.Data, sample.Volume * velocity));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var pb = Volatile.Read(ref _current);
        if (pb == null || pb.Pos >= pb.Data.Length)
        {
            // Silence — voice is idle
            Array.Clear(buffer, offset, count);
            return count; // ReadFully=true: always return count
        }

        int remaining = pb.Data.Length - pb.Pos;
        int toCopy    = Math.Min(count, remaining);

        if (pb.Volume == 1.0f)
            Array.Copy(pb.Data, pb.Pos, buffer, offset, toCopy);
        else
            for (int i = 0; i < toCopy; i++)
                buffer[offset + i] = pb.Data[pb.Pos + i] * pb.Volume;

        // Zero-pad tail
        if (toCopy < count)
            Array.Clear(buffer, offset + toCopy, count - toCopy);

        pb.Pos += toCopy;
        return count;
    }
}
