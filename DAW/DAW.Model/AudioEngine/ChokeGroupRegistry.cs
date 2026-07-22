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
internal static class ChokeGroupRegistry
{
    private static readonly object _lock = new();
    private static readonly Dictionary<int, List<ChannelRackBusProvider>> _byGroup = new();

    /// <summary>
    /// Moves a bus from its old choke-group bucket to its new one. Also used
    /// to register a bus for the first time (pass oldGroup = 0 / whatever the
    /// bus was previously reporting).
    /// </summary>
    public static void SetGroup(ChannelRackBusProvider bus, int oldGroup, int newGroup)
    {
        if (oldGroup == newGroup) return;
        lock (_lock)
        {
            if (oldGroup != 0 && _byGroup.TryGetValue(oldGroup, out var oldList))
                oldList.Remove(bus);

            if (newGroup == 0) return; // "None" — not choke-able, nothing to track

            if (!_byGroup.TryGetValue(newGroup, out var list))
                _byGroup[newGroup] = list = new List<ChannelRackBusProvider>();
            list.Add(bus);
        }
    }

    /// <summary>Removes a bus from its group entirely (channel/strip deleted).</summary>
    public static void Unregister(ChannelRackBusProvider bus, int group)
    {
        if (group == 0) return;
        lock (_lock)
            if (_byGroup.TryGetValue(group, out var list))
                list.Remove(bus);
    }

    /// <summary>
    /// Fades out (chokes) every bus currently registered under <paramref name="group"/>.
    /// Safe to call from the pattern-clock thread — each bus's own
    /// <see cref="ChannelRackBusProvider.ChokeActiveVoices"/> just raises a flag
    /// that its own mixer-thread Read() applies, so no bus's voice list is
    /// touched from here.
    /// </summary>
    public static void Choke(int group)
    {
        if (group == 0) return;

        ChannelRackBusProvider[] targets;
        lock (_lock)
            targets = _byGroup.TryGetValue(group, out var list) ? list.ToArray() : [];

        foreach (var bus in targets)
            bus.ChokeActiveVoices();
    }
}
