namespace DAW.Audio.Effects;

/// <summary>
/// Delay/Echo effect with feedback.
/// </summary>
public class DelayEffect : AudioEffect
{
    private double _delayTime = 250;
    private double _feedback = 0.4;
    private double _wetLevel = 0.5;
    private double _dryLevel = 1.0;
    private bool _pingPong;
    
    // Delay buffer
    private float[]? _delayBufferL;
    private float[]? _delayBufferR;
    private int _writeIndex;
    private int _lastSampleRate;

    public DelayEffect()
    {
        Name = "Delay";
    }

    public override string EffectType => "Delay";
    public override string Icon => "🔁";

    /// <summary>Delay time in milliseconds (1-2000)</summary>
    public double DelayTime
    {
        get => _delayTime;
        set
        {
            if (SetField(ref _delayTime, Math.Clamp(value, 1, 2000)))
            {
                _delayBufferL = null; // Force reallocation
                _delayBufferR = null;
            }
        }
    }

    /// <summary>Feedback amount (0-0.95)</summary>
    public double Feedback
    {
        get => _feedback;
        set => SetField(ref _feedback, Math.Clamp(value, 0, 0.95));
    }

    /// <summary>Wet signal level (0-1)</summary>
    public double WetLevel
    {
        get => _wetLevel;
        set => SetField(ref _wetLevel, Math.Clamp(value, 0, 1));
    }

    /// <summary>Dry signal level (0-1)</summary>
    public double DryLevel
    {
        get => _dryLevel;
        set => SetField(ref _dryLevel, Math.Clamp(value, 0, 1));
    }

    /// <summary>Enable ping-pong stereo delay</summary>
    public bool PingPong
    {
        get => _pingPong;
        set => SetField(ref _pingPong, value);
    }

    private void EnsureBuffer(int sampleRate)
    {
        if (_delayBufferL != null && _lastSampleRate == sampleRate) return;
        
        int bufferSize = (int)(sampleRate * DelayTime / 1000.0) + 1;
        _delayBufferL = new float[bufferSize];
        _delayBufferR = new float[bufferSize];
        _writeIndex = 0;
        _lastSampleRate = sampleRate;
    }


    public override void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        if (!IsEnabled) return;
        
        EnsureBuffer(sampleRate);
        
        if (_delayBufferL == null || _delayBufferR == null) return;
        
        int bufLen = _delayBufferL.Length;
        if (bufLen == 0) return;
        
        int delaySamples = (int)(sampleRate * DelayTime / 1000.0);
        delaySamples = Math.Clamp(delaySamples, 1, bufLen - 1);
        
        float feedback = (float)Feedback;
        float wet = (float)WetLevel;
        float dry = (float)DryLevel;
        
        int endIndex = offset + count;
        // Ensure we process complete frames (all channels together)
        int sampleCount = channels > 1 ? (endIndex - offset) / 2 * 2 : count;
        
        for (int i = offset; i < offset + sampleCount; i += channels)
        {
            int readIndex = (_writeIndex - delaySamples + bufLen) % bufLen;
            
            float inputL = buffer[i];
            float inputR = channels > 1 ? buffer[i + 1] : inputL;
            
            float delayedL = _delayBufferL[readIndex];
            float delayedR = _delayBufferR[readIndex];
            
            if (PingPong && channels > 1)
            {
                // Ping-pong: left feeds right delay, right feeds left delay
                _delayBufferL[_writeIndex] = Math.Clamp(inputL + delayedR * feedback, -1f, 1f);
                _delayBufferR[_writeIndex] = Math.Clamp(inputR + delayedL * feedback, -1f, 1f);
            }
            else
            {
                // Normal delay
                _delayBufferL[_writeIndex] = Math.Clamp(inputL + delayedL * feedback, -1f, 1f);
                _delayBufferR[_writeIndex] = Math.Clamp(inputR + delayedR * feedback, -1f, 1f);
            }
            
            // Mix with clamping
            buffer[i] = Math.Clamp(inputL * dry + delayedL * wet, -1f, 1f);
            if (channels > 1)
            {
                buffer[i + 1] = Math.Clamp(inputR * dry + delayedR * wet, -1f, 1f);
            }
            
            _writeIndex = (_writeIndex + 1) % bufLen;
        }
    }

    public override void Reset()
    {
        if (_delayBufferL != null) Array.Clear(_delayBufferL);
        if (_delayBufferR != null) Array.Clear(_delayBufferR);
        _writeIndex = 0;
    }
}
