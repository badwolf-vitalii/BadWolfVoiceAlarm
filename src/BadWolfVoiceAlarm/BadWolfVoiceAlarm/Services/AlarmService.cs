using System;
using System.Collections.Generic;
using System.IO;
using BadWolfVoiceAlarm.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BadWolfVoiceAlarm.Services;

public sealed class AlarmService : IDisposable
{
    private readonly MemoryStream _audioData;
    private readonly WaveFileReader _audioReader;
    private readonly LoopingWaveStream _loopStream;
    private WasapiOut? _output;
    private MMDevice? _playbackDevice;
    private float _previousVolume;
    private bool _previousMute;
    private bool _shouldRestoreVolume;

    public bool IsActive => _output is not null;

    public AlarmService()
    {
        _audioData = new MemoryStream(GenerateAlarmWave());
        _audioReader = new WaveFileReader(_audioData);
        _loopStream = new LoopingWaveStream(_audioReader);
    }

    public static List<AudioOutputDevice> GetOutputDevices()
    {
        var devices = new List<AudioOutputDevice>
        {
            new(AudioOutputDevice.SystemDefaultId, "System default (Windows)")
        };

        using var enumerator = new MMDeviceEnumerator();
        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                devices.Add(new AudioOutputDevice(device.ID, device.FriendlyName));
            }
            finally
            {
                device.Dispose();
            }
        }

        return devices;
    }

    public bool Start(bool forceMaximumVolume, string? outputDeviceId)
    {
        if (IsActive)
        {
            return false;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _playbackDevice = string.IsNullOrEmpty(outputDeviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(outputDeviceId);

            if (_playbackDevice.State != DeviceState.Active)
            {
                throw new InvalidOperationException(
                    "The selected audio output device is unavailable. Reconnect it or select another device.");
            }

            if (forceMaximumVolume)
            {
                var volume = _playbackDevice.AudioEndpointVolume;
                _previousVolume = volume.MasterVolumeLevelScalar;
                _previousMute = volume.Mute;
                _shouldRestoreVolume = true;
                volume.MasterVolumeLevelScalar = 1.0f;
                volume.Mute = false;
            }

            _audioReader.Position = 0;
            _output = new WasapiOut(_playbackDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
            _output.Init(_loopStream);
            _output.Play();
            return true;
        }
        catch
        {
            try
            {
                Stop();
            }
            catch
            {
                // Preserve the original playback error if cleanup also fails.
            }

            throw;
        }
    }

    public void Stop()
    {
        WasapiOut? output = _output;
        _output = null;

        try
        {
            output?.Stop();
        }
        finally
        {
            try
            {
                output?.Dispose();
            }
            finally
            {
                try
                {
                    RestoreVolume();
                }
                finally
                {
                    _playbackDevice?.Dispose();
                    _playbackDevice = null;
                }
            }
        }
    }

    private void RestoreVolume()
    {
        if (!_shouldRestoreVolume || _playbackDevice is null)
        {
            return;
        }

        _shouldRestoreVolume = false;
        var volume = _playbackDevice.AudioEndpointVolume;
        try
        {
            volume.MasterVolumeLevelScalar = _previousVolume;
        }
        finally
        {
            volume.Mute = _previousMute;
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
