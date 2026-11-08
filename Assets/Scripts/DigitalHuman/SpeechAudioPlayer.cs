using System;
using UnityEngine;

namespace DataPerson.DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class SpeechAudioPlayer : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 0.75f;
        [SerializeField, Range(5, 60)] private int streamBufferSeconds = 30;
        [SerializeField, Range(20, 500)] private int streamPrebufferMilliseconds = 180;

        private AudioClip generatedClip;
        private bool wasPlaying;
        private bool isPaused;
        private readonly object streamGate = new();
        private float[] streamBuffer;
        private int streamReadIndex;
        private int streamWriteIndex;
        private int streamBufferedSamples;
        private int streamSampleRate;
        private int streamChannels;
        private long streamPlayedSamples;
        private bool streamActive;
        private bool streamEnding;
        private bool streamPlaybackStarted;

        public event Action PlaybackStarted;
        public event Action PlaybackCompleted;

        public AudioSource Source => audioSource;
        public bool IsStreaming => streamActive;
        public double StreamPlayedMilliseconds => streamSampleRate > 0 && streamChannels > 0
            ? streamPlayedSamples * 1000d / (streamSampleRate * streamChannels)
            : 0d;

        public void Initialize()
        {
            audioSource = audioSource ? audioSource : GetComponent<AudioSource>();
            if (!audioSource)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = spatialBlend;
        }

        public void Play(AudioClip clip)
        {
            if (!clip)
            {
                throw new ArgumentNullException(nameof(clip));
            }
            EnsureInitialized();
            ReleaseGeneratedClip();
            audioSource.clip = clip;
            audioSource.Play();
            wasPlaying = true;
            isPaused = false;
            PlaybackStarted?.Invoke();
        }

        public void PlayPcm16(byte[] pcmData, int sampleRate, int channels = 1)
        {
            if (pcmData == null || pcmData.Length == 0 || pcmData.Length % 2 != 0)
            {
                throw new ArgumentException("PCM16 data must contain complete 16-bit samples.", nameof(pcmData));
            }
            if (sampleRate <= 0 || channels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            var sampleCount = pcmData.Length / 2;
            if (sampleCount % channels != 0)
            {
                throw new ArgumentException("PCM16 sample count must be divisible by channel count.", nameof(pcmData));
            }

            var samples = new float[sampleCount];
            for (var index = 0; index < sampleCount; index++)
            {
                samples[index] = BitConverter.ToInt16(pcmData, index * 2) / 32768f;
            }

            ReleaseGeneratedClip();
            generatedClip = AudioClip.Create(
                "AssistantSpeech",
                sampleCount / channels,
                channels,
                sampleRate,
                false);
            generatedClip.SetData(samples, 0);
            PlayGeneratedClip();
        }

        public void BeginPcm16Stream(int sampleRate, int channels = 1)
        {
            if (sampleRate <= 0 || channels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            EnsureInitialized();
            Stop();
            ReleaseGeneratedClip();
            lock (streamGate)
            {
                streamSampleRate = sampleRate;
                streamChannels = channels;
                streamBuffer = new float[sampleRate * channels * streamBufferSeconds];
                streamReadIndex = 0;
                streamWriteIndex = 0;
                streamBufferedSamples = 0;
                streamPlayedSamples = 0;
                streamActive = true;
                streamEnding = false;
                streamPlaybackStarted = false;
            }
            generatedClip = AudioClip.Create(
                "AssistantSpeechStream",
                sampleRate * 60,
                channels,
                sampleRate,
                true,
                FillStreamAudio);
            audioSource.clip = generatedClip;
            audioSource.loop = true;
        }

        public void AppendPcm16Stream(byte[] pcmData)
        {
            if (pcmData == null || pcmData.Length % 2 != 0)
            {
                throw new ArgumentException("PCM16 data must contain complete 16-bit samples.", nameof(pcmData));
            }
            if (pcmData.Length == 0) return;

            lock (streamGate)
            {
                if (!streamActive) throw new InvalidOperationException("No PCM stream is active.");
                var sampleCount = pcmData.Length / 2;
                if (sampleCount > streamBuffer.Length - streamBufferedSamples)
                {
                    throw new InvalidOperationException("Assistant audio stream buffer is full.");
                }
                for (var index = 0; index < sampleCount; index++)
                {
                    var low = pcmData[index * 2];
                    var high = pcmData[index * 2 + 1];
                    var sample = (short)(low | (high << 8));
                    streamBuffer[streamWriteIndex] = sample / 32768f;
                    streamWriteIndex = (streamWriteIndex + 1) % streamBuffer.Length;
                }
                streamBufferedSamples += sampleCount;
            }
        }

        public void EndPcm16Stream()
        {
            lock (streamGate)
            {
                if (streamActive) streamEnding = true;
            }
        }

        public void Stop()
        {
            lock (streamGate)
            {
                streamActive = false;
                streamEnding = false;
                streamPlaybackStarted = false;
                streamBufferedSamples = 0;
                streamBuffer = null;
            }
            if (!audioSource)
            {
                return;
            }
            audioSource.Stop();
            audioSource.loop = false;
            wasPlaying = false;
            isPaused = false;
        }

        public void Pause()
        {
            if (audioSource && audioSource.isPlaying)
            {
                audioSource.Pause();
                isPaused = true;
            }
        }

        public void Resume()
        {
            if (audioSource && wasPlaying && isPaused)
            {
                audioSource.UnPause();
                isPaused = false;
            }
        }

        private void Update()
        {
            if (streamActive)
            {
                UpdateStreamPlayback();
                return;
            }
            if (!wasPlaying || isPaused || !audioSource || audioSource.isPlaying)
            {
                return;
            }

            wasPlaying = false;
            PlaybackCompleted?.Invoke();
        }

        private void OnDestroy()
        {
            ReleaseGeneratedClip();
        }

        private void EnsureInitialized()
        {
            if (!audioSource)
            {
                Initialize();
            }
        }

        private void PlayGeneratedClip()
        {
            EnsureInitialized();
            audioSource.clip = generatedClip;
            audioSource.loop = false;
            audioSource.Play();
            wasPlaying = true;
            isPaused = false;
            PlaybackStarted?.Invoke();
        }

        private void ReleaseGeneratedClip()
        {
            if (!generatedClip)
            {
                return;
            }
            if (audioSource && audioSource.clip == generatedClip)
            {
                audioSource.clip = null;
            }
            Destroy(generatedClip);
            generatedClip = null;
        }

        private void UpdateStreamPlayback()
        {
            int bufferedSamples;
            bool ending;
            lock (streamGate)
            {
                bufferedSamples = streamBufferedSamples;
                ending = streamEnding;
            }

            var prebufferSamples = streamSampleRate * streamChannels * streamPrebufferMilliseconds / 1000;
            if (!streamPlaybackStarted && (bufferedSamples >= prebufferSamples || (ending && bufferedSamples > 0)))
            {
                audioSource.Play();
                streamPlaybackStarted = true;
                wasPlaying = true;
                PlaybackStarted?.Invoke();
            }
            if (!ending || bufferedSamples > 0) return;

            audioSource.Stop();
            audioSource.loop = false;
            lock (streamGate)
            {
                streamActive = false;
                streamEnding = false;
                streamPlaybackStarted = false;
                streamBuffer = null;
            }
            wasPlaying = false;
            PlaybackCompleted?.Invoke();
        }

        private void FillStreamAudio(float[] data)
        {
            Array.Clear(data, 0, data.Length);
            lock (streamGate)
            {
                if (!streamActive || streamBuffer == null) return;
                var available = Math.Min(data.Length, streamBufferedSamples);
                for (var index = 0; index < available; index++)
                {
                    data[index] = streamBuffer[streamReadIndex];
                    streamReadIndex = (streamReadIndex + 1) % streamBuffer.Length;
                }
                streamBufferedSamples -= available;
                streamPlayedSamples += available;
            }
        }
    }
}
