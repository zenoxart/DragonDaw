namespace DAW.Audio;

/// <summary>
/// Cross-channel choke-group coordinator for the Channel Rack ("Cut" / "By"
/// groups, FL Studio style). Each <see cref="ChannelRackBusProvider"/> registers
/// itself here under its own <see cref="ChannelRackBusProvider.CutGroup"/> so
/// that another channel's trigger can silence it — e.g. a closed hi-hat with
/// CutByGroup = 1 chokes an open hi-hat whose own CutGroup = 1.
///
/// Membership changes happen on the UI thread (the user editing Cut/By in the
/// Sound Settings window); triggering happens on the pattern-clock thread. Both
/// are rare compared to audio rendering, so a simple lock is sufficient — no
/// bus ever touches this class from inside its own Read().
/// </summary>
internal static class CutGroupRegistry
{
    private static readonly object _lock = new();
    private static readonly Dictionary<int, List<ChannelRackBusProvider>> _byGroup = new();

    /// <summary>Registers a newly created bus under its (initially 0 = "None") group.</summary>
    public static void Register(ChannelRackBusProvider bus)
    {
        lock (_lock) AddTo(bus.CutGroup, bus);
    }

    /// <summary>Removes a bus from every group it belongs to (channel/strip deleted).</summary>
    public static void Unregister(ChannelRackBusProvider bus)
    {
        lock (_lock)
            foreach (var list in _byGroup.Values)
                list.Remove(bus);
    }

    /// <summary>Moves a bus from its old choke-group bucket to its new one.</summary>
    public static void UpdateCutGroup(ChannelRackBusProvider bus, int oldGroup, int newGroup)
    {
        if (oldGroup == newGroup) return;
        lock (_lock)
        {
            if (_byGroup.TryGetValue(oldGroup, out var oldList)) oldList.Remove(bus);
            AddTo(newGroup, bus);
        }
    }

    private static void AddTo(int group, ChannelRackBusProvider bus)
    {
        if (!_byGroup.TryGetValue(group, out var list))
            _byGroup[group] = list = new List<ChannelRackBusProvider>();
        list.Add(bus);
    }

    /// <summary>
    /// Requests that every bus whose CutGroup matches <paramref name="group"/> fade
    /// out its ringing voice(s) via its own Release stage. Safe to call from the
    /// pattern-clock thread; the actual fade is applied by each target bus on its
    /// own mixer-thread Read(), so no bus's <c>_active</c> list is touched from here.
    /// </summary>
    public static void Choke(int group, ChannelRackBusProvider triggeredBy)
    {
        if (group <= 0) return;

        ChannelRackBusProvider[] targets;
        lock (_lock)
            targets = _byGroup.TryGetValue(group, out var list) ? list.ToArray() : [];

        foreach (var bus in targets)
            bus.RequestChoke();
    }
}
