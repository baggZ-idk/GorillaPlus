using System;
using UnityEngine;

namespace GorillaNetworking
{
    [RequireComponent(typeof(AudioSource))]
    public class VoicePlayback : MonoBehaviour
    {
        const int PullBlock = 480;

        AudioSource source;
        volatile VoiceJitterBuffer jitter;
        int outputRate;
        double ratio;
        float[] scratch = new float[4096];

        // Resampler state (audio thread only)
        readonly float[] src = new float[PullBlock];
        int srcPos = PullBlock;
        float s0, s1;
        double t;
        volatile bool resetResampler = true;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.pitch = 1f;
            source.dopplerLevel = 0f;

            source.clip = AudioClip.Create("voice_silence", VoiceConstants.SampleRate, 1, VoiceConstants.SampleRate, false);

            outputRate = AudioSettings.outputSampleRate;
            ratio = (double)VoiceConstants.SampleRate / outputRate;

            Debug.Log($"[GorillaNetworking] Unity output rate {outputRate} Hz, voice 48000 Hz, resample ratio {ratio:0.###}" +
                      (outputRate == VoiceConstants.SampleRate ? " (no resampling)" : " (resampling active)"));
        }

        public void Attach(VoiceJitterBuffer buffer)
        {
            resetResampler = true;
            jitter = buffer;
            if (!source.isPlaying) source.Play();
        }

        public void Detach()
        {
            jitter = null;
            if (source != null) source.Stop();
        }

        float NextSource(VoiceJitterBuffer j)
        {
            if (srcPos >= PullBlock)
            {
                j.Read(src, PullBlock);
                srcPos = 0;
            }
            return src[srcPos++];
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var j = jitter;
            if (j == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            int frames = data.Length / channels;

            if (outputRate == VoiceConstants.SampleRate)
            {
                if (scratch.Length < frames) scratch = new float[frames];
                j.Read(scratch, frames);

                for (int f = 0; f < frames; f++)
                {
                    float s = scratch[f];
                    int baseIdx = f * channels;
                    for (int c = 0; c < channels; c++) data[baseIdx + c] = s;
                }
                return;
            }

            if (resetResampler)
            {
                resetResampler = false;
                srcPos = PullBlock;
                s0 = NextSource(j);
                s1 = NextSource(j);
                t = 0.0;
            }

            for (int f = 0; f < frames; f++)
            {
                float s = s0 + (s1 - s0) * (float)t;
                int baseIdx = f * channels;
                for (int c = 0; c < channels; c++) data[baseIdx + c] = s;

                t += ratio;
                while (t >= 1.0)
                {
                    t -= 1.0;
                    s0 = s1;
                    s1 = NextSource(j);
                }
            }
        }
    }
}