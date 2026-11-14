using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BadWolfVoiceAlarm.Services;

public sealed class AlarmService : IDisposable
{
    private readonly MemoryStream _audioData;
    private readonly WaveFileReader _audioReader;
    private readonly LoopingWaveStream _loopStream;
    private WaveOutEvent? _output;
    private MMDevice? _volumeDevice;
    private float _previousVolume;
    private bool _previousMute;

    public bool IsActive => _output is not null;

    public AlarmService()
    {
        _audioData = new MemoryStream(GenerateAlarmWave());
        _audioReader = new WaveFileReader(_audioData);
        _loopStream = new LoopingWaveStream(_audioReader);
    }

    public bool Start(bool forceMaximumVolume)
    {
        if (IsActive)
        {
            return false;
        }

        try
        {
            if (forceMaximumVolume)
            {
                using var enumerator = new MMDeviceEnumerator();
                _volumeDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _previousVolume = _volumeDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                _previousMute = _volumeDevice.AudioEndpointVolume.Mute;
                _volumeDevice.AudioEndpointVolume.MasterVolumeLevelScalar = 1.0f;
                _volumeDevice.AudioEndpointVolume.Mute = false;
            }

            _audioReader.Position = 0;
            _output = new WaveOutEvent { DesiredLatency = 100, Volume = 1.0f };
            _output.Init(_loopStream);
            _output.Play();
            return true;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        WaveOutEvent? output = _output;
        _output = null;
        try
        {
            try
            {
                output?.Stop();
            }
            finally
            {
                output?.Dispose();
            }
        }
        finally
        {
            RestoreVolume();
        }
    }

    private void RestoreVolume()
    {
        MMDevice? device = _volumeDevice;
        _volumeDevice = null;
        if (device is null)
        {
            return;
        }

        try
        {
            device.AudioEndpointVolume.MasterVolumeLevelScalar = _previousVolume;
            device.AudioEndpointVolume.Mute = _previousMute;
        }
        finally
        {
            device.Dispose();
        }
    }

    public void Dispose()
    {
        Stop();
        _loopStream.Dispose();
        _audioReader.Dispose();
        _audioData.Dispose();
    }

    private static byte[] GenerateAlarmWave()
    {
        const int sampleRate = 44100;
        const int durationSeconds = 3;
        const int channelCount = 1;
        const int bytesPerSample = 2;
        int sampleCount = sampleRate * durationSeconds;
        int dataLength = sampleCount * bytesPerSample;

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channelCount);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channelCount * bytesPerSample);
            writer.Write((short)(channelCount * bytesPerSample));
            writer.Write((short)(bytesPerSample * 8));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            for (int index = 0; index < sampleCount; index++)
            {
                double time = (double)index / sampleRate;
                double burstPosition = time % 0.75;
                double amplitude = 0;
                if (time < 2.8 && burstPosition < 0.48)
                {
                    double attack = Math.Min(1.0, burstPosition / 0.015);
                    double release = Math.Min(1.0, (0.48 - burstPosition) / 0.018);
                    double envelope = Math.Min(attack, release);
                    double frequency = ((int)(time / 0.75) % 2 == 0) ? 880 : 1100;
                    amplitude = envelope * 0.60 * (
                        Math.Sin(2 * Math.PI * frequency * time) * 0.75 +
                        Math.Sin(2 * Math.PI * frequency * 1.5 * time) * 0.25);
                }

                writer.Write((short)(Math.Clamp(amplitude, -1.0, 1.0) * short.MaxValue));
            }
        }

        return stream.ToArray();
    }

    private sealed class LoopingWaveStream : WaveStream
    {
        private readonly WaveStream _source;

        public LoopingWaveStream(WaveStream source) => _source = source;

        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length => _source.Length;
        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = _source.Read(buffer, offset + totalRead, count - totalRead);
                if (bytesRead == 0)
                {
                    if (_source.Position == 0)
                    {
                        break;
                    }

                    _source.Position = 0;
                    continue;
                }

                totalRead += bytesRead;
            }

            return totalRead;
        }
    }
}
