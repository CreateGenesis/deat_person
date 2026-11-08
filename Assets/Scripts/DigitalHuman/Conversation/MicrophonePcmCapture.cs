using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace DataPerson.DigitalHuman.Conversation
{
    public sealed class MicrophonePcmCapture : IDisposable
    {
        private const int RequestedSampleRate = 48000;
        private AudioClip clip;
        private string deviceName;
        private int readPosition;
        private Pcm16FrameEncoder encoder;

        public event Action<byte[]> FrameReady;

        public bool IsCapturing => clip;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (IsCapturing) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                var permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => permission.TrySetResult(true);
                callbacks.PermissionDenied += _ => permission.TrySetResult(false);
                callbacks.PermissionDeniedAndDontAskAgain += _ => permission.TrySetResult(false);
                Permission.RequestUserPermission(Permission.Microphone, callbacks);
                using var registration = cancellationToken.Register(() => permission.TrySetCanceled(cancellationToken));
                if (!await permission.Task)
                {
                    throw new InvalidOperationException("Microphone permission was denied.");
                }
            }
#endif
            cancellationToken.ThrowIfCancellationRequested();
            if (Microphone.devices.Length == 0)
            {
                throw new InvalidOperationException("No microphone is available.");
            }
            deviceName = Microphone.devices[0];
            clip = Microphone.Start(deviceName, true, 1, RequestedSampleRate);
            if (!clip)
            {
                throw new InvalidOperationException("Microphone capture could not be started.");
            }
            readPosition = 0;
            encoder = new Pcm16FrameEncoder(clip.frequency, clip.channels);
            var startDeadline = Time.realtimeSinceStartup + 2f;
            while (Microphone.GetPosition(deviceName) <= 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartup >= startDeadline)
                {
                    Stop(false);
                    throw new InvalidOperationException("Microphone started but did not produce audio samples.");
                }
                await Task.Yield();
            }
            Debug.Log($"[Voice] Microphone ready: {deviceName}, {clip.frequency} Hz, {clip.channels} channel(s).");
        }

        public void Pump()
        {
            if (!clip) return;
            var writePosition = Microphone.GetPosition(deviceName);
            if (writePosition < 0 || writePosition == readPosition) return;
            if (writePosition > readPosition)
            {
                ReadSamples(readPosition, writePosition - readPosition);
            }
            else
            {
                ReadSamples(readPosition, clip.samples - readPosition);
                if (writePosition > 0) ReadSamples(0, writePosition);
            }
            readPosition = writePosition;
        }

        public void Stop(bool flushPartialFrame)
        {
            if (!clip) return;
            Pump();
            if (flushPartialFrame)
            {
                var finalFrame = encoder.FlushPaddedFrame();
                if (finalFrame != null) FrameReady?.Invoke(finalFrame);
            }
            Microphone.End(deviceName);
            UnityEngine.Object.Destroy(clip);
            clip = null;
            encoder = null;
            readPosition = 0;
            deviceName = null;
        }

        public void Dispose()
        {
            Stop(false);
        }

        private void ReadSamples(int offset, int frameCount)
        {
            if (frameCount <= 0) return;
            var samples = new float[frameCount * clip.channels];
            if (!clip.GetData(samples, offset))
            {
                throw new InvalidOperationException("Microphone samples could not be read.");
            }
            foreach (var frame in encoder.Push(samples)) FrameReady?.Invoke(frame);
        }
    }

    public sealed class Pcm16FrameEncoder
    {
        private readonly int sourceSampleRate;
        private readonly int sourceChannels;
        private readonly byte[] frame = new byte[RealtimeProtocol.InputFrameBytes];
        private readonly double sourceStep;
        private int frameOffset;
        private long sourceFrameIndex = -1;
        private double nextOutputIndex;
        private float previousSample;

        public Pcm16FrameEncoder(int inputSampleRate, int inputChannels)
        {
            if (inputSampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputSampleRate));
            if (inputChannels <= 0) throw new ArgumentOutOfRangeException(nameof(inputChannels));
            sourceSampleRate = inputSampleRate;
            sourceChannels = inputChannels;
            sourceStep = (double)sourceSampleRate / RealtimeProtocol.InputSampleRate;
        }

        public IReadOnlyList<byte[]> Push(float[] interleavedSamples)
        {
            if (interleavedSamples == null) throw new ArgumentNullException(nameof(interleavedSamples));
            if (interleavedSamples.Length % sourceChannels != 0)
            {
                throw new ArgumentException("Input samples must contain complete channel frames.", nameof(interleavedSamples));
            }

            var output = new List<byte[]>();
            for (var offset = 0; offset < interleavedSamples.Length; offset += sourceChannels)
            {
                var mono = 0f;
                for (var channel = 0; channel < sourceChannels; channel++) mono += interleavedSamples[offset + channel];
                mono /= sourceChannels;
                sourceFrameIndex++;
                if (sourceFrameIndex == 0)
                {
                    previousSample = mono;
                }
                while (nextOutputIndex <= sourceFrameIndex)
                {
                    var fraction = sourceFrameIndex == 0 ? 0d : nextOutputIndex - (sourceFrameIndex - 1d);
                    var sample = sourceFrameIndex == 0
                        ? mono
                        : previousSample + (mono - previousSample) * (float)Math.Clamp(fraction, 0d, 1d);
                    WriteSample(sample, output);
                    nextOutputIndex += sourceStep;
                }
                previousSample = mono;
            }
            return output;
        }

        public byte[] FlushPaddedFrame()
        {
            if (frameOffset == 0) return null;
            Array.Clear(frame, frameOffset, frame.Length - frameOffset);
            var value = (byte[])frame.Clone();
            frameOffset = 0;
            return value;
        }

        private void WriteSample(float value, ICollection<byte[]> output)
        {
            var clamped = Mathf.Clamp(value, -1f, 1f);
            var sample = (short)Mathf.RoundToInt(clamped * (clamped < 0f ? 32768f : 32767f));
            frame[frameOffset++] = (byte)sample;
            frame[frameOffset++] = (byte)(sample >> 8);
            if (frameOffset != frame.Length) return;
            output.Add((byte[])frame.Clone());
            frameOffset = 0;
        }
    }
}
