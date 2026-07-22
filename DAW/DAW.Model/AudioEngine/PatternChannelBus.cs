using System.Collections.Concurrent;
using DAW.MVVM.Models.Sequencer;

namespace DAW.Audio;

/// <summary>
/// Hands sample-accurately rendered audio from <see cref="PatternSequencerProvider"/>
/// (Playlist-triggered pattern playback) over to each Channel-Rack channel's own
/// mixer strip, so Playlist playback goes through the same Volume/Pan/Mute,
/// insert effects, and metering as live Channel-Rack triggering — instead of
/// bypassing the strip entirely.
///
/// One queue per channel. The sequencer pushes exactly one block per channel on
/// every <see cref="PatternSequencerProvider.Read"/> call (even a silent one,
/// so the queue depth stays essentially 0–1); each channel's
/// <see cref="PatternChannelTapProvider"/> — wired into that channel's strip
/// alongside its <see cref="ChannelRackBusProvider"/> — dequeues exactly one
/// block per its own Read(). Both sides are pulled once per mixer buffer, so
/// depth never grows in steady state; the depth cap below only guards against
/// a channel's tap having been torn down mid-playback.
/// </summary>
internal static class PatternChannelBus
{
    private static readonly ConcurrentDictionary<ChannelModel, ConcurrentQueue<float[]>> _queues = new();

    private static ConcurrentQueue<float[]> QueueFor(ChannelModel channel)
        => _queues.GetOrAdd(channel, _ => new ConcurrentQueue<float[]>());

    /// <summary>Called by the sequencer once per channel per rendered block.</summary>
    public static void Push(ChannelModel channel, float[] block)
    {
        var q = QueueFor(channel);
        q.Enqueue(block);
        while (q.Count > 4) q.TryDequeue(out _); // safety net if nothing is draining this channel
    }

    /// <summary>Called by the channel's tap provider to fetch the next block.</summary>
    public static bool TryTake(ChannelModel channel, out float[]? block)
        => QueueFor(channel).TryDequeue(out block);

    /// <summary>Drops a channel's queue entirely (strip/channel deleted).</summary>
    public static void Remove(ChannelModel channel) => _queues.TryRemove(channel, out _);
}
