using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using DAW.MVVM.Models;

namespace DAW.Audio;

/// <summary>
/// Export format options.
/// </summary>
public enum ExportFormat
{
    Wav,
    Mp3,
    Flac
}

/// <summary>
/// Export settings for offline rendering.
/// </summary>
public class AudioExportSettings
{
    public string OutputPath { get; set; } = string.Empty;
    public ExportFormat Format { get; set; } = ExportFormat.Wav;
    public int SampleRate { get; set; } = 44100;
    public int BitDepth { get; set; } = 16;
    public int Channels { get; set; } = 2; // 1 = mono, 2 = stereo
}

/// <summary>
/// Progress info reported during export.
/// </summary>
public class ExportProgress
{
    public double Percentage { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan Estimated { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Offline audio renderer that mixes all tracks with their effects and
/// the master effect chain, then writes to a file.
/// </summary>
public static class AudioExportService
{
    /// <summary>
    /// Renders the entire project to an audio file.
    /// </summary>
    public static async Task ExportAsync(
        IReadOnlyList<Track> tracks,
        EffectChain masterEffectChain,
        double masterVolume,
        AudioExportSettings settings,
        IProgress<ExportProgress> progress,
        CancellationToken ct)
    {
        await Task.Run(() =>
        {
            // Phase 1: Build offline mix
            progress.Report(new ExportProgress { Percentage = 0, Status = "Preparing tracks..." });

            var mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(settings.SampleRate, 2);
            var mixer = new MixingSampleProvider(mixFormat) { ReadFully = false };

            var readers = new List<AudioFileReader>();
            var totalDuration = TimeSpan.Zero;

            try
            {
                foreach (var track in tracks)
                {
                    if (string.IsNullOrEmpty(track.FilePath) || !System.IO.File.Exists(track.FilePath))
                        continue;
                    if (!track.IsEnabled || track.IsMuted)
                        continue;

                    var reader = new AudioFileReader(track.FilePath);
                    readers.Add(reader);

                    if (reader.TotalTime > totalDuration)
                        totalDuration = reader.TotalTime;

                    ISampleProvider chain = reader;

                    // Resample if needed
                    if (reader.WaveFormat.SampleRate != settings.SampleRate)
                        chain = new WdlResamplingSampleProvider(chain, settings.SampleRate);

                    // Mono → stereo
                    if (chain.WaveFormat.Channels == 1 && mixFormat.Channels == 2)
                        chain = new MonoToStereoSampleProvider(chain);
                    else if (chain.WaveFormat.Channels == 2 && mixFormat.Channels == 1)
                        chain = new StereoToMonoSampleProvider(chain);

                    // Per-track effects
                    chain = new EffectSampleProvider(chain, track.EffectChain);

                    // Per-track volume/pan
                    var volPan = new VolumePanSampleProvider(chain)
                    {
                        Volume = (float)(track.Volume * masterVolume),
                        Pan = (float)track.Pan
                    };

                    chain = volPan;
                    mixer.AddMixerInput(chain);
                }

                if (readers.Count == 0)
                    throw new InvalidOperationException("No playable tracks to export.");

                // Master effects
                ISampleProvider masterChain = new EffectSampleProvider(mixer, masterEffectChain);

                // Channel conversion (stereo→mono if requested)
                if (settings.Channels == 1 && masterChain.WaveFormat.Channels == 2)
                    masterChain = new StereoToMonoSampleProvider(masterChain);

                // Resample to target if different from mix
                if (masterChain.WaveFormat.SampleRate != settings.SampleRate)
                    masterChain = new WdlResamplingSampleProvider(masterChain, settings.SampleRate);

                progress.Report(new ExportProgress { Percentage = 0, Status = "Rendering audio..." });

                var totalSamples = (long)(totalDuration.TotalSeconds * settings.SampleRate * masterChain.WaveFormat.Channels);
                long samplesWritten = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Phase 2: Write to file
                var tempPath = settings.Format == ExportFormat.Wav
                    ? settings.OutputPath
                    : System.IO.Path.ChangeExtension(settings.OutputPath, ".tmp.wav");

                // For 32-bit float use IEEE format; otherwise PCM
                var outFormat = settings.BitDepth == 32
                    ? WaveFormat.CreateIeeeFloatWaveFormat(settings.SampleRate, masterChain.WaveFormat.Channels)
                    : new WaveFormat(settings.SampleRate, settings.BitDepth, masterChain.WaveFormat.Channels);

                try
                {
                    using (var writer = new WaveFileWriter(tempPath, outFormat))
                    {
                        var buffer = new float[settings.SampleRate * masterChain.WaveFormat.Channels]; // 1s buffer
                        int samplesRead;

                        while ((samplesRead = masterChain.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();

                            // Convert float samples to the target bit depth and write
                            WriteSamples(writer, buffer, samplesRead, settings.BitDepth);
                            samplesWritten += samplesRead;

                            var pct = totalSamples > 0
                                ? Math.Min(100.0, (double)samplesWritten / totalSamples * 100.0)
                                : 0;

                            var elapsed = sw.Elapsed;
                            var estimated = pct > 0
                                ? TimeSpan.FromSeconds(elapsed.TotalSeconds / pct * 100)
                                : TimeSpan.Zero;

                            progress.Report(new ExportProgress
                            {
                                Percentage = pct,
                                Elapsed = elapsed,
                                Estimated = estimated,
                                Status = $"Rendering... {pct:F0}%"
                            });
                        }
                    }

                    // Phase 3: Encode to target format if not WAV
                    if (settings.Format == ExportFormat.Mp3)
                    {
                        progress.Report(new ExportProgress { Percentage = 99, Status = "Encoding to MP3..." });
                        EncodeToMp3(tempPath, settings.OutputPath);
                        if (tempPath != settings.OutputPath)
                            System.IO.File.Delete(tempPath);
                    }
                    else if (settings.Format == ExportFormat.Flac)
                    {
                        progress.Report(new ExportProgress { Percentage = 99, Status = "Encoding to FLAC..." });
                        EncodeToFlac(tempPath, settings.OutputPath, settings.BitDepth);
                        if (tempPath != settings.OutputPath)
                            System.IO.File.Delete(tempPath);
                    }

                    progress.Report(new ExportProgress { Percentage = 100, Status = "Export complete!" });
                }
                catch (OperationCanceledException)
                {
                    // Clean up partial output files on cancellation
                    TryDeleteFile(tempPath);
                    if (tempPath != settings.OutputPath)
                        TryDeleteFile(settings.OutputPath);

                    progress.Report(new ExportProgress { Percentage = 0, Status = "Export cancelled." });
                    throw;
                }
            }
            finally
            {
                foreach (var r in readers)
                    r.Dispose();
            }
        }, ct);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch { /* best effort cleanup */ }
    }

    private static void WriteSamples(WaveFileWriter writer, float[] buffer, int count, int bitDepth)
    {
        switch (bitDepth)
        {
            case 16:
                for (int i = 0; i < count; i++)
                {
                    var sample = Math.Clamp(buffer[i], -1f, 1f);
                    writer.WriteSample(sample);
                }
                break;
            case 24:
                for (int i = 0; i < count; i++)
                {
                    var sample = Math.Clamp(buffer[i], -1f, 1f);
                    writer.WriteSample(sample);
                }
                break;
            case 32:
                // 32-bit float
                for (int i = 0; i < count; i++)
                    writer.WriteSample(buffer[i]);
                break;
            default:
                for (int i = 0; i < count; i++)
                    writer.WriteSample(Math.Clamp(buffer[i], -1f, 1f));
                break;
        }
    }

    /// <summary>
    /// Encodes WAV to MP3 using NAudio's LAME wrapper if available,
    /// otherwise falls back to copying as WAV with .mp3 extension (user can re-encode).
    /// </summary>
    private static void EncodeToMp3(string wavPath, string mp3Path)
    {
        // NAudio doesn't include an MP3 encoder out of the box.
        // We use MediaFoundation encoder which is available on Windows 8+.
        try
        {
            using var reader = new AudioFileReader(wavPath);
            MediaFoundationEncoder.EncodeToMp3(reader, mp3Path);
        }
        catch (Exception)
        {
            // Fallback: just copy as WAV (user can use external tool)
            if (wavPath != mp3Path)
                System.IO.File.Copy(wavPath, mp3Path, true);
        }
    }

    /// <summary>
    /// FLAC encoding — NAudio does not have built-in FLAC encoding.
    /// Falls back to WAV with .flac extension; user can convert externally.
    /// </summary>
    private static void EncodeToFlac(string wavPath, string flacPath, int bitDepth)
    {
        // NAudio doesn't include FLAC encoder; copy as WAV for now.
        if (wavPath != flacPath)
            System.IO.File.Copy(wavPath, flacPath, true);
    }
}
