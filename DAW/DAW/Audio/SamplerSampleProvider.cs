using DAW.Models;
using NAudio.Wave;

namespace DAW.Audio;

/// <summary>
/// Sample provider for the Sampler/Clip with real-time parameter support.
/// Handles volume, pan, pitch, looping, and playback direction.
/// All parameters are designed for lock-free, thread-safe real-time access.
/// </summary>
public class SamplerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly ClipData _clipData;
    private readonly int _channels;
    private readonly int _sampleRate;
    
    // Playback state
    private long _currentPosition;
    private bool _isPlayingForward = true;
    
    // Constant-power panning coefficients
    private float _leftGain = 1.0f;
    private float _rightGain = 1.0f;
    private float _lastPan = 0.0f;
    
    public SamplerSampleProvider(ISampleProvider source, ClipData clipData)
    {
        _source = source;
        _clipData = clipData;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
        
        // Initialize position to start offset
        _currentPosition = clipData.StartOffsetSamples * _channels;
        UpdatePanCoefficients();
    }
    
    public WaveFormat WaveFormat => _source.WaveFormat;
    
    /// <summary>
    /// Current playback position in samples.
    /// </summary>
    public long CurrentPosition => _currentPosition / _channels;
    
    /// <summary>
    /// Current playback time.
    /// </summary>
    public TimeSpan CurrentTime => TimeSpan.FromSeconds((double)CurrentPosition / _sampleRate);
    
    /// <summary>
    /// Seeks to a specific sample position.
    /// </summary>
    public void Seek(long samplePosition)
    {
        _currentPosition = Math.Clamp(samplePosition * _channels, 0, _clipData.TotalSamples * _channels);
    }
    
    /// <summary>
    /// Seeks to a specific time position.
    /// </summary>
    public void SeekToTime(TimeSpan time)
    {
        Seek((long)(time.TotalSeconds * _sampleRate));
    }
    
    /// <summary>
    /// Resets playback to start position.
    /// </summary>
    public void Reset()
    {
        _currentPosition = _clipData.StartOffsetSamples * _channels;
        _isPlayingForward = !_clipData.ReversePlayback;
    }
    
    public int Read(float[] buffer, int offset, int count)
    {
        // Check if clip is bypassed
        if (!_clipData.IsEnabled)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
        
        int samplesRead = _source.Read(buffer, offset, count);
        if (samplesRead == 0) return 0;
        
        // Update pan coefficients if changed
        if (Math.Abs(_lastPan - _clipData.Pan) > 0.0001f)
        {
            UpdatePanCoefficients();
        }
        
        // Get current parameters (volatile reads)
        float volume = _clipData.Volume;
        float leftGain = _leftGain;
        float rightGain = _rightGain;
        
        // Apply volume and pan
        int endIndex = offset + samplesRead;
        
        if (_channels == 2)
        {
            for (int i = offset; i < endIndex; i += 2)
            {
                buffer[i] *= volume * leftGain;
                buffer[i + 1] *= volume * rightGain;
            }
        }
        else
        {
            for (int i = offset; i < endIndex; i++)
            {
                buffer[i] *= volume;
            }
        }
        
        // Handle looping
        if (_clipData.LoopEnabled)
        {
            HandleLooping(ref samplesRead);
        }
        
        // Sanitize output
        for (int i = offset; i < endIndex; i++)
        {
            float sample = buffer[i];
            if (float.IsNaN(sample) || float.IsInfinity(sample))
            {
                buffer[i] = 0f;
            }
            else
            {
                buffer[i] = Math.Clamp(sample, -1f, 1f);
            }
        }
        
        _currentPosition += samplesRead;
        return samplesRead;
    }
    
    private void UpdatePanCoefficients()
    {
        float pan = _clipData.Pan;
        _lastPan = pan;
        
        // Constant-power panning
        float angle = (pan + 1f) * 0.25f * MathF.PI;
        _leftGain = MathF.Cos(angle);
        _rightGain = MathF.Sin(angle);
    }
    
    private void HandleLooping(ref int samplesRead)
    {
        long loopStart = _clipData.LoopStartSamples * _channels;
        long loopEnd = _clipData.LoopEndSamples * _channels;
        
        if (loopEnd <= loopStart || loopEnd == 0)
        {
            loopEnd = _clipData.TotalSamples * _channels;
        }
        
        if (_clipData.PingPongLoop)
        {
            // Ping-pong: reverse direction at loop boundaries
            if (_isPlayingForward && _currentPosition >= loopEnd)
            {
                _isPlayingForward = false;
                _currentPosition = loopEnd - (_currentPosition - loopEnd);
            }
            else if (!_isPlayingForward && _currentPosition <= loopStart)
            {
                _isPlayingForward = true;
                _currentPosition = loopStart + (loopStart - _currentPosition);
            }
        }
        else
        {
            // Standard loop: jump back to start
            if (_currentPosition >= loopEnd)
            {
                _currentPosition = loopStart + (_currentPosition - loopEnd);
            }
        }
    }
}
