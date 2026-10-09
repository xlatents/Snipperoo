using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Snipperoo.Capture;

/// <summary>
/// Records what the default output device plays (WASAPI loopback) to a WAV file.
/// Loopback delivers no packets while nothing is playing, so gaps are filled with silence to keep
/// the file length equal to wall-clock time; the audio/video sync relies on that.
/// </summary>
internal sealed class AudioRecorder : IDisposable
{
    // Gaps shorter than this are normal buffer jitter, not silence.
    private static readonly TimeSpan GapTolerance = TimeSpan.FromMilliseconds(40);

    private readonly WasapiRecorder _capture;
    private readonly WaveFileWriter _writer;
    private readonly Stopwatch _clock = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private long _bytesWritten;
    private TimeSpan? _stopAt;

    public AudioRecorder(string wavPath)
    {
        _capture = new WasapiRecorderBuilder().WithLoopbackCapture().Build();
        _writer = new WaveFileWriter(wavPath, _capture.WaveFormat);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
                Log.Error("Audio capture stopped with an error", e.Exception);
            _stopped.TrySetResult();
        };
    }

    /// <summary>Wall-clock time of the first sample in the file.</summary>
    public DateTime StartedAtUtc { get; private set; }

    public void Start()
    {
        StartedAtUtc = DateTime.UtcNow;
        _clock.Start();
        _capture.StartRecording();
    }

    /// <summary>Ends the file at the moment of this call, padding with silence up to it, and closes it.</summary>
    public async Task StopAsync()
    {
        lock (_gate)
            _stopAt = _clock.Elapsed;

        _capture.StopRecording();
        try
        {
            await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log.Error("Audio capture did not confirm stop; finishing the file anyway");
        }

        lock (_gate)
        {
            PadTo(_stopAt.Value);
            _writer.Dispose();
        }
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        lock (_gate)
        {
            if (_stopAt is not null)
                return;

            // The buffer holds the most recent audio, so after writing it the file should reach "now".
            var bufferLength = TimeSpan.FromSeconds((double)buffer.Length / _capture.WaveFormat.AverageBytesPerSecond);
            PadTo(_clock.Elapsed - bufferLength);

            // WASAPI may leave stale data in buffers flagged silent.
            if ((flags & AudioClientBufferFlags.Silent) != 0)
                WriteSilence(buffer.Length);
            else
                _writer.Write(buffer);
            _bytesWritten += buffer.Length;
        }
    }

    private void PadTo(TimeSpan target)
    {
        var format = _capture.WaveFormat;
        long targetBytes = (long)(target.TotalSeconds * format.AverageBytesPerSecond);
        long missing = targetBytes - _bytesWritten;
        if (missing <= GapTolerance.TotalSeconds * format.AverageBytesPerSecond)
            return;

        missing -= missing % format.BlockAlign;
        WriteSilence(missing);
        _bytesWritten += missing;
    }

    private void WriteSilence(long bytes)
    {
        Span<byte> zeros = stackalloc byte[4096];
        zeros.Clear();
        for (long left = bytes; left > 0; left -= zeros.Length)
            _writer.Write(zeros[..(int)Math.Min(left, zeros.Length)]);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _writer.Dispose();
    }
}
