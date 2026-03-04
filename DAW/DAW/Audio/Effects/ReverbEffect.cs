namespace DAW.Audio.Effects;

/// <summary>
/// Simple algorithmic reverb effect using comb and allpass filters.
/// </summary>
public class ReverbEffect : AudioEffect
{
    private double _roomSize = 0.5;
    private double _damping = 0.5;
    private double _wetLevel = 0.3;
    private double _dryLevel = 1.0;
    private double _width = 1.0;
    
    // Comb filter delay lines (8 filters)
    private readonly float[][] _combBuffersL = new float[8][];
    private readonly float[][] _combBuffersR = new float[8][];
    private readonly int[] _combIndices = new int[8];
    private readonly float[] _combFilterStore = new float[8];
    
    // Allpass filter delay lines (4 filters)
    private readonly float[][] _allpassBuffers = new float[4][];
    private readonly int[] _allpassIndices = new int[4];
    
    // Comb filter lengths (in samples at 44100Hz)
    private static readonly int[] CombTunings = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    private static readonly int[] AllpassTunings = [556, 441, 341, 225];
    
    private bool _initialized;
    private int _lastSampleRate;

    public ReverbEffect()
    {
        Name = "Reverb";
    }

    public override string EffectType => "Reverb";
    public override string Icon => "🏛️";

    /// <summary>Room size (0-1)</summary>
    public double RoomSize
    {
        get => _roomSize;
        set => SetField(ref _roomSize, Math.Clamp(value, 0, 1));
    }

    /// <summary>High frequency damping (0-1)</summary>
    public double Damping
    {
        get => _damping;
        set => SetField(ref _damping, Math.Clamp(value, 0, 1));
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

    /// <summary>Stereo width (0-1)</summary>
    public double Width
    {
        get => _width;
        set => SetField(ref _width, Math.Clamp(value, 0, 1));
    }

    private void Initialize(int sampleRate)
    {
        if (_initialized && _lastSampleRate == sampleRate) return;
        
        double scale = sampleRate / 44100.0;
        
        for (int i = 0; i < 8; i++)
        {
            int size = (int)(CombTunings[i] * scale);
            _combBuffersL[i] = new float[size];
            _combBuffersR[i] = new float[size + 23]; // Stereo spread
            _combIndices[i] = 0;
            _combFilterStore[i] = 0;
        }
        
        for (int i = 0; i < 4; i++)
        {
            int size = (int)(AllpassTunings[i] * scale);
            _allpassBuffers[i] = new float[size];
            _allpassIndices[i] = 0;
        }
        
        _initialized = true;
        _lastSampleRate = sampleRate;
    }

    public override void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        if (!IsEnabled) return;
        
        Initialize(sampleRate);
        
        float feedback = (float)(RoomSize * 0.28 + 0.7);
        float damp1 = (float)(Damping * 0.4);
        float damp2 = 1f - damp1;
        float wet = (float)WetLevel;
        float dry = (float)DryLevel;
        float wet1 = wet * ((float)Width / 2f + 0.5f);
        float wet2 = wet * ((1f - (float)Width) / 2f);

        int endIndex = offset + count;
        // Ensure we process complete frames (all channels together)
        int sampleCount = channels > 1 ? (endIndex - offset) / 2 * 2 : count;
        
        for (int i = offset; i < offset + sampleCount; i += channels)
        {
            float inputL = buffer[i];
            float inputR = channels > 1 ? buffer[i + 1] : inputL;
            float input = (inputL + inputR) * 0.5f;
            
            float outL = 0, outR = 0;
            
            // Process comb filters in parallel
            for (int c = 0; c < 8; c++)
            {
                var bufL = _combBuffersL[c];
                var bufR = _combBuffersR[c];
                if (bufL == null || bufR == null || bufL.Length == 0 || bufR.Length == 0) 
                    continue;
                    
                int idxL = _combIndices[c] % bufL.Length;
                int idxR = _combIndices[c] % bufR.Length;
                
                float outputL = bufL[idxL];
                float outputR = bufR[idxR];
                
                _combFilterStore[c] = outputL * damp2 + _combFilterStore[c] * damp1;
                
                bufL[idxL] = input + _combFilterStore[c] * feedback;
                bufR[idxR] = input + _combFilterStore[c] * feedback;
                
                outL += outputL;
                outR += outputR;
                
                _combIndices[c]++;
            }
            
            // Process allpass filters in series
            for (int a = 0; a < 4; a++)
            {
                var buf = _allpassBuffers[a];
                if (buf == null || buf.Length == 0) continue;
                
                int idx = _allpassIndices[a] % buf.Length;
                
                float bufOut = buf[idx];
                buf[idx] = outL + bufOut * 0.5f;
                outL = bufOut - outL;
                
                _allpassIndices[a]++;
            }
            
            outR = outL; // Simplified stereo
            
            // Mix wet and dry with clamping
            buffer[i] = Math.Clamp(inputL * dry + outL * wet1 + outR * wet2, -1f, 1f);
            if (channels > 1)
            {
                buffer[i + 1] = Math.Clamp(inputR * dry + outR * wet1 + outL * wet2, -1f, 1f);
            }
        }
    }

    public override void Reset()
    {
        for (int i = 0; i < 8; i++)
        {
            if (_combBuffersL[i] != null) Array.Clear(_combBuffersL[i]);
            if (_combBuffersR[i] != null) Array.Clear(_combBuffersR[i]);
            _combFilterStore[i] = 0;
        }
        
        for (int i = 0; i < 4; i++)
        {
            if (_allpassBuffers[i] != null) Array.Clear(_allpassBuffers[i]);
        }
    }
}
