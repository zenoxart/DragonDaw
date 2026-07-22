using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DAW.Audio.Effects;

namespace DAW.Audio;

/// <summary>
/// Thread-safe effect chain.
///
/// The audio thread reads <see cref="EffectsSnapshot"/> — a volatile array swapped
/// atomically on every modification.  All mutation methods update both the internal
/// list and the snapshot atomically under <see cref="_modifyLock"/>.
///
/// UI binds to <see cref="Effects"/> (ObservableCollection).  Updates to it are always
/// posted to the UI thread via Dispatcher.BeginInvoke so the audio callback never blocks.
/// </summary>
public class EffectChain : INotifyPropertyChanged
{
    private volatile bool _isBypassed;
    private volatile AudioEffect[] _effectsSnapshot = [];
    private readonly List<AudioEffect> _effects = [];
    private readonly object _modifyLock = new();

    public ObservableCollection<AudioEffect> Effects { get; } = [];

    public AudioEffect[] EffectsSnapshot => _effectsSnapshot;

    public bool IsBypassed
    {
        get => _isBypassed;
        set { if (_isBypassed != value) { _isBypassed = value; OnPropertyChanged(); } }
    }

    public int EffectCount => _effectsSnapshot.Length;

    public int TotalLatencySamples
        => _effectsSnapshot.Sum(e => e.IsEnabled ? e.LatencySamples : 0);

    private void SwapSnapshot()
    {
        _effectsSnapshot = _effects.ToArray();
    }

    private static void OnUI(Action a)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess())
            d.BeginInvoke(a, System.Windows.Threading.DispatcherPriority.Normal);
        else
            a();
    }

    public void AddEffect(AudioEffect effect)
    {
        lock (_modifyLock)
        {
            _effects.Add(effect);
            SwapSnapshot();
        }
        OnUI(() => { Effects.Add(effect); OnPropertyChanged(nameof(EffectCount)); });
    }

    public void InsertEffect(int index, AudioEffect effect)
    {
        int clamped;
        lock (_modifyLock)
        {
            clamped = Math.Clamp(index, 0, _effects.Count);
            _effects.Insert(clamped, effect);
            SwapSnapshot();
        }
        OnUI(() => { Effects.Insert(clamped, effect); OnPropertyChanged(nameof(EffectCount)); });
    }

    public bool RemoveEffect(AudioEffect effect)
    {
        bool removed;
        lock (_modifyLock)
        {
            removed = _effects.Remove(effect);
            if (removed) SwapSnapshot();
        }
        if (removed)
            OnUI(() => { Effects.Remove(effect); OnPropertyChanged(nameof(EffectCount)); });
        return removed;
    }

    public void MoveEffectUp(AudioEffect effect)
    {
        lock (_modifyLock)
        {
            int idx = _effects.IndexOf(effect);
            if (idx <= 0) return;
            _effects.RemoveAt(idx); _effects.Insert(idx - 1, effect);
            SwapSnapshot();
        }
        OnUI(() => { int i = Effects.IndexOf(effect); if (i > 0) Effects.Move(i, i - 1); });
    }

    public void MoveEffectDown(AudioEffect effect)
    {
        lock (_modifyLock)
        {
            int idx = _effects.IndexOf(effect);
            if (idx < 0 || idx >= _effects.Count - 1) return;
            _effects.RemoveAt(idx); _effects.Insert(idx + 1, effect);
            SwapSnapshot();
        }
        OnUI(() => { int i = Effects.IndexOf(effect); if (i >= 0 && i < Effects.Count - 1) Effects.Move(i, i + 1); });
    }

    public void Reset()
    {
        var snap = _effectsSnapshot;
        foreach (var e in snap) try { e.Reset(); } catch { }
    }

    public void Clear()
    {
        lock (_modifyLock) { _effects.Clear(); SwapSnapshot(); }
        OnUI(() => { Effects.Clear(); OnPropertyChanged(nameof(EffectCount)); });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
