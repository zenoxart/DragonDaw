using NAudio.Wave;
using DAW.MVVM.Models.Sequencer;

namespace DAW.Audio;

/// <summary>
/// Reads one Channel-Rack channel's Playlist-triggered audio, handed off
/// sample-accurately by <see cref="PatternSequencerProvider"/> via
/// <see cref="PatternChannelBus"/>. Wired into the channel's strip alongside
/// its <see cref="ChannelRackBusProvider"/> (which still carries live/preview
/// triggers), so both sources reach the same Volume/Pan/Mute, insert effects,
/// and meter — this is what makes Playlist-driven pattern playback visible in
/// the mixer instead of being summed straight to master.
/// </summary>
public sealed class PatternChannelTapProvider : ISampleProvider
{
    private readonly ChannelModel _channel;

    public WaveFormat WaveFormat { get; }

    public PatternChannelTapProvider(WaveFormat format, ChannelModel channel)
    {
        WaveFormat = format;
        _channel   = channel;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        // Only accept a block that matches this call's size — a stale block from
        // a different buffer size (e.g. right after a device restart) would
        // otherwise misalign; silence for one block is inaudible, drift is not.
        if (PatternChannelBus.TryTake(_channel, out var block) && block != null && block.Length == count)
        {
            Array.Copy(block, 0, buffer, offset, count);
        }
        else
        {
            Array.Clear(buffer, offset, count);
        }
        return count;
    }
}
